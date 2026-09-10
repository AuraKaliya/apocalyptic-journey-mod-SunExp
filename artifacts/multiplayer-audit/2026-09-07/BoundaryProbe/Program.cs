using System.Text.Json;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

// Diagnostic probes: production classes are linked, native APIs are stubs.
// These reproduce boundary behavior; they do not exercise Mirror or Unity.
if (args.Length == 2 && args[0] == "epoch")
{
    var priorBattles = int.Parse(args[1]);
    for (var index = 0; index < priorBattles; index++)
    {
        CompanionAuthorityService.BeginBattleEpoch();
        CompanionAuthorityService.InvalidateBattleEpoch();
    }
    CompanionAuthorityService.BeginBattleEpoch();
    Print(new { Probe = "process-battle-history", PriorBattles = priorBattles,
        CurrentEpoch = CompanionAuthorityService.BattleEpoch });
    return;
}

GameServer.Instance = new GameServer
{
    LobbyInfo = new LobbyInfo
    {
        AddedPlayers = new() { new PlayerInfo("host"), new PlayerInfo("guest") }
    }
};
var hostTarget = new PlayerManager { PlayerId = "host", playerInfo = new PlayerInfo("host") };
PlayerManager.Instance = hostTarget;
AuraRpcSender? receivedSender = null;
var command = new object();
AuraRpcAuthorityRuntime.Register(new ModConfig(), "probe", item => ReferenceEquals(item, command),
    (_, sender) => receivedSender = sender);
// Matching native decompile discards senderConnection and passes only obj/command.
AuraSharedHooks.Handlers[AuraRpcAuthorityRuntime.DefaultReceiveHookTargets[0]](
    new ModHookContext { Target = hostTarget, Arguments = new[] { command } });
Require(receivedSender?.PlayerId == "host" && receivedSender.IsLobbyHost,
    "The receiver derives authority from the invoked object.");
Print(new { Probe = "rpc-target-binding", NativeConnectionPlayer = "guest",
    InvokedTargetPlayer = "host", BoundPlayer = receivedSender!.PlayerId,
    GrantedHostAuthority = receivedSender.IsLobbyHost });

var ledger = new OlimyaGoldenizationLedger();
ledger.Reset(3); // `epoch 1`: one previous fight plus the current fight.
var accepted = ledger.TryAccept(new OlimyaGoldenizationCommand
{
    BattleEpoch = 1, // `epoch 0`: fresh process entering the same current fight.
    Kind = OlimyaGoldenizationCommandKind.Apply,
    OwnerStatusId = "guest", TargetStatusId = "enemy",
    Token = Guid.NewGuid().ToString("N"), Sequence = 1
}, senderOwnsStatus: true);
Require(!accepted, "Unequal process histories reject an otherwise valid goldenization command.");
Print(new { Probe = "companion-epoch-command", HostEpoch = 3, GuestEpoch = 1, Accepted = accepted });

var pending = new SolarMemoryRoleCommitPendingState();
Require(pending.TryBegin("guest", "old-run"), "Initial pending commit should begin.");
var newRunAccepted = pending.TryBegin("guest", "new-run");
var wrongCancelCleared = pending.Cancel("guest", "new-run");
Require(!newRunAccepted && !wrongCancelCleared && pending.IsPending("guest", "old-run"),
    "An unacknowledged old commit keeps a new run blocked.");
Print(new { Probe = "solar-pending-new-run", NewRunAccepted = newRunAccepted,
    NewRunCancelClearedOld = wrongCancelCleared, OldStillPending = pending.IsPending("guest", "old-run") });

Require(EmberAdventureStateService.ApplySnapshot(new EmberAdventureStateSnapshot
{
    OwnerPlayerId = "guest", OwnerStatusId = "guest-status", Level = 20, Sequence = 20
}, "old-session"), "Old peer session should establish state.");
// A new adventure clears save data but does not clear the service's static sequence dictionary.
PlayerApi.Values.Clear();
var newEmberAccepted = EmberAdventureStateService.ApplySnapshot(new EmberAdventureStateSnapshot
{
    OwnerPlayerId = "guest", OwnerStatusId = "guest-status", Level = 1, Sequence = 1
}, "guest-restarted-new-session");
Require(!newEmberAccepted && PlayerApi.Values.Count == 0,
    "A restarted peer's valid low sequence is rejected after prior session state.");
Print(new { Probe = "ember-peer-restart", PreviousSequence = 20, NewSequence = 1,
    Accepted = newEmberAccepted, PersistedValues = PlayerApi.Values.Count });

static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
