using System;
using System.Linq;
using AuraShared.Core;
using Mirror;

namespace AuraToolsExp.Dll.GameApi;

internal sealed class AuraToolsControlledSoloSession : IDisposable
{
    private readonly GameServer server;
    private readonly FightManager fight;
    private readonly RoleTable role;
    private readonly long room;
    private readonly string adventure;
    private readonly bool acceptJoin;
    private readonly bool fake;
    private readonly string level;
    private readonly string wantedLevel;
    private readonly int waitCount;
    private readonly NetworkConnectionToClient connection;
    private bool disposed;
    private bool cancelled;
    private string queuedLevel = "";
    private static AuraToolsControlledSoloSession? pendingStart;
    private static bool guardRegistered;

    private AuraToolsControlledSoloSession()
    {
        server = GameServer.Instance; fight = FightManager.Instance; role = RoleTable.Instance;
        room = AuraNetworkIdentityRuntime.RoomGeneration; adventure = AuraNetworkIdentityRuntime.AdventureId;
        connection = NetworkServer.localConnection;
        acceptJoin = server.isAcceptJoin; fake = fight.IsFake;
        level = fight.level; wantedLevel = fight.wantLevel; waitCount = fight.waitCount;
        server.isAcceptJoin = false;
    }

    internal static bool IsAvailable(out string reason)
    {
        var manager = PlayerManager.Instance;
        var local = NetworkServer.localConnection;
        var players = GameServer.Instance?.LobbyInfo?.AddedPlayers;
        var allowed = NetworkServer.active && manager != null && manager.isServer
            && NetworkClient.ready && local?.isReady == true
            && local != null && ReferenceEquals(manager.connectionToClient, local)
            && NetworkServer.connections.Count == 1 && NetworkServer.connections.Values.All(item => ReferenceEquals(item, local))
            && players != null && players.Count == 1 && players[0].Id == manager.PlayerId
            && LobbyManager.Instance != null && LobbyManager.Instance.lobbyId == 0
            && AuraNetworkIdentityRuntime.EnsureCurrentAdventure();
        reason = allowed ? "" : "实机验证仅支持受控单机会话，请先离开联机房间并进入单机冒险。";
        return allowed;
    }

    internal static AuraToolsControlledSoloSession Acquire()
    {
        if (!IsAvailable(out var reason)) throw new InvalidOperationException(reason);
        if (!guardRegistered)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(FightManager).TypeHandle);
            AuraNativeRpcReceiveAdapter.RegisterCommandGuard("AuraToolsExp.GameValidation", "System.Void FightManager::ReadyToInit(System.String)",
                (target, reader, sender) =>
                {
                    var current = pendingStart;
                    if (current == null || !ReferenceEquals(target, current.fight) || !ReferenceEquals(sender, current.connection)) return true;
                    if (reader.ReadString() != current.queuedLevel) return true;
                    pendingStart = null;
                    return !current.disposed && !current.cancelled && current.CanRun;
                });
            guardRegistered = true;
        }
        return new AuraToolsControlledSoloSession();
    }

    internal bool IsCurrent => ReferenceEquals(server, GameServer.Instance) && ReferenceEquals(fight, FightManager.Instance)
        && ReferenceEquals(role, RoleTable.Instance) && room == AuraNetworkIdentityRuntime.RoomGeneration
        && adventure == AuraNetworkIdentityRuntime.AdventureId && LobbyManager.Instance?.lobbyId == 0;
    internal bool CanRun => IsCurrent && IsAvailable(out _);
    internal void BeginBattle(string requestedLevel)
    {
        if (!CanRun) throw new InvalidOperationException("验证会话已变化。");
        cancelled = false; queuedLevel = requestedLevel; pendingStart = this; fight.IsFake = true;
    }
    internal void CancelStart() => cancelled = true;
    internal void EndBattle() { if (IsCurrent) fight.IsFake = fake; }
    public void Dispose()
    {
        disposed = true;
        cancelled = true;
        if (!IsCurrent) return;
        fight.IsFake = fake; fight.level = level; fight.wantLevel = wantedLevel; fight.waitCount = waitCount;
        if (!server.isAcceptJoin) server.isAcceptJoin = acceptJoin;
    }
}
