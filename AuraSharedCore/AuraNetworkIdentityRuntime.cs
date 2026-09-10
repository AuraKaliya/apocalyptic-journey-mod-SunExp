using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Data.Save;
using Mirror;
using Mirror.RemoteCalls;
using Network.Command;
using Network.Query;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace AuraShared.Core;

public interface IAuraSharedServerBoundRpcCommand { void BindServerSender(AuraRpcSender sender); }

public abstract class AuraBattleRpcCommand : RpcCommandBase
{
    public string NetworkBattleId { get; set; } = AuraNetworkIdentityRuntime.BattleId;
}

public static class AuraNetworkIdentityRuntime
{
    public const string AdventureKey = "Aura.Shared.Network.AdventureId";
    public const string ProtocolKey = "Aura.Shared.Network.Protocol";
    public const int ProtocolVersion = 1;
    private static readonly AuraNetworkIdentityState State = new();
    private static bool initialized;
    private static object? connectionIdentity;
    private static SaveInfo? boundSave;
    private static int nextBattleEpoch;
    private static PendingInitialization? pending;
    private static readonly System.Collections.Generic.Dictionary<string, Version> ProtocolProviders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Generic.HashSet<string> LegacyAdventureKeys = new(StringComparer.Ordinal);
    public static void RegisterProtocolProvider(string modName, string minimumVersion)
        => ProtocolProviders[modName] = Version.Parse(minimumVersion);
    public static void RegisterLegacyAdventureIdKey(string key) => LegacyAdventureKeys.Add(key);

    public static event Action? Changed;
    public static event Action? Updating;
    public static event Action<string>? SynchronizationFailed;
    public static string AdventureId => State.AdventureId;
    public static string BattleId => State.BattleId;
    public static int BattleEpoch => State.BattleEpoch;
    public static bool BattleReady => State.BattleReady;
    public static long RoomGeneration => State.RoomGeneration;
    public static bool IsAuthority => NetworkServer.active;
    public static bool MatchesBattle(string id) => State.MatchesBattle(id);
    public static bool MatchesBattleResult(string id) => State.MatchesBattleResult(id);
    public static bool MatchesAdventure(string id) => AuraNetworkIdentityState.ValidId(id) && id == AdventureId;
    public static string LocalPlayerId => PlayerManager.Instance?.PlayerId ?? "single-player";

    public static void Initialize(ModConfig config)
    {
        if (initialized) return;
        AuraRpcAdmission.Register<RpcAuraBattleIdentityRequest>(AuraRpcPublication.MemberRequest);
        AuraRpcAdmission.RegisterHandler<RpcAuraBattleIdentityRequest>((request, sender) =>
        {
            ResolveIdentity(request, sender);
            if (request.TargetQueryId == 0 || !NetworkServer.connections.TryGetValue(sender.ConnectionId, out var connection)) return;
            if (!AuraNativeTargetedQueryTransport.TrySend(connection, request.TargetQueryId,
                new AuraBattleIdentityQuery { Result = AuraSharedJson.Serialize(request) }, out var error)) Warn(error);
        });
        AuraRpcAuthorityRuntime.Register(config, "AuraNetwork",
            command => command is IAuraSharedServerBoundRpcCommand,
            (command, sender) => ((IAuraSharedServerBoundRpcCommand)command).BindServerSender(sender));
        RuntimeHelpers.RunClassConstructor(typeof(FightManager).TypeHandle);
        var hooks = new AuraHookRegistry(config, "AuraNetworkIdentity", null, Warn);
        hooks.AfterRouted("GameSaveManager.Select", _ => BindSelectedSave(), "AfterSaveSelect");
        hooks.BeforeRouted("GameSaveManager.Save", _ => EnsureCurrentAdventure(), "BeforeSave");
        hooks.BeforeRouted("PlayerManager.UserCode_CmdSendSave", _ => EnsureCurrentAdventure(), "BeforeNativeSaveSync");
        hooks.BeforeRouted("GameEntryUI.StartGame", _ => EnsureCurrentAdventure(), "AdventureStarting");
        hooks.BeforeRouted("FightManager.Init", context =>
        {
            if (!NetworkServer.active || context.Arguments == null) return;
            var fingerprint = Fingerprint(context.Arguments);
            BeginHostBattle(fingerprint);
        }, "HostBattleInit");
        hooks.BeforeRouted("FightManager.ClearFightui", _ =>
        {
            if (NetworkServer.active) { State.EndBattle(); RaiseChanged(); }
        }, "HostBattleRestart");
        foreach (var target in new[] { "Fight_Win.ResetStates", "Fight_Escape.ResetStates", "Fight_Loss.Init" })
            hooks.AfterRouted(target, _ => { State.EndBattle(); RaiseChanged(); }, "EndBattle:" + target);

        AuraNativeRpcReceiveAdapter.InstallDelegate(
            "System.Void FightManager::Init(System.String,System.Byte[],System.Byte[],System.Single,System.Single)",
            (original, target, reader, sender) => ReceiveNativeBattle(original, target, reader, sender, false));
        AuraNativeRpcReceiveAdapter.InstallDelegate("System.Void FightManager::ClearFightui()",
            (original, target, reader, sender) => ReceiveNativeBattle(original, target, reader, sender, true));
        initialized = true;
        var driver = new GameObject("AuraShared.NetworkIdentity");
        UnityEngine.Object.DontDestroyOnLoad(driver);
        driver.AddComponent<AuraNetworkIdentityDriver>();
        BindSelectedSave();
    }

    public static bool EnsureCurrentAdventure()
    {
        ObserveConnection();
        if (!NetworkServer.active && !NetworkClient.active) return false;
        var save = GameSaveManager.GetNowSave();
        if (save == null) return false;
        if (IsAuthority)
        {
            if (save.GameVars == null || !save.GameVars.TryGetValue(AdventureKey, out var savedId) || !AuraNetworkIdentityState.ValidId(savedId)
                || !save.GameVars.TryGetValue(ProtocolKey, out var savedProtocol) || savedProtocol != ProtocolVersion.ToString())
            {
                var previousVars = save.GameVars == null ? null : new System.Collections.Generic.Dictionary<string, string>(save.GameVars);
                try { EnsureSaveIdentity(save); AuraNativeSaveStore.Commit(save); }
                catch
                {
                    // An unpersisted id must not become the next successful lookup.
                    save.GameVars = previousVars!;
                    throw;
                }
            }
            boundSave = save;
        }
        if (!ReferenceEquals(boundSave, save)) return false;
        var id = save.GameVars != null && save.GameVars.TryGetValue(AdventureKey, out var value) ? value : "";
        var changed = id != AdventureId;
        if (changed) CancelPending();
        if (!AuraNetworkIdentityState.ValidId(id))
        {
            State.ClearAdventure();
            if (changed) RaiseChanged();
            return false;
        }
        var bound = State.BindAdventure(id);
        if (bound && changed) RaiseChanged();
        return bound;
    }

    private static bool EnsureSaveIdentity(SaveInfo save)
    {
        save.GameVars ??= new System.Collections.Generic.Dictionary<string, string>();
        var changed = !save.GameVars.TryGetValue(AdventureKey, out var id) || !AuraNetworkIdentityState.ValidId(id);
        if (changed)
        {
            var legacy = LegacyAdventureKeys.Select(key => save.GameVars.TryGetValue(key, out var value) ? value : "")
                .Where(AuraNetworkIdentityState.ValidId).Distinct(StringComparer.Ordinal).ToArray();
            save.GameVars[AdventureKey] = legacy.Length == 1 ? legacy[0] : Guid.NewGuid().ToString("N");
        }
        if (!save.GameVars.TryGetValue(ProtocolKey, out var protocol) || protocol != ProtocolVersion.ToString()) changed = true;
        save.GameVars[ProtocolKey] = ProtocolVersion.ToString();
        return changed;
    }

    private static void BindSelectedSave()
    {
        ObserveConnection();
        var selected = GameSaveManager.GetNowSave();
        var selectedId = selected?.GameVars != null && selected.GameVars.TryGetValue(AdventureKey, out var id) ? id : "";
        if (selectedId != AdventureId)
        {
            CancelPending();
            State.ClearAdventure();
            RaiseChanged();
        }
        boundSave = selected;
        EnsureCurrentAdventure();
    }

    private static void ObserveConnection()
    {
        object? current = NetworkClient.connection ?? (object?)PlayerManager.Instance;
        if (ReferenceEquals(current, connectionIdentity)) return;
        connectionIdentity = current;
        CancelPending();
        boundSave = null;
        State.ChangeRoom();
        RaiseChanged();
    }

    private static void BeginHostBattle(string fingerprint)
    {
        if (!EnsureCurrentAdventure()) throw new InvalidOperationException("An authoritative save is required before battle initialization.");
        if (++nextBattleEpoch <= 0) nextBattleEpoch = 1;
        State.BindBattle(AdventureId, Guid.NewGuid().ToString("N"), nextBattleEpoch, fingerprint);
        RaiseChanged();
    }

    private static void ReceiveNativeBattle(RemoteCallDelegate original, NetworkBehaviour target, NetworkReader reader, NetworkConnectionToClient sender, bool restart)
    {
        ObserveConnection();
        if (restart)
        {
            // ClearFightui synchronously schedules a fresh Init on the host.
            // Cleaning the old UI must not wait for an identity that Init will replace.
            CancelPending();
            State.EndBattle();
            RaiseChanged();
            original(target, reader, sender);
            return;
        }
        if (NetworkServer.active) { original(target, reader, sender); return; }
        // An optional tool on a guest must not block a vanilla/older host's native battle.
        var save = GameSaveManager.GetNowSave();
        if (!HostSupportsIdentity() || save?.GameVars == null || !save.GameVars.TryGetValue(ProtocolKey, out var protocol) || protocol != ProtocolVersion.ToString())
        {
            State.EndBattle();
            original(target, reader, sender);
            return;
        }
        boundSave = save;
        EnsureCurrentAdventure();
        var position = reader.Position;
        var payload = reader.ReadBytes(new byte[reader.Remaining], reader.Remaining);
        reader.Position = position;
        var fingerprint = Fingerprint(new object[] { reader.ReadString(), reader.ReadBytesAndSize(), reader.ReadBytesAndSize(), reader.ReadFloat(), reader.ReadFloat() });
        reader.Position = position;
        State.EndBattle();
        CancelPending();
        pending = new PendingInitialization(original, target, sender, payload, fingerprint, RoomGeneration);
        SendPending();
    }

    private static string Fingerprint(object[] args)
    {
        if (args.Length != 5) throw new InvalidDataException("Native fight initialization signature changed.");
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
        {
            writer.Write((string)args[0]);
            foreach (var bytes in new[] { (byte[])args[1], (byte[])args[2] }) { writer.Write(bytes.Length); writer.Write(bytes); }
            writer.Write((float)args[3]); writer.Write((float)args[4]);
        }
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-", "").ToLowerInvariant();
    }

    private static void SendPending()
    {
        var value = pending;
        if (value == null || value.Generation != RoomGeneration || PlayerManager.Instance == null) return;
        value.RetryAt = DateTime.UtcNow.AddSeconds(1);
        AuraNativeTargetedQueryTransport.RemovePending(value.Manager, value.QueryId);
        if (!AuraNativeTargetedQueryTransport.TryRegister(value.Manager, new AuraBattleIdentityQuery(), payload =>
            {
                try { var reply = AuraSharedJson.Deserialize<RpcAuraBattleIdentityRequest>(payload); if (reply != null) AcceptIdentity(reply); }
                catch (Exception ex) { Warn("Invalid identity response: " + ex.Message); }
            }, out var queryId, out var error)) { Warn(error); return; }
        value.QueryId = queryId;
        PlayerManager.Instance.SendRpcCommand(new RpcAuraBattleIdentityRequest
        { Nonce = value.Nonce, NativeFingerprint = value.Fingerprint, KnownAdventureId = AdventureId, TargetQueryId = queryId });
    }

    internal static void ResolveIdentity(RpcAuraBattleIdentityRequest request, AuraRpcSender sender)
    {
        request.Accepted = false;
        request.PlayerId = sender.PlayerId;
        if (!sender.IsAvailable || !sender.IsLobbyMember || !IsAuthority || !BattleReady
            || request.Protocol != ProtocolVersion || !AuraNetworkIdentityState.ValidId(request.Nonce)
            || request.NativeFingerprint != State.NativeFingerprint
            || request.KnownAdventureId != AdventureId) return;
        request.AdventureId = AdventureId; request.BattleId = BattleId; request.Epoch = BattleEpoch;
        request.Accepted = true;
    }

    internal static void AcceptIdentity(RpcAuraBattleIdentityRequest reply)
    {
        var value = pending;
        if (value == null || !reply.Accepted || reply.PlayerId != LocalPlayerId
            || reply.Nonce != value.Nonce || reply.NativeFingerprint != value.Fingerprint
            || value.Generation != RoomGeneration || !MatchesAdventure(reply.AdventureId)) return;
        State.BindBattle(reply.AdventureId, reply.BattleId, reply.Epoch, reply.NativeFingerprint);
        CancelPending();
        RaiseChanged();
        using var reader = NetworkReaderPool.Get(value.Payload);
        value.Original(value.Target, reader, value.Sender);
    }

    internal static void Tick()
    {
        ObserveConnection();
        if (pending != null)
        {
            if (pending.Deadline <= DateTime.UtcNow)
            {
                CancelPending();
                const string error = "无法确认房主的战斗身份；本次战斗初始化已终止，请返回大厅后重试。";
                Warn(error);
                SynchronizationFailed?.Invoke(error);
                Witch.UI.UIManager.Instance?.ShowModalWindow("联机同步失败", error);
            }
            else if (pending.RetryAt <= DateTime.UtcNow)
            {
                try { SendPending(); } catch (Exception ex) { Warn("Identity request failed: " + ex.Message); }
            }
        }
        foreach (Action callback in Updating?.GetInvocationList() ?? Array.Empty<Delegate>())
        { try { callback(); } catch (Exception ex) { Warn("Network update failed: " + ex.Message); } }
    }

    private static void RaiseChanged()
    {
        foreach (Action callback in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
        { try { callback(); } catch (Exception ex) { Warn("Identity observer failed: " + ex.Message); } }
    }
    private static void Warn(string message) => AuraSharedLog.Warn("NetworkIdentity", message);

    private static void CancelPending()
    {
        var value = pending;
        pending = null;
        if (value != null) AuraNativeTargetedQueryTransport.RemovePending(value.Manager, value.QueryId);
    }

    private static bool HostSupportsIdentity()
    {
        var host = PlayerManager.Instance?.LobbyInfos?.AddedPlayers?.FirstOrDefault();
        return host?.Mods?.Any(mod => mod != null && mod.Enabled && ProtocolProviders.TryGetValue(mod.ModName, out var minimum)
            && Version.TryParse((mod.ModVersion ?? "").Split('-')[0], out var version) && version >= minimum) == true;
    }

    private sealed class PendingInitialization
    {
        internal readonly RemoteCallDelegate Original;
        internal readonly NetworkBehaviour Target;
        internal readonly NetworkConnectionToClient Sender;
        internal readonly byte[] Payload;
        internal readonly string Fingerprint;
        internal readonly long Generation;
        internal readonly string Nonce = Guid.NewGuid().ToString("N");
        internal readonly PlayerManager Manager = PlayerManager.Instance;
        internal uint QueryId;
        internal readonly DateTime Deadline = DateTime.UtcNow.AddSeconds(15);
        internal DateTime RetryAt;
        internal PendingInitialization(RemoteCallDelegate original, NetworkBehaviour target, NetworkConnectionToClient sender, byte[] payload, string fingerprint, long generation)
        { Original = original; Target = target; Sender = sender; Payload = payload; Fingerprint = fingerprint; Generation = generation; }
    }
}

public sealed class RpcAuraBattleIdentityRequest : RpcCommandBase, IAuraSharedServerBoundRpcCommand
{
    private AuraRpcSender sender = AuraRpcSender.Unbound;
    public int Protocol { get; set; } = AuraNetworkIdentityRuntime.ProtocolVersion;
    public uint TargetQueryId { get; set; }
    public string Nonce { get; set; } = "";
    public string NativeFingerprint { get; set; } = "";
    public string KnownAdventureId { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string AdventureId { get; set; } = "";
    public string BattleId { get; set; } = "";
    public int Epoch { get; set; }
    public bool Accepted { get; set; }
    public void BindServerSender(AuraRpcSender value) => sender = value;
    public override void CmdExecute() => AuraNetworkIdentityRuntime.ResolveIdentity(this, sender);
    public override void RpcExecute() { }
}

public sealed class AuraBattleIdentityQuery : QueryBase<string>
{
    public override void CmdExecute() { Result = ""; }
}

public sealed class AuraNetworkIdentityDriver : MonoBehaviour
{
    private void Update() => AuraNetworkIdentityRuntime.Tick();
}
