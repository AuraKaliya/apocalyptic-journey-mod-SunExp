using System;
using System.Collections;
using AuraToolsExp.Dll.Infrastructure;
using UiTransitionGuardShared;
using UnityEngine;

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

        var transition = AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(
            CommitAfterRenderBarrier(
                returnState,
                export: false,
                started: null,
                failed));
        if (transition == null)
        {
            const string detail = "无法调度回放主渲染帧确认。";
            MatchReplayPlayer.FailPreparedStart(detail);
            failed?.Invoke(detail);
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

        var transition = AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(
            CommitAfterRenderBarrier(
                returnState,
                export: true,
                started,
                failed));
        if (transition == null)
        {
            message = "无法调度视频导出的主渲染帧确认。";
            MatchReplayPlayer.FailPreparedStart(message);
            failed?.Invoke(message);
            return false;
        }
        message = "回放首帧已生成，正在等待游戏主渲染帧确认。";
        return true;
    }

    private static IEnumerator CommitAfterRenderBarrier(
        MatchRecordLibraryViewState returnState,
        bool export,
        Action? started,
        Action<string>? failed)
    {
        yield return new WaitForEndOfFrame();
        if (!MatchReplayPlayer.TryConfirmPreparedRenderBarrier(out var barrierMessage))
        {
            MatchReplayPlayer.FailPreparedStart(barrierMessage);
            failed?.Invoke(barrierMessage);
            yield break;
        }

        var originCommitted = false;
        try
        {
            MatchReplayReturnCoordinator.Arm(returnState);
            MatchReplayPlayer.CommitOrigin();
            originCommitted = true;
            var source = export ? "Match replay video export commit" : "Match replay launch commit";
            UiTransitionGuardRuntime.BeginTransition(null, AuraToolsIds.ModId, source, 8);
            MatchReplayUiLifecycle.CloseOriginUi(source);
            UiTransitionGuardRuntime.RunAfterGuard(
                null,
                AuraToolsIds.ModId,
                export ? "Match replay video export activate" : "Match replay launch activate",
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
        }
        catch (Exception ex)
        {
            var detail = (export ? "无法提交视频导出界面切换：" : "无法提交回放界面切换：")
                         + ex.Message;
            MatchReplayPlayer.FailCommittedStart(detail);
            if (!originCommitted) failed?.Invoke(detail);
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
