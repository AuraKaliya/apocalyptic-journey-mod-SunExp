using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Presentation;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;
using UiTransitionGuardShared;
using Witch.Core;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayPlayer
{
    private static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };
    private static readonly MatchReplayReadModel ReadModel = new();
    private static MatchRecord? record;
    private static ReplayDocumentV11? nativeDocument;
    private static ReplayNativeAudioPlayer? nativeAudio;
    private static List<MatchReplayEvent> events = new();
    private static List<long> timeline = new();
    private static MatchReplayActionTimeline actionTimeline = MatchReplayActionTimeline.Build(Array.Empty<MatchReplayEvent>());
    private static int eventIndex;
    private static int pendingStartIndex;
    private static float playbackClock;
    private static int speedIndex = 1;
    private static bool paused;
    private static bool seeking;
    private static bool seekPreviewing;
    private static float seekPreviewProgress;
    private static bool resetting;
    private static bool controlsVisible;
    private static bool externalClock;
    private static bool runtimeReady;
    private static CanvasGroup? fightCanvasGroup;
    private static string playbackHealth = MatchReplayCompatibilityLevels.Compatible;
    private static string playbackIssue = "";
    private static string preparationStatus = "";
    private static string preparedKindsDescription = "";
    private static string preparedScene = "";
    private static string preparedAdapter = "";
    private static string lastStartFailure = "";
    private static int failedEventCount;
    private static PendingVisualProjection? pendingVisualProjection;

    internal static bool IsActive => record != null;
    internal static bool IsPaused => paused;
    internal static bool IsRuntimeReady => runtimeReady;
    internal static float Speed => Speeds[speedIndex];
    internal static int EventIndex => eventIndex;
    internal static int EventCount => events.Count;
    internal static int ActionCount => actionTimeline.Count;
    internal static int CompletedActionCount => actionTimeline.CompletedActionsAtEventIndex(eventIndex);
    internal static bool IsSeeking => seeking;
    internal static bool IsFinished => IsActive
                                       && eventIndex >= events.Count
                                       && playbackClock >= DurationMilliseconds;
    internal static float Progress => seekPreviewing || seeking
        ? seekPreviewProgress
        : actionTimeline.Count == 0
            ? 0f
            : Math.Max(0f, Math.Min(1f, CompletedActionCount / (float)actionTimeline.Count));
    internal static long DurationMilliseconds => timeline.Count == 0
        ? 0
        : timeline[timeline.Count - 1]
          + MatchReplayPresentationSchedule.Duration(
              events[events.Count - 1],
              AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay.PresentationMode);
    internal static IReadOnlyList<MatchReplayEvent> Events => events;
    internal static bool IsReadyForExport => IsActive
                                             && runtimeReady
                                             && externalClock
                                             && !seeking
                                             && !HasBlockingError;
    internal static string PlaybackHealth => playbackHealth;
    internal static string PlaybackIssue => playbackIssue;
    internal static string PreparationStatus => preparationStatus;
    internal static string LastStartFailure => lastStartFailure;
    internal static int FailedEventCount => failedEventCount;
    internal static bool HasBlockingError => playbackHealth == "Desynced" || playbackHealth == "Failed";
    internal static int CurrentTurn => runtimeReady ? ReadModel.CurrentTurn : 1;
    internal static int TurnCount => record?.TurnCount ?? 0;

    internal static bool TryPrepareInteractive(
        string recordId,
        long startSequence,
        out string message)
    {
        return TryPrepareCore(recordId, true, startSequence, true, out message);
    }

    internal static bool TryPrepareForExport(
        string recordId,
        bool returnToLibrary,
        out string message)
    {
        return TryPrepareCore(recordId, false, 0, returnToLibrary, out message);
    }

    internal static bool TryStartForExport(string recordId, out string message)
    {
        if (!TryPrepareForExport(recordId, false, out message))
        {
            return false;
        }

        return TryActivatePrepared(out message);
    }

    internal static void CommitOrigin()
    {
        MatchReplaySessionState.CommitOrigin();
    }

    internal static bool TryActivatePrepared(out string message)
    {
        message = "";
        if (MatchReplaySessionState.Phase != MatchReplayLifecyclePhase.Prepared)
        {
            message = "回放原生视图尚未准备完成。";
            return false;
        }

        try
        {
            if (FightManager.Instance == null)
            {
                throw new InvalidOperationException("Prepared replay FightManager is unavailable.");
            }

            MatchReplayFightSandboxInitializer.Activate(FightManager.Instance);
            var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI")
                          ?? throw new InvalidOperationException("Prepared FightUI is unavailable.");
            if (!externalClock && nativeDocument != null)
            {
                nativeAudio = new ReplayNativeAudioPlayer(nativeDocument, fightUi.transform);
            }

            MatchReplayPresentationDirector.ShowTurn(ReadModel.CurrentTurn, ResolveRecordedPlayerId());
            if (controlsVisible)
            {
                MatchReplayControlsPresenter.Show();
            }

            runtimeReady = true;
            MatchReplaySessionState.MarkActive();
            preparationStatus = "";
            if (pendingStartIndex > 0)
            {
                var target = pendingStartIndex;
                pendingStartIndex = 0;
                SeekToIndex(target);
            }

            AuraToolsLog.Info("[MatchRecords] v" + record?.ReplayProtocol + " replay started: record=" + record?.RecordId
                              + ", events=" + events.Count
                              + ", kinds=" + preparedKindsDescription
                              + ", compatibility=" + playbackHealth
                              + ", mode=native-view-projection"
                              + ", scene=" + preparedScene
                              + ", adapter=" + preparedAdapter + ".");
            RefreshControls();
            message = "开始回放。";
            return true;
        }
        catch (Exception ex)
        {
            var failure = "无法显示已准备的回放：" + ex.Message;
            lastStartFailure = failure;
            AuraToolsLog.Error("[MatchRecords] replay activation failed: " + ex.Message, ex);
            BeginStop(MatchReplayExitKind.RuntimeFailed, failure);
            message = failure;
            return false;
        }
    }

    internal static void FailCommittedStart(string detail)
    {
        BeginStop(MatchReplayExitKind.RuntimeFailed, detail);
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
        {
            return Reject(
                recordId,
                MatchReplaySessionState.IsExiting ? "previous-replay-exiting" : "already-active",
                MatchReplaySessionState.IsExiting
                    ? "上一场回放仍在退出并恢复对局记录页面，请稍候再试。"
                    : "已有对局正在准备或回放。",
                out message);
        }

        if (!AuraToolsMatchRecordsRuntime.Enabled)
        {
            return Reject(recordId, "module-disabled", "对局记录模块尚未开启。", out message);
        }

        if (FightManager.Instance != null && FightManager.Instance.fightType != FightType.None)
        {
            return Reject(recordId, "fight-active", "当前战斗尚未结束，请先返回主菜单再开始回放。", out message);
        }
        if (NetworkServer.active || NetworkClient.active)
        {
            return Reject(recordId, "network-active", "请先离开联机大厅；原生回放不会启动或复用网络会话。", out message);
        }

        var lifecycleStarted = false;
        try
        {
            if (!ReplayNativeDocumentAdapter.TryLoad(recordId, out var nativeLoad, out var loadMessage))
            {
                return Reject(recordId, "native-document-incompatible", loadMessage, out message);
            }
            var loaded = nativeLoad.Record;
            var decoded = nativeLoad.Events;
            var loadedDocument = nativeLoad.Document;

            var metadataCompatibility = MatchReplayCompatibility.Evaluate(loaded);
            if (!metadataCompatibility.CanPlay)
            {
                return Reject(recordId, "metadata-incompatible", metadataCompatibility.Message, out message);
            }

            var quality = MatchReplayCaptureQuality.Evaluate(decoded);
            if (!quality.CanPlay)
            {
                MatchRecordStorage.Database.UpdateReplayState(recordId, MatchReplayStates.Incomplete);
                return Reject(recordId, "capture-incomplete", quality.Message, out message);
            }

            var compatibility = MatchReplayCompatibility.Evaluate(loaded, decoded);
            if (!compatibility.CanPlay)
            {
                return Reject(recordId, "event-stream-incompatible", compatibility.Message, out message);
            }

            var decodedActionTimeline = MatchReplayActionTimeline.Build(decoded);
            if (decodedActionTimeline.Count == 0)
            {
                return Reject(recordId, "action-frame-missing", "回放没有可定位的权威动作帧。", out message);
            }

            if (loaded.InitialState?.BaselineState == null)
            {
                return Reject(recordId, "baseline-missing", "回放缺少初始状态基线。", out message);
            }

            if (!MatchReplaySessionState.TryBeginPreparation(
                    recordId,
                    returnToLibrary,
                    out var lifecycleMessage))
            {
                return Reject(recordId, "lifecycle-busy", lifecycleMessage, out message);
            }
            lifecycleStarted = true;

            record = loaded;
            nativeDocument = loadedDocument;
            events = decoded;
            timeline = MatchReplayPresentationSchedule.Build(
                decoded,
                AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay.PresentationMode);
            actionTimeline = decodedActionTimeline;
            eventIndex = 0;
            pendingStartIndex = startSequence <= 0 ? 0 : events.FindIndex(item => item.Sequence >= startSequence);
            if (pendingStartIndex < 0)
            {
                pendingStartIndex = events.Count;
            }

            playbackClock = 0f;
            paused = false;
            speedIndex = 1;
            controlsVisible = showControls;
            externalClock = !showControls;
            runtimeReady = false;
            seeking = false;
            seekPreviewing = false;
            playbackHealth = compatibility.Level;
            playbackIssue = "";
            preparationStatus = "正在构造只读原生战斗视图";
            preparedKindsDescription = quality.DescribeCounts();
            failedEventCount = 0;
            MatchReplayUiLifecycle.PrepareForReplayView();
            MatchReplayEnvironmentScope.CaptureAndInstallRoleTable(record.InitialState);
            PrepareNativeView();
            MatchReplaySessionState.MarkPrepared();

            message = "回放原生视图已准备完成。";
            return true;
        }
        catch (Exception ex)
        {
            var failure = "无法开始回放：" + ex.Message;
            message = failure;
            AuraToolsLog.Error("[MatchRecords] replay initialization failed: record=" + recordId + ", error=" + ex.Message, ex);
            if (lifecycleStarted)
            {
                BeginStop(MatchReplayExitKind.StartFailed, failure);
            }
            lastStartFailure = failure;
            return false;
        }
    }

    internal static void Tick()
    {
        if (!IsActive || resetting)
        {
            return;
        }

        if (controlsVisible)
        {
            MatchReplayControlsPresenter.Tick(Time.unscaledDeltaTime * 1000f);
        }

        if (!runtimeReady)
        {
            RefreshControls();
            return;
        }

        if (HasBlockingError)
        {
            if (externalClock)
            {
                return;
            }
            BeginStop(MatchReplayExitKind.RuntimeFailed, playbackIssue);
            return;
        }

        if (!externalClock && IsFinished)
        {
            BeginStop(MatchReplayExitKind.Completed);
            return;
        }

        FreezeBattleRuntime();
        if (externalClock || seeking)
        {
            return;
        }

        if (paused)
        {
            SyncAudio();
            RefreshControls();
            return;
        }

        var elapsed = Time.unscaledDeltaTime * 1000f * Speed;
        playbackClock += elapsed;
        MatchReplayPresentationDirector.Tick(elapsed);
        ExecuteDueEvents(maximum: 16);
        FlushPendingVisualProjection();
        SyncAudio();
        if (HasBlockingError)
        {
            BeginStop(MatchReplayExitKind.RuntimeFailed, playbackIssue);
            return;
        }
        if (eventIndex >= events.Count && playbackClock >= DurationMilliseconds)
        {
            BeginStop(MatchReplayExitKind.Completed);
            return;
        }

        RefreshControls();
    }

    internal static void TogglePause()
    {
        if (!runtimeReady)
        {
            return;
        }

        paused = !paused;
        RefreshControls();
    }

    internal static void CycleSpeed()
    {
        if (!runtimeReady)
        {
            return;
        }

        speedIndex = (speedIndex + 1) % Speeds.Length;
        RefreshControls();
    }

    internal static void SeekNormalized(float value)
    {
        if (!runtimeReady || actionTimeline.Count == 0)
        {
            return;
        }

        var completed = Math.Max(0, Math.Min(
            actionTimeline.Count,
            (int)Math.Round(Math.Max(0f, Math.Min(1f, value)) * actionTimeline.Count)));
        SeekToIndex(actionTimeline.EventIndexForCompletedActions(completed, events.Count));
    }

    internal static void BeginSeekPreview(float value)
    {
        if (!runtimeReady || seeking)
        {
            return;
        }

        seekPreviewing = true;
        seekPreviewProgress = Math.Max(0f, Math.Min(1f, value));
    }

    internal static void PreviewSeekNormalized(float value)
    {
        if (seekPreviewing)
        {
            seekPreviewProgress = Math.Max(0f, Math.Min(1f, value));
        }
    }

    internal static void CommitSeekPreview(float value)
    {
        seekPreviewing = false;
        SeekNormalized(Math.Max(0f, Math.Min(1f, value)));
    }

    internal static void SeekTurn(int delta)
    {
        if (!runtimeReady || events.Count == 0)
        {
            return;
        }

        var targetTurn = Math.Max(1, Math.Min(Math.Max(1, TurnCount), CurrentTurn + delta));
        var target = events.FindIndex(item => item.Kind == MatchReplayEventKinds.TurnFrame
                                              && item.TurnFrame?.TurnIndex >= targetTurn);
        SeekToIndex(target < 0 ? events.Count : Math.Min(events.Count, target + 1));
    }

    internal static void AdvanceExportClock(float milliseconds)
    {
        if (!IsReadyForExport || paused || IsFinished)
        {
            return;
        }

        var elapsed = Math.Max(0f, milliseconds);
        playbackClock += elapsed;
        MatchReplayPresentationDirector.Tick(elapsed);
        ExecuteDueEvents(maximum: 64);
        FlushPendingVisualProjection();
        SyncAudio();
        if (eventIndex >= events.Count && playbackClock >= DurationMilliseconds)
        {
            paused = true;
        }
    }

    internal static void Stop()
    {
        BeginStop(MatchReplayExitKind.Cancelled);
    }

    internal static void StopForModuleDisabled()
    {
        BeginStop(MatchReplayExitKind.ModuleDisabled);
    }

    internal static void StopAfterExport(bool completed, string detail)
    {
        BeginStop(
            completed ? MatchReplayExitKind.ExportCompleted : MatchReplayExitKind.ExportFailed,
            detail);
    }

    private static void BeginStop(MatchReplayExitKind kind, string detail = "")
    {
        if (resetting) return;
        if (record == null && !MatchReplaySessionState.IsPlayback)
        {
            MatchReplayControlsPresenter.Close();
            MatchReplayReturnCoordinator.Clear();
            return;
        }
        resetting = true;
        paused = true;
        var exitDecision = MatchReplaySessionState.BeginExit(kind, detail);
        var cleanupFailures = 0;
        UiTransitionGuardRuntime.BeginTransition(null, AuraToolsIds.ModId, "Match replay stop", 8);
        RunCleanupStep("controls", MatchReplayControlsPresenter.Close, ref cleanupFailures);
        RunCleanupStep("presentation", MatchReplayPresentationDirector.Reset, ref cleanupFailures);
        ResetPlaybackState();
        RunCleanupStep(
            "native-presentation-ui",
            () => MatchReplayUiLifecycle.CloseReplayOwnedPresentationUis("Match replay stop"),
            ref cleanupFailures);
        RunCleanupStep("native-view", MatchReplayNativeViewRuntime.Dispose, ref cleanupFailures);
        RunCleanupStep("environment", MatchReplayEnvironmentScope.Restore, ref cleanupFailures);
        UiTransitionGuardRuntime.ScrubNow(null, AuraToolsIds.ModId, "Match replay stop");
        UiTransitionGuardRuntime.RecoverNativeInput(null, AuraToolsIds.ModId, "Match replay stop", 8);
        var cleanup = AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(
            CompleteStopAfterUnityDestroy(exitDecision, cleanupFailures));
        if (cleanup == null)
        {
            AuraToolsLog.Error(
                "[MatchRecords] replay teardown finalizer could not be scheduled.",
                new InvalidOperationException("Match-record runtime coroutine driver is unavailable."));
        }
    }

    private static System.Collections.IEnumerator CompleteStopAfterUnityDestroy(
        MatchReplayExitDecision exitDecision,
        int cleanupFailures)
    {
        // Object.Destroy and GraphicRegistry removal become terminal at the next
        // Unity frame boundary. The session remains Exiting until those owned
        // objects have actually disappeared.
        yield return null;
        try
        {
            MatchReplayNativeViewRuntime.CompleteDispose();
            if (MatchReplayUiLifecycle.ReplayOwnedPresentationUiCount != 0)
            {
                throw new InvalidOperationException(
                    "Replay-owned native UI remained registered after teardown.");
            }
        }
        catch (Exception ex)
        {
            cleanupFailures++;
            AuraToolsLog.Error("[MatchRecords] replay teardown terminal check failed", ex);
        }

        RunCleanupStep(
            "ui-ownership",
            MatchReplayUiLifecycle.ReleaseReplayOwnership,
            ref cleanupFailures);

        MatchReplaySessionState.CompleteExit();
        resetting = false;
        AuraToolsLog.Debug("[MatchRecords] native replay cleanup completed: failures=" + cleanupFailures + ".");
        if (exitDecision.ReturnToLibrary)
        {
            var message = cleanupFailures == 0
                ? exitDecision.Message
                : exitDecision.Message + " 清理校验发现 " + cleanupFailures + " 个错误，请查看日志。";
            MatchReplayReturnCoordinator.ReturnToLibrary(message);
        }
        else
        {
            MatchReplayReturnCoordinator.Clear();
        }
    }

    private static void RunCleanupStep(string name, Action action, ref int failureCount)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failureCount++;
            AuraToolsLog.Error("[MatchRecords] replay cleanup step failed: step=" + name, ex);
        }
    }

    private static void ResetPlaybackState()
    {
        nativeAudio?.Dispose();
        nativeAudio = null;
        MatchReplayOutcomePresenter.Clear();
        nativeDocument = null;
        record = null;
        events.Clear();
        timeline.Clear();
        actionTimeline = MatchReplayActionTimeline.Build(Array.Empty<MatchReplayEvent>());
        ReadModel.Reset(null);
        eventIndex = 0;
        pendingStartIndex = 0;
        playbackClock = 0f;
        paused = false;
        seeking = false;
        seekPreviewing = false;
        seekPreviewProgress = 0f;
        fightCanvasGroup = null;
        controlsVisible = false;
        externalClock = false;
        runtimeReady = false;
        playbackHealth = MatchReplayCompatibilityLevels.Compatible;
        playbackIssue = "";
        preparationStatus = "";
        preparedKindsDescription = "";
        preparedScene = "";
        preparedAdapter = "";
        failedEventCount = 0;
        pendingVisualProjection = null;
    }

    private static void ExecuteDueEvents(int maximum)
    {
        var executed = 0;
        while (eventIndex < events.Count
               && playbackClock >= timeline[eventIndex]
               && executed++ < Math.Max(1, maximum))
        {
            Execute(events[eventIndex], animate: true, project: true);
            FlushPendingVisualProjection();
            eventIndex++;
            if (HasBlockingError)
            {
                paused = true;
                break;
            }
        }
    }

    private static bool Execute(MatchReplayEvent item, bool animate, bool project)
    {
        try
        {
            switch (item.Kind)
            {
                case MatchReplayEventKinds.TurnFrame:
                    if (item.TurnFrame == null)
                    {
                        throw new InvalidOperationException("回合帧为空。");
                    }

                    if (!string.Equals(
                            MatchReplayProjectionState.Hash(item.TurnFrame.State),
                            item.TurnFrame.StateHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("回合帧状态校验失败。");
                    }

                    var turnCardTransitions = MatchReplayActionDerivation.BuildCardTransitions(
                        ReadModel.Current,
                        item.TurnFrame.State);
                    ReadModel.Reset(item.TurnFrame.State);
                    if (project)
                    {
                        ProjectCurrent(restoreCards: false, restoreRoleTable: false);
                        MatchReplayCardStateCapture.ApplyTransitions(
                            ReadModel.Current.Cards,
                            ReadModel.Current.CardTopCount,
                            turnCardTransitions);
                        MatchReplayPresentationDirector.ShowTurn(
                            item.TurnFrame.TurnIndex,
                            item.TurnFrame.ActiveActorId);
                    }
                    break;
                case MatchReplayEventKinds.ActionFrame:
                    if (item.ActionFrame == null)
                    {
                        throw new InvalidOperationException("动作帧为空。");
                    }

                    if (animate)
                    {
                        MatchReplayPresentationDirector.PlayAction(item.ActionFrame);
                    }

                    ReadModel.Apply(item.ActionFrame.Delta);
                    var stateHash = MatchReplayProjectionState.Hash(ReadModel.Current);
                    if (!string.Equals(stateHash, item.ActionFrame.FinalStateHash, StringComparison.OrdinalIgnoreCase))
                    {
                        playbackHealth = "Desynced";
                        playbackIssue = "动作帧数据校验失败：" + item.ActionFrame.ActionId;
                        failedEventCount++;
                        return false;
                    }

                    if (project)
                    {
                        var projectionDelay = animate
                            ? MatchReplayPresentationSchedule.OutcomeProjectionDelay(
                                item.ActionFrame,
                                AuraToolsExp.Dll.Config.AuraToolsConfigService.MatchExperience.MatchRecords.Replay.PresentationMode)
                            : 0;
                        if (projectionDelay > 0)
                        {
                            pendingVisualProjection = new PendingVisualProjection
                            {
                                DueMilliseconds = (eventIndex < timeline.Count ? timeline[eventIndex] : playbackClock)
                                                  + projectionDelay,
                                ChangedStatusIds = item.ActionFrame.Delta.StatusUpserts
                                    .Select(status => status.InstanceId)
                                    .ToList(),
                                ReplaceCards = MatchReplayProjectionState.HasCardChanges(item.ActionFrame.Delta),
                                CardTransitions = item.ActionFrame.CardTransitions ?? new List<MatchReplayCardTransition>()
                            };
                        }
                        else
                        {
                            ProjectActionResult(item.ActionFrame);
                        }
                    }
                    break;
                case MatchReplayEventKinds.SeekCheckpoint:
                    if (item.SeekCheckpoint == null)
                    {
                        throw new InvalidOperationException("定位检查点为空。");
                    }

                    if (!string.Equals(
                            MatchReplayProjectionState.Hash(item.SeekCheckpoint.State),
                            item.SeekCheckpoint.StateHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("定位检查点状态校验失败。");
                    }

                    ReadModel.Reset(item.SeekCheckpoint.State);
                    break;
                case MatchReplayEventKinds.BattleResultFrame:
                    if (item.BattleResultFrame == null)
                        throw new InvalidOperationException("战斗结果帧为空。");
                    if (project) MatchReplayOutcomePresenter.Show(item.BattleResultFrame.Result);
                    break;
                default:
                    throw new InvalidOperationException("只读投影播放器拒绝命令重演事件：" + item.Kind);
            }

            return true;
        }
        catch (Exception ex)
        {
            playbackHealth = "Failed";
            playbackIssue = "无法投影事件 " + item.Sequence + "：" + ex.Message;
            failedEventCount++;
            AuraToolsLog.Warn("[MatchRecords] replay projection event " + item.Sequence + " failed: " + ex.Message);
            return false;
        }
    }

    private static void SeekToIndex(int targetIndex)
    {
        if (record?.InitialState.BaselineState == null || seeking)
        {
            return;
        }

        seeking = true;
        paused = true;
        try
        {
            var target = Math.Max(0, Math.Min(events.Count, targetIndex));
            MatchReplayPresentationDirector.Reset();
            MatchReplayOutcomePresenter.Clear();
            pendingVisualProjection = null;
            ReadModel.Reset(record.InitialState.BaselineState);
            var start = 0;
            for (var index = target - 1; index >= 0; index--)
            {
                var checkpoint = events[index].SeekCheckpoint;
                if (events[index].Kind == MatchReplayEventKinds.SeekCheckpoint && checkpoint != null)
                {
                    if (!string.Equals(
                            MatchReplayProjectionState.Hash(checkpoint.State),
                            checkpoint.StateHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("定位检查点状态校验失败。");
                    }

                    ReadModel.Reset(checkpoint.State);
                    start = index + 1;
                    break;
                }

                var turnFrame = events[index].TurnFrame;
                if (events[index].Kind == MatchReplayEventKinds.TurnFrame && turnFrame != null)
                {
                    if (!string.Equals(
                            MatchReplayProjectionState.Hash(turnFrame.State),
                            turnFrame.StateHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("回合帧状态校验失败。");
                    }

                    ReadModel.Reset(turnFrame.State);
                    start = index + 1;
                    break;
                }
            }

            for (var index = start; index < target; index++)
            {
                if (!Execute(events[index], animate: false, project: false))
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(playbackIssue)
                            ? "定位重建未能应用事件 " + events[index].Sequence + "。"
                            : playbackIssue);
                }
            }

            ProjectCurrent(restoreCards: true, restoreRoleTable: false);
            var actorId = events.Take(target)
                .Where(item => item.Kind == MatchReplayEventKinds.TurnFrame && item.TurnFrame != null)
                .Select(item => item.TurnFrame!.ActiveActorId)
                .LastOrDefault() ?? ResolveRecordedPlayerId();
            MatchReplayPresentationDirector.ShowTurn(ReadModel.CurrentTurn, actorId);
            eventIndex = target;
            playbackClock = eventIndex < timeline.Count ? timeline[eventIndex] : DurationMilliseconds;
            seekPreviewProgress = actionTimeline.Count == 0
                ? 0f
                : CompletedActionCount / (float)actionTimeline.Count;
            SyncAudio();
            preparationStatus = "";
            AuraToolsLog.Info("[MatchRecords] replay seek completed: event=" + eventIndex
                              + ", action=" + CompletedActionCount + "/" + actionTimeline.Count + ".");
        }
        catch (Exception ex)
        {
            playbackHealth = "Failed";
            playbackIssue = "回放定位失败：" + ex.Message;
            AuraToolsLog.Error("[MatchRecords] replay seek failed: " + ex.Message, ex);
        }
        finally
        {
            seeking = false;
            RefreshControls();
        }
    }

    private static void PrepareNativeView()
    {
        if (record?.InitialState.BaselineState == null)
        {
            throw new InvalidOperationException("Replay baseline is unavailable during preparation.");
        }

        preparedScene = MatchReplayEnvironmentScope.InstallPresentationScene(record.InitialState.BackgroundScene);
        MatchReplayNativeViewRuntime.Create();
        MatchReplayNativeViewRuntime.ApplySkinSelections(nativeDocument?.NativeBattle.SkinSelections);
        InitializeView();
        preparationStatus = "回放原生视图已准备，等待界面切换";
    }

    private static void SyncAudio()
    {
        try
        {
            nativeAudio?.SyncTimeline((long)Math.Max(0f, playbackClock), Speed, paused);
        }
        catch (Exception ex)
        {
            playbackHealth = "Failed";
            playbackIssue = "回放音频失败：" + ex.Message;
            paused = true;
            AuraToolsLog.Error("[MatchRecords] replay audio failed.", ex);
        }
    }

    private static void InitializeView()
    {
        if (record?.InitialState.BaselineState == null || FightManager.Instance == null)
        {
            throw new InvalidOperationException("Replay view runtime is unavailable.");
        }

        MatchReplayEnvironmentScope.RestoreInitialRoleTable(record.InitialState.RoleTableJson);
        preparedAdapter = MatchReplayFightSandboxInitializer.Initialize(FightManager.Instance, record.InitialState);
        FreezeBattleRuntime();
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (FightPlayer.Instance == null
            || FightManager.Instance.statuses == null
            || FightManager.Instance.statuses.Count == 0
            || fightUi == null)
        {
            throw new InvalidOperationException("回放角色或原生战斗界面未能完成初始化。");
        }

        fightCanvasGroup = fightUi.gameObject.GetComponent<CanvasGroup>()
                           ?? fightUi.gameObject.AddComponent<CanvasGroup>();
        fightCanvasGroup.alpha = 1f;
        fightCanvasGroup.interactable = false;
        fightCanvasGroup.blocksRaycasts = false;
        MatchReplayPresentationDirector.Reset();
        pendingVisualProjection = null;
        ReadModel.Reset(record.InitialState.BaselineState);
        ProjectCurrent(restoreCards: true, restoreRoleTable: true);
        eventIndex = 0;
        playbackClock = 0f;
        AuraToolsLog.Debug("[MatchRecords] replay projection view prepared: adapter=" + preparedAdapter + ".");
    }

    private static void ProjectCurrent(
        bool restoreCards,
        bool restoreRoleTable,
        IReadOnlyCollection<string>? changedStatusIds = null)
    {
        if (!MatchReplayStateCapture.Project(
                ReadModel.Current,
                restoreCards,
                restoreRoleTable,
                changedStatusIds))
        {
            throw new InvalidOperationException("记录状态无法投影到原生战斗视图。");
        }

        FreezeBattleRuntime();
    }

    private static void ProjectActionResult(MatchReplayActionFrame frame)
    {
        ProjectCurrent(
            restoreCards: false,
            restoreRoleTable: false,
            frame.Delta.StatusUpserts.Select(status => status.InstanceId).ToList());
        if (MatchReplayProjectionState.HasCardChanges(frame.Delta))
        {
            MatchReplayCardStateCapture.ApplyTransitions(
                ReadModel.Current.Cards,
                ReadModel.Current.CardTopCount,
                frame.CardTransitions);
        }
    }

    private static void FlushPendingVisualProjection()
    {
        var pending = pendingVisualProjection;
        if (pending == null || playbackClock < pending.DueMilliseconds)
        {
            return;
        }

        pendingVisualProjection = null;
        try
        {
            ProjectCurrent(
                restoreCards: false,
                restoreRoleTable: false,
                pending.ChangedStatusIds);
            if (pending.ReplaceCards)
            {
                MatchReplayCardStateCapture.ApplyTransitions(
                    ReadModel.Current.Cards,
                    ReadModel.Current.CardTopCount,
                    pending.CardTransitions);
            }
        }
        catch (Exception ex)
        {
            playbackHealth = "Failed";
            playbackIssue = "敌方动作结果投影失败：" + ex.Message;
            failedEventCount++;
            paused = true;
            AuraToolsLog.Error("[MatchRecords] deferred action projection failed: " + ex.Message, ex);
        }
    }

    private static void FreezeBattleRuntime()
    {
        if (FightManager.Instance != null)
        {
            FightManager.Instance.fightType = FightType.None;
            FightManager.Instance.ActionQueue?.Clear();
        }
    }

    private static string ResolveRecordedPlayerId()
    {
        var playerId = RoleTable.Instance?.Id ?? "";
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            return playerId;
        }

        try
        {
            var roles = JsonConvert.DeserializeObject<List<FightManager.RoleData>>(
                GZip.DecompressToString(record?.InitialState.RoleQueue ?? Array.Empty<byte>()));
            return roles?.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.InstanceId))?.InstanceId ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void RefreshControls()
    {
        if (controlsVisible)
        {
            MatchReplayControlsPresenter.Refresh();
        }
    }

    private static bool Reject(string recordId, string reason, string detail, out string message)
    {
        message = detail;
        AuraToolsLog.Warn("[MatchRecords] replay start rejected: record=" + (recordId ?? "")
                          + ", reason=" + reason
                          + ", fight=" + (FightManager.Instance?.fightType.ToString() ?? "missing")
                          + ", networkClientActive=" + NetworkClient.active
                          + ", networkClientConnected=" + NetworkClient.isConnected
                          + ", networkServerActive=" + NetworkServer.active
                          + ", detail=" + detail);
        return false;
    }

    private sealed class PendingVisualProjection
    {
        internal float DueMilliseconds { get; set; }
        internal List<string> ChangedStatusIds { get; set; } = new();
        internal bool ReplaceCards { get; set; }
        internal List<MatchReplayCardTransition> CardTransitions { get; set; } = new();
    }
}
