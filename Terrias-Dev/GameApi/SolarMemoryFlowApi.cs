using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.GameApi;

public static class SolarMemoryFlowApi
{
    public static void ContinueAfterPreparation()
    {
        if (!IsPreparationComplete())
        {
            TerriasLog.Info("[SolarMemoryEvent] continue requested before preparation complete; resuming preparation.");
            StartOrResumePreparation();
            return;
        }

        if (SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryPostPreparationDialoguePendingKey))
        {
            TerriasLog.Info("[SolarMemoryEvent] clearing stale post-preparation dialogue state before opening C# flow.");
            SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryPostPreparationDialoguePendingKey, false);
        }

        if (SolarMemoryStoryGateService.TryStartPostPreparationDialogue(
            SolarMemoryModeRuntime.IsSolarMemoryRun(),
            SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryPostPreparationDialogueSeenKey),
            _ => CompletePostPreparationDialogue()))
        {
            SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryPostPreparationDialoguePendingKey, true);
            return;
        }

        CompletePreparedEvent();
    }

    public static void CompletePostPreparationDialogue()
    {
        SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryPostPreparationDialoguePendingKey, false);
        SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryPostPreparationDialogueSeenKey, true);
        CompletePreparedEvent();
    }

    public static bool StartSecondSunEndingDialogue()
    {
        return SolarMemoryStoryGateService.TryStartDialogue(
            TerriasIds.SolarMemorySecondSunEndingDialogueFlowId,
            TerriasIds.SolarMemorySecondSunEndingDialogueId,
            TerriasIds.SolarMemorySecondSunEndingCompleteDialogueId,
            "second-sun ending",
            _ => CompleteSecondSunEndingDialogue());
    }

    public static bool StartSaintWunaPreludeDialogue()
    {
        return SolarMemoryStoryGateService.TryStartDialogue(
            TerriasIds.SolarMemorySaintWunaPreludeDialogueFlowId,
            TerriasIds.SolarMemorySaintWunaPreludeDialogueId,
            TerriasIds.SolarMemorySaintWunaPreludeCompleteDialogueId,
            "saint-wuna prelude",
            _ => CompleteSaintWunaPreludeDialogue());
    }

    public static bool StartSaintWunaEndingDialogue()
    {
        return SolarMemoryStoryGateService.TryStartDialogue(
            TerriasIds.SolarMemorySaintWunaEndingDialogueFlowId,
            TerriasIds.SolarMemorySaintWunaEndingDialogueId,
            TerriasIds.SolarMemorySaintWunaEndingCompleteDialogueId,
            "saint-wuna ending",
            _ => CompleteSaintWunaEndingDialogue());
    }

    public static void CompleteSecondSunEndingDialogue()
    {
        SolarMemoryBossTransitionCoordinator.CompleteSolarMemoryRunForSettlementFromDialogue("SolarMemoryDialogue:second_sun_without_key_card");
    }

    public static void CompleteSaintWunaPreludeDialogue()
    {
        SolarMemoryBossTransitionCoordinator.ContinueSaintWunaBossFromPreludeDialogue("SolarMemoryDialogue:saint_wuna_prelude");
    }

    public static void CompleteSaintWunaEndingDialogue()
    {
        SolarMemoryBossTransitionCoordinator.CompleteSolarMemoryRunForSettlementFromDialogue("SolarMemoryDialogue:saint_wuna");
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
        if (SolarMemoryPlayerSetupState.GetValue(TerriasIds.SolarMemoryOriginPointsKey, "") == "")
        {
            SolarMemoryPlayerSetupState.SetInt(TerriasIds.SolarMemoryOriginPointsKey, defaultValue);
        }
    }

    public static void MarkPrepared()
    {
        SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryPreparedKey, true);
    }

    private static void CompletePreparedEvent()
    {
        TerriasLog.Info("[SolarMemoryEvent] continue accepted; prepared=1.");
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
        SolarMemoryDeckIsolationRuntime.OpenDeckWindow();
    }

    public static void ShowSettlement()
    {
        SolarMemorySettlementCoordinator.ShowSolarMemorySettlement();
    }
}
