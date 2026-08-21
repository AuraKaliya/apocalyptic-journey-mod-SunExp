using AuraShared.Core;

internal static partial class AuraToolsTestSuite
{
    public static void TestBackgroundWorkOwnerCancellation()
    {
        const string owner = "Test.Background.CancelOwner";
        var started = new ManualResetEventSlim();
        var cancelled = new ManualResetEventSlim();
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
                ApplyOnMainThread = _ => { }
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
                ApplyOnMainThread = _ => { }
            }),
            "background scheduler accepts a second queued owner task");

        var cancellationCount =
            AuraSharedBackgroundWorkScheduler.CancelOwner(owner);
        Assert(cancellationCount == 2
               && cancelled.Wait(TimeSpan.FromSeconds(2))
               && AuraSharedBackgroundWorkScheduler.PendingCpuCount == 0,
            "activation disposal removes queued work and cancels the active owner worker");
    }
}
