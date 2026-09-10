using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Infrastructure;

internal static partial class AuraToolsTestSuite
{
    public static void TestDamageNetworkRuntimeRecovery()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AuraDamageNetwork-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            DamageHistoryStorage.Database = new DamageHistoryDatabase(Path.Combine(root, "history.sqlite3"));
            AuraNetworkIdentityRuntime.AdventureId = Guid.NewGuid().ToString("N");
            AuraToolsConfigService.MatchExperience.DamageMeter.MaxEventsPerBatch = 24;
            DamageMeterNetworkRuntime.BeginAdventure();
            AuraBattleLifecycleRouter.CurrentBattleSessionId++;
            DamageMeterNetworkRuntime.StartFight(true);
            DamageMeterNetworkRuntime.StartRound();
            var session = DamageMeterNetworkRuntime.Ledger.SessionId;
            var values = Enumerable.Range(1, 64).Select(index => Damage(index, session)).ToList();
            var first = Batch(session, values);
            first.CmdExecute(); first.RpcExecute();
            Assert(first.AcknowledgedThrough == 64 && first.Confirmed.Count == 64
                && DamageMeterNetworkRuntime.Ledger.ServerSequence == 64, "host batch preference 24 cannot truncate a legal 64-event submission");
            var retry = Batch(session, values);
            retry.CmdExecute(); retry.RpcExecute();
            Assert(retry.AcknowledgedThrough == 64 && retry.Confirmed.Count == 0
                && DamageMeterNetworkRuntime.Ledger.Combatants.Single().TotalHpDamage == 640,
                "lost acknowledgement retransmission returns the same prefix without duplicating damage");
            DamageMeterNetworkRuntime.StartRound();
            var late = Batch(session, new List<DamageEvent> { Damage(65, session) });
            late.CmdExecute(); late.RpcExecute();
            Assert(DamageMeterNetworkRuntime.Ledger.Combatants.Single().CurrentRoundHpDamage == 0
                && DamageMeterNetworkRuntime.Ledger.Combatants.Single().Rounds.Single(round => round.RoundIndex == 1).HpDamage == 650,
                "delayed round-one events remain in round one after round two begins");
            DamageMeterNetworkRuntime.EndFight("Win");
            Assert(DamageMeterNetworkRuntime.Ledger.InFight, "host waits for remote reporter completion before sealing");
            var final = Batch(session, new()); final.IsFinalMarker = true; final.FinalReporterSequence = 65;
            final.CmdExecute(); final.RpcExecute(); DamageMeterNetworkRuntime.Tick();
            Assert(!DamageMeterNetworkRuntime.Ledger.InFight && DamageMeterNetworkRuntime.History.TotalCount == 1
                && DamageMeterNetworkRuntime.RunAggregate.EncounterCount == 1, "all acknowledged reporters permit one final archive: inFight="
                + DamageMeterNetworkRuntime.Ledger.InFight + ", history=" + DamageMeterNetworkRuntime.History.TotalCount
                + ", encounters=" + DamageMeterNetworkRuntime.RunAggregate.EncounterCount);
            var finalRetry = Batch(session, new()); finalRetry.IsFinalMarker = true; finalRetry.FinalReporterSequence = 65;
            finalRetry.CmdExecute();
            Assert(finalRetry.FinalMarkerAccepted && finalRetry.AcknowledgedThrough == 65, "closed sessions can repeat a lost final acknowledgement");

            AuraBattleLifecycleRouter.CurrentBattleSessionId++;
            DamageMeterNetworkRuntime.StartFight(true); DamageMeterNetworkRuntime.StartRound();
            session = DamageMeterNetworkRuntime.Ledger.SessionId;
            var later = Batch(session, new List<DamageEvent> { Damage(2, session) }); later.CmdExecute();
            Assert(later.AcknowledgedThrough == 0 && later.Confirmed.Count == 0, "server does not acknowledge a gap");
            var earlier = Batch(session, new List<DamageEvent> { Damage(1, session) }); earlier.CmdExecute();
            Assert(earlier.AcknowledgedThrough == 2 && earlier.Confirmed.Count == 2, "server drains reordered buffered damage when the missing event arrives");
            DamageMeterNetworkRuntime.EndFight("Win");
            AuraBattleLifecycleRouter.CurrentBattleSessionId++;
            DamageMeterNetworkRuntime.StartFight(true);
            var history = DamageHistoryStorage.Database.LoadFightPage(AuraNetworkIdentityRuntime.AdventureId);
            Assert(history.Items.Any(item => item.SessionId == session && !item.Snapshot.IsComplete),
                "advancing without a reporter final marker archives an explicit incomplete result");
        }
        finally
        {
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup.");
            Directory.Delete(root, true);
        }
    }

    private static DamageMeterSubmitBatchCommand Batch(string session, List<DamageEvent> values)
    {
        var command = new DamageMeterSubmitBatchCommand { ProtocolVersion = DamageMeterProtocol.Version, SessionId = session, Candidates = values };
        command.BindServerSender(new AuraToolsRpcSender("guest", "guest", true, false, "native", true));
        return command;
    }
    private static DamageEvent Damage(long sequence, string session) => new()
    {
        ProtocolVersion = DamageMeterProtocol.Version, SessionId = session, ReporterPlayerId = "guest", ReporterSequence = sequence,
        SourceInstanceId = "guest", SourceDisplayName = "guest", TargetInstanceId = "enemy", HpDamage = 10, FinalDamage = 10, RoundIndex = 1
    };
}
