using AuraShared.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Mirror;
using Mirror.RemoteCalls;

internal static class Host
{
    internal static bool Authority;
    internal static bool FailSave;
    internal static int Saves;
    internal static int Dispatches;
    internal static Action<ProbeCommand>? OnDispatch;
    internal static string Root = "";
    internal static void Persist(Data.Save.SaveInfo save)
    {
        if (FailSave) throw new IOException("injected save failure");
        AuraSharedFileStore.WriteAllText("test", Path.Combine(Root, "native-save.json"), JsonConvert.SerializeObject(save));
        Saves++;
    }
    internal static void Dispatch(NetworkBehaviour target, NetworkReader reader, string hook)
    {
        Dispatches++;
        var command = JObject.Parse(reader.ReadString()).ToObject<ProbeCommand>()!;
        AuraSharedHooks.Fire(hook, new Witch.Core.ModHookContext { Target = target, Arguments = new object[] { command } });
        OnDispatch?.Invoke(command);
    }
}
public sealed class ProbeCommand { public string Owner { get; set; } = ""; [JsonIgnore] public AuraRpcSender Sender = AuraRpcSender.Unbound; }
public sealed class HostOnlyProbe { }
public sealed class PlayerInfo(string id) { public string Id = id; public string Name = id; public NetworkConnectionToClient? Connection; }
public sealed class LobbyInfo { public List<PlayerInfo> AddedPlayers = new(); }
public sealed class PlayerManager : NetworkBehaviour
{
    public static PlayerManager? Instance;
    public string PlayerId = "";
    public PlayerInfo? playerInfo;
    static PlayerManager()
    {
        foreach (var method in new[] { "CmdReceiveRpcCommand", "CmdReceiveRpcCommandExcludeOwner", "RpcReceiveRpcCommand", "RpcReceiveRpcCommandExcludeOwner" })
        {
            var hook = "PlayerManager.UserCode_" + method + "__RpcCommandBase";
            RemoteProcedureCalls.Register("System.Void PlayerManager::" + method + "(Network.Command.RpcCommandBase)",
                (obj, reader, sender) => Host.Dispatch(obj, reader, hook));
        }
    }
}
public sealed class GameServer { public static GameServer Instance = new(); public LobbyInfo LobbyInfo = new(); public Dictionary<string, RoleTable> RoleTables = new(); }
public sealed class RoleTable
{
    public static RoleTable Instance = new();
    public string Id = "guest";
    public int Progress;
    public Dictionary<string, string> SpecialVarMap = new();
}
public interface IStatusManager { string InstanceId { get; } object? MirrorSc { get; } void RemoveBuff(string id); }
public sealed class ScriptExecutor { }
public sealed class FightPlayer { public static FightPlayer? Instance; public IStatusManager? Status; }
namespace Witch { public class Marker { } }
namespace Witch.Mod { public sealed class ModConfig { } }
namespace Witch.Core { public sealed class ModHookContext { public object? Target; public object[]? Arguments; } }
namespace Data.Save
{
    public sealed class SaveInfo { public Dictionary<string, string> GameVars = new(); public Dictionary<string, RoleTable> roleTable = new(); }
    public static class GameSaveManager
    {
        public static SaveInfo Current = new();
        public static SaveInfo GetNowSave() => Current;
        public static T? GetValue<T>(string key) => Current.GameVars.TryGetValue(key, out var value) ? (T)Convert.ChangeType(value, typeof(T)) : default;
        public static void UpdateRoles(RoleTable role) => Current.roleTable[role.Id] = role;
    }
}
namespace Mirror
{
    public class NetworkBehaviour { public NetworkConnectionToClient? connectionToClient; }
    public sealed class NetworkIdentity
    {
        public PlayerManager? Player;
        public T? GetComponent<T>() where T : class => Player as T;
    }
    public sealed class NetworkConnectionToClient(int id) { public int connectionId = id; public NetworkIdentity? identity; }
    public sealed class NetworkReader(string json)
    {
        public int Position;
        public string ReadString() { if (Position != 0) throw new InvalidOperationException("Reader was not rewound."); Position = json.Length; return json; }
    }
    public static class NetworkServer { public static bool active; public static Dictionary<int, NetworkConnectionToClient> connections = new(); public static NetworkConnectionToClient? localConnection; }
    public static class NetworkClient { public static bool active; }
    public static class HashExtensions { public static int GetStableHashCode(this string text) { unchecked { var hash = 23; foreach (var ch in text) hash = hash * 31 + ch; return hash; } } }
}
namespace Mirror.RemoteCalls
{
    public delegate void RemoteCallDelegate(NetworkBehaviour obj, NetworkReader reader, NetworkConnectionToClient senderConnection);
    public static class RemoteProcedureCalls
    {
        private sealed class Invoker { public RemoteCallDelegate function = null!; }
        private static readonly Dictionary<ushort, Invoker> remoteCallDelegates = new();
        public static void Register(string method, RemoteCallDelegate callback) => remoteCallDelegates[unchecked((ushort)method.GetStableHashCode())] = new() { function = callback };
        public static RemoteCallDelegate? GetDelegate(ushort hash) => remoteCallDelegates.TryGetValue(hash, out var entry) ? entry.function : null;
    }
}
namespace AuraShared.Core
{
    public sealed class AuraRoutedHookRequest { public string OwnerModId = ""; public string HandlerId = ""; public bool SafeInvoke; public Action<Witch.Core.ModHookContext> Handler = null!; }
    public static class AuraSharedHooks
    {
        private static readonly Dictionary<string, Dictionary<string, Action<Witch.Core.ModHookContext>>> hooks = new();
        public static void RegisterBeforeRouted(Witch.Mod.ModConfig config, string target, AuraRoutedHookRequest request, Action<string>? info, Action<string>? warn)
        {
            if (!hooks.TryGetValue(target, out var handlers)) hooks[target] = handlers = new();
            handlers[request.OwnerModId] = request.Handler;
        }
        public static void Fire(string target, Witch.Core.ModHookContext context) { if (hooks.TryGetValue(target, out var handlers)) foreach (var action in handlers.Values) action(context); }
    }
    public static class AuraSharedLog { public static void Warn(string owner, string message) { } }
    public static class AuraNativeSaveStore { public static void Commit(Data.Save.SaveInfo save) => Host.Persist(save); }
    public static class AuraNetworkIdentityRuntime
    {
        public static string AdventureId = "";
        public static string BattleId = "battle";
        public static long RoomGeneration;
        public static bool IsAuthority => Host.Authority;
        public static string LocalPlayerId = "guest";
        public static event Action? Changed;
        public static event Action? Updating;
        public static bool EnsureCurrentAdventure() => AuraNetworkIdentityState.ValidId(AdventureId);
        public static bool MatchesAdventure(string id) => id == AdventureId;
        public static bool MatchesBattle(string id) => id == BattleId;
        public static bool MatchesBattleResult(string id) => id == BattleId;
        public static void Change(string id) { AdventureId = id; RoomGeneration++; Changed?.Invoke(); }
        public static void Tick() => Updating?.Invoke();
    }
}
namespace Terrias.Dll.Infrastructure
{
    public static class TerriasLog { public static void Warn(string message) { } public static void Error(string message, Exception exception) { } }
    public static class DictionaryUtil { public static int ParseInt(string value) => int.TryParse(value, out var result) ? result : 0; }
}
namespace Terrias.Dll.GameApi
{
    public static class PlayerApi
    {
        public static void ShowCaption(string text) { }
        public static string LocalPlayerStatusId() => "guest";
        public static string GetGameVar(string key, string fallback) => Data.Save.GameSaveManager.Current.GameVars.GetValueOrDefault(key, fallback);
        public static string GetScopedGameVar(string key, IStatusManager? status, string fallback, bool migrateLegacyWhenSolo) => GetGameVar(key, fallback);
    }
    public static class BuffApi
    {
        public static void ClearEmberDamageBonus(ScriptExecutor? executor, IStatusManager status) { }
        public static int Level(IStatusManager status, string id) => 0;
        public static void SetExactLevel(IStatusManager status, string id, int level) { }
        public static void SyncEmberDamageBonus(ScriptExecutor? executor, IStatusManager status) { }
    }
}
