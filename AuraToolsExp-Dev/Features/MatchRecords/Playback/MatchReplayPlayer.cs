using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Mirror;
using UiTransitionGuardShared;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Drives the portable v12 journal through Aura's own scene. This player never
/// creates a battle manager, battle UI, role, card, buff, or gameplay script.
/// </summary>
internal static class MatchReplayPlayer
{
    private static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };
    private static readonly ReplayStateReducerV12 Reducer = new();
    private static readonly ReplayPovReducerV12 PovReducer = new();
    private static MatchRecord? record;
    private static ReplayDocumentEnvelopeV12? envelope;
    private static ReplayPovSidecarV12? pov;
    private static ReplaySceneRuntime? scene;
    private static List<ReplayJournalEventV12> events = new();
    private static HashSet<string> actionTransactions = new(StringComparer.Ordinal);
    private static int eventIndex;
    private static int povEventIndex;
    private static int completedActionCount;
    private static long logicalTicks;
    private static long durationTicks;
    private static long pendingStartTicks;
    private static int speedIndex = 1;
    private static bool paused;
    private static bool seeking;
    private static bool seekPreviewing;
    private static float seekPreviewProgress;
    private static bool resetting;
    private static bool controlsVisible;
    private static bool externalClock;
    private static bool runtimeReady;
    private static string playbackHealth = "Compatible";
    private static string playbackIssue = "";
    private static string preparationStatus = "";
    private static string lastStartFailure = "";
    private static int failedEventCount;

    internal static bool IsActive => record != null;
    internal static bool IsPaused => paused;
    internal static bool IsRuntimeReady => runtimeReady;
    internal static float Speed => Speeds[speedIndex];
    internal static int EventIndex => eventIndex;
    internal static int EventCount => events.Count;
    internal static int ActionCount => actionTransactions.Count;
    internal static int CompletedActionCount => completedActionCount;
    internal static bool IsSeeking => seeking;
    internal static bool IsFinished => IsActive && eventIndex >= events.Count && logicalTicks >= durationTicks;
    internal static float Progress => seekPreviewing || seeking
        ? seekPreviewProgress
        : durationTicks <= 0 ? (eventIndex >= events.Count ? 1f : 0f) : Clamp01(logicalTicks / (float)durationTicks);
    internal static long DurationMilliseconds => durationTicks * 1000L / ReplayProtocolV12.TimebaseTicksPerSecond;
    internal static IReadOnlyList<ReplayJournalEventV12> Events => events;
    internal static bool IsReadyForExport => IsActive && runtimeReady && externalClock && !seeking && !HasBlockingError;
    internal static string PlaybackHealth => playbackHealth;
    internal static string PlaybackIssue => playbackIssue;
    internal static string PreparationStatus => preparationStatus;
    internal static string LastStartFailure => lastStartFailure;
    internal static int FailedEventCount => failedEventCount;
    internal static bool HasBlockingError => playbackHealth == "Desynced" || playbackHealth == "Failed";
    internal static int CurrentTurn => Math.Max(1, Reducer.Current.RoundSequence);
    internal static int TurnCount => Math.Max(record?.TurnCount ?? 0, events.Count == 0 ? 0 : events.Max(item => item.RoundSequence));
    internal static long LogicalTicks => logicalTicks;
    internal static Camera? RenderCamera => scene?.Camera;

    internal static bool TryPrepareInteractive(string recordId, long startSequence, out string message)
    {
        return TryPrepareCore(recordId, showControls: true, startSequence, returnToLibrary: true, out message);
    }

    internal static bool TryPrepareForExport(string recordId, bool returnToLibrary, out string message)
    {
        return TryPrepareCore(recordId, showControls: false, startSequence: 0, returnToLibrary, out message);
    }

    internal static bool TryStartForExport(string recordId, out string message)
    {
        return TryPrepareForExport(recordId, returnToLibrary: false, out message)
               && TryActivatePrepared(out message);
    }

    internal static void CommitOrigin() => MatchReplaySessionState.CommitOrigin();

    internal static bool TryActivatePrepared(out string message)
    {
        message = "";
        if (MatchReplaySessionState.Phase != MatchReplayLifecyclePhase.Prepared || envelope == null)
        {
            message = "回放数据尚未准备完成。";
            return false;
        }

        try
        {
            MatchReplayUiLifecycle.PrepareForReplayView();
            scene = new ReplaySceneRuntime(envelope.Document, pov, includeHud: true);
            Reducer.Reset(envelope.Document.InitialState);
            logicalTicks = 0;
            scene.Tick(0);
            scene.Restore(Reducer.Current, null);
            ResetPovThrough(0);
            ExecuteDueEvents(suppressAudio: externalClock);
            scene.Tick(0);
            runtimeReady = true;
            MatchReplaySessionState.MarkActive();
            if (controlsVisible) MatchReplayControlsPresenter.Show();
            if (pendingStartTicks > 0) SeekToTicks(pendingStartTicks);
            preparationStatus = "";
            scene.SetPlaybackSpeed(Speed);
            scene.SetPaused(paused);
            AuraToolsLog.Info("[MatchRecords] portable v12 replay started: record=" + record?.RecordId
                              + ", events=" + events.Count + ", actions=" + ActionCount
                              + ", scene=aura-independent, gameplay-scripts=disabled.");
            RefreshControls();
            message = "开始回放。";
            return true;
        }
        catch (Exception ex)
        {
            lastStartFailure = "无法创建独立回放场景：" + ex.Message;
            AuraToolsLog.Error("[MatchRecords] portable replay activation failed", ex);
            BeginStop(MatchReplayExitKind.RuntimeFailed, lastStartFailure);
            message = lastStartFailure;
            return false;
        }
    }

    internal static void FailCommittedStart(string detail) => BeginStop(MatchReplayExitKind.StartFailed, detail);

    internal static void Tick()
    {
        if (!IsActive || !runtimeReady || externalClock || resetting) return;
        var elapsedMilliseconds = Math.Max(0f, Time.unscaledDeltaTime * 1000f);
        MatchReplayControlsPresenter.Tick(elapsedMilliseconds);
        if (!paused && !seeking && !HasBlockingError)
            AdvanceClock(elapsedMilliseconds * Speed);
    }

    internal static void TogglePause()
    {
        if (!runtimeReady || seeking) return;
        paused = !paused;
        scene?.SetPaused(paused);
        RefreshControls();
    }

    internal static void CycleSpeed()
    {
        if (!runtimeReady || seeking) return;
        speedIndex = (speedIndex + 1) % Speeds.Length;
        scene?.SetPlaybackSpeed(Speed);
        RefreshControls();
    }

    internal static void SeekNormalized(float value)
    {
        if (!runtimeReady || durationTicks <= 0) return;
        SeekToTicks((long)Math.Round(Clamp01(value) * durationTicks));
    }

    internal static void BeginSeekPreview(float value)
    {
        if (!runtimeReady) return;
        seekPreviewing = true;
        seekPreviewProgress = Clamp01(value);
        RefreshControls();
    }

    internal static void PreviewSeekNormalized(float value)
    {
        if (!seekPreviewing) return;
        seekPreviewProgress = Clamp01(value);
        RefreshControls();
    }

    internal static void CommitSeekPreview(float value)
    {
        if (!seekPreviewing) return;
        seekPreviewing = false;
        SeekNormalized(value);
    }

    internal static void SeekTurn(int delta)
    {
        if (!runtimeReady || delta == 0) return;
        var rounds = events.Where(item => item.RoundSequence > 0)
            .GroupBy(item => item.RoundSequence)
            .Select(group => new { Round = group.Key, Ticks = group.Min(item => item.TimeTicks) })
            .OrderBy(item => item.Round)
            .ToList();
        if (rounds.Count == 0) return;
        var current = rounds.FindLastIndex(item => item.Ticks <= logicalTicks);
        if (current < 0) current = 0;
        var target = Math.Max(0, Math.Min(rounds.Count - 1, current + delta));
        SeekToTicks(rounds[target].Ticks);
    }

    internal static void AdvanceExportClock(float milliseconds)
    {
        if (!IsReadyForExport || paused) return;
        AdvanceClock(Math.Max(0f, milliseconds));
    }

    internal static void SetRenderHudVisible(bool visible) => scene?.SetHudVisible(visible);

    internal static void Stop() => BeginStop(MatchReplayExitKind.Cancelled);

    internal static void StopForModuleDisabled() => BeginStop(MatchReplayExitKind.ModuleDisabled);

    internal static void StopAfterExport(bool completed, string detail)
    {
        BeginStop(completed ? MatchReplayExitKind.ExportCompleted : MatchReplayExitKind.ExportFailed, detail);
    }

    private static bool TryPrepareCore(
        string recordId,
        bool showControls,
        long startSequence,
        bool returnToLibrary,
        out string message)
    {
        message = "";
        lastStartFailure = "";
        if (MatchReplaySessionState.Phase != MatchReplayLifecyclePhase.Idle)
            return Reject("已有对局正在准备或回放。", out message);
        if (!AuraToolsMatchRecordsRuntime.Enabled)
            return Reject("对局记录模块尚未开启。", out message);
        if (MatchReplayRecorder.IsRecording)
            return Reject("当前战斗仍在记录，不能同时启动回放。", out message);
        if (NetworkServer.active || NetworkClient.active)
            return Reject("请先离开联机会话再开始回放。", out message);
        if (!MatchReplaySessionState.TryBeginPreparation(recordId, returnToLibrary, out message)) return false;

        try
        {
            preparationStatus = "正在校验 Replay Document v12...";
            var loadedRecord = MatchRecordStorage.Database.Get(recordId)
                               ?? throw new InvalidOperationException("找不到这条对局记录。");
            if (loadedRecord.ReplayProtocol != ReplayProtocolV12.DocumentVersion
                || !string.Equals(loadedRecord.ReplayState, MatchReplayStates.Ready, StringComparison.Ordinal))
                throw new InvalidOperationException("该记录不是可播放的 Replay Document v12；旧记录仅保留摘要与分析。");
            var loadedEnvelope = MatchRecordStorage.Database.LoadV12(recordId, loadAssetPayloads: true)
                                 ?? throw new InvalidOperationException("Replay Document v12 数据不存在。");
            var validation = ReplayDocumentValidatorV12.Validate(loadedEnvelope);
            if (!validation.IsValid)
                throw new InvalidOperationException("回放完整性校验失败：" + validation.Message);
            ValidateRequiredAssets(loadedEnvelope.Document);

            ReplayPovSidecarV12? loadedPov = null;
            try
            {
                loadedPov = MatchRecordStorage.Database.LoadFirstPovV12(recordId, loadAssetPayloads: true);
                if (loadedPov != null
                    && !string.Equals(loadedPov.ParentDocumentRoot, loadedEnvelope.DeclaredDocumentRoot, StringComparison.OrdinalIgnoreCase))
                    loadedPov = null;
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[MatchRecords] optional POV sidecar ignored: " + ex.Message);
            }

            record = loadedRecord;
            envelope = loadedEnvelope;
            pov = loadedPov;
            events = loadedEnvelope.Document.TruthEvents
                .Concat(loadedEnvelope.Document.PresentationEvents)
                .OrderBy(item => item.Sequence)
                .ToList();
            actionTransactions = events
                .Where(item => item.EventType == ReplayEventTypesV12.TransactionStarted
                               && item.Transaction != null
                               && IsAction(item.Transaction.Kind))
                .Select(item => item.TransactionId)
                .ToHashSet(StringComparer.Ordinal);
            durationTicks = CalculateDurationTicks(events);
            pendingStartTicks = ResolveStartTicks(events, startSequence);
            eventIndex = 0;
            completedActionCount = 0;
            logicalTicks = 0;
            controlsVisible = showControls;
            externalClock = !showControls;
            paused = false;
            seeking = false;
            seekPreviewing = false;
            seekPreviewProgress = 0f;
            runtimeReady = false;
            playbackHealth = "Compatible";
            playbackIssue = loadedPov == null ? "" : "POV";
            failedEventCount = 0;
            Reducer.Reset(loadedEnvelope.Document.InitialState);
            MatchReplaySessionState.MarkPrepared();
            preparationStatus = "v12 已校验，等待创建独立回放场景。";
            message = preparationStatus;
            return true;
        }
        catch (Exception ex)
        {
            lastStartFailure = ex.Message;
            AuraToolsLog.Warn("[MatchRecords] replay preparation rejected: record=" + recordId + ", reason=" + ex.Message);
            var decision = MatchReplaySessionState.BeginExit(MatchReplayExitKind.StartFailed, ex.Message);
            ResetPlaybackState();
            MatchReplaySessionState.CompleteExit();
            if (!decision.ReturnToLibrary) MatchReplayReturnCoordinator.Clear();
            message = ex.Message;
            return false;
        }
    }

    private static void AdvanceClock(float milliseconds)
    {
        var deltaTicks = (long)Math.Round(milliseconds * ReplayProtocolV12.TimebaseTicksPerSecond / 1000d);
        logicalTicks = Math.Min(durationTicks, logicalTicks + Math.Max(0L, deltaTicks));
        ExecuteDueEvents(suppressAudio: externalClock);
        scene?.Tick(logicalTicks);
        if (IsFinished)
        {
            paused = true;
            scene?.SetPaused(true);
        }
        RefreshControls();
    }

    private static void ExecuteDueEvents(bool suppressAudio)
    {
        while (eventIndex < events.Count && events[eventIndex].TimeTicks <= logicalTicks)
        {
            try
            {
                var value = events[eventIndex];
                if (value.Lane == ReplayJournalLanesV12.Truth)
                {
                    Reducer.Apply(value);
                    scene?.ApplyState(Reducer.Current);
                    if (value.EventType == ReplayEventTypesV12.TransactionCompleted
                        && actionTransactions.Contains(value.TransactionId))
                        completedActionCount++;
                }
                else
                {
                    scene?.ApplyPresentation(value, Reducer.Current, suppressAudio);
                }
                eventIndex++;
                ApplyPovThrough(value.Sequence);
            }
            catch (Exception ex)
            {
                failedEventCount++;
                playbackHealth = "Desynced";
                playbackIssue = "回放在事件 " + events[eventIndex].Sequence + " 停止：" + ex.Message;
                paused = true;
                scene?.SetPaused(true);
                AuraToolsLog.Error("[MatchRecords] portable replay event failed", ex);
                break;
            }
        }
    }

    private static void SeekToTicks(long targetTicks)
    {
        if (envelope == null || scene == null || HasBlockingError) return;
        seeking = true;
        seekPreviewing = false;
        var wasPaused = paused;
        scene.SetPaused(true);
        try
        {
            var target = Math.Max(0L, Math.Min(durationTicks, targetTicks));
            var targetSequence = events.Where(item => item.TimeTicks <= target)
                .Select(item => item.Sequence)
                .DefaultIfEmpty(0L)
                .Max();
            var truthCheckpoint = envelope.Document.TruthCheckpoints
                .Where(item => item.EventSequence <= targetSequence)
                .OrderBy(item => item.EventSequence)
                .LastOrDefault();
            var presentationCheckpoint = envelope.Document.PresentationCheckpoints
                .Where(item => item.EventSequence <= (truthCheckpoint?.EventSequence ?? 0L))
                .OrderBy(item => item.EventSequence)
                .LastOrDefault();
            var checkpointSequence = truthCheckpoint?.EventSequence ?? 0L;
            var lastTruthSequence = events.Where(item => item.Lane == ReplayJournalLanesV12.Truth
                                                          && item.Sequence <= checkpointSequence)
                .Select(item => item.Sequence)
                .DefaultIfEmpty(0L)
                .Max();
            Reducer.Reset(truthCheckpoint?.State ?? envelope.Document.InitialState, lastTruthSequence);
            eventIndex = events.FindIndex(item => item.Sequence > checkpointSequence);
            if (eventIndex < 0) eventIndex = events.Count;
            logicalTicks = truthCheckpoint?.TimeTicks ?? 0L;
            scene.Tick(logicalTicks);
            scene.Restore(Reducer.Current, presentationCheckpoint);
            ResetPovThrough(checkpointSequence);
            completedActionCount = events.Take(eventIndex)
                .Count(item => item.EventType == ReplayEventTypesV12.TransactionCompleted
                               && actionTransactions.Contains(item.TransactionId));
            logicalTicks = target;
            ExecuteDueEvents(suppressAudio: true);
            scene.RestoreTimedPresentationsAt(
                envelope.Document.PresentationEvents,
                logicalTicks,
                includeAudio: !externalClock);
            scene.Tick(logicalTicks);
            seekPreviewProgress = durationTicks <= 0 ? 0f : Clamp01(logicalTicks / (float)durationTicks);
        }
        catch (Exception ex)
        {
            failedEventCount++;
            playbackHealth = "Failed";
            playbackIssue = "定位回放失败：" + ex.Message;
            paused = true;
            AuraToolsLog.Error("[MatchRecords] portable replay seek failed", ex);
        }
        finally
        {
            seeking = false;
            if (!HasBlockingError) paused = wasPaused;
            scene.SetPaused(paused);
            RefreshControls();
        }
    }

    private static void BeginStop(MatchReplayExitKind kind, string detail = "")
    {
        if (resetting) return;
        if (!IsActive && !MatchReplaySessionState.IsPlayback)
        {
            MatchReplayControlsPresenter.Close();
            MatchReplayReturnCoordinator.Clear();
            return;
        }
        resetting = true;
        var decision = MatchReplaySessionState.BeginExit(kind, detail);
        UiTransitionGuardRuntime.BeginTransition(null, AuraToolsIds.ModId, "Match replay stop", 4);
        MatchReplayControlsPresenter.Close();
        try { scene?.Dispose(); }
        catch (Exception ex) { AuraToolsLog.Error("[MatchRecords] replay scene cleanup failed", ex); }
        ResetPlaybackState();
        var finalizer = AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(CompleteStopAfterUnityDestroy(decision));
        if (finalizer == null)
        {
            MatchReplaySessionState.CompleteExit();
            resetting = false;
            if (decision.ReturnToLibrary) MatchReplayReturnCoordinator.ReturnToLibrary(decision.Message);
            else MatchReplayReturnCoordinator.Clear();
        }
    }

    private static System.Collections.IEnumerator CompleteStopAfterUnityDestroy(MatchReplayExitDecision decision)
    {
        yield return null;
        MatchReplaySessionState.CompleteExit();
        resetting = false;
        UiTransitionGuardRuntime.ScrubNow(null, AuraToolsIds.ModId, "Match replay stop");
        UiTransitionGuardRuntime.RecoverNativeInput(null, AuraToolsIds.ModId, "Match replay stop", 8);
        if (decision.ReturnToLibrary) MatchReplayReturnCoordinator.ReturnToLibrary(decision.Message);
        else MatchReplayReturnCoordinator.Clear();
    }

    private static void ResetPlaybackState()
    {
        scene = null;
        record = null;
        envelope = null;
        pov = null;
        events.Clear();
        actionTransactions.Clear();
        eventIndex = 0;
        povEventIndex = 0;
        completedActionCount = 0;
        logicalTicks = 0;
        durationTicks = 0;
        pendingStartTicks = 0;
        paused = false;
        seeking = false;
        seekPreviewing = false;
        seekPreviewProgress = 0f;
        controlsVisible = false;
        externalClock = false;
        runtimeReady = false;
        playbackHealth = "Compatible";
        playbackIssue = "";
        preparationStatus = "";
        failedEventCount = 0;
        Reducer.Reset(new ReplayPublicStateV12());
        PovReducer.Reset();
    }

    private static void ResetPovThrough(long canonicalSequence)
    {
        PovReducer.Reset();
        povEventIndex = 0;
        ApplyPovThrough(canonicalSequence);
    }

    private static void ApplyPovThrough(long canonicalSequence)
    {
        if (pov == null)
        {
            scene?.ApplyPovCards(Array.Empty<ReplayPublicCardStateV12>());
            return;
        }
        while (povEventIndex < pov.Events.Count
               && pov.Events[povEventIndex].CanonicalSequence <= canonicalSequence)
            PovReducer.Apply(pov.Events[povEventIndex++]);
        scene?.ApplyPovCards(PovReducer.Cards);
    }

    private static void ValidateRequiredAssets(ReplayDocumentV12 document)
    {
        foreach (var asset in document.Assets)
        {
            var error = ReplayAssetContractV12.Validate(asset, requirePayload: true);
            if (error.Length > 0)
                throw new InvalidOperationException("回放内嵌资源缺失或损坏：" + asset.Sha256 + "，" + error);
        }
    }

    private static bool IsAction(string kind)
    {
        return kind == ReplayTransactionKindsV12.Card
               || kind == ReplayTransactionKindsV12.Skill
               || kind == ReplayTransactionKindsV12.Intent
               || kind == ReplayTransactionKindsV12.Passive
               || kind == ReplayTransactionKindsV12.Transform
               || kind == ReplayTransactionKindsV12.ImplicitNative;
    }

    private static long CalculateDurationTicks(IEnumerable<ReplayJournalEventV12> values)
    {
        var maximum = 0L;
        foreach (var value in values)
        {
            var duration = Math.Max(0L, value.Presentation?.DurationTicks ?? 0L);
            if (value.Presentation?.Audio is { } audio && audio.DurationSamples > 0)
                duration = Math.Max(duration, audio.DurationSamples * ReplayProtocolV12.TimebaseTicksPerSecond / 48_000L);
            var delay = Math.Max(0L, value.Presentation?.DelayTicks ?? 0L);
            maximum = Math.Max(maximum, value.TimeTicks + delay + duration);
        }
        return maximum == 0 ? 0 : maximum + ReplayProtocolV12.TimebaseTicksPerSecond / 2;
    }

    private static long ResolveStartTicks(IEnumerable<ReplayJournalEventV12> values, long sequence)
    {
        if (sequence <= 0) return 0;
        return values.Where(item => item.Sequence >= sequence)
            .OrderBy(item => item.Sequence)
            .Select(item => item.TimeTicks)
            .DefaultIfEmpty(0L)
            .First();
    }

    private static bool Reject(string reason, out string message)
    {
        message = reason;
        lastStartFailure = reason;
        return false;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

    private static void RefreshControls()
    {
        if (controlsVisible) MatchReplayControlsPresenter.Refresh();
    }
}
