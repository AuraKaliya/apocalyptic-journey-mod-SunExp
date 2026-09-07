using System;
using System.Collections.Generic;
using System.Diagnostics;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

// Pending work belongs to the recording, not to the current battle or feature
// switch. Disabling capture must still finish already accepted durable writes.
internal static class ReplayBackgroundWork
{
    internal static readonly AuraSharedOrderedWorkQueue Storage = Create("Storage", AuraSharedBackgroundWorkKind.Io);
    internal static readonly AuraSharedOrderedWorkQueue Finalization = Create("Finalization", AuraSharedBackgroundWorkKind.Cpu);

    private static AuraSharedOrderedWorkQueue Create(string name, AuraSharedBackgroundWorkKind kind)
    {
        var queue = new AuraSharedOrderedWorkQueue(AuraToolsIds.ModId + ".ReplayPersistence", name, kind);
        queue.Measured += value =>
        {
            if (name == "Storage" && value.Source != "RecordCounts")
                AuraToolsExp.Dll.Features.MatchRecords.Storage.MatchRecordStorage.InvalidateCounts();
            if (value.WorkMilliseconds >= 100d || value.ApplyMilliseconds >= 8d)
                AuraToolsLog.Info("[MatchRecords:perf] work=" + value.Source
                    + ", workerMs=" + value.WorkMilliseconds.ToString("0.###")
                    + ", queueMs=" + value.QueueMilliseconds.ToString("0.###")
                    + ", mainApplyMs=" + value.ApplyMilliseconds.ToString("0.###")
                    + ", retainedBytes=" + queue.RetainedBytes + ".");
        };
        queue.CompletionFailed += ex => AuraToolsLog.Warn("[MatchRecords] completion failed: " + ex.Message);
        return queue;
    }

    internal static void Pump()
    {
        Storage.Pump();
        Finalization.Pump();
    }
}
