using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using AuraSkin.Shared.Models;
using Newtonsoft.Json;
internal static partial class AuraToolsTestSuite
{
    public static void TestRoundAndDpt()
    {
        var ledger = NewLedger();
        ledger.StartRound(1);
        Apply(ledger, 1, "p1", 70, 30, DamageTeam.Friendly, "card_a");
        Assert(ledger.CurrentRoundIndex == 1, "round one starts");
        var p1 = ledger.Combatants.Single();
        Assert(p1.DisplayCurrentRound(true) == 100, "round damage includes shield");
    
        ledger.StartRound(2);
        Assert(ledger.CompletedRoundCount == 1, "previous round closes once");
        Assert(p1.DisplayCurrentRound(true) == 0, "new round resets current counters");
        Assert(p1.Rounds.Count == 1 && p1.Rounds[0].HpDamage == 70, "round history preserved");
    
        Apply(ledger, 2, "p1", 50, 0, DamageTeam.Friendly, "card_b");
        Assert(Math.Abs(p1.AveragePerCompletedRound(true, ledger.AveragingRoundCount) - 75d) < 0.001,
            "live average DPT includes the active round");
        ledger.EndFight();
        Assert(ledger.CompletedRoundCount == 2, "fight end closes final active round");
        Assert(Math.Abs(p1.AveragePerCompletedRound(true, ledger.CompletedRoundCount) - 75d) < 0.001,
            "final DPT includes final round");
    }
    
    public static void TestModSyncRequestTracker()
    {
        var tracker = new AuraToolsModSyncRequestTracker();
        var now = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc);
        tracker.Begin("request-a", now, TimeSpan.FromSeconds(5), AuraToolsModSyncRequestMode.Targeted);
    
        Assert(tracker.IsPending
               && tracker.IsPendingRequest("request-a")
               && tracker.Mode == AuraToolsModSyncRequestMode.Targeted,
            "mod sync request tracker starts a correlated pending request");
        Assert(tracker.Matches("request-a") && tracker.Matches(""),
            "mod sync request tracker accepts correlated and legacy responses while pending");
        Assert(!tracker.Matches("request-b"),
            "mod sync request tracker rejects unrelated responses");
        Assert(!tracker.IsExpired(now.AddSeconds(4)) && tracker.IsExpired(now.AddSeconds(5)),
            "mod sync request tracker expires at its bounded deadline");
    
        tracker.Begin("request-b", now, TimeSpan.FromSeconds(10), AuraToolsModSyncRequestMode.BroadcastFallback);
        Assert(tracker.Mode == AuraToolsModSyncRequestMode.BroadcastFallback
               && tracker.IsPendingRequest("request-b")
               && !tracker.Matches("request-a"),
            "mod sync request tracker replaces a timed-out targeted request with one broadcast fallback");
        Assert(AuraToolsModSyncProtocolPolicy.TryNextFallback(
                   AuraToolsModSyncRequestMode.Targeted,
                   out var broadcastMode)
               && broadcastMode == AuraToolsModSyncRequestMode.BroadcastFallback
               && AuraToolsModSyncProtocolPolicy.ProtocolVersionFor(broadcastMode)
               == AuraToolsModSyncProtocolPolicy.CurrentProtocolVersion
               && AuraToolsModSyncProtocolPolicy.TryNextFallback(
                   broadcastMode,
                   out var legacyMode)
               && legacyMode == AuraToolsModSyncRequestMode.LegacyBroadcastFallback
               && AuraToolsModSyncProtocolPolicy.ProtocolVersionFor(legacyMode)
               == AuraToolsModSyncProtocolPolicy.MinimumSupportedProtocolVersion
               && !AuraToolsModSyncProtocolPolicy.TryNextFallback(
                   legacyMode,
                   out _),
            "mod sync retries current targeted, current broadcast, then legacy broadcast exactly once");
    
        tracker.Clear();
        Assert(!tracker.IsPending && !tracker.Matches("request-b"),
            "mod sync request tracker rejects late responses after completion or fallback");
    }
    
    public static void TestShieldViewRecalculation()
    {
        var ledger = NewLedger();
        ledger.StartRound(1);
        Apply(ledger, 1, "p1", 40, 60, DamageTeam.Friendly, "card");
        var stat = ledger.Combatants.Single();
        Assert(stat.DisplayTotal(true) == 100, "shield-inclusive view");
        Assert(stat.DisplayTotal(false) == 40, "shield-exclusive view recalculates from raw ledger");
    }
    
    public static void TestSnapshotRecovery()
    {
        var source = NewLedger();
        source.StartRound(1);
        Apply(source, 1, "p1", 20, 5, DamageTeam.Friendly, "card");
        source.StartRound(2);
        Apply(source, 2, "p2", 30, 0, DamageTeam.Unknown, "buff");
    
        var restored = new DamageLedger();
        Assert(restored.ApplySnapshot(source.CreateSnapshot()), "snapshot accepted");
        Assert(restored.SessionId == source.SessionId
               && restored.ServerSequence == 2
               && restored.CurrentRoundIndex == 2,
            "snapshot restores protocol state");
        Assert(restored.Combatants.Count == 2, "snapshot restores all combatants");
        var stale = source.CreateSnapshot();
        Apply(source, 3, "p1", 1, 0, DamageTeam.Friendly, "card");
        Assert(!source.ApplySnapshot(stale) && source.ServerSequence == 3,
            "same-session stale snapshot cannot roll the ledger back");
    }
    
    public static void TestSequenceAndSessionGuards()
    {
        var ledger = NewLedger();
        ledger.StartRound(1);
        var first = Event(ledger, 1, "p1", 10, 0, DamageTeam.Friendly, "card");
        Assert(ledger.Apply(first), "first event accepted");
        Assert(!ledger.Apply(first.Copy()), "duplicate server sequence rejected");
    
        var gap = Event(ledger, 3, "p1", 10, 0, DamageTeam.Friendly, "card");
        Assert(!ledger.Apply(gap), "sequence gap rejected for snapshot recovery");
    
        var wrongSession = Event(ledger, 2, "p1", 10, 0, DamageTeam.Friendly, "card");
        wrongSession.SessionId = "old-session";
        Assert(!ledger.Apply(wrongSession), "old session rejected");
    
        var zero = Event(ledger, 2, "p1", 0, 0, DamageTeam.Friendly, "card");
        Assert(!ledger.Apply(zero) && ledger.ServerSequence == 1, "zero damage does not consume sequence");
    
        var restored = new DamageLedger();
        Assert(restored.ApplySnapshot(new DamageMeterSnapshot
        {
            SessionId = "session",
            InFight = true,
            SharedEnabled = true,
            CurrentRoundIndex = 1,
            ServerSequence = 5000
        }), "high-sequence snapshot accepted");
        Assert(!restored.Apply(Event(restored, 1, "p1", 10, 0, DamageTeam.Friendly, "card")),
            "replayed old sequence rejected after snapshot");
    }
    
    public static void TestLongRunningTotals()
    {
        var ledger = NewLedger();
        ledger.StartRound(1);
        for (var sequence = 1; sequence <= 30; sequence++)
        {
            Apply(ledger, sequence, "p1", DamageMeterProtocol.MaxDamagePerEvent, 0, DamageTeam.Friendly, "card");
        }
    
        Assert(ledger.Combatants.Single().TotalHpDamage == 3_000_000_000L,
            "long fights cannot overflow aggregate damage");
    }
    
    public static void TestRunAggregateSurvivesHistoryRetention()
    {
        var history = new DamageHistoryStore();
        var run = new DamageRunLedger();
        run.BeginAdventure("endless", "start");
        long expectedTotal = 0;
        const int expectedRounds = 75;
    
        for (var index = 1; index <= expectedRounds; index++)
        {
            var ledger = new DamageLedger();
            ledger.StartFight("endless-fight-" + index, true);
            ledger.StartRound(1);
            var damage = Event(ledger, 1, "alpha", index, 0, DamageTeam.Friendly, "card_" + index);
            Assert(ledger.Apply(damage), "endless fight event accepted " + index);
            Assert(run.Apply(damage), "run aggregate event accepted " + index);
            ledger.EndFight();
            var snapshot = ledger.CreateSnapshot();
            Assert(run.RecordEncounter(snapshot), "run aggregate records encounter " + index);
            Assert(!run.RecordEncounter(snapshot), "run aggregate rejects duplicate encounter " + index);
            Assert(history.Archive(snapshot, "Win", index.ToString()), "fight history archives " + index);
            expectedTotal += index;
        }
    
        Assert(history.Records.Count == expectedRounds,
            "fight history retains every encounter without a hard cap");
    
        var historyRecord = OutOfRunDamageHistoryBuilder.Build(
            history.Records,
            new OutOfRunDamageHistoryBuildRequest
            {
                AdventureId = "endless",
                TeamMembers = new[]
                {
                    new OutOfRunTeamMemberSnapshot { InstanceId = "alpha", PlayerId = "alpha" }
                }
            });
        Assert(historyRecord.TeamTotalDamage == expectedTotal,
            "unbounded history still represents the complete adventure total");
    
        var aggregate = run.CreateSnapshot();
        var runRecord = OutOfRunDamageHistoryBuilder.Build(
            aggregate,
            new OutOfRunDamageHistoryBuildRequest
            {
                AdventureId = "endless",
                TeamMembers = new[]
                {
                    new OutOfRunTeamMemberSnapshot { InstanceId = "alpha", PlayerId = "alpha" }
                }
            });
        Assert(aggregate.EncounterCount == expectedRounds
               && aggregate.TotalRounds == expectedRounds
               && aggregate.ConfirmedEventCount == expectedRounds,
            "run aggregate keeps unbounded encounter metadata");
        Assert(runRecord.TeamTotalDamage == expectedTotal
               && runRecord.TotalRounds == expectedRounds
               && runRecord.TeamMembers[0].TotalDamage == expectedTotal,
            "run aggregate powers endless out-of-run totals");
    
        var restored = new DamageRunLedger();
        Assert(restored.ApplySnapshot(aggregate), "run aggregate snapshot restores");
        var stale = restored.CreateSnapshot();
        var extraLedger = new DamageLedger();
        extraLedger.StartFight("endless-extra", true);
        extraLedger.StartRound(1);
        var extraDamage = Event(extraLedger, 1, "alpha", 10, 0, DamageTeam.Friendly, "extra");
        Assert(extraLedger.Apply(extraDamage), "extra event accepted");
        Assert(restored.Apply(extraDamage), "restored aggregate advances");
        Assert(!restored.ApplySnapshot(stale), "stale run aggregate snapshot cannot roll totals back");
    }
    
    public static void TestFilteringAndGrandTotal()
    {
        var ledger = NewLedger();
        ledger.StartRound(1);
        Apply(ledger, 1, "friendly", 100, 0, DamageTeam.Friendly, "a");
        Apply(ledger, 2, "enemy", 80, 0, DamageTeam.Enemy, "b");
        Apply(ledger, 3, "unknown", 60, 0, DamageTeam.Unknown, "c");
        var all = DamageMeterHudPresenter.BuildRows(
            ledger.Combatants,
            DamageMeterTeamFilters.All,
            ledger.AveragingRoundCount,
            ledger);
        var friendly = DamageMeterHudPresenter.BuildRows(
            ledger.Combatants,
            DamageMeterTeamFilters.Friendly,
            ledger.AveragingRoundCount,
            ledger);
        var enemy = DamageMeterHudPresenter.BuildRows(
            ledger.Combatants,
            DamageMeterTeamFilters.Enemy,
            ledger.AveragingRoundCount,
            ledger);
        Assert(all.Count == 3 && all.Sum(row => row.Total) == 240,
            "all-team HUD filter includes friendly, enemy, and unknown sources");
        Assert(friendly.Count == 1 && friendly.Single().Total == 100,
            "friendly HUD filter excludes enemy and unknown sources");
        Assert(enemy.Count == 1 && enemy.Single().Total == 80,
            "enemy HUD filter excludes friendly and unknown sources");
    }
}
