using System.Collections.Concurrent;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestReplayBackgroundPersistence()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraReplayPersistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var mainThread = Environment.CurrentManagedThreadId;
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var oldLimit = AuraSharedBackgroundWorkScheduler.MaxPendingIo;
        var oldConcurrency = AuraSharedBackgroundWorkScheduler.MaxIoConcurrency;
        var queue = new AuraSharedOrderedWorkQueue("Test.Replay", "PressureTest", AuraSharedBackgroundWorkKind.Io);
        var errors = new ConcurrentQueue<Exception>();
        var phases = new ConcurrentQueue<string>();
        var callbacks = new List<string>();
        var threads = new ConcurrentBag<int>();
        bool Drain() => SpinWait.SpinUntil(() =>
        {
            AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions();
            AuraSharedFrameScheduler.AdvanceFrame(); queue.Pump(); return queue.IsIdle;
        }, TimeSpan.FromSeconds(15));
        try
        {
            AuraSharedBackgroundWorkScheduler.MaxIoConcurrency = 1;
            AuraSharedBackgroundWorkScheduler.MaxPendingIo = 1;
            Assert(AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<int>
            {
                OwnerId = "Replay.Pressure.Blocker", Key = "active", Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ => { started.Set(); gate.Wait(); return 1; }, ApplyOnMainThread = _ => { }
            }) && started.Wait(TimeSpan.FromSeconds(5)), "replay pressure fixture occupies the I/O worker");
            Assert(AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<int>
            {
                OwnerId = "Replay.Pressure.Blocker", Key = "pending", Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ => 1, ApplyOnMainThread = _ => { }
            }), "replay pressure fixture fills the shared pending queue");

            var database = new MatchRecordDatabase(Path.Combine(root, "capture.db"));
            database.Initialize();
            var envelope = BuildReplayV17("ordered-handoff");
            var original = ReplayCanonicalJsonV17.SerializeUtf8(envelope);
            var record = Summary(envelope);
            record.ReplayState = MatchReplayStates.Recording;
            var ordered = envelope.Document.TruthEvents.Concat(envelope.Document.PresentationEvents).OrderBy(x => x.Sequence).ToList();
            var split = ordered[ordered.Count / 2].Sequence;
            var first = CaptureBatch(envelope.Document, 0, e => e.Sequence <= split);
            var second = CaptureBatch(envelope.Document, 1, e => e.Sequence > split);
            void Submit(string phase, Action work) => queue.Enqueue(phase, () =>
            {
                threads.Add(Environment.CurrentManagedThreadId);
                work(); phases.Enqueue(phase); return phase;
            }, value => { Assert(Environment.CurrentManagedThreadId == mainThread, "persistence completion returns to main thread"); callbacks.Add(value); }, errors.Enqueue);

            Submit("seed", () => database.BeginCaptureV17(record, envelope.Document.Header, envelope.Document.InitialState, first));
            Submit("batch", () => { if (!database.AppendCaptureBatchV17(record.RecordId, second)) throw new Exception("batch missing"); });
            Submit("draft", () => database.SaveFinalizingCaptureV17(record, envelope, Array.Empty<string>()));
            Assert(queue.PendingCount == 3 && queue.AdmissionDeferrals >= 3 && phases.IsEmpty,
                "scheduler saturation retains every write without database work on the caller");
            gate.Set();
            Assert(Drain(), "pending writes drain when shared scheduler capacity returns");
            Assert(errors.IsEmpty && phases.SequenceEqual(new[] { "seed", "batch", "draft" }) && callbacks.SequenceEqual(phases),
                "seed, batches, durable terminal draft and acknowledgements retain FIFO order");
            Assert(threads.All(id => id != mainThread), "capture persistence executes off the caller thread");
            Assert(database.Get(record.RecordId)?.ReplayState == MatchReplayStates.Finalizing
                   && original.SequenceEqual(ReplayCanonicalJsonV17.SerializeUtf8(envelope)),
                "background draft persistence preserves the detached document byte for byte");
            var restarted = new MatchRecordDatabase(Path.Combine(root, "capture.db"));
            restarted.Initialize();
            Assert(restarted.RecoverFinalizingCapturesV17() == 1 && restarted.LoadV17(record.RecordId) != null,
                "a background-committed terminal draft remains recoverable after restart");

            var pending = BuildReplayV17("still-recording");
            var pendingRecord = Summary(pending);
            pendingRecord.ReplayState = MatchReplayStates.Recording;
            restarted.BeginCaptureV17(pendingRecord, pending.Document.Header, pending.Document.InitialState,
                CaptureBatch(pending.Document, 0, _ => true));
            var finalizing = BuildReplayV17("still-finalizing");
            var finalizingRecord = Summary(finalizing);
            restarted.BeginCaptureV17(finalizingRecord, finalizing.Document.Header, finalizing.Document.InitialState,
                CaptureBatch(finalizing.Document, 0, _ => true));
            restarted.SaveFinalizingCaptureV17(finalizingRecord, finalizing, Array.Empty<string>());
            Assert(restarted.EnforceAutoLimit(1) == 0 && restarted.Get(pendingRecord.RecordId) != null
                   && restarted.Get(finalizingRecord.RecordId)?.ReplayState == MatchReplayStates.Finalizing,
                "retention does not delete or count captures that still have a writer");

            queue.Enqueue<int>("failed-write", () => throw new IOException("injected"), _ => throw new Exception("unexpected success"), errors.Enqueue);
            Submit("after-error", () => { });
            Assert(Drain() && errors.Count == 1 && callbacks.Last() == "after-error",
                "a failed write reports once without stranding subsequent records");
        }
        finally
        {
            gate.Set();
            AuraSharedBackgroundWorkScheduler.MaxPendingIo = oldLimit;
            AuraSharedBackgroundWorkScheduler.MaxIoConcurrency = oldConcurrency;
            Drain();
            Directory.Delete(root, true);
        }
    }

    public static void TestReplayReplicationAudience()
    {
        Assert(!ReplayReplicationV17.HasRemoteAudience(true, "host", new[] { "host", " HOST ", "" })
               && !ReplayReplicationV17.HasRemoteAudience(true, "host", null)
               && !ReplayReplicationV17.HasRemoteAudience(true, "", new[] { "guest" }),
            "single player, duplicate self and missing identity cannot start canonical encoding");
        Assert(ReplayReplicationV17.HasRemoteAudience(true, "host", new[] { "host", "guest" })
               && !ReplayReplicationV17.HasRemoteAudience(false, "guest", new[] { "host", "guest" }),
            "a real remote audience is served only by its authoritative host");
        var envelope = BuildReplayV17("replication-ownership");
        Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope).IsValid,
            "network fixture is sealed before its immutable transfer handoff");
        var record = Summary(envelope);
        var before = ReplayCanonicalJsonV17.SerializeUtf8(envelope);
        var transfer = ReplayReplicationV17.CreateTransfer(record, envelope);
        Assert(ReferenceEquals(transfer.Envelope, envelope) && ReferenceEquals(transfer.Record, record),
            "network handoff does not clone a sealed journal through JSON");
        var decoded = ReplayPayloadV17.Decode<ReplayNetworkTransferV17>(ReplayPayloadV17.Encode(transfer));
        ReplayAssetPayloadTransferV17.AttachAndValidate(decoded.Envelope.Document, decoded.AssetPayloads);
        Assert(before.SequenceEqual(ReplayCanonicalJsonV17.SerializeUtf8(envelope))
               && decoded.Envelope.DeclaredDocumentRoot == envelope.DeclaredDocumentRoot
               && ReplayDocumentValidatorV17.Validate(decoded.Envelope).IsValid,
            "replication preserves canonical roots, assets and the original sealed document");
    }

    public static void TestIncomingReplayRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraIncoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new MatchRecordDatabase(Path.Combine(root, "incoming.db")); database.Initialize();
            var envelope = BuildReplayV17("incoming-durable");
            Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope).IsValid, "incoming fixture is sealed");
            var payload = ReplayPayloadV17.Encode(ReplayReplicationV17.CreateTransfer(Summary(envelope), envelope));
            database.StageIncomingReplay("transfer", envelope.DeclaredDocumentRoot, payload);
            database.StageIncomingReplay("transfer", envelope.DeclaredDocumentRoot, payload);
            Assert(database.IncomingReplayIds().Count == 1, "duplicate incoming staging has one durable owner");
            var restarted = new MatchRecordDatabase(Path.Combine(root, "incoming.db")); restarted.Initialize();
            Assert(ReplayReplicaStoreV17.Recover(restarted, 20) == 1
                && restarted.LoadV17(envelope.Document.Header.RecordId)?.DeclaredDocumentRoot == envelope.DeclaredDocumentRoot
                && restarted.IncomingReplayIds().Count == 0,
                "restart commits a staged transfer and retires its input only after a valid replay exists");
            restarted.StageIncomingReplay("repeat", envelope.DeclaredDocumentRoot, payload);
            Assert(ReplayReplicaStoreV17.Recover(restarted, 20) == 1 && restarted.Count(MatchRecordCollections.Auto) == 1,
                "resuming an already committed replay is idempotent");
            restarted.StageIncomingReplay("invalid", envelope.DeclaredDocumentRoot, new byte[] { 1, 2, 3 });
            var rejected = 0;
            try { ReplayReplicaStoreV17.Recover(restarted, 20, _ => rejected++); }
            catch (InvalidDataException) { rejected++; }
            Assert(rejected == 1 && restarted.LoadV17(envelope.Document.Header.RecordId) != null,
                "a malformed incoming record cannot replace an existing valid replay");
        }
        finally { Directory.Delete(root, true); }
    }

    public static void TestOrderedQueueBudgets()
    {
        var queue = new AuraSharedOrderedWorkQueue("Test.Budget", "writes", AuraSharedBackgroundWorkKind.Cpu, 2, 64);
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var completed = 0; var errors = 0;
        Assert(queue.TryEnqueue("held", () => { started.Set(); release.Wait(); return 1; }, _ => completed++, _ => errors++, 40),
            "bounded queue accepts a write within its byte budget");
        Assert(started.Wait(TimeSpan.FromSeconds(5)) && queue.RetainedBytes == 40,
            "running work remains charged to the retained-data budget");
        Assert(!queue.TryEnqueue("too-large", () => 1, _ => completed++, _ => errors++, 25),
            "byte budget rejects new work without executing it");
        Assert(queue.TryEnqueue("second", () => 1, _ => completed++, _ => errors++, 16)
            && !queue.TryEnqueue("third", () => 1, _ => completed++, _ => errors++, 1),
            "item budget includes accepted running and queued work");
        release.Set();
        Assert(SpinWait.SpinUntil(() =>
        {
            AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions(); AuraSharedFrameScheduler.AdvanceFrame(); return queue.IsIdle;
        }, TimeSpan.FromSeconds(5)) && completed == 2 && errors == 0 && queue.RetainedBytes == 0,
            "accepted writes drain and release their budgets after completion");

        using var workerDone = new ManualResetEventSlim();
        var late = new AuraSharedOrderedWorkQueue("Test.LateCancel", "writes", AuraSharedBackgroundWorkKind.Cpu);
        var lateApplied = 0;
        late.Enqueue("write", () => { workerDone.Set(); return 7; }, value => lateApplied += value, _ => errors++);
        Assert(workerDone.Wait(TimeSpan.FromSeconds(5)), "late-cancel fixture finishes its worker before applying");
        // Deliver to the next-frame queue without advancing it. Cancellation
        // after worker exit must still return the retained receipt to its owner.
        var frameQueue = typeof(AuraSharedFrameScheduler).GetField("Pending", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert(SpinWait.SpinUntil(() =>
        {
            AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions();
            return ((System.Collections.ICollection)frameQueue.GetValue(null)!).Count > 0;
        }, TimeSpan.FromSeconds(5)), "late-cancel fixture reaches the frame handoff");
        AuraSharedBackgroundWorkScheduler.CancelOwner("Test.LateCancel");
        Assert(SpinWait.SpinUntil(() =>
        {
            AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions(); AuraSharedFrameScheduler.AdvanceFrame(); return late.IsIdle;
        }, TimeSpan.FromSeconds(5)) && lateApplied == 7 && errors == 0,
            "cancellation after worker exit cannot strand a committed write or its capacity receipt");

        using var unavailableDone = new ManualResetEventSlim();
        var unavailable = new AuraSharedOrderedWorkQueue("Test.RunnerRecovery", "writes", AuraSharedBackgroundWorkKind.Cpu);
        var recovered = false;
        unavailable.Enqueue("write", () => { unavailableDone.Set(); return true; }, value => recovered = value, _ => errors++);
        Assert(unavailableDone.Wait(TimeSpan.FromSeconds(5)), "runner-loss fixture completes its background write");
        AuraSharedFrameScheduler.RunnerAvailable = false;
        try
        {
            // A barrier behind the worker ensures its receipt was queued.
            using var barrier = new ManualResetEventSlim();
            AuraSharedFrameScheduler.RunnerAvailable = true;
            AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<int> { OwnerId = "Test.RunnerBarrier", Work = _ => { barrier.Set(); return 0; }, ApplyOnMainThread = _ => { } });
            Assert(barrier.Wait(TimeSpan.FromSeconds(5)), "runner-loss fixture reaches scheduler completion");
            AuraSharedFrameScheduler.RunnerAvailable = false;
            AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions();
            Assert(!recovered && !unavailable.IsIdle, "frame-runner refusal retains the result and its budget");
        }
        finally { AuraSharedFrameScheduler.RunnerAvailable = true; }
        Assert(SpinWait.SpinUntil(() =>
        {
            AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions(); AuraSharedFrameScheduler.AdvanceFrame(); return unavailable.IsIdle;
        }, TimeSpan.FromSeconds(5)) && recovered, "result is delivered after the main-thread runner becomes available again");
    }
}
