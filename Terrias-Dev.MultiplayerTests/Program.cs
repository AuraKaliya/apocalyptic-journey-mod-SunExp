using AuraShared.Core;
using Data.Save;
using Mirror;
using Mirror.RemoteCalls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terrias.Dll.Application;
using Terrias.Dll.Contracts;
using Terrias.Dll.Infrastructure;

var count = 0;
var baseRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AuraMultiplayer.Tests"));
var root = Path.Combine(baseRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); Host.Root = root; AuraSharedPaths.RootDirectory = root;
try
{
    var hostConnection = Connect(1, "host"); var guestConnection = Connect(2, "guest");
    NetworkServer.active = true; NetworkServer.localConnection = hostConnection;
    PlayerManager.Instance = hostConnection.identity!.Player;
    GameServer.Instance.LobbyInfo.AddedPlayers.AddRange(new[] { new PlayerInfo("host") { Connection = hostConnection }, new PlayerInfo("guest") { Connection = guestConnection } });
    AuraRpcAdmission.Register<ProbeCommand>(AuraRpcPublication.MemberRequest);
    AuraRpcAdmission.Register<HostOnlyProbe>(AuraRpcPublication.HostOnly);
    AuraRpcAuthorityRuntime.Register(new Witch.Mod.ModConfig(), "probe", value => value is ProbeCommand,
        (value, sender) => ((ProbeCommand)value).Sender = sender);
    Host.OnDispatch = command => Check(command.Sender.PlayerId == "guest" && !command.Sender.IsLobbyHost, "actual guest connection is bound even when invoking host object");
    Invoke(new ProbeCommand { Owner = "guest" }, guestConnection, PlayerManager.Instance!);
    var before = Host.Dispatches;
    Invoke(new HostOnlyProbe(), guestConnection, PlayerManager.Instance!);
    Check(Host.Dispatches == before, "host-only publication is stopped before native dispatch/broadcast");
    Host.OnDispatch = _ => throw new InvalidOperationException("injected handler failure");
    try { Invoke(new ProbeCommand(), guestConnection, PlayerManager.Instance!); } catch (InvalidOperationException) { }
    Check(!AuraRpcReceiveContext.Sender.IsAvailable, "native exception cannot leak sender to a later request");
    NetworkServer.connections.Remove(2);
    before = Host.Dispatches; Invoke(new ProbeCommand(), guestConnection, PlayerManager.Instance!);
    Check(before == Host.Dispatches, "disconnected sender objects cannot retain authority");
    NetworkServer.connections[2] = guestConnection; Host.OnDispatch = null;
    var nativeMethod = "System.Void PlayerManager::CmdReceiveRpcCommand(Network.Command.RpcCommandBase)";
    RemoteProcedureCalls.Register(nativeMethod, (target, reader, sender) => Host.Dispatch(target, reader, "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase"));
    AuraNativeRpcReceiveAdapter.Install();
    before = Host.Dispatches; Invoke(new HostOnlyProbe(), guestConnection, PlayerManager.Instance!);
    Check(before == Host.Dispatches, "re-registration cannot leave a new native invoker without admission");
    var queuedMethod = "System.Void FightManager::ReadyToInit(System.String)";
    var nativeStarts = 0;
    RemoteProcedureCalls.Register(queuedMethod, (_, reader, _) => { reader.ReadString(); nativeStarts++; });
    var permitStart = false;
    AuraNativeRpcReceiveAdapter.RegisterCommandGuard("validation", queuedMethod, (_, reader, _) => { reader.ReadString(); return permitStart; });
    var startHandler = RemoteProcedureCalls.GetDelegate(unchecked((ushort)queuedMethod.GetStableHashCode()))!;
    startHandler(PlayerManager.Instance!, new NetworkReader("level"), hostConnection);
    Check(nativeStarts == 0, "cancelled native initialization is rejected before modifying readiness counters");
    permitStart = true;
    startHandler(PlayerManager.Instance!, new NetworkReader("level"), hostConnection);
    Check(nativeStarts == 1, "allowed initialization preserves the original reader position");

    Host.Authority = true;
    var adventure = Guid.NewGuid().ToString("N"); AuraNetworkIdentityRuntime.Change(adventure);
    GameSaveManager.Current.GameVars[TerriasIds.SolarMemoryModeKey] = "1";
    var sender = new TerriasRpcSender("guest", "", true, false, "native", true);
    var record = Prepared(adventure, 17);
    Check(SolarMemoryRoleCommitApplication.Resolve(record, sender, true, 2, out _) == "NotFound", "status query does not commit");
    Check(SolarMemoryRoleCommitApplication.Resolve(record, sender, false, 2, out _) == "Accepted", "role and receipt commit together");
    GameSaveManager.Current = JsonConvert.DeserializeObject<SaveInfo>(File.ReadAllText(Path.Combine(root, "native-save.json")))!;
    GameSaveManager.Current.roleTable["guest"].Progress = 99;
    var saves = Host.Saves;
    Check(SolarMemoryRoleCommitApplication.Resolve(record, sender, false, 2, out _) == "Accepted"
        && GameSaveManager.Current.roleTable["guest"].Progress == 99 && Host.Saves == saves, "durable duplicate receipt survives reload without overwriting later role progress");
    record.PayloadHash = AuraVersionedReceipt.Hash("changed");
    Check(SolarMemoryRoleCommitApplication.Resolve(record, sender, false, 2, out _) == "Rejected", "same token with changed content is rejected");

    adventure = Guid.NewGuid().ToString("N"); AuraNetworkIdentityRuntime.Change(adventure);
    GameSaveManager.Current = new(); GameSaveManager.Current.GameVars[TerriasIds.SolarMemoryModeKey] = "1";
    GameSaveManager.Current.roleTable["guest"] = new() { Id = "guest", Progress = 5 };
    GameServer.Instance.RoleTables["guest"] = new() { Id = "guest", Progress = 5 };
    Host.FailSave = true;
    Check(SolarMemoryRoleCommitApplication.Resolve(Prepared(adventure, 42), sender, false, 2, out _) == "Retry"
        && GameSaveManager.Current.roleTable["guest"].Progress == 5 && GameServer.Instance.RoleTables["guest"].Progress == 5,
        "failed native persistence rolls back both authoritative role dictionaries");
    Host.FailSave = false;
    GameSaveManager.Current.GameVars[TerriasIds.PersistentEmber + "_Owner_guest"] = "9";
    var request = new EmberUpdateRequest { AdventureId = adventure, PlayerId = "guest", RequestId = Guid.NewGuid().ToString("N"), Query = true };
    var result = EmberAdventureStateService.Resolve(request, sender);
    Check(result.Status == "Snapshot" && result.Snapshot.Level == 9, "owner-scoped legacy ember is preserved");
    request.Query = false; request.Level = 10;
    Check(EmberAdventureStateService.Resolve(request, sender).Snapshot.Version == 1, "host assigns persisted version");
    Check(EmberAdventureStateService.Resolve(request, sender).Status == "Duplicate", "ember retransmission is idempotent");
    request = new EmberUpdateRequest { AdventureId = adventure, PlayerId = "guest", RequestId = Guid.NewGuid().ToString("N"), Level = 11, ExpectedVersion = 0 };
    Check(EmberAdventureStateService.Resolve(request, sender).Status == "Conflict", "restarted client cannot overwrite a newer version");
    request.ExpectedVersion = 1;
    Host.FailSave = true;
    Check(EmberAdventureStateService.Resolve(request, sender).Status == "Retry", "persistence failure is retryable, not falsely accepted");
    Host.FailSave = false;
    request.Query = true;
    result = EmberAdventureStateService.Resolve(request, sender);
    Check(result.Snapshot.Level == 10 && result.Snapshot.Version == 1, "ember value and version roll back together");

    Host.Authority = false;
    var oldAdventure = Guid.NewGuid().ToString("N"); AuraNetworkIdentityRuntime.Change(oldAdventure);
    RoleTable.Instance = new() { Id = "guest" }; RoleTable.Instance.SpecialVarMap[TerriasIds.SolarMemorySetupFinishedKey] = "1";
    SolarMemoryCommitRecord? sent = null;
    SolarMemoryRoleCommitApplication.Send = (value, query) => { sent = value; return true; };
    var callbacks = 0;
    Check(SolarMemoryRoleCommitApplication.SubmitFinal(RoleTable.Instance, "test", (_, _) => callbacks++) == SolarMemoryRoleCommitSubmission.Pending, "unacknowledged client commit remains pending");
    var oldSent = sent!;
    var newAdventure = Guid.NewGuid().ToString("N"); AuraNetworkIdentityRuntime.Change(newAdventure);
    RoleTable.Instance = new() { Id = "guest" }; RoleTable.Instance.SpecialVarMap[TerriasIds.SolarMemorySetupFinishedKey] = "1";
    Check(SolarMemoryRoleCommitApplication.SubmitFinal(RoleTable.Instance, "new", (_, _) => callbacks++) == SolarMemoryRoleCommitSubmission.Pending,
        "old pending transaction cannot block a new adventure");
    SolarMemoryRoleCommitApplication.ReceiveAuthoritativeResult(oldAdventure, "guest", oldSent.Token, oldSent.PayloadHash, "Accepted", "");
    Check(callbacks == 0 && !SolarMemoryRoleCommitApplication.IsConfirmedForCurrentRole, "late reply cannot complete another adventure or call stale UI");
    Check(Directory.GetFiles(Path.Combine(root, "Data", "Owners", "Terrias", "SolarCommits"), "*.json").Length == 2,
        "each adventure retains its own recoverable outbox");
    var retainedPath = Path.Combine(root, "Data", "Owners", "Terrias", "SolarCommits", AuraVersionedReceipt.Hash(oldAdventure + ":guest") + ".json");
    var retained = JsonConvert.DeserializeObject<SolarMemoryCommitRecord>(File.ReadAllText(retainedPath))!;
    Host.Authority = true; AuraNetworkIdentityRuntime.AdventureId = oldAdventure;
    GameSaveManager.Current = new(); GameSaveManager.Current.GameVars[TerriasIds.SolarMemoryModeKey] = "1";
    Check(SolarMemoryRoleCommitApplication.Resolve(retained, sender, false, 2, out _) == "Accepted", "host may commit while its reply is lost");
    GameSaveManager.Current.roleTable["guest"].Progress = 77;
    var savedCount = Host.Saves;
    Host.Authority = false; RoleTable.Instance = new() { Id = "guest", Progress = 77 };
    SolarMemoryRoleCommitApplication.Send = (value, query) =>
    {
        Check(query && value.Token == retained.Token, "recovery first queries the original durable transaction");
        Host.Authority = true;
        var status = SolarMemoryRoleCommitApplication.Resolve(value, sender, query, 2, out var reason);
        Host.Authority = false;
        SolarMemoryRoleCommitApplication.ReceiveAuthoritativeResult(value.AdventureId, value.PlayerId, value.Token, value.PayloadHash, status, reason);
        return true;
    };
    AuraNetworkIdentityRuntime.Change(oldAdventure); AuraNetworkIdentityRuntime.Tick();
    Check(SolarMemoryRoleCommitApplication.IsConfirmedForCurrentRole && !File.Exists(retainedPath)
        && Host.Saves == savedCount && GameSaveManager.Current.roleTable["guest"].Progress == 77,
        "lost acknowledgement recovery completes without repeating the role write");
    Console.WriteLine("Multiplayer receive and transaction tests passed: " + count + " assertions.");
}
finally
{
    if (!Path.GetFullPath(root).StartsWith(baseRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
    Directory.Delete(root, true);
}

void Check(bool value, string message) { count++; if (!value) throw new InvalidOperationException(message); }
NetworkConnectionToClient Connect(int id, string player)
{
    var connection = new NetworkConnectionToClient(id);
    var manager = new PlayerManager { PlayerId = player, playerInfo = new PlayerInfo(player), connectionToClient = connection };
    connection.identity = new NetworkIdentity { Player = manager }; NetworkServer.connections[id] = connection; return connection;
}
void Invoke(object value, NetworkConnectionToClient sender, PlayerManager target)
{
    var method = "System.Void PlayerManager::CmdReceiveRpcCommand(Network.Command.RpcCommandBase)";
    var callback = RemoteProcedureCalls.GetDelegate(unchecked((ushort)method.GetStableHashCode()))!;
    callback(target, new NetworkReader(JsonConvert.SerializeObject(value, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All })), sender);
}
SolarMemoryCommitRecord Prepared(string adventure, int progress)
{
    var token = Guid.NewGuid().ToString("N"); var role = new RoleTable { Id = "guest", Progress = progress };
    role.SpecialVarMap[TerriasIds.SolarMemorySetupFinishedKey] = "1"; role.SpecialVarMap[TerriasIds.SolarMemorySetupCommitTokenKey] = token;
    var json = JsonConvert.SerializeObject(role);
    return new() { AdventureId = adventure, PlayerId = "guest", Token = token, PayloadHash = AuraVersionedReceipt.Hash(json), Payload = AuraBoundedCompressedJson.Encode(json, 40000) };
}
