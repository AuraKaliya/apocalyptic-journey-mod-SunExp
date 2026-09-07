using AuraShared.Core;

internal static partial class AuraToolsTestSuite
{
    public static void TestBackgroundWorkOwnerCancellation()
    {
        const string owner = "Test.Background.CancelOwner";
        var started = new ManualResetEventSlim();
        var cancelled = new ManualResetEventSlim();
        var cancellationCallbacks = new CountdownEvent(2);
        var cancellationReasons = new System.Collections.Concurrent.ConcurrentBag<string>();
        AuraSharedBackgroundWorkScheduler.MaxCpuConcurrency = 1;
        AuraSharedBackgroundWorkScheduler.MaxPendingPerOwner = 4;
        Assert(AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<int>
            {
                OwnerId = owner,
                Key = "active",
                Work = cancellation =>
                {
                    started.Set();
                    try
                    {
                        cancellation.WaitHandle.WaitOne();
                        cancellation.ThrowIfCancellationRequested();
                        return 1;
                    }
                    finally
                    {
                        if (cancellation.IsCancellationRequested)
                        {
                            cancelled.Set();
                        }
                    }
                },
                ApplyOnMainThread = _ => { },
                OnCancelledOnMainThread = reason =>
                {
                    cancellationReasons.Add(reason);
                    cancellationCallbacks.Signal();
                }
            }),
            "background scheduler accepts an owner-scoped active task");
        Assert(started.Wait(TimeSpan.FromSeconds(2)),
            "background owner cancellation test starts its active worker");
        Assert(AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<int>
            {
                OwnerId = owner,
                Key = "queued",
                Work = _ => 2,
                ApplyOnMainThread = _ => { },
                OnCancelledOnMainThread = reason =>
                {
                    cancellationReasons.Add(reason);
                    cancellationCallbacks.Signal();
                }
            }),
            "background scheduler accepts a second queued owner task");

        var cancellationCount =
            AuraSharedBackgroundWorkScheduler.CancelOwner(owner);
        Assert(cancellationCount == 2
               && cancelled.Wait(TimeSpan.FromSeconds(2))
               && AuraSharedBackgroundWorkScheduler.PendingCpuCount == 0,
            "activation disposal removes queued work and cancels the active owner worker");
        var callbacksCompleted = SpinWait.SpinUntil(() =>
        {
            AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions();
            AuraSharedFrameScheduler.AdvanceFrame();
            return cancellationCallbacks.IsSet;
        }, TimeSpan.FromSeconds(2));
        Assert(callbacksCompleted
               && cancellationReasons.Count == 2
               && cancellationReasons.All(reason => reason == "owner-cancelled"),
            "queued and active cancellation both publish one terminal callback on the main thread");
    }

    public static void TestBackgroundReplacementAdmission()
    {
        var previousLimit = AuraSharedBackgroundWorkScheduler.MaxPendingCpu;
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var originalCancelled = false;
        var originalApplied = false;
        var replacementRan = false;
        try
        {
            AuraSharedBackgroundWorkScheduler.MaxPendingCpu = 1;
            Assert(AuraSharedBackgroundWorkScheduler.TryQueue(new AuraSharedBackgroundWorkRequest<int>
            {
                OwnerId = "Admission.Owner", Key = "refresh",
                Work = token => { started.Set(); release.Wait(); originalCancelled = token.IsCancellationRequested; return 1; },
                ApplyOnMainThread = _ => originalApplied = true
            }) == AuraSharedWorkAdmission.Accepted && started.Wait(TimeSpan.FromSeconds(5)),
                "replacement fixture starts an accepted task");
            Assert(AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<int>
            {
                OwnerId = "Admission.Other", Key = "fill", Work = _ => 0, ApplyOnMainThread = _ => { }
            }), "replacement fixture fills pending capacity");
            var result = AuraSharedBackgroundWorkScheduler.TryQueue(new AuraSharedBackgroundWorkRequest<int>
            {
                OwnerId = "Admission.Owner", Key = "refresh",
                Work = _ => { replacementRan = true; return 2; }, ApplyOnMainThread = _ => { }
            });
            Assert(result == AuraSharedWorkAdmission.BackPressure && !replacementRan,
                "rejected replacement reports backpressure without execution");
            release.Set();
            Assert(SpinWait.SpinUntil(() =>
            {
                AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions();
                AuraSharedFrameScheduler.AdvanceFrame();
                return originalApplied;
            }, TimeSpan.FromSeconds(5)) && !originalCancelled && !replacementRan,
                "rejection preserves the previously accepted work and its completion");
            var ran = false;
            AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
                { DelayFrames = 2, Action = () => ran = true });
            Assert(!ran, "test frame scheduler never runs a deferred callback inline");
            AuraSharedFrameScheduler.AdvanceFrame();
            Assert(!ran, "two-frame callback is not ready after one frame");
            AuraSharedFrameScheduler.AdvanceFrame();
            Assert(ran, "two-frame callback runs after its actual frame boundary");
        }
        finally
        {
            release.Set();
            AuraSharedBackgroundWorkScheduler.MaxPendingCpu = previousLimit;
        }
    }

    public static void TestInitializationDependencies()
    {
        var report = new AuraSharedInitializationReport();
        var dependentRan = false; var independentRan = false;
        Assert(!report.Run("required", () => throw new InvalidOperationException("unavailable")), "required initialization failure is recorded");
        Assert(!report.Run("dependent", () => dependentRan = true, null, "required") && !dependentRan,
            "unavailable prerequisite blocks dependent activation");
        Assert(report.Run("independent", () => independentRan = true) && independentRan,
            "an independent capability can initialize after another capability fails");
        Assert(report.Steps.Single(step => step.Name == "dependent").State == AuraInitializationState.Blocked,
            "blocked state remains distinguishable from direct initialization failure");
    }
}
