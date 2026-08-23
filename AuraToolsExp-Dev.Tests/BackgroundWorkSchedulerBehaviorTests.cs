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
            return cancellationCallbacks.IsSet;
        }, TimeSpan.FromSeconds(2));
        Assert(callbacksCompleted
               && cancellationReasons.Count == 2
               && cancellationReasons.All(reason => reason == "owner-cancelled"),
            "queued and active cancellation both publish one terminal callback on the main thread");
    }
}
