using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.UI;

namespace SunExp.Dll.Hooks;

public enum SolarMemoryPrepStep
{
    None,
    DeckSelection,
    OriginAllocation,
    BlessingSelection,
    Complete
}

public static class SolarMemoryPreparationRuntime
{
    public static void StartOrResume()
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                SunExpLog.Warn("[SolarMemoryPrep] StartOrResume skipped: not a Solar Memory run.");
                return;
            }

            if (RoleTable.Instance == null)
            {
                SunExpLog.Warn("[SolarMemoryPrep] StartOrResume skipped: RoleTable is null.");
                return;
            }

            var step = ReadOrInferStep();
            WriteStep(step);
            SunExpLog.Info("[SolarMemoryPrep] StartOrResume: step=" + step + "; inferredState=" + StateSnapshot());
            EnterStep(step);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory preparation start failed", ex);
        }
    }

    public static void CompleteDeckSelection()
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            PlayerApi.SetGameVar(SunExpIds.SolarMemoryDeckConfiguredKey, "1");
            WriteStep(SolarMemoryPrepStep.OriginAllocation);
            SunExpLog.Info("[SolarMemoryPrep] DeckSelection complete; next=OriginAllocation.");
            EnterStep(SolarMemoryPrepStep.OriginAllocation);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory deck preparation completion failed", ex);
        }
    }

    public static void CompleteOriginAllocation()
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            PlayerApi.SetGameVar(SunExpIds.SolarMemoryOriginConfiguredKey, "1");
            WriteStep(SolarMemoryPrepStep.BlessingSelection);
            SunExpLog.Info("[SolarMemoryPrep] OriginAllocation complete; next=BlessingSelection.");
            EnterStep(SolarMemoryPrepStep.BlessingSelection);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory origin preparation completion failed", ex);
        }
    }

    public static void CompleteBlessingSelection()
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun())
            {
                return;
            }

            PlayerApi.SetGameVar(SunExpIds.SolarMemoryBlessConfiguredKey, "1");
            WriteStep(SolarMemoryPrepStep.Complete);
            SunExpLog.Info("[SolarMemoryPrep] BlessingSelection complete; next=Complete.");
            FinishPreparation();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory blessing preparation completion failed", ex);
        }
    }

    public static bool IsComplete()
    {
        return ReadOrInferStep() == SolarMemoryPrepStep.Complete;
    }

    private static void EnterStep(SolarMemoryPrepStep step)
    {
        switch (step)
        {
            case SolarMemoryPrepStep.DeckSelection:
                if (!SolarMemoryStarterDeckRuntime.OpenOrResume())
                {
                    SunExpLog.Info("[SolarMemoryPrep] DeckSelection already satisfied or unavailable; advancing from state snapshot: " + StateSnapshot());
                    CompleteDeckSelection();
                }
                return;
            case SolarMemoryPrepStep.OriginAllocation:
                SolarMemorySetupFlowRuntime.OpenOriginSetupWindow();
                return;
            case SolarMemoryPrepStep.BlessingSelection:
                SolarMemorySetupFlowRuntime.OpenBlessingSetupWindow();
                return;
            case SolarMemoryPrepStep.Complete:
                FinishPreparation();
                return;
            default:
                WriteStep(SolarMemoryPrepStep.DeckSelection);
                EnterStep(SolarMemoryPrepStep.DeckSelection);
                return;
        }
    }

    private static void FinishPreparation()
    {
        PlayerApi.SetGameVar(SunExpIds.SolarMemoryBlessConfiguredKey, "1");
        PlayerApi.SetGameVar(SunExpIds.SolarMemorySetupFinishedKey, "1");
        WriteStep(SolarMemoryPrepStep.Complete);
        SolarMemorySetupFlowRuntime.ClosePreparationWindows();
        UIManager.Instance?.ShowTip("日耀回忆整备完成", null);
        SunExpLog.Info("[SolarMemoryPrep] Complete; setupFinished=1; snapshot=" + StateSnapshot());
    }

    private static SolarMemoryPrepStep ReadOrInferStep()
    {
        var saved = PlayerApi.GetGameVar(SunExpIds.SolarMemoryPrepStepKey, "");
        if (Enum.TryParse<SolarMemoryPrepStep>(saved, out var parsed)
            && parsed != SolarMemoryPrepStep.None
            && IsStepStillValid(parsed))
        {
            return parsed;
        }

        var inferred = InferStepFromLegacyState();
        SunExpLog.Info("[SolarMemoryPrep] inferred step from legacy state: saved="
            + (string.IsNullOrWhiteSpace(saved) ? "<empty>" : saved)
            + "; inferred="
            + inferred
            + "; snapshot="
            + StateSnapshot());
        WriteStep(inferred);
        return inferred;
    }

    private static bool IsStepStillValid(SolarMemoryPrepStep step)
    {
        return step switch
        {
            SolarMemoryPrepStep.Complete => PlayerApi.GetGameVar(SunExpIds.SolarMemorySetupFinishedKey, "0") == "1"
                || PlayerApi.GetGameVar(SunExpIds.SolarMemoryBlessConfiguredKey, "0") == "1",
            SolarMemoryPrepStep.BlessingSelection => PlayerApi.GetGameVar(SunExpIds.SolarMemoryOriginConfiguredKey, "0") == "1"
                && PlayerApi.GetGameVar(SunExpIds.SolarMemoryBlessConfiguredKey, "0") != "1",
            SolarMemoryPrepStep.OriginAllocation => IsDeckConfigured()
                && PlayerApi.GetGameVar(SunExpIds.SolarMemoryOriginConfiguredKey, "0") != "1",
            SolarMemoryPrepStep.DeckSelection => !IsDeckConfigured(),
            _ => false
        };
    }

    private static SolarMemoryPrepStep InferStepFromLegacyState()
    {
        if (PlayerApi.GetGameVar(SunExpIds.SolarMemorySetupFinishedKey, "0") == "1"
            || PlayerApi.GetGameVar(SunExpIds.SolarMemoryBlessConfiguredKey, "0") == "1")
        {
            return SolarMemoryPrepStep.Complete;
        }

        if (PlayerApi.GetGameVar(SunExpIds.SolarMemoryOriginConfiguredKey, "0") == "1")
        {
            return SolarMemoryPrepStep.BlessingSelection;
        }

        return IsDeckConfigured()
            ? SolarMemoryPrepStep.OriginAllocation
            : SolarMemoryPrepStep.DeckSelection;
    }

    private static bool IsDeckConfigured()
    {
        if (PlayerApi.GetGameVar(SunExpIds.SolarMemoryDeckConfiguredKey, "0") == "1"
            || PlayerApi.GetGameVar(SunExpIds.SolarMemoryStarterDeckAppliedKey, "0") == "1")
        {
            return true;
        }

        var role = RoleTable.Instance;
        return role?.SpecialVarMap != null
            && role.SpecialVarMap.TryGetValue(SunExpIds.SolarMemoryStarterDeckAppliedKey, out var value)
            && value == "1";
    }

    private static void WriteStep(SolarMemoryPrepStep step)
    {
        PlayerApi.SetGameVar(SunExpIds.SolarMemoryPrepStepKey, step.ToString());
    }

    private static string StateSnapshot()
    {
        return "deck="
            + PlayerApi.GetGameVar(SunExpIds.SolarMemoryDeckConfiguredKey, "0")
            + "; starter="
            + PlayerApi.GetGameVar(SunExpIds.SolarMemoryStarterDeckAppliedKey, "0")
            + "; origin="
            + PlayerApi.GetGameVar(SunExpIds.SolarMemoryOriginConfiguredKey, "0")
            + "; bless="
            + PlayerApi.GetGameVar(SunExpIds.SolarMemoryBlessConfiguredKey, "0")
            + "; setup="
            + PlayerApi.GetGameVar(SunExpIds.SolarMemorySetupFinishedKey, "0")
            + "; step="
            + PlayerApi.GetGameVar(SunExpIds.SolarMemoryPrepStepKey, "");
    }
}
