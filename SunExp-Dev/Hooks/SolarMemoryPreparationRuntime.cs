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

            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryDeckConfiguredKey, true);
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

            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryOriginConfiguredKey, true);
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

            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryBlessConfiguredKey, true);
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
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryBlessConfiguredKey, true);
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemorySetupFinishedKey, true);
        WriteStep(SolarMemoryPrepStep.Complete);
        SolarMemoryRoleCommitApi.CommitFinal(RoleTable.Instance, "SunExp.SolarMemory.SetupFinished");
        SolarMemorySetupFlowRuntime.ClosePreparationWindows();
        UIManager.Instance?.ShowTip("日耀回忆整备完成", null);
        SunExpLog.Info("[SolarMemoryPrep] Complete; setupFinished=1; snapshot=" + StateSnapshot());
    }

    private static SolarMemoryPrepStep ReadOrInferStep()
    {
        var saved = SolarMemoryPlayerSetupState.GetValue(SunExpIds.SolarMemoryPrepStepKey, "");
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
            SolarMemoryPrepStep.Complete => SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemorySetupFinishedKey)
                || SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryBlessConfiguredKey),
            SolarMemoryPrepStep.BlessingSelection => SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryOriginConfiguredKey)
                && !SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryBlessConfiguredKey),
            SolarMemoryPrepStep.OriginAllocation => IsDeckConfigured()
                && !SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryOriginConfiguredKey),
            SolarMemoryPrepStep.DeckSelection => !IsDeckConfigured(),
            _ => false
        };
    }

    private static SolarMemoryPrepStep InferStepFromLegacyState()
    {
        if (SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemorySetupFinishedKey)
            || SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryBlessConfiguredKey))
        {
            return SolarMemoryPrepStep.Complete;
        }

        if (SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryOriginConfiguredKey))
        {
            return SolarMemoryPrepStep.BlessingSelection;
        }

        return IsDeckConfigured()
            ? SolarMemoryPrepStep.OriginAllocation
            : SolarMemoryPrepStep.DeckSelection;
    }

    private static bool IsDeckConfigured()
    {
        if (SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryDeckConfiguredKey)
            || SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryStarterDeckAppliedKey))
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
        SolarMemoryPlayerSetupState.SetValue(SunExpIds.SolarMemoryPrepStepKey, step.ToString());
    }

    private static string StateSnapshot()
    {
        return SolarMemoryPlayerSetupState.Snapshot();
    }
}
