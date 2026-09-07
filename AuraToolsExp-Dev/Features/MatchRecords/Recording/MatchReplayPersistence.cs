using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Network;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static partial class MatchReplayRecorder
{
    private const int MaximumOutstandingRecords = 4;
    private static readonly List<PendingFinalization> PendingFinalizations = new();
    private static readonly Dictionary<string, PendingSummary> PendingSummaries = new(StringComparer.Ordinal);
    private static CaptureSeed? captureSeed;
    private static bool seedSubmitted;
    private static ReplayCaptureBatchV17? pendingBatch;
    private static bool captureBudgetExceeded;
    private static long lastBudgetCheck;

    private static bool CanBeginPersistence => MatchRecordStorage.Ready
        && PendingFinalizations.Count + PendingSummaries.Count < MaximumOutstandingRecords;

    private static void BeginCapturePersistenceNoLock()
    {
        if (capturePersistenceStarted || activeRecord == null || builder == null || catalog == null) return;
        var first = CreateCaptureBatchNoLock() ?? throw new InvalidOperationException("Replay baseline has no durable prefix.");
        activeRecord.ReplayState = MatchReplayStates.Recording;
        captureSeed = new CaptureSeed
        {
            Record = ReplayCanonicalJsonV17.Clone(activeRecord), Header = ReplayCanonicalJsonV17.Clone(builder.Document.Header),
            Initial = ReplayStateReducerV17.Normalize(builder.Document.InitialState), First = first
        };
        capturePersistenceStarted = true;
        seedSubmitted = TrySubmitSeed(captureSeed);
    }

    private static bool TrySubmitSeed(CaptureSeed seed) => ReplayBackgroundWork.Storage.TryEnqueue("BeginCapture." + seed.Record.RecordId,
        () => { seed.Write(MatchRecordStorage.Database); return true; }, _ => { },
        ex => AuraToolsLog.Warn("[MatchRecords] capture seed remains available for terminal retry: " + ex.Message), BatchBytes(seed.First));

    private static void QueueCaptureBatchNoLock()
    {
        if (!capturePersistenceStarted || activeRecord == null || captureSeed == null) return;
        if (!seedSubmitted && !(seedSubmitted = TrySubmitSeed(captureSeed))) return;
        var batch = pendingBatch ?? CreateCaptureBatchNoLock();
        if (batch == null) return;
        var id = activeRecord.RecordId;
        if (ReplayBackgroundWork.Storage.TryEnqueue("CaptureBatch." + id + "." + batch.BatchIndex,
            () => MatchRecordStorage.Database.AppendCaptureBatchV17(id, batch),
            stored => { if (!stored) AuraToolsLog.Warn("[MatchRecords] capture session unavailable: " + id); },
            ex => AuraToolsLog.Warn("[MatchRecords] incremental write failed; terminal draft retains the full journal: " + ex.Message), BatchBytes(batch)))
            pendingBatch = null;
        else pendingBatch = batch;
    }

    private static long BatchBytes(ReplayCaptureBatchV17 batch) => 4096L + batch.Assets.Sum(asset => asset.Payload?.LongLength ?? 0)
        + batch.TruthEvents.Sum(ReplayMemoryEstimateV17.Event) + batch.PresentationEvents.Sum(ReplayMemoryEstimateV17.Event);

    private static void CheckCaptureBudget(bool force = false)
    {
        if (builder == null || captureBudgetExceeded) return;
        if (!force && ElapsedMilliseconds(lastBudgetCheck) < 1000d) return;
        lastBudgetCheck = Stopwatch.GetTimestamp();
        var bytes = ReplayMemoryEstimateV17.Document(builder.Document) + (catalog?.AssetBytes ?? 0L) + (catalog?.DescriptorCount ?? 0) * 4096L;
        if (bytes <= ReplayMemoryEstimateV17.MaximumCaptureBytes) return;
        captureBudgetExceeded = true;
        AddDiagnosticNoLock("capture-memory-budget-exceeded:" + bytes);
        AuraToolsLog.Warn("[MatchRecords] 本局结构化记录超出内存预算，已停止继续采集；战斗正常继续，结算时保留摘要。");
    }

    private static void QueueRejectedSummary(MatchRecord record)
    {
        PendingSummaries[record.RecordId] = new PendingSummary { Record = record };
        PumpPersistence();
    }

    private static void QueueFinalization(CompletionSnapshot completion)
    {
        var bytes = ReplayMemoryEstimateV17.Document(completion.Envelope.Document);
        if (bytes > ReplayMemoryEstimateV17.MaximumCaptureBytes)
        {
            completion.Record.CaptureDiagnostics.Add("terminal-memory-budget-exceeded:" + bytes);
            QueueRejectedSummary(completion.Record);
            return;
        }
        PendingFinalizations.Add(new PendingFinalization
        {
            Completion = completion,
            Limit = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit,
            ChunkBytes = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.ChunkTargetBytes,
            Bytes = bytes
        });
        PumpPersistence();
    }

    internal static void PumpPersistence()
    {
        foreach (var pending in PendingSummaries.Values.Where(item => !item.InFlight && item.RetryAt <= DateTime.UtcNow).ToArray())
        {
            pending.InFlight = true;
            if (!ReplayBackgroundWork.Storage.TryEnqueue("RejectedSummary." + pending.Record.RecordId,
                () => MatchRecordStorage.Database.SaveSummaryV17(pending.Record, MatchAnalysisBuilder.BuildSummary(pending.Record), rejected: true),
                _ => PendingSummaries.Remove(pending.Record.RecordId),
                ex => { pending.InFlight = false; pending.RetryAt = DateTime.UtcNow.AddSeconds(15); AuraToolsLog.Warn("[MatchRecords] 摘要未保存，保留待重试：" + ex.Message); },
                4096L + pending.Record.StatisticsJson.Length * 2L)) pending.InFlight = false;
        }
        foreach (var pending in PendingFinalizations.Where(item => !item.InFlight && item.RetryAt <= DateTime.UtcNow).ToArray())
        {
            pending.InFlight = true;
            var completion = pending.Completion;
            if (pending.Result != null)
            {
                pending.InFlight = false;
                var ready = pending.Result;
                if (ready.Record != null && ready.Envelope != null && ready.ReplayReady
                    && !ReplayNetworkAuthorityV17.PublishCanonical(ready.Record, ready.Envelope)) continue;
                PendingFinalizations.Remove(pending); LogFinalization(ready);
                continue;
            }
            if (!pending.DraftSaved)
            {
                if (!ReplayBackgroundWork.Storage.TryEnqueue("FinalizingDraft." + completion.Record.RecordId, () =>
                {
                    var database = MatchRecordStorage.Database;
                    completion.Seed?.Write(database);
                    if (completion.PendingBatch != null) database.AppendCaptureBatchV17(completion.Record.RecordId, completion.PendingBatch);
                    database.SaveFinalizingCaptureV17(completion.Record, completion.Envelope, completion.Diagnostics);
                    return true;
                }, _ => { pending.InFlight = false; pending.DraftSaved = true; }, ex =>
                {
                    pending.InFlight = false;
                    if (ex is InvalidDataException)
                    {
                        PendingFinalizations.Remove(pending);
                        completion.Record.CaptureDiagnostics.Add("terminal-draft-rejected:" + ex.Message);
                        QueueRejectedSummary(completion.Record);
                    }
                    else pending.RetryAt = DateTime.UtcNow.AddSeconds(15);
                    AuraToolsLog.Warn("[MatchRecords] 终局草稿未保存：" + ex.Message);
                }, pending.Bytes)) pending.InFlight = false;
            }
            else if (!ReplayBackgroundWork.Finalization.TryEnqueue("Finalize." + completion.Record.RecordId,
                () => FinalizeDetached(completion, MatchRecordStorage.Database, pending.Limit, pending.ChunkBytes), result =>
                {
                    pending.InFlight = false; pending.Result = result;
                }, ex =>
                {
                    pending.InFlight = false; pending.RetryAt = DateTime.UtcNow.AddSeconds(15);
                    AuraToolsLog.Warn("[MatchRecords] 已落库的终局草稿等待重试：" + ex.Message);
                }, pending.Bytes)) pending.InFlight = false;
        }
    }

    private sealed class CaptureSeed
    {
        internal MatchRecord Record = null!;
        internal ReplayDocumentHeaderCoreV17 Header = null!;
        internal ReplayVisibleStateV17 Initial = null!;
        internal ReplayCaptureBatchV17 First = null!;
        internal void Write(MatchRecordDatabase database) => database.BeginCaptureV17(Record, Header, Initial, First);
    }
    private sealed class PendingFinalization
    {
        internal CompletionSnapshot Completion = null!;
        internal int Limit, ChunkBytes;
        internal long Bytes;
        internal bool InFlight, DraftSaved;
        internal DateTime RetryAt;
        internal FinalizationResult? Result;
    }
    private sealed class PendingSummary
    {
        internal MatchRecord Record = null!;
        internal bool InFlight;
        internal DateTime RetryAt;
    }
}
