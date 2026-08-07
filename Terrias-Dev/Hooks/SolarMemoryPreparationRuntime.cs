using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.UI;

namespace Terrias.Dll.Hooks;

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
                TerriasLog.Warn("[SolarMemoryPrep] StartOrResume skipped: not a Solar Memory run.");
                return;
            }

            if (RoleTable.Instance == null)
            {
                TerriasLog.Warn("[SolarMemoryPrep] StartOrResume skipped: RoleTable is null.");
                return;
            }

            var step = ReadOrInferStep();
            WriteStep(step);
            TerriasLog.Info("[SolarMemoryPrep] StartOrResume: step=" + step + "; inferredState=" + StateSnapshot());
            EnterStep(step);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory preparation start failed", ex);
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

            SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryDeckConfiguredKey, true);
            WriteStep(SolarMemoryPrepStep.OriginAllocation);
            TerriasLog.Info("[SolarMemoryPrep] DeckSelection complete; next=OriginAllocation.");
            EnterStep(SolarMemoryPrepStep.OriginAllocation);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory deck preparation completion failed", ex);
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

            SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryOriginConfiguredKey, true);
            WriteStep(SolarMemoryPrepStep.BlessingSelection);
            TerriasLog.Info("[SolarMemoryPrep] OriginAllocation complete; next=BlessingSelection.");
            EnterStep(SolarMemoryPrepStep.BlessingSelection);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory origin preparation completion failed", ex);
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

            SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryBlessConfiguredKey, true);
            WriteStep(SolarMemoryPrepStep.Complete);
            TerriasLog.Info("[SolarMemoryPrep] BlessingSelection complete; next=Complete.");
            FinishPreparation();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory blessing preparation completion failed", ex);
        }
    }

    public static bool IsComplete()
    {
        return SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemorySetupFinishedKey)
            && ReadOrInferStep() == SolarMemoryPrepStep.Complete;
    }

    private static void EnterStep(SolarMemoryPrepStep step)
    {
        switch (step)
        {
            case SolarMemoryPrepStep.DeckSelection:
                if (!SolarMemoryStarterDeckRuntime.OpenOrResume())
                {
                    TerriasLog.Info("[SolarMemoryPrep] DeckSelection already satisfied or unavailable; advancing from state snapshot: " + StateSnapshot());
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
        SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemoryBlessConfiguredKey, true);
        SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemorySetupFinishedKey, true);
        WriteStep(SolarMemoryPrepStep.Complete);
        var submission = SolarMemoryRoleCommitApi.SubmitFinal(
            RoleTable.Instance,
            "Terrias.SolarMemory.SetupFinished",
            OnRoleCommitResolved);
        if (submission == SolarMemoryRoleCommitSubmission.Rejected)
        {
            RejectRoleCommit("submission rejected");
            return;
        }

        if (submission == SolarMemoryRoleCommitSubmission.Pending)
        {
            UIManager.Instance?.ShowTip("日耀回忆整备已提交，等待主机确认", null);
            TerriasLog.Info("[SolarMemoryPrep] final role commit is awaiting host acknowledgement. snapshot=" + StateSnapshot());
            return;
        }

        CompleteAfterRoleCommit();
    }

    private static void OnRoleCommitResolved(bool accepted, string rejectionReason)
    {
        if (!accepted)
        {
            RejectRoleCommit(rejectionReason);
            return;
        }

        CompleteAfterRoleCommit();
    }

    private static void CompleteAfterRoleCommit()
    {
        SolarMemorySetupFlowRuntime.ClosePreparationWindows();
        UIManager.Instance?.ShowTip("日耀回忆整备完成", null);
        TerriasLog.Info("[SolarMemoryPrep] Complete; setupFinished=1; snapshot=" + StateSnapshot());
    }

    private static void RejectRoleCommit(string reason)
    {
        SolarMemoryPlayerSetupState.SetFlag(TerriasIds.SolarMemorySetupFinishedKey, false);
        SolarMemoryPlayerSetupState.SetValue(TerriasIds.SolarMemorySetupCommitTokenKey, "");
        TerriasLog.Warn("[SolarMemoryPrep] final role commit failed; setup completion is pending retry. reason="
                        + reason
                        + ", snapshot="
                        + StateSnapshot());
        UIManager.Instance?.ShowTip("日耀回忆整备提交失败，请重试", null);
    }

    private static SolarMemoryPrepStep ReadOrInferStep()
    {
        var saved = SolarMemoryPlayerSetupState.GetValue(TerriasIds.SolarMemoryPrepStepKey, "");
        if (Enum.TryParse<SolarMemoryPrepStep>(saved, out var parsed)
            && parsed != SolarMemoryPrepStep.None
            && IsStepStillValid(parsed))
        {
            return parsed;
        }

        var inferred = InferStepFromLegacyState();
        TerriasLog.Info("[SolarMemoryPrep] inferred step from legacy state: saved="
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
            SolarMemoryPrepStep.Complete => SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemorySetupFinishedKey),
            SolarMemoryPrepStep.BlessingSelection => SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryOriginConfiguredKey)
                && !SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryBlessConfiguredKey),
            SolarMemoryPrepStep.OriginAllocation => IsDeckConfigured()
                && !SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryOriginConfiguredKey),
            SolarMemoryPrepStep.DeckSelection => !IsDeckConfigured(),
            _ => false
        };
    }

    private static SolarMemoryPrepStep InferStepFromLegacyState()
    {
        if (SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemorySetupFinishedKey)
            || SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryBlessConfiguredKey))
        {
            return SolarMemoryPrepStep.Complete;
        }

        if (SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryOriginConfiguredKey))
        {
            return SolarMemoryPrepStep.BlessingSelection;
        }

        return IsDeckConfigured()
            ? SolarMemoryPrepStep.OriginAllocation
            : SolarMemoryPrepStep.DeckSelection;
    }

    private static bool IsDeckConfigured()
    {
        if (SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryDeckConfiguredKey)
            || SolarMemoryPlayerSetupState.IsSet(TerriasIds.SolarMemoryStarterDeckAppliedKey))
        {
            return true;
        }

        var role = RoleTable.Instance;
        return role?.SpecialVarMap != null
            && role.SpecialVarMap.TryGetValue(TerriasIds.SolarMemoryStarterDeckAppliedKey, out var value)
            && value == "1";
    }

    private static void WriteStep(SolarMemoryPrepStep step)
    {
        SolarMemoryPlayerSetupState.SetValue(TerriasIds.SolarMemoryPrepStepKey, step.ToString());
    }

    private static string StateSnapshot()
    {
        return SolarMemoryPlayerSetupState.Snapshot();
    }
}
