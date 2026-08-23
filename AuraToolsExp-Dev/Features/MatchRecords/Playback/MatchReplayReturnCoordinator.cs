using System;
using System.Collections;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using UiTransitionGuardShared;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Reconstructs the one supported replay destination: AuraTools match records.
/// It retains only logical page state; every native UI object is recreated after
/// replay teardown reaches its Unity frame boundary.
/// </summary>
internal static class MatchReplayReturnCoordinator
{
    private static MatchRecordLibraryViewState? destination;
    private static bool returning;

    internal static bool IsReturning => returning;

    internal static void Arm(MatchRecordLibraryViewState state)
    {
        if (returning || destination != null)
        {
            throw new InvalidOperationException("A replay return destination is already active.");
        }

        destination = (state ?? throw new ArgumentNullException(nameof(state))).CloneNormalized();
    }

    internal static void Clear()
    {
        destination = null;
        returning = false;
    }

    internal static void ReturnToLibrary(string message)
    {
        if (destination == null)
        {
            return;
        }

        if (returning)
        {
            return;
        }

        returning = true;
        if (AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(
                RebuildLibrary(destination.CloneNormalized(), message ?? "")) == null)
        {
            var failure = new InvalidOperationException(
                "Match-record runtime coroutine driver is unavailable during replay return.");
            AuraToolsLog.Error("[MatchRecords] replay return could not be scheduled", failure);
            Clear();
        }
    }

    private static IEnumerator RebuildLibrary(
        MatchRecordLibraryViewState state,
        string message)
    {
        Exception? failure = null;
        try
        {
            // Remove any stale native cache or overlay from the committed origin.
            // Object.Destroy becomes terminal before the next frame resumes here.
            MatchReplayUiLifecycle.CloseOriginUi("Match replay return reconciliation");
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        yield return null;
        try
        {
            if (failure != null)
            {
                throw new InvalidOperationException(
                    "Replay return reconciliation failed before the Unity frame boundary.",
                    failure);
            }

            if (MatchReplayUiLifecycle.SettingUiCount != 0)
            {
                throw new InvalidOperationException(
                    "A stale SettingUI survived replay return reconciliation.");
            }

            NativeSettingUiCacheApi.PrewarmAndHideFresh();
            var manager = WitchUiManager.Instance
                          ?? throw new InvalidOperationException(
                              "UIManager is unavailable while returning to match records.");
            var setting = manager.ShowUI<SettingUI>("SettingUI")
                          ?? throw new InvalidOperationException(
                              "SettingUI could not be reopened after replay teardown.");
            setting.transform.SetAsLastSibling();
            var panel = AuraToolsSettingsRuntime.OpenForReplayReturn(setting);
            MatchRecordLibraryPresenter.Show(panel, state, message);
            UiTransitionGuardRuntime.ScrubNow(
                null,
                AuraToolsIds.ModId,
                "Match replay returned to records");
            UiTransitionGuardRuntime.RecoverNativeInput(
                null,
                AuraToolsIds.ModId,
                "Match replay returned to records",
                12);
            AuraToolsLog.Info("[MatchRecords] replay returned to match-record library.");
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (failure != null)
        {
            AuraToolsLog.Error("[MatchRecords] replay return to match-record library failed", failure);
        }
        Clear();
    }
}
