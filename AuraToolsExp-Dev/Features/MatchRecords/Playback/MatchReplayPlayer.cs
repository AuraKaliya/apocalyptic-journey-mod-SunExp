using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
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
    private static readonly MatchReplayRuntimeBootstrap RuntimeBootstrap = new();
    private static readonly MatchReplayReadModel ReadModel = new();
    private static MatchRecord? record;
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
    private static string lastStartFailure = "";
    private static string lastRuntimeReadiness = "";
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
    internal static bool IsReadyForExport => IsActive && runtimeReady && externalClock && !seeking;
    internal static string PlaybackHealth => playbackHealth;
    internal static string PlaybackIssue => playbackIssue;
    internal static string PreparationStatus => preparationStatus;
    internal static string LastStartFailure => lastStartFailure;
    internal static int FailedEventCount => failedEventCount;
    internal static bool HasBlockingError => playbackHealth == "Desynced" || playbackHealth == "Failed";
    internal static int CurrentTurn => runtimeReady ? ReadModel.CurrentTurn : 1;
    internal static int TurnCount => record?.TurnCount ?? 0;

    internal static bool TryStart(string recordId, out string message)
    {
        return TryStartCore(recordId, true, 0, out message);
    }

    internal static bool TryStartAtSequence(string recordId, long eventSequence, out string message)
    {
        return TryStartCore(recordId, true, eventSequence, out message);
    }

    internal static bool TryStartForExport(string recordId, out string message)
    {
        return TryStartCore(recordId, false, 0, out message);
    }

    private static bool TryStartCore(string recordId, bool showControls, long startSequence, out string message)
    {
        message = "";
        lastStartFailure = "";
        if (IsActive)
        {
            return Reject(recordId, "already-active", "已有对局正在回放。", out message);
        }

        if (resetting
            || MatchReplaySessionState.IsExiting
            || MatchReplayLifecycleRunner.IsStopping
            || MatchReplayLocalHostRuntime.OwnsHost)
        {
            return Reject(
                recordId,
                "previous-replay-exiting",
                "上一场回放仍在退出并重建主菜单，请稍候再试。",
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

        try
        {
            var loaded = MatchRecordStorage.Database.Get(recordId);
            if (loaded == null)
            {
                return Reject(recordId, "record-missing", "找不到这条对局记录。", out message);
            }

            var metadataCompatibility = MatchReplayCompatibility.Evaluate(loaded);
            if (!metadataCompatibility.CanPlay)
            {
                return Reject(recordId, "metadata-incompatible", metadataCompatibility.Message, out message);
            }

            var decoded = MatchReplayChunker.Decode(MatchRecordStorage.Database.LoadChunks(recordId)).ToList();
            if (decoded.Count != loaded.EventCount)
            {
                return Reject(recordId, "event-count-mismatch", "回放事件数量校验失败，记录可能不完整。", out message);
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

            if (!RuntimeBootstrap.Begin(
                    NetworkServer.active,
                    NetworkClient.active,
                    LobbyManager.Instance != null,
                    out var bootstrapMessage))
            {
                return Reject(recordId, RuntimeBootstrap.FailureCode, RuntimeBootstrap.FailureMessage, out message);
            }

            record = loaded;
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
            playbackIssue = compatibility.Level == MatchReplayCompatibilityLevels.Degraded ? compatibility.Message : "";
            preparationStatus = bootstrapMessage;
            preparedKindsDescription = quality.DescribeCounts();
            failedEventCount = 0;
            MatchReplaySessionState.IsPlayback = true;
            MatchReplayUiLifecycle.PrepareForReplayHost();
            MatchReplayEnvironmentScope.CaptureAndInstallRoleTable(record.InitialState);
            if (showControls)
            {
                MatchReplayControlsPresenter.Show();
            }

            try
            {
                MatchReplayLocalHostRuntime.Start();
            }
            catch (Exception ex)
            {
                RuntimeBootstrap.MarkHostStartFailed(ex.Message);
                throw new InvalidOperationException(RuntimeBootstrap.FailureMessage, ex);
            }

            AdvanceRuntimeBootstrap(0);
            if (RuntimeBootstrap.Phase == MatchReplayRuntimeBootstrapPhases.Ready)
            {
                CompletePreparation();
            }

            message = runtimeReady ? "开始回放。" : preparationStatus;
            return true;
        }
        catch (Exception ex)
        {
            var failure = "无法开始回放：" + ex.Message;
            message = failure;
            AuraToolsLog.Error("[MatchRecords] replay initialization failed: record=" + recordId + ", error=" + ex.Message, ex);
            Stop();
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
            TickPreparation();
            RefreshControls();
            return;
        }

        FreezeBattleRuntime();
        if (externalClock || seeking)
        {
            return;
        }

        if (paused)
        {
            RefreshControls();
            return;
        }

        var elapsed = Time.unscaledDeltaTime * 1000f * Speed;
        playbackClock += elapsed;
        MatchReplayPresentationDirector.Tick(elapsed);
        ExecuteDueEvents(maximum: 16);
        FlushPendingVisualProjection();
        if (eventIndex >= events.Count && playbackClock >= DurationMilliseconds)
        {
            paused = true;
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

    internal static void ContinueDegraded()
    {
        if (!HasBlockingError)
        {
            return;
        }

        playbackHealth = MatchReplayCompatibilityLevels.Degraded;
        playbackIssue = "用户选择继续查看；后续状态仍以记录数据为准。";
        paused = false;
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
        if (eventIndex >= events.Count && playbackClock >= DurationMilliseconds)
        {
            paused = true;
        }
    }

    internal static void Stop()
    {
        if (resetting)
        {
            return;
        }

        if (record == null && !MatchReplaySessionState.IsPlayback && !MatchReplayLocalHostRuntime.OwnsHost)
        {
            MatchReplayControlsPresenter.Close();
            return;
        }

        resetting = true;
        paused = true;
        MatchReplaySessionState.IsExiting = true;
        var cleanupFailures = 0;
        // FightUI is deliberately excluded. It remains registered and alive
        // until Mirror has stopped, then follows GameApp.ReturnToMenu/UIBase.Close.
        // Waiting for it here would always time out and force-destroy the native
        // battle UI while network-owned callbacks and tweens still reference it.
        var teardownRoots = MatchReplayUiLifecycle.SnapshotOriginTransitionRoots();
        var controlsRoot = MatchReplayControlsPresenter.RootObject;
        if (controlsRoot != null)
        {
            teardownRoots.Add(controlsRoot);
        }

        try
        {
            RunCleanupStep("failure-notification", MatchReplayFailurePresenter.Dismiss, ref cleanupFailures);
            UiTransitionGuardRuntime.BeginTransition(null, AuraToolsIds.ModId, "Match replay stop", 8);
            RunCleanupStep("controls", MatchReplayControlsPresenter.Close, ref cleanupFailures);
            RunCleanupStep(
                "origin-ui",
                () => MatchReplayUiLifecycle.RequestCloseOriginUi("Match replay stop"),
                ref cleanupFailures);
            RunCleanupStep("fight-raycast", () =>
            {
                if (fightCanvasGroup == null)
                {
                    return;
                }

                fightCanvasGroup.interactable = false;
                fightCanvasGroup.blocksRaycasts = false;
            }, ref cleanupFailures);

            // Stop replay-owned animation queues, but leave Mirror-owned status,
            // FightManager and native UI objects intact until the local host has
            // completed its OnDisable/OnDestroy lifecycle.
            RunCleanupStep("presentation-quiesce", MatchReplayPresentationDirector.Reset, ref cleanupFailures);

            RunCleanupStep("local-host", MatchReplayLocalHostRuntime.Stop, ref cleanupFailures);
        }
        catch (Exception ex)
        {
            cleanupFailures++;
            AuraToolsLog.Error("[MatchRecords] replay cleanup transaction failed before lifecycle wait.", ex);
        }

        ResetPlaybackState();
        try
        {
            MatchReplayLifecycleRunner.BeginStop(teardownRoots, () =>
            {
                MatchReplayUiLifecycle.ReleaseReplayOwnership();
                MatchReplaySessionState.IsPlayback = false;
                MatchReplaySessionState.IsExiting = false;
                resetting = false;
                AuraToolsLog.Debug("[MatchRecords] replay cleanup completed: failures=" + cleanupFailures + ".");
            });
        }
        catch (Exception ex)
        {
            cleanupFailures++;
            AuraToolsLog.Error("[MatchRecords] replay lifecycle barrier failed to start.", ex);
            try
            {
                MatchReplayLocalHostRuntime.ForceStop();
                if (MatchReplayLocalHostRuntime.IsTransportQuiescent)
                {
                    MatchReplayLocalHostRuntime.CompleteStop(allowTransportOnly: true);
                }
            }
            catch (Exception completeEx)
            {
                cleanupFailures++;
                AuraToolsLog.Error("[MatchRecords] replay host fallback finalization failed.", completeEx);
            }

            try
            {
                MatchReplayChatUiLeaseRuntime.ForceFinalizeAfterTimeout(
                    "Match replay stop fallback");
                MatchReplayUiLifecycle.ForceCloseOriginUi("Match replay stop fallback");
                MatchReplayUiLifecycle.ForceCloseReplayOwnedPresentationUis(
                    "Match replay stop fallback");
            }
            catch (Exception uiEx)
            {
                cleanupFailures++;
                AuraToolsLog.Error("[MatchRecords] replay fallback UI cleanup failed.", uiEx);
            }

            try
            {
                MatchReplayEnvironmentScope.Restore();
            }
            catch (Exception restoreEx)
            {
                cleanupFailures++;
                AuraToolsLog.Error("[MatchRecords] replay environment fallback restoration failed.", restoreEx);
            }
            finally
            {
                MatchReplayUiLifecycle.ReleaseReplayOwnership();
            }
            UiTransitionGuardRuntime.ScrubNow(null, AuraToolsIds.ModId, "Match replay stop");
            UiTransitionGuardRuntime.RecoverNativeInput(
                null,
                AuraToolsIds.ModId,
                "Match replay stop",
                12);
            MatchReplaySessionState.IsPlayback = false;
            MatchReplaySessionState.IsExiting = false;
            resetting = false;
            AuraToolsLog.Debug("[MatchRecords] replay cleanup completed: failures=" + cleanupFailures + ".");
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
        lastRuntimeReadiness = "";
        failedEventCount = 0;
        pendingVisualProjection = null;
        RuntimeBootstrap.Reset();
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
                                ReplaceCards = item.ActionFrame.Delta.ReplaceCards,
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
                default:
                    throw new InvalidOperationException("v8 播放器拒绝命令重演事件：" + item.Kind);
            }

            return true;
        }
        catch (Exception ex)
        {
            playbackHealth = MatchReplayCompatibilityLevels.Degraded;
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

    private static void TickPreparation()
    {
        try
        {
            var elapsed = Math.Max(1, (int)Math.Ceiling(Time.unscaledDeltaTime * 1000f));
            AdvanceRuntimeBootstrap(elapsed);
            if (RuntimeBootstrap.Phase == MatchReplayRuntimeBootstrapPhases.Ready)
            {
                CompletePreparation();
                return;
            }

            if (RuntimeBootstrap.Phase == MatchReplayRuntimeBootstrapPhases.Failed)
            {
                AbortPreparation(RuntimeBootstrap.FailureCode, RuntimeBootstrap.FailureMessage, null);
                return;
            }

            preparationStatus = "正在等待专用回放视图：" + RuntimeBootstrap.MissingRuntime;
        }
        catch (Exception ex)
        {
            AbortPreparation(
                "runtime-initialization-failed",
                "专用回放视图初始化失败：" + ex.Message,
                ex);
        }
    }

    private static void AdvanceRuntimeBootstrap(int elapsedMilliseconds)
    {
        var readiness = MatchReplayLocalHostRuntime.CaptureReadiness();
        var snapshot = readiness.DescribeState();
        if (!string.Equals(snapshot, lastRuntimeReadiness, StringComparison.Ordinal))
        {
            lastRuntimeReadiness = snapshot;
            AuraToolsLog.Debug("[MatchRecords] replay view readiness: " + snapshot
                               + "; context=" + MatchReplayEnvironmentScope.DescribeMapContext() + ".");
        }

        RuntimeBootstrap.Advance(elapsedMilliseconds, readiness);
    }

    private static void CompletePreparation()
    {
        if (record?.InitialState.BaselineState == null)
        {
            throw new InvalidOperationException("Replay baseline is unavailable during preparation.");
        }

        MatchReplayLocalHostRuntime.BindReplayIdentity(ResolveRecordedPlayerId());
        if (MatchReplayEnvironmentScope.UsesCompatibilityDice)
        {
            playbackHealth = MatchReplayCompatibilityLevels.Degraded;
            playbackIssue = "记录缺少原始场景游标，当前使用兼容视图环境。";
        }

        var scene = MatchReplayEnvironmentScope.InstallPresentationScene(record.InitialState.BackgroundScene);
        InitializeView();
        runtimeReady = true;
        preparationStatus = "";
        if (pendingStartIndex > 0)
        {
            var target = pendingStartIndex;
            pendingStartIndex = 0;
            SeekToIndex(target);
        }

        AuraToolsLog.Info("[MatchRecords] v8 replay started: record=" + record.RecordId
                          + ", events=" + events.Count
                          + ", kinds=" + preparedKindsDescription
                          + ", compatibility=" + playbackHealth
                          + ", mode=state-projection"
                          + ", scene=" + scene + ".");
        RefreshControls();
    }

    private static void InitializeView()
    {
        if (record?.InitialState.BaselineState == null || FightManager.Instance == null)
        {
            throw new InvalidOperationException("Replay view runtime is unavailable.");
        }

        MatchReplayEnvironmentScope.RestoreInitialRoleTable(record.InitialState.RoleTableJson);
        var initializer = MatchReplayFightSandboxInitializer.Initialize(FightManager.Instance, record.InitialState);
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
        MatchReplayPresentationDirector.ShowTurn(ReadModel.CurrentTurn, ResolveRecordedPlayerId());
        eventIndex = 0;
        playbackClock = 0f;
        AuraToolsLog.Debug("[MatchRecords] replay projection view initialized: adapter=" + initializer + ".");
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
        if (frame.Delta.ReplaceCards)
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

    private static void AbortPreparation(string reason, string detail, Exception? error)
    {
        var showFailure = controlsVisible;
        var recordId = record?.RecordId ?? "";
        var logMessage = "[MatchRecords] replay preparation failed: record=" + recordId
                         + ", reason=" + reason
                         + ", missing=" + RuntimeBootstrap.MissingRuntime
                         + ", detail=" + detail;
        AuraToolsLog.Error(logMessage, error);
        Stop();
        lastStartFailure = detail;
        if (showFailure)
        {
            MatchReplayFailurePresenter.Schedule("回放启动失败", detail);
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
                          + ", replayHostOwned=" + MatchReplayLocalHostRuntime.OwnsHost
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
