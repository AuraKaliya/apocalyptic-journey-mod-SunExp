using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.GameApi;

public static class SolarMemoryFlowApi
{
    public static void ContinueAfterPreparation()
    {
        if (!IsPreparationComplete())
        {
            SunExpLog.Info("[SolarMemoryEvent] continue requested before preparation complete; resuming preparation.");
            StartOrResumePreparation();
            return;
        }

        if (SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryPostPreparationDialoguePendingKey))
        {
            SunExpLog.Info("[SolarMemoryEvent] clearing stale post-preparation dialogue state before opening C# flow.");
            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryPostPreparationDialoguePendingKey, false);
        }

        if (SolarMemoryStoryGateService.TryStartPostPreparationDialogue(
            SolarMemoryModeRuntime.IsSolarMemoryRun(),
            SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryPostPreparationDialogueSeenKey),
            CompletePostPreparationDialogue))
        {
            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryPostPreparationDialoguePendingKey, true);
            return;
        }

        CompletePreparedEvent();
    }

    public static void CompletePostPreparationDialogue()
    {
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryPostPreparationDialoguePendingKey, false);
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryPostPreparationDialogueSeenKey, true);
        CompletePreparedEvent();
    }

    public static bool IsPreparationComplete()
    {
        return SolarMemoryPreparationRuntime.IsComplete();
    }

    public static void StartOrResumePreparation()
    {
        SolarMemoryPreparationRuntime.StartOrResume();
    }

    public static void EnsureOriginPoints(int defaultValue)
    {
        if (SolarMemoryPlayerSetupState.GetValue(SunExpIds.SolarMemoryOriginPointsKey, "") == "")
        {
            SolarMemoryPlayerSetupState.SetInt(SunExpIds.SolarMemoryOriginPointsKey, defaultValue);
        }
    }

    public static void MarkPrepared()
    {
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryPreparedKey, true);
    }

    private static void CompletePreparedEvent()
    {
        SunExpLog.Info("[SolarMemoryEvent] continue accepted; prepared=1.");
        MarkPrepared();
        PlayerApi.EndEvent();
    }

    public static void OpenOriginWindow()
    {
        SolarMemoryModeRuntime.OpenOriginWindow();
    }

    public static void OpenBlessingWindow()
    {
        SolarMemoryModeRuntime.OpenBlessingWindow();
    }

    public static void OpenDeckWindow()
    {
        SolarMemoryModeRuntime.OpenDeckWindow();
    }

    public static void ShowSettlement()
    {
        SolarMemoryModeRuntime.ShowSolarMemorySettlement();
    }
}
