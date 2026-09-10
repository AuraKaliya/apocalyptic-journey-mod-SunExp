using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Network.Command;

public sealed class PlayerManager { public static PlayerManager Instance = new(); }
public sealed class GameServer
{
    public static GameServer Instance = new();
    public LobbyInfo LobbyInfo = new();
}
public sealed class LobbyInfo { public List<PlayerEntry> AddedPlayers = new(); }
public sealed class PlayerEntry { public string Id = ""; }
namespace Network.Command
{
    public abstract class RpcCommandBase { public virtual void CmdExecute() { } public abstract void RpcExecute(); }
}
namespace AuraShared.Core
{
    public static class AuraNetworkIdentityRuntime
    {
        public static string AdventureId = Guid.NewGuid().ToString("N");
        public static long RoomGeneration;
        public static bool EnsureCurrentAdventure() => true;
    }
    public static class AuraBattleLifecycleRouter { public static long CurrentBattleSessionId; }
}
namespace AuraToolsExp.Dll.GameApi
{
    internal static class AuraToolsNetworkSession
    {
        internal static bool NetworkActive = true;
        internal static bool IsAuthority = true;
        internal static string LocalPlayerId = "host";
        internal static string[] PlayerIds = new[] { "host", "guest" };
    }
}
namespace AuraToolsExp.Dll.Infrastructure
{
    public static class AuraToolsRpcTransport
    {
        public static bool Send(PlayerManager? manager, RpcCommandBase command, string source)
        {
            if (command is IAuraToolsServerBoundRpcCommand bound)
                bound.BindServerSender(new AuraToolsRpcSender("host", "", true, true, "test", true));
            command.CmdExecute(); command.RpcExecute(); return true;
        }
    }
}
namespace AuraToolsExp.Dll.Features.DamageMeter
{
    public static class AuraToolsDamageMeterRuntime
    {
        internal static DamageLedger Ledger => DamageMeterNetworkRuntime.Ledger;
        internal static void NotifyLedgerChanged() { }
    }
    internal static class DamageMeterPerformanceCounters
    {
        internal static long StartSample() => 0;
        internal static double ElapsedMs(long stamp) => 0;
        internal static void RecordSubmitted(bool localApplied) { }
        internal static void RecordSnapshot(double milliseconds, int before, int after, bool compacted) { }
    }
}
namespace AuraToolsExp.Dll.Features.DamageMeter.Resolution
{
    internal static class CombatantTeamResolver
    {
        internal static object? ResolveStatus(string id) => null;
        internal static (string InstanceId, string DisplayName, DamageTeam Team) ResolveAttribution(object value, string id, string name) => (id, name, DamageTeam.Friendly);
    }
}
namespace AuraToolsExp.Dll.Features.DamageMeter.Network
{
}
namespace AuraToolsExp.Dll.Features.DamageMeter.Storage
{
    internal static class DamageHistoryStorage
    {
        internal static DamageHistoryDatabase Database = null!;
        internal static void EnsureLegacyMigrations() { }
    }
}
