using AuraShared.Core;
using Mirror;
using Mirror.RemoteCalls;
using Network.Command;
using Witch.Core;

// Host seams only: the production identity runtime and state run unchanged.
// These tests do not represent real Mirror delivery or Unity execution.
internal static class Host
{
    internal const string Init = "System.Void FightManager::Init(System.String,System.Byte[],System.Byte[],System.Single,System.Single)";
    internal const string Clear = "System.Void FightManager::ClearFightui()";
    internal static int NativeInitializations, NativeClears, Saves;
    internal static bool FailSave;
    internal static readonly Dictionary<string, Action<ModHookContext>> Hooks = new();
    internal static readonly Dictionary<string, Action<RemoteCallDelegate, NetworkBehaviour, NetworkReader, NetworkConnectionToClient>> Receivers = new();
    internal static readonly List<RpcAuraBattleIdentityRequest> Sent = new();
    internal static byte[]? LastNativePayload;
    internal static void Receive(string method, byte[] payload)
    {
        using var reader = new NetworkReader(payload);
        RemoteCallDelegate original = (_, input, _) =>
        {
            if (method == Clear) NativeClears++;
            else { NativeInitializations++; LastNativePayload = input.ReadBytes(new byte[input.Remaining], input.Remaining); }
        };
        Receivers[method](original, new FightManager(), reader, new NetworkConnectionToClient(0));
    }
    internal static void Fire(string method, object[]? args = null) => Hooks[method](new ModHookContext { Arguments = args });
}
public sealed class PlayerManager
{
    public static PlayerManager Instance = new();
    public string PlayerId = "host";
    public LobbyInfo LobbyInfos = new();
    public void SendRpcCommand(RpcCommandBase command) => Host.Sent.Add((RpcAuraBattleIdentityRequest)command);
}
public sealed class LobbyInfo { public List<PlayerInfo> AddedPlayers = new(); }
public sealed class PlayerInfo { public List<ModInfo> Mods = new(); }
public sealed class ModInfo { public bool Enabled = true; public string ModName = "AuraToolsExp"; public string ModVersion = "0.12.0"; }
public sealed class FightManager : NetworkBehaviour { }
namespace Data.Save
{
    public sealed class SaveInfo { public Dictionary<string, string> GameVars = new(); }
    public static class GameSaveManager { public static SaveInfo Current = new(); public static SaveInfo GetNowSave() => Current; }
}
namespace Witch.Mod { public sealed class ModConfig { } }
namespace Witch.Core { public sealed class ModHookContext { public object[]? Arguments; } }
namespace Witch.UI
{
    public sealed class UIManager { public static UIManager Instance = new(); public void ShowModalWindow(string title, string text) { } }
}
namespace Network.Command { public class RpcCommandBase { public virtual void CmdExecute() { } public virtual void RpcExecute() { } } }
namespace Network.Query { public abstract class QueryBase<T> { public T? Result; public abstract void CmdExecute(); } }
namespace UnityEngine
{
    public class Object { public static void DontDestroyOnLoad(Object value) { } }
    public class MonoBehaviour { }
    public sealed class GameObject : Object { public GameObject(string name) { } public static int Drivers; public T AddComponent<T>() where T : new() { Drivers++; return new(); } }
}
namespace Mirror
{
    public class NetworkBehaviour { }
    public sealed class NetworkConnectionToClient(int id) { public int connectionId = id; }
    public static class NetworkServer { public static bool active; public static Dictionary<int, NetworkConnectionToClient> connections = new(); }
    public static class NetworkClient { public static bool active; public static object? connection; }
    public sealed class NetworkReader(byte[] bytes) : IDisposable
    {
        private readonly BinaryReader reader = new(new MemoryStream(bytes));
        public int Position { get => (int)reader.BaseStream.Position; set => reader.BaseStream.Position = value; }
        public int Remaining => (int)(reader.BaseStream.Length - reader.BaseStream.Position);
        public string ReadString() => reader.ReadString();
        public byte[] ReadBytesAndSize() => reader.ReadBytes(reader.ReadInt32());
        public float ReadFloat() => reader.ReadSingle();
        public byte[] ReadBytes(byte[] buffer, int count) { var read = reader.Read(buffer, 0, count); if (read != count) throw new EndOfStreamException(); return buffer; }
        public void Dispose() => reader.Dispose();
    }
    public static class NetworkReaderPool { public static NetworkReader Get(byte[] bytes) => new(bytes); }
}
namespace Mirror.RemoteCalls { public delegate void RemoteCallDelegate(NetworkBehaviour target, NetworkReader reader, NetworkConnectionToClient sender); }
namespace AuraShared.Core
{
    public sealed class AuraHookRegistry
    {
        public AuraHookRegistry(Witch.Mod.ModConfig config, string owner, Action<string>? info, Action<string>? warn) { }
        public void BeforeRouted(string method, Action<ModHookContext> callback, string id) => Host.Hooks["Before:" + method] = callback;
        public void AfterRouted(string method, Action<ModHookContext> callback, string id) => Host.Hooks["After:" + method] = callback;
    }
    public static class AuraRpcAuthorityRuntime
    {
        public static void Register(Witch.Mod.ModConfig config, string owner, Func<object, bool> select, Action<object, AuraRpcSender> bind) { }
    }
    public static class AuraNativeRpcReceiveAdapter
    {
        internal static void InstallDelegate(string method, Action<RemoteCallDelegate, NetworkBehaviour, NetworkReader, NetworkConnectionToClient> receive) => Host.Receivers[method] = receive;
    }
    public static class AuraNativeSaveStore
    {
        public static void Commit(Data.Save.SaveInfo save) { if (Host.FailSave) throw new IOException("injected persistence failure"); Host.Saves++; }
    }
    public static class AuraSharedLog { public static void Warn(string area, string text) { } }
    public static class AuraNativeTargetedQueryTransport
    {
        private static uint next;
        internal static readonly Dictionary<(PlayerManager Owner, uint Id), Action<string>> Pending = new();
        public static bool TryRegister(PlayerManager owner, AuraBattleIdentityQuery query, Action<string> callback, out uint id, out string error)
        { id = ++next; Pending[(owner, id)] = callback; error = ""; return true; }
        public static void RemovePending(PlayerManager owner, uint id) => Pending.Remove((owner, id));
        public static bool TrySend(NetworkConnectionToClient connection, uint id, AuraBattleIdentityQuery query, out string error) { error = ""; return true; }
    }
}
