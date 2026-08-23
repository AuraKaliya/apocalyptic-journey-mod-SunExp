using System;
using AuraToolsExp.Dll.Infrastructure;
using UiTransitionGuardShared;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayLaunchCoordinator
{
    internal static void Start(
        string recordId,
        long eventSequence,
        MatchRecordLibraryViewState returnState,
        Action<string> failed)
    {
        if (!CanLaunch(out var blocker))
        {
            failed?.Invoke(blocker);
            return;
        }

        if (!MatchReplayPlayer.TryPrepareInteractive(recordId, eventSequence, out var preparation))
        {
            failed?.Invoke(preparation);
            return;
        }

        var originCommitted = false;
        try
        {
            MatchReplayReturnCoordinator.Arm(returnState);
            MatchReplayPlayer.CommitOrigin();
            originCommitted = true;
            UiTransitionGuardRuntime.BeginTransition(
                null,
                AuraToolsIds.ModId,
                "Match replay launch commit",
                8);
            MatchReplayUiLifecycle.CloseOriginUi("Match replay launch commit");
            UiTransitionGuardRuntime.RunAfterGuard(
                null,
                AuraToolsIds.ModId,
                "Match replay launch activate",
                () => MatchReplayPlayer.TryActivatePrepared(out _),
                2);
        }
        catch (Exception ex)
        {
            var detail = "无法提交回放界面切换：" + ex.Message;
            MatchReplayPlayer.FailCommittedStart(detail);
            if (!originCommitted)
            {
                failed?.Invoke(detail);
            }
        }
    }

    internal static bool TryStartForExport(
        string recordId,
        MatchRecordLibraryViewState returnState,
        Action started,
        Action<string> failed,
        out string message)
    {
        if (!CanLaunch(out message))
        {
            return false;
        }

        if (!MatchReplayPlayer.TryPrepareForExport(recordId, true, out message))
        {
            return false;
        }

        var originCommitted = false;
        try
        {
            MatchReplayReturnCoordinator.Arm(returnState);
            MatchReplayPlayer.CommitOrigin();
            originCommitted = true;
            UiTransitionGuardRuntime.BeginTransition(
                null,
                AuraToolsIds.ModId,
                "Match replay video export commit",
                8);
            MatchReplayUiLifecycle.CloseOriginUi("Match replay video export commit");
            UiTransitionGuardRuntime.RunAfterGuard(
                null,
                AuraToolsIds.ModId,
                "Match replay video export activate",
                () =>
                {
                    if (!MatchReplayPlayer.TryActivatePrepared(out var activation))
                    {
                        failed?.Invoke(activation);
                        return;
                    }

                    started?.Invoke();
                },
                2);
            message = "回放视图已准备，正在进入视频导出。";
            return true;
        }
        catch (Exception ex)
        {
            message = "无法提交视频导出界面切换：" + ex.Message;
            MatchReplayPlayer.FailCommittedStart(message);
            if (!originCommitted)
            {
                failed?.Invoke(message);
            }
            return false;
        }
    }

    private static bool CanLaunch(out string message)
    {
        if (MatchReplayReturnCoordinator.IsReturning)
        {
            message = "上一场回放正在恢复对局记录页面，请稍候再试。";
            return false;
        }

        if (MatchReplaySessionState.Phase != MatchReplayLifecyclePhase.Idle)
        {
            message = MatchReplaySessionState.IsExiting
                ? "上一场回放仍在退出并恢复对局记录页面，请稍候再试。"
                : "已有对局正在准备或回放。";
            return false;
        }

        message = "";
        return true;
    }
}
