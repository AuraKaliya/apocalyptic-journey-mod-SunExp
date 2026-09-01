using AuraCombatAi.Shared;

internal static class CombatLiveDecisionLaneBehaviorTests
{
    public static void Run()
    {
        ExecutionPreemptsOpportunisticWork();
        SessionCancellationPublishesTerminalReceipt();
        HardDeadlineCancelsRunningSearch();
    }

    private static void HardDeadlineCancelsRunningSearch()
    {
        var worker = new BlockingDecisionWorker(new ManualResetEventSlim());
        using var lane = new CombatLiveDecisionLane("tests.live-deadline");
        var request = Request(
            worker,
            31,
            "block",
            CombatLiveDecisionPurpose.Execution,
            CombatLiveDecisionPriority.Execution);
        request.HardDeadlineMilliseconds = 50;
        var requestId = lane.Submit(request);
        var receipt = Drain(lane, 1).Single();
        CombatAiTestFixtures.Assert(
            receipt.RequestId == requestId
            && receipt.Status
               == CombatLiveDecisionReceiptStatus.DeadlineExceeded
            && receipt.Reason == "hard-deadline"
            && receipt.Timing.ComputeMilliseconds >= 25d,
            "hard deadline cooperatively interrupts running live work and publishes a deadline receipt");
    }

    private static void ExecutionPreemptsOpportunisticWork()
    {
        using var started = new ManualResetEventSlim();
        var worker = new BlockingDecisionWorker(started);
        using var lane = new CombatLiveDecisionLane("tests.live-decision");
        var shadowId = lane.Submit(Request(
            worker,
            17,
            "block",
            CombatLiveDecisionPurpose.Shadow,
            CombatLiveDecisionPriority.Opportunistic));
        CombatAiTestFixtures.Assert(
            started.Wait(TimeSpan.FromSeconds(2)),
            "live decision lane starts opportunistic work on its dedicated worker");

        var executionId = lane.Submit(Request(
            worker,
            17,
            "execute",
            CombatLiveDecisionPurpose.Execution,
            CombatLiveDecisionPriority.Execution));
        var receipts = Drain(lane, 2);
        var shadow = receipts.Single(item => item.RequestId == shadowId);
        var execution = receipts.Single(item => item.RequestId == executionId);
        CombatAiTestFixtures.Assert(
            shadow.Status == CombatLiveDecisionReceiptStatus.Superseded
            && execution.Status == CombatLiveDecisionReceiptStatus.Completed,
            "execution work cancels shadow work and both requests publish one terminal receipt");
        CombatAiTestFixtures.Assert(
            execution.Decision.Action?.CandidateId == "execute-action"
            && worker.ObservedThreadName == "tests.live-decision",
            "execution completes on the named live decision thread without the CLR ThreadPool");
        var snapshot = lane.Snapshot();
        CombatAiTestFixtures.Assert(
            snapshot.SubmittedRequests == 2
            && snapshot.CompletedRequests == 1
            && snapshot.SupersededRequests == 1,
            "live decision telemetry classifies completed and superseded work independently");
    }

    private static void SessionCancellationPublishesTerminalReceipt()
    {
        var worker = new BlockingDecisionWorker(new ManualResetEventSlim());
        using var lane = new CombatLiveDecisionLane("tests.live-cancel");
        var requestId = lane.Submit(Request(
            worker,
            29,
            "block",
            CombatLiveDecisionPurpose.Execution,
            CombatLiveDecisionPriority.Execution));
        SpinWait.SpinUntil(
            () => lane.Snapshot().HasActiveRequest,
            TimeSpan.FromSeconds(2));
        lane.CancelSession(29, "battle-finalized");
        var receipt = Drain(lane, 1).Single();
        CombatAiTestFixtures.Assert(
            receipt.RequestId == requestId
            && receipt.Status == CombatLiveDecisionReceiptStatus.Cancelled
            && receipt.Reason == "battle-finalized",
            "battle cancellation terminates active live work with its lifecycle reason");
    }

    private static CombatLiveDecisionRequest Request(
        ICombatLiveDecisionWorker worker,
        long sessionId,
        string fingerprint,
        CombatLiveDecisionPurpose purpose,
        CombatLiveDecisionPriority priority)
    {
        return new CombatLiveDecisionRequest
        {
            BattleSessionId = sessionId,
            Generation = 1,
            ObservationRevision = 1,
            StateFingerprint = fingerprint,
            Purpose = purpose,
            Authority = purpose == CombatLiveDecisionPurpose.Shadow
                ? CombatLiveDecisionAuthority.Model
                : CombatLiveDecisionAuthority.RuleBaseline,
            Priority = priority,
            State = new CombatStateObservation { Fingerprint = fingerprint },
            Profile = new CombatDecisionProfile
            {
                SearchTimeBudgetMilliseconds = 50
            },
            Worker = worker,
            HardDeadlineMilliseconds = 2000
        };
    }

    private static List<CombatLiveDecisionReceipt> Drain(
        CombatLiveDecisionLane lane,
        int count)
    {
        var result = new List<CombatLiveDecisionReceipt>();
        var completed = SpinWait.SpinUntil(() =>
        {
            while (lane.TryTakeReceipt(out var receipt))
            {
                result.Add(receipt);
            }
            return result.Count >= count;
        }, TimeSpan.FromSeconds(3));
        CombatAiTestFixtures.Assert(
            completed,
            "live decision lane publishes every expected receipt");
        return result;
    }

    private sealed class BlockingDecisionWorker : ICombatLiveDecisionWorker
    {
        private readonly ManualResetEventSlim started;

        public BlockingDecisionWorker(ManualResetEventSlim started)
        {
            this.started = started;
        }

        public string ObservedThreadName { get; private set; } = "";

        public CombatDecision Choose(
            CombatStateObservation state,
            CombatDecisionProfile profile,
            CombatSearchExplorationOptions? exploration,
            CancellationToken cancellationToken)
        {
            ObservedThreadName = Thread.CurrentThread.Name ?? "";
            if (state.Fingerprint == "block")
            {
                started.Set();
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new CombatDecision
            {
                HasAction = true,
                Action = new CombatActionObservation
                {
                    CandidateId = state.Fingerprint + "-action"
                }
            };
        }

        public long ReleaseRetainedMemory()
        {
            return 0L;
        }
    }
}
