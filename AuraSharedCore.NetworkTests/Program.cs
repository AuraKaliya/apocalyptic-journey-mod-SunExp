using AuraShared.Core;
using Data.Save;
using Mirror;

var assertions = 0;
NetworkServer.active = true;
NetworkClient.active = true;
NetworkClient.connection = new object();
Host.FailSave = true;
try { AuraNetworkIdentityRuntime.EnsureCurrentAdventure(); throw new Exception("Expected save failure."); }
catch (IOException) { }
Check(!GameSaveManager.Current.GameVars.ContainsKey(AuraNetworkIdentityRuntime.AdventureKey)
    && AuraNetworkIdentityRuntime.AdventureId == "", "failed identity persistence rolls back allocation before publication");
Host.FailSave = false;
Check(AuraNetworkIdentityRuntime.EnsureCurrentAdventure() && Host.Saves == 1, "identity allocation retries durably after write failure");
var adventure = AuraNetworkIdentityRuntime.AdventureId;
AuraNetworkIdentityRuntime.EnsureCurrentAdventure();
Check(Host.Saves == 1 && adventure == AuraNetworkIdentityRuntime.AdventureId, "stable save identity does not rewrite on every lookup");
AuraNetworkIdentityRuntime.RegisterProtocolProvider("AuraToolsExp", "0.12.0");
AuraNetworkIdentityRuntime.RegisterLegacyAdventureIdKey("legacy-run");
AuraNetworkIdentityRuntime.Initialize(new Witch.Mod.ModConfig());
AuraNetworkIdentityRuntime.Initialize(new Witch.Mod.ModConfig());
Check(UnityEngine.GameObject.Drivers == 1, "repeated consumer initialization owns one driver");

var payload = InitPayload("level-a");
Host.Fire("Before:FightManager.Init", new object[] { "level-a", new byte[] { 1, 2 }, new byte[] { 3 }, 1.5f, 40f });
var battle = AuraNetworkIdentityRuntime.BattleId;
var epoch = AuraNetworkIdentityRuntime.BattleEpoch;
Check(AuraNetworkIdentityRuntime.BattleReady, "host identity exists before native initialization is broadcast");
Host.Fire("Before:FightManager.ClearFightui");
Check(!AuraNetworkIdentityRuntime.BattleReady && AuraNetworkIdentityRuntime.BattleId == battle,
    "restart cleanup invalidates the old battle without minting a temporary battle");
Host.Fire("Before:FightManager.Init", new object[] { "level-a", new byte[] { 1, 2 }, new byte[] { 3 }, 1.5f, 40f });
var restartedBattle = AuraNetworkIdentityRuntime.BattleId;
Check(restartedBattle != battle && AuraNetworkIdentityRuntime.BattleEpoch == epoch + 1, "restart mints identity exactly at fresh native Init");

NetworkServer.active = false;
NetworkClient.connection = new object();
PlayerManager.Instance = new() { PlayerId = "guest" };
PlayerManager.Instance.LobbyInfos.AddedPlayers.Add(new PlayerInfo { Mods = new() { new ModInfo() } });
Host.Fire("After:GameSaveManager.Select");
Host.Receive(Host.Init, payload);
var first = Host.Sent.Last();
Check(Host.NativeInitializations == 0 && !AuraNetworkIdentityRuntime.BattleReady, "guest holds native Init until authoritative reply");
AuraNetworkIdentityRuntime.AcceptIdentity(Reply(first, battle, epoch));
Check(Host.NativeInitializations == 1 && Host.LastNativePayload!.SequenceEqual(payload)
    && AuraNetworkIdentityRuntime.BattleId == battle && AuraNativeTargetedQueryTransport.Pending.Count == 0,
    "confirmation resumes the exact original initialization and releases query");
var requests = Host.Sent.Count;
Host.Receive(Host.Clear, Array.Empty<byte>());
Check(Host.NativeClears == 1 && Host.Sent.Count == requests && !AuraNetworkIdentityRuntime.BattleReady,
    "ClearFightui executes immediately without an identity handshake that Init could supersede");
Host.Receive(Host.Init, payload);
var second = Host.Sent.Last();
AuraNetworkIdentityRuntime.AcceptIdentity(Reply(first, battle, epoch));
Check(Host.NativeInitializations == 1, "old initialization response cannot satisfy restart");
AuraNetworkIdentityRuntime.AcceptIdentity(Reply(second, restartedBattle, epoch + 1));
Check(Host.NativeInitializations == 2 && AuraNetworkIdentityRuntime.BattleId == restartedBattle, "restart adopts host identity before native readiness");

Host.Receive(Host.Init, payload);
var interrupted = Host.Sent.Last();
Host.Receive(Host.Clear, Array.Empty<byte>());
AuraNetworkIdentityRuntime.AcceptIdentity(Reply(interrupted, Guid.NewGuid().ToString("N"), 99));
Check(Host.NativeClears == 2 && Host.NativeInitializations == 2 && AuraNativeTargetedQueryTransport.Pending.Count == 0,
    "cleanup cancels an unfinished initialization and ignores its late response");

var synchronizedSave = GameSaveManager.Current;
GameSaveManager.Current = new();
Host.Fire("After:GameSaveManager.Select");
Check(AuraNetworkIdentityRuntime.AdventureId == "" && AuraNetworkIdentityRuntime.BattleId == "",
    "selecting a save without network identity cannot retain a prior adventure in the same room");
GameSaveManager.Current = synchronizedSave;
Host.Fire("After:GameSaveManager.Select");

Host.Receive(Host.Init, payload);
var oldManager = PlayerManager.Instance;
var oldPending = Host.Sent.Last();
PlayerManager.Instance = new() { PlayerId = "guest" };
NetworkClient.connection = new object();
AuraNativeTargetedQueryTransport.TryRegister(PlayerManager.Instance, new(), _ => { }, out var unrelatedId, out _);
AuraNetworkIdentityRuntime.Tick();
Check(!AuraNativeTargetedQueryTransport.Pending.ContainsKey((oldManager, oldPending.TargetQueryId))
    && AuraNativeTargetedQueryTransport.Pending.ContainsKey((PlayerManager.Instance, unrelatedId)),
    "room replacement releases only callbacks owned by the old manager");
Host.Receive(Host.Init, payload);
Check(Host.NativeInitializations == 3 && Host.Sent.Last() == oldPending, "optional guest on unsupported host retains native battle flow without new RPC types");

NetworkServer.active = true;
NetworkClient.connection = new object();
var legacy = Guid.NewGuid().ToString("N");
GameSaveManager.Current = new();
GameSaveManager.Current.GameVars["legacy-run"] = legacy;
Host.Fire("After:GameSaveManager.Select");
Check(AuraNetworkIdentityRuntime.AdventureId == legacy, "unambiguous legacy run identity migrates to canonical native save key");
Host.Fire("After:GameSaveManager.Select");
Check(AuraNetworkIdentityRuntime.AdventureId == legacy, "normal save selection retains canonical adventure identity");
GameSaveManager.Current = new();
Host.Fire("After:GameSaveManager.Select");
Check(AuraNetworkIdentityRuntime.AdventureId != legacy && !AuraNetworkIdentityRuntime.BattleReady, "new adventure cannot retain old battle identity");
GameSaveManager.Current = new();
Host.FailSave = true;
try { Host.Fire("After:GameSaveManager.Select"); throw new Exception("Expected selected-save failure."); }
catch (IOException) { }
Check(AuraNetworkIdentityRuntime.AdventureId == "" && AuraNetworkIdentityRuntime.BattleId == "",
    "failure to persist a newly selected save cannot expose the previous adventure identity");
Host.FailSave = false;
Check(AuraNetworkIdentityRuntime.EnsureCurrentAdventure() && Host.Saves > 1, "selected adventure identity can recover after a persistence failure");
Console.WriteLine($"Shared native identity tests passed: {assertions} assertions.");

void Check(bool value, string name) { if (!value) throw new Exception(name); assertions++; }
RpcAuraBattleIdentityRequest Reply(RpcAuraBattleIdentityRequest request, string id, int number) => new()
{
    Accepted = true, PlayerId = "guest", Nonce = request.Nonce, NativeFingerprint = request.NativeFingerprint,
    AdventureId = adventure, BattleId = id, Epoch = number
};
byte[] InitPayload(string level)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(level); writer.Write(2); writer.Write(new byte[] { 1, 2 }); writer.Write(1); writer.Write(new byte[] { 3 }); writer.Write(1.5f); writer.Write(40f);
    return stream.ToArray();
}
