using System;
using AuraToolsExp.Dll.Infrastructure;
using UiTransitionGuardShared;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayLaunchCoordinator
{
    internal static void Start(
        string recordId,
        long eventSequence,
        Action closeOrigin,
        Action<string> failed)
    {
        if (!CanLaunch(out var blocker))
        {
            failed?.Invoke(blocker);
            return;
        }

        var trackedRoots = MatchReplayUiLifecycle.SnapshotTransitionRoots();
        MatchReplayFailurePresenter.Dismiss();
        UiTransitionGuardRuntime.BeginTransition(
            null,
            AuraToolsIds.ModId,
            "Match replay launch",
            8);
        closeOrigin?.Invoke();
        MatchReplayUiLifecycle.RequestCloseOriginUi("Match replay launch");
        try
        {
            MatchReplayLifecycleRunner.BeginLaunch(trackedRoots, () =>
            {
                UiTransitionGuardRuntime.RunAfterGuard(
                    null,
                    AuraToolsIds.ModId,
                    "Match replay launch",
                    () =>
                    {
                        var started = eventSequence <= 0
                            ? MatchReplayPlayer.TryStart(recordId, out var result)
                            : MatchReplayPlayer.TryStartAtSequence(recordId, eventSequence, out result);
                        if (!started)
                        {
                            failed?.Invoke(result);
                        }
                    },
                    2);
            });
        }
        catch (Exception ex)
        {
            failed?.Invoke("无法开始回放：" + ex.Message);
        }
    }

    internal static bool TryStartForExport(
        string recordId,
        Action closeOrigin,
        Action started,
        Action<string> failed,
        out string message)
    {
        if (!CanLaunch(out message))
        {
            return false;
        }

        try
        {
            var trackedRoots = MatchReplayUiLifecycle.SnapshotTransitionRoots();
            MatchReplayFailurePresenter.Dismiss();
            UiTransitionGuardRuntime.BeginTransition(
                null,
                AuraToolsIds.ModId,
                "Match replay video export launch",
                8);
            closeOrigin?.Invoke();
            MatchReplayUiLifecycle.RequestCloseOriginUi("Match replay video export launch");
            MatchReplayLifecycleRunner.BeginLaunch(trackedRoots, () =>
            {
                UiTransitionGuardRuntime.RunAfterGuard(
                    null,
                    AuraToolsIds.ModId,
                    "Match replay video export launch",
                    () =>
                    {
                        if (!MatchReplayPlayer.TryStartForExport(recordId, out var result))
                        {
                            failed?.Invoke(result);
                            return;
                        }

                        started?.Invoke();
                    },
                    2);
            });
            message = "正在关闭原界面并准备战斗回放。";
            return true;
        }
        catch (Exception ex)
        {
            message = "无法开始视频导出：" + ex.Message;
            failed?.Invoke(message);
            return false;
        }
    }

    private static bool CanLaunch(out string message)
    {
        if (MatchReplaySessionState.IsExiting || MatchReplayLifecycleRunner.IsStopping)
        {
            message = "上一场回放仍在退出并重建主菜单，请稍候再试。";
            return false;
        }

        message = "";
        return true;
    }
}
