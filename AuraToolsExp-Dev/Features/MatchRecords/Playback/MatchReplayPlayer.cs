using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraReplay.Presentation.Shared;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using Mirror;
using UiTransitionGuardShared;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Drives the v17 visible-state and observed-presentation journals through an
/// isolated native-prefab presentation scene. It never runs gameplay scripts.
/// </summary>
internal static class MatchReplayPlayer
{
    private static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };
    private static readonly ReplayStateReducerV17 Reducer = new();
    private static readonly ReplayStateReducerV17 VisualReducer = new();
    private static MatchRecord? record;
    private static ReplayDocumentEnvelopeV17? envelope;
    private static ReplayBattleSceneRuntimeV17? scene;
    private static List<ReplayJournalEventV17> events = new();
    private static HashSet<string> actionTransactions = new(StringComparer.Ordinal);
    private static HashSet<long> deferredVisualTruthSequences = new();
    private static Dictionary<long, ReplayVisibleStateV17> visualCommitStates = new();
    private static int eventIndex;
    private static int completedActionCount;
    private static long lastProcessedTruthSequence;
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
    internal static long DurationMilliseconds => durationTicks * 1000L / ReplayProtocolV17.TimebaseTicksPerSecond;
    internal static IReadOnlyList<ReplayJournalEventV17> Events => events;
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
    internal static bool RenderHudVisible => scene?.HudVisible ?? true;

    internal static bool TryPrepareInteractive(ReplayLoadedRecord data, long startSequence, out string message)
    {
        return TryPrepareCore(data, showControls: true, startSequence, returnToLibrary: true, out message);
    }

    internal static bool TryPrepareForExport(ReplayLoadedRecord data, bool returnToLibrary, out string message)
    {
        return TryPrepareCore(data, showControls: false, startSequence: 0, returnToLibrary, out message);
    }

    internal static bool TryStartForExport(ReplayLoadedRecord data, out string message)
    {
        if (!TryPrepareForExport(data, returnToLibrary: false, out message)) return false;
        var activation = AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(
            ActivatePreparedAfterRenderBarrier());
        if (activation != null)
        {
            message = "回放首帧已生成，正在等待游戏主渲染帧确认。";
            return true;
        }
        message = "无法调度回放主渲染帧确认。";
        FailPreparedStart(message);
        return false;
    }

    internal static void CommitOrigin() => MatchReplaySessionState.CommitOrigin();

    internal static bool TryActivatePrepared(out string message)
    {
        message = "";
        if (MatchReplaySessionState.Phase != MatchReplayLifecyclePhase.Prepared
            || envelope == null
            || scene == null
            || !scene.IsActivationReady)
        {
            message = "回放数据、首帧或游戏主渲染帧确认尚未准备完成。";
            return false;
        }

        try
        {
            scene.ActivateDisplay(controlsVisible);
            runtimeReady = true;
            MatchReplaySessionState.MarkActive();
            if (controlsVisible) MatchReplayControlsPresenter.Show();
            if (pendingStartTicks > 0) SeekToTicks(pendingStartTicks);
            else
            {
                scene.RestoreTimedPresentationsAt(
                    envelope.Document.PresentationEvents,
                    logicalTicks,
                    includeAudio: !externalClock);
                scene.Tick(logicalTicks);
            }
            preparationStatus = "";
            scene.SetPlaybackSpeed(Speed);
            scene.SetPaused(paused);
            if (controlsVisible) scene.RenderInteractive();
            AuraToolsLog.Info("[MatchRecords] perspective-instruction v17 replay started: record=" + record?.RecordId
                              + ", events=" + events.Count + ", actions=" + ActionCount
                              + ", scene=sanitized-native-prefabs, render=offscreen-manual-v17"
                              + ", renderer=auratools-dedicated-native-profile"
                              + ", gameplay-scripts=disabled.");
            RefreshControls();
            message = "开始回放。";
            return true;
        }
        catch (Exception ex)
        {
            lastStartFailure = "无法创建独立回放场景：" + ex.Message;
            AuraToolsLog.Error("[MatchRecords] perspective replay activation failed", ex);
            BeginStop(MatchReplayExitKind.RuntimeFailed, lastStartFailure);
            message = lastStartFailure;
            return false;
        }
    }

    internal static void FailCommittedStart(string detail) => BeginStop(MatchReplayExitKind.StartFailed, detail);

    internal static void FailPreparedStart(string detail) => BeginStop(MatchReplayExitKind.StartFailed, detail);

    internal static bool TryConfirmPreparedRenderBarrier(out string message)
    {
        message = "";
        if (MatchReplaySessionState.Phase != MatchReplayLifecyclePhase.Prepared
            || scene == null
            || !scene.IsPreflighted)
        {
            message = "回放首帧尚未进入游戏主渲染帧确认阶段。";
            return false;
        }
        try
        {
            scene.ConfirmFrameBarrier();
            preparationStatus = "v17 数据、独立 renderer、首帧和游戏主渲染帧均已通过预检。";
            message = preparationStatus;
            return true;
        }
        catch (Exception ex)
        {
            lastStartFailure = "游戏主渲染帧确认失败：" + ex.Message;
            AuraToolsLog.Error("[MatchRecords] replay render frame barrier failed", ex);
            message = lastStartFailure;
            return false;
        }
    }

    internal static void Tick()
    {
        if (!IsActive || !runtimeReady || externalClock || resetting) return;
        try
        {
            var elapsedMilliseconds = Math.Max(0f, Time.unscaledDeltaTime * 1000f);
            MatchReplayControlsPresenter.Tick(elapsedMilliseconds);
            if (!paused && !seeking && !HasBlockingError)
                AdvanceClock(elapsedMilliseconds * Speed);
            scene?.RenderInteractive();
        }
        catch (Exception ex)
        {
            failedEventCount++;
            playbackHealth = "Failed";
            playbackIssue = "回放离屏渲染失败：" + ex.Message;
            lastStartFailure = playbackIssue;
            paused = true;
            runtimeReady = false;
            AuraToolsLog.Error("[MatchRecords] replay render host failed; playback will stop", ex);
            BeginStop(MatchReplayExitKind.RuntimeFailed, playbackIssue);
        }
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
            .Select(group => new { Round = group.Key, Ticks = group.Min(PlaybackTicks) })
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

    internal static ReplayRenderExportLeaseV17 AcquireExportTarget(RenderTexture target)
    {
        return scene?.AcquireExportTarget(target)
               ?? throw new InvalidOperationException("独立回放渲染宿主不可用。");
    }

    internal static void Stop() => BeginStop(MatchReplayExitKind.Cancelled);

    internal static void StopForModuleDisabled() => BeginStop(MatchReplayExitKind.ModuleDisabled);

    internal static void StopAfterExport(bool completed, string detail)
    {
        BeginStop(completed ? MatchReplayExitKind.ExportCompleted : MatchReplayExitKind.ExportFailed, detail);
    }

    private static bool TryPrepareCore(
        ReplayLoadedRecord data,
        bool showControls,
        long startSequence,
        bool returnToLibrary,
        out string message)
    {
        message = "";
        lastStartFailure = "";
        var recordId = data.Record.RecordId;
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
            preparationStatus = "正在校验 Replay Document v17...";
            var loadedRecord = data.Record
                               ?? throw new InvalidOperationException("找不到这条对局记录。");
            if (loadedRecord.ReplayProtocol != ReplayProtocolV17.DocumentVersion
                || !string.Equals(loadedRecord.ReplayState, MatchReplayStates.Ready, StringComparison.Ordinal))
                throw new InvalidOperationException("该记录不是可播放的 Replay Document v17；旧记录仅保留摘要与分析。");
            var loadedEnvelope = data.Envelope
                                 ?? throw new InvalidOperationException("Replay Document v17 数据不存在。");
            ValidatePerspectiveRuntime(loadedEnvelope.Document);
            ValidatePresentationModules(loadedEnvelope.Document);

            record = loadedRecord;
            envelope = loadedEnvelope;
            events = loadedEnvelope.Document.TruthEvents
                .Concat(loadedEnvelope.Document.PresentationEvents)
                .OrderBy(PlaybackTicks)
                .ThenBy(item => item.Sequence)
                .ToList();
            actionTransactions = events
                .Where(item => item.EventType == ReplayEventTypesV17.TransactionStarted
                               && item.Transaction != null
                               && IsAction(item.Transaction.Kind))
                .Select(item => item.TransactionId)
                .ToHashSet(StringComparer.Ordinal);
            BuildVisualCommitIndex(loadedEnvelope.Document);
            durationTicks = CalculateDurationTicks(events);
            pendingStartTicks = ResolveStartTicks(events, startSequence);
            eventIndex = 0;
            completedActionCount = 0;
            lastProcessedTruthSequence = 0L;
            logicalTicks = 0;
            controlsVisible = showControls;
            externalClock = !showControls;
            paused = false;
            seeking = false;
            seekPreviewing = false;
            seekPreviewProgress = 0f;
            runtimeReady = false;
            playbackHealth = "Compatible";
            playbackIssue = "";
            failedEventCount = 0;
            Reducer.Reset(loadedEnvelope.Document.InitialState);
            VisualReducer.Reset(loadedEnvelope.Document.InitialState);
            MatchReplayUiLifecycle.PrepareForReplayView();
            scene = new ReplayBattleSceneRuntimeV17(loadedEnvelope.Document, includeHud: true);
            logicalTicks = 0;
            scene.Tick(0);
            scene.Restore(Reducer.Current, null);
            ExecuteDueEvents(suppressAudio: true);
            scene.Tick(0);
            if (HasBlockingError)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(playbackIssue) ? "回放首帧投影失败。" : playbackIssue);
            preparationStatus = "正在执行回放首帧离屏渲染预检...";
            scene.PreflightRender();
            MatchReplaySessionState.MarkPrepared();
            preparationStatus = "v17 数据、独立 renderer 与首帧已通过，等待游戏主渲染帧确认。";
            message = preparationStatus;
            return true;
        }
        catch (Exception ex)
        {
            lastStartFailure = ex.Message;
            AuraToolsLog.Warn("[MatchRecords] replay preparation rejected: record=" + recordId + ", reason=" + ex.Message);
            try { scene?.Dispose(); }
            catch (Exception cleanupError)
            {
                AuraToolsLog.Error("[MatchRecords] failed replay preparation cleanup failed", cleanupError);
            }
            var decision = MatchReplaySessionState.BeginExit(MatchReplayExitKind.StartFailed, ex.Message);
            ResetPlaybackState();
            MatchReplaySessionState.CompleteExit();
            if (!decision.ReturnToLibrary) MatchReplayReturnCoordinator.Clear();
            message = ex.Message;
            return false;
        }
    }

    private static System.Collections.IEnumerator ActivatePreparedAfterRenderBarrier()
    {
        yield return new WaitForEndOfFrame();
        if (!TryConfirmPreparedRenderBarrier(out var barrierMessage))
        {
            FailPreparedStart(barrierMessage);
            yield break;
        }
        if (!TryActivatePrepared(out var activationMessage))
            FailPreparedStart(activationMessage);
    }

    private static void AdvanceClock(float milliseconds)
    {
        var deltaTicks = (long)Math.Round(milliseconds * ReplayProtocolV17.TimebaseTicksPerSecond / 1000d);
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
        while (eventIndex < events.Count && PlaybackTicks(events[eventIndex]) <= logicalTicks)
        {
            try
            {
                var value = events[eventIndex];
                if (value.Lane == ReplayJournalLanesV17.Truth)
                {
                    Reducer.Apply(value);
                    lastProcessedTruthSequence = value.Sequence;
                    if (MutatesVisibleState(value) && !deferredVisualTruthSequences.Contains(value.Sequence))
                    {
                        VisualReducer.Apply(value, verifyHashes: false);
                        scene?.ApplyState(VisualReducer.Current);
                    }
                    if (value.EventType == ReplayEventTypesV17.TransactionCompleted
                        && actionTransactions.Contains(value.TransactionId))
                        completedActionCount++;
                }
                else
                {
                    if (value.EventType == ReplayEventTypesV17.VisualStateCommitted
                        && value.Presentation != null
                        && visualCommitStates.TryGetValue(value.Presentation.TruthEventSequence, out var committedState))
                    {
                        VisualReducer.Reset(committedState, lastProcessedTruthSequence);
                        scene?.ApplyState(VisualReducer.Current);
                    }
                    else scene?.ApplyPresentation(value, Reducer.Current, suppressAudio);
                }
                eventIndex++;
            }
            catch (Exception ex)
            {
                failedEventCount++;
                playbackHealth = "Desynced";
                playbackIssue = "回放在事件 " + events[eventIndex].Sequence + " 停止：" + ex.Message;
                paused = true;
                scene?.SetPaused(true);
                AuraToolsLog.Error("[MatchRecords] perspective replay event failed", ex);
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
            var truthCheckpoint = envelope.Document.TruthCheckpoints
                .Where(item => item.TimeTicks <= target)
                .OrderBy(item => item.TimeTicks)
                .ThenBy(item => item.EventSequence)
                .LastOrDefault();
            var checkpointSequence = truthCheckpoint?.EventSequence ?? 0L;
            var checkpointTruthSequence = envelope.Document.TruthEvents
                .Where(item => item.Sequence <= checkpointSequence)
                .Select(item => item.Sequence)
                .DefaultIfEmpty(0L)
                .Max();
            Reducer.Reset(truthCheckpoint?.State ?? envelope.Document.InitialState, checkpointTruthSequence);
            foreach (var truth in envelope.Document.TruthEvents
                         .Where(item => item.Sequence > checkpointTruthSequence && item.TimeTicks <= target)
                         .OrderBy(item => item.Sequence))
                Reducer.Apply(truth);
            var lastTruthSequence = envelope.Document.TruthEvents
                .Where(item => item.TimeTicks <= target)
                .Select(item => item.Sequence)
                .DefaultIfEmpty(0L)
                .Max();
            lastProcessedTruthSequence = lastTruthSequence;
            var visualState = VisualStateAtTicks(target);
            VisualReducer.Reset(visualState, lastTruthSequence);
            eventIndex = events.FindIndex(item => PlaybackTicks(item) > target);
            if (eventIndex < 0) eventIndex = events.Count;
            logicalTicks = target;
            scene.Restore(visualState, PresentationBindingsAtTicks(target, visualState));
            completedActionCount = events.Take(eventIndex)
                .Count(item => item.EventType == ReplayEventTypesV17.TransactionCompleted
                               && actionTransactions.Contains(item.TransactionId));
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
            AuraToolsLog.Error("[MatchRecords] perspective replay seek failed", ex);
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
        events.Clear();
        actionTransactions.Clear();
        deferredVisualTruthSequences.Clear();
        visualCommitStates.Clear();
        eventIndex = 0;
        completedActionCount = 0;
        lastProcessedTruthSequence = 0L;
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
        Reducer.Reset(new ReplayVisibleStateV17());
        VisualReducer.Reset(new ReplayVisibleStateV17());
    }

    private static void BuildVisualCommitIndex(ReplayDocumentV17 document)
    {
        deferredVisualTruthSequences = document.PresentationEvents
            .Where(item => item.EventType == ReplayEventTypesV17.VisualStateCommitted
                           && item.Presentation?.TruthEventSequence > 0)
            .Select(item => item.Presentation!.TruthEventSequence)
            .ToHashSet();
        visualCommitStates = new Dictionary<long, ReplayVisibleStateV17>();
        var reducer = new ReplayStateReducerV17();
        reducer.Reset(document.InitialState);
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence))
        {
            reducer.Apply(value);
            if (deferredVisualTruthSequences.Contains(value.Sequence))
                visualCommitStates[value.Sequence] = ReplayStateReducerV17.Normalize(reducer.Current);
        }
    }

    private static ReplayVisibleStateV17 VisualStateAtTicks(long ticks)
    {
        if (envelope == null || ticks < 0) return envelope?.Document.InitialState ?? new ReplayVisibleStateV17();
        var truthReducer = new ReplayStateReducerV17();
        var visualReducer = new ReplayStateReducerV17();
        truthReducer.Reset(envelope.Document.InitialState);
        visualReducer.Reset(envelope.Document.InitialState);
        var visual = ReplayStateReducerV17.Normalize(envelope.Document.InitialState);
        var combined = envelope.Document.TruthEvents.Cast<ReplayJournalEventV17>()
            .Concat(envelope.Document.PresentationEvents)
            .Where(item => PlaybackTicks(item) <= ticks)
            .OrderBy(PlaybackTicks)
            .ThenBy(item => item.Sequence);
        var lastTruthSequence = 0L;
        foreach (var value in combined)
        {
            if (value.Lane == ReplayJournalLanesV17.Truth)
            {
                truthReducer.Apply(value);
                lastTruthSequence = value.Sequence;
                if (MutatesVisibleState(value) && !deferredVisualTruthSequences.Contains(value.Sequence))
                {
                    visualReducer.Apply(value, verifyHashes: false);
                    visual = ReplayStateReducerV17.Normalize(visualReducer.Current);
                }
            }
            else if (value.EventType == ReplayEventTypesV17.VisualStateCommitted
                     && value.Presentation != null
                     && visualCommitStates.TryGetValue(value.Presentation.TruthEventSequence, out var committed))
            {
                visualReducer.Reset(committed, lastTruthSequence);
                visual = ReplayStateReducerV17.Normalize(committed);
            }
        }
        return visual;
    }

    private static ReplayPresentationCheckpointV17 PresentationBindingsAtTicks(
        long ticks,
        ReplayVisibleStateV17 state)
    {
        if (envelope == null) return new ReplayPresentationCheckpointV17 { TimeTicks = ticks };
        var active = state.Entities
            .Select(item => item.EntityId + "|" + item.SpawnGeneration)
            .ToHashSet(StringComparer.Ordinal);
        var bindings = envelope.Document.PresentationEvents
            .Where(item => PlaybackTicks(item) <= ticks && item.Presentation?.EntityBinding != null)
            .OrderBy(PlaybackTicks)
            .ThenBy(item => item.Sequence)
            .Select(item => item.Presentation!.EntityBinding!)
            .GroupBy(item => item.EntityId + "|" + item.SpawnGeneration, StringComparer.Ordinal)
            .Select(group => group.Last())
            .Where(item => active.Contains(item.EntityId + "|" + item.SpawnGeneration))
            .Select(ReplayCanonicalJsonV17.Clone)
            .ToList();
        return new ReplayPresentationCheckpointV17
        {
            TimeTicks = ticks,
            EntityBindings = bindings
        };
    }

    private static void ValidatePerspectiveRuntime(ReplayDocumentV17 document)
    {
        var currentGameBuild = ReplayResourceCompatibilityApi.CurrentGameBuild;
        if (!string.Equals(document.Header.GameBuildProvenance, currentGameBuild, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "该记录的战斗资源版本与当前游戏不一致：recorded="
                + document.Header.GameBuildProvenance + "，current=" + currentGameBuild + "。");
        if (string.IsNullOrWhiteSpace(document.Header.PerspectivePlayerId)
            || !string.Equals(document.Header.PerspectivePlayerId, document.InitialState.PerspectivePlayerId, StringComparison.Ordinal))
            throw new InvalidOperationException("回放固定视角身份缺失或与初始可见状态不一致。");
    }

    private static void ValidatePresentationModules(ReplayDocumentV17 document)
    {
        var missing = ReplayPresentationModuleCompatibilityV17.FindUnsatisfied(
            document.Presentation.Modules, AuraReplayPresentationRuntime.SnapshotModules());
        if (missing != null)
            throw new InvalidOperationException(
                "回放缺少兼容的必要表现模块：" + missing.OwnerModId + "/" + missing.TypeId
                + " schema=" + missing.SchemaVersion + " capability=" + missing.RendererCapability + "。");
    }

    private static bool IsAction(string kind)
    {
        return kind == ReplayTransactionKindsV17.Card
               || kind == ReplayTransactionKindsV17.Skill
               || kind == ReplayTransactionKindsV17.Intent
               || kind == ReplayTransactionKindsV17.Passive
               || kind == ReplayTransactionKindsV17.Transform
               || kind == ReplayTransactionKindsV17.ImplicitObserved;
    }

    private static long CalculateDurationTicks(IEnumerable<ReplayJournalEventV17> values)
    {
        var maximum = 0L;
        foreach (var value in values)
        {
            var duration = Math.Max(0L, value.Presentation?.DurationTicks ?? 0L);
            if (value.Presentation?.Audio is { } audio && audio.DurationSamples > 0)
                duration = Math.Max(duration, audio.DurationSamples * ReplayProtocolV17.TimebaseTicksPerSecond / 48_000L);
            maximum = Math.Max(maximum, PlaybackTicks(value) + duration);
        }
        return maximum == 0 ? 0 : maximum + ReplayProtocolV17.TimebaseTicksPerSecond / 2;
    }

    private static long ResolveStartTicks(IEnumerable<ReplayJournalEventV17> values, long sequence)
    {
        if (sequence <= 0) return 0;
        return values.Where(item => item.Sequence >= sequence)
            .OrderBy(item => item.Sequence)
            .Select(PlaybackTicks)
            .DefaultIfEmpty(0L)
            .First();
    }

    private static long PlaybackTicks(ReplayJournalEventV17 value) =>
        ReplayPresentationTimingV17.EffectiveTimeTicks(value);

    private static bool MutatesVisibleState(ReplayJournalEventV17 value) =>
        value.EventType == ReplayEventTypesV17.EntitySpawned
        || value.EventType == ReplayEventTypesV17.EntityDespawned
        || value.EventType == ReplayEventTypesV17.StateDeltaApplied;

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
