using System;
using AuraToolsExp.Dll.Features.Settings;
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
        MatchReplayFailurePresenter.Dismiss();
        AuraToolsUi.CloseSelectPopup();
        closeOrigin?.Invoke();
        UiTransitionGuardRuntime.BeginTransition(
            null,
            AuraToolsIds.ModId,
            "Match replay launch",
            8);
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
            4);
    }
}
