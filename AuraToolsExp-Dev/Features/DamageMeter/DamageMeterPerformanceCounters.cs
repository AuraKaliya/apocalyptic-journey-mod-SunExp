using System;
using System.Diagnostics;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageMeterPerformanceCounters
{
    private const long LogIntervalMs = 10000;
    private static readonly double TimestampToMs = 1000d / Stopwatch.Frequency;
    private static long windowStartedAtMs = NowMs();
    private static long nextLogAtMs = windowStartedAtMs + LogIntervalMs;
    private static int hitHooks;
    private static int damageTextHooks;
    private static int pureHpHooks;
    private static int hpSetterHooks;
    private static int buffHooks;
    private static int submittedEvents;
    private static int localAppliedEvents;
    private static int pendingBatches;
    private static int batchFlushes;
    private static int batchCommands;
    private static int maxPendingBatch;
    private static long batchFlushTotalMs;
    private static long batchFlushMaxMs;
    private static int uiRefreshes;
    private static long uiRefreshTotalMs;
    private static long uiRefreshMaxMs;
    private static int uiRowsMax;
    private static int snapshots;
    private static int compactedSnapshots;
    private static long snapshotTotalMs;
    private static long snapshotMaxBytes;

    public static long StartSample()
    {
        return Stopwatch.GetTimestamp();
    }

    public static long ElapsedMs(long startedAt)
    {
        if (startedAt <= 0)
        {
            return 0;
        }

        return (long)((Stopwatch.GetTimestamp() - startedAt) * TimestampToMs);
    }

    public static void RecordHitHook()
    {
        hitHooks++;
    }

    public static void RecordDamageTextHook()
    {
        damageTextHooks++;
    }

    public static void RecordPureHpHook()
    {
        pureHpHooks++;
    }

    public static void RecordHpSetterHook()
    {
        hpSetterHooks++;
    }

    public static void RecordBuffHook()
    {
        buffHooks++;
    }

    public static void RecordSubmitted(bool localApplied)
    {
        submittedEvents++;
        if (localApplied)
        {
            localAppliedEvents++;
        }
    }

    public static void RecordPendingBatch(int pendingCount)
    {
        pendingBatches++;
        if (pendingCount > maxPendingBatch)
        {
            maxPendingBatch = pendingCount;
        }
    }

    public static void RecordBatchFlush(int eventCount, int commandCount, long elapsedMs)
    {
        batchFlushes++;
        batchCommands += Math.Max(0, commandCount);
        batchFlushTotalMs += Math.Max(0, elapsedMs);
        if (elapsedMs > batchFlushMaxMs)
        {
            batchFlushMaxMs = elapsedMs;
        }

        if (eventCount > maxPendingBatch)
        {
            maxPendingBatch = eventCount;
        }
    }

    public static void RecordUiRefresh(long elapsedMs, int visibleRows, bool inFight)
    {
        uiRefreshes++;
        uiRefreshTotalMs += Math.Max(0, elapsedMs);
        uiRowsMax = Math.Max(uiRowsMax, Math.Max(0, visibleRows));
        if (elapsedMs > uiRefreshMaxMs)
        {
            uiRefreshMaxMs = elapsedMs;
        }
    }

    public static void RecordSnapshot(long elapsedMs, int beforeBytes, int afterBytes, bool compacted)
    {
        snapshots++;
        snapshotTotalMs += Math.Max(0, elapsedMs);
        snapshotMaxBytes = Math.Max(snapshotMaxBytes, Math.Max(beforeBytes, afterBytes));
        if (compacted)
        {
            compactedSnapshots++;
        }
    }

    public static void MaybeLog()
    {
        var now = NowMs();
        if (now < nextLogAtMs)
        {
            return;
        }

        var hooks = hitHooks + damageTextHooks + pureHpHooks + hpSetterHooks + buffHooks;
        if (hooks > 0
            || submittedEvents > 0
            || uiRefreshes > 0
            || snapshots > 0
            || batchFlushes > 0)
        {
            var elapsed = Math.Max(1, now - windowStartedAtMs);
            AuraToolsLog.Debug("[DamageMeter:perf] windowMs="
                               + elapsed
                               + ", hooks(hit/text/pure/set/buff)="
                               + hitHooks + "/" + damageTextHooks + "/" + pureHpHooks + "/" + hpSetterHooks + "/" + buffHooks
                               + ", submit="
                               + submittedEvents
                               + ", local="
                               + localAppliedEvents
                               + ", batch(pending/flush/cmd/max/avgMs/maxMs)="
                               + pendingBatches + "/" + batchFlushes + "/" + batchCommands + "/" + maxPendingBatch
                               + "/" + Average(batchFlushTotalMs, batchFlushes) + "/" + batchFlushMaxMs
                               + ", ui(count/avg/max/rows)="
                               + uiRefreshes + "/" + Average(uiRefreshTotalMs, uiRefreshes) + "/" + uiRefreshMaxMs + "/" + uiRowsMax
                               + ", snapshot(count/compact/avgMs/maxBytes)="
                               + snapshots + "/" + compactedSnapshots + "/" + Average(snapshotTotalMs, snapshots) + "/" + snapshotMaxBytes);
        }

        ResetWindow(now);
    }

    private static long Average(long total, int count)
    {
        return count <= 0 ? 0 : total / count;
    }

    private static void ResetWindow(long now)
    {
        windowStartedAtMs = now;
        nextLogAtMs = now + LogIntervalMs;
        hitHooks = 0;
        damageTextHooks = 0;
        pureHpHooks = 0;
        hpSetterHooks = 0;
        buffHooks = 0;
        submittedEvents = 0;
        localAppliedEvents = 0;
        pendingBatches = 0;
        batchFlushes = 0;
        batchCommands = 0;
        maxPendingBatch = 0;
        batchFlushTotalMs = 0;
        batchFlushMaxMs = 0;
        uiRefreshes = 0;
        uiRefreshTotalMs = 0;
        uiRefreshMaxMs = 0;
        uiRowsMax = 0;
        snapshots = 0;
        compactedSnapshots = 0;
        snapshotTotalMs = 0;
        snapshotMaxBytes = 0;
    }

    private static long NowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
