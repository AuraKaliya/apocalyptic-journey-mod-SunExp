// Deliberately limited native seams. No game process, network, user saves, or assets are touched.
public sealed class PlayerInfo(string id)
{
    public string Id = id;
    public string Name = id;
}
public sealed class LobbyInfo { public List<PlayerInfo> AddedPlayers = new(); }
public sealed class PlayerManager
{
    public static PlayerManager? Instance;
    public string PlayerId = "";
    public PlayerInfo? playerInfo;
}
public sealed class GameServer
{
    public static GameServer? Instance;
    public LobbyInfo? LobbyInfo;
}
public sealed class TempDataManager { public Dictionary<string, List<string>> RoleStatusMap = new(); }
public static class Singleton<T> where T : new() { public static T Instance = new(); }
public interface IStatusManager
{
    string InstanceId { get; }
    object? MirrorSc { get; }
    void RemoveBuff(string id);
}
public sealed class ScriptExecutor { }
public sealed class FightPlayer { public static FightPlayer? Instance; public IStatusManager? Status; }
namespace Witch { public sealed class NamespaceMarker { } }
namespace Witch.Mod { public sealed class ModConfig { } }
namespace Witch.Core
{
    public sealed class ModHookContext
    {
        public object? Target;
        public object[]? Arguments;
    }
}
namespace AuraShared.Core
{
    public sealed class AuraRoutedHookRequest
    {
        public string OwnerModId = "";
        public string HandlerId = "";
        public Action<Witch.Core.ModHookContext> Handler = null!;
        public bool SafeInvoke;
    }
    public static class AuraSharedHooks
    {
        public static Dictionary<string, Action<Witch.Core.ModHookContext>> Handlers = new();
        public static void RegisterBeforeRouted(Witch.Mod.ModConfig config, string target,
            AuraRoutedHookRequest request, Action<string>? info, Action<string>? warn)
            => Handlers[target] = request.Handler;
    }
}
namespace Terrias.Dll.Infrastructure
{
    public static class TerriasLog { public static void Warn(string value) { } public static void Debug(string value) { } }
    public static class TerriasIds
    {
        public const string PersistentEmber = "PersistentEmber";
        public const string WunaPersistentEmber = "LegacyEmber";
        public const string Ember = "EmberBuff";
    }
    public static class DictionaryUtil
    {
        public static int ParseInt(string value, int fallback = 0) => int.TryParse(value, out var result) ? result : fallback;
    }
}
namespace Terrias.Dll.GameApi
{
    public static class TerriasNetworkQueries
    {
        public static bool NetworkActive() => true;
        public static bool IsServer() => true;
        public static bool IsClientOnly() => false;
        public static string LocalPlayerId() => "host";
        public static bool IsLocalPlayer(string id) => id == LocalPlayerId();
    }
    public static class PlayerApi
    {
        public static readonly Dictionary<string, string> Values = new();
        public static string LocalPlayerStatusId() => "host-status";
        public static string GetGameVar(string key, string fallback) => Values.GetValueOrDefault(key, fallback);
        public static void SetGameVar(string key, string value) => Values[key] = value;
        public static string GetScopedGameVar(string key, IStatusManager? status, string fallback, bool migrateLegacyWhenSolo) => GetGameVar("scoped:" + key, fallback);
        public static void SetScopedGameVar(string key, IStatusManager? status, string value) => SetGameVar("scoped:" + key, value);
    }
    public static class BuffApi
    {
        public static int Level(IStatusManager status, string id) => 0;
        public static void ClearEmberDamageBonus(ScriptExecutor? executor, IStatusManager status) { }
        public static void SetExactLevel(IStatusManager status, string id, int level) { }
        public static void SyncEmberDamageBonus(ScriptExecutor? executor, IStatusManager status) { }
    }
}
namespace Terrias.Dll.Mechanics
{
    public sealed class CompanionEntityIdentity
    {
        public string StatusId = "";
        public string SemanticOwnerPlayerId = "";
        public string SemanticOwnerStatusId = "";
        public string ExecutionRoutePlayerId = "";
        public string RoleId = "";
        public string Faction = "";
        public string EntityKind = "";
        public int SlotIndex;
    }
    public static class CompanionNativeStatusRouting
    {
        public static bool Register(Dictionary<string, List<string>>? map, string owner, string status) => true;
        public static void Remove(Dictionary<string, List<string>> map, string status) { }
    }
}
namespace Terrias.Dll.Network
{
    public sealed class RpcEmberAdventureStateCommit(Terrias.Dll.Mechanics.EmberAdventureStateSnapshot snapshot)
    {
        public bool Accepted;
        public static void ApplyOnServer(Terrias.Dll.Mechanics.EmberAdventureStateSnapshot value, object sender, bool remote) { }
    }
    public static class TerriasNetworkRuntime { public static void Send(object command, string source) { } }
    public static class TerriasRpcAuthorityRuntime { public static object CreateLocalServerSender(string source) => new(); }
}
