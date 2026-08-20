using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public static class TerriasFightPresentationInvalidationService
{
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        TerriasBuffMutationRouter.Register("FightPresentationInvalidation", new TerriasBuffMutationSubscription
        {
            Priority = -1000,
            Changed = OnBuffChanged,
            CheckCompleted = OnCheckCompleted
        });
    }

    private static void OnBuffChanged(TerriasBuffMutationContext mutation)
    {
        if (mutation.ContainsNestedMutation)
        {
            return;
        }

        if (!TerriasBuffPresentationDependencyCatalog.TryResolve(mutation.BuffId, out var rule))
        {
            TerriasPerformanceCounters.Record("PresentationInvalidation.UnknownBuff");
            return;
        }

        if (!RuleApplies(rule, mutation.Status))
        {
            return;
        }

        if (!rule.ShouldInvalidate(mutation.BeforeLevel, mutation.AfterLevel))
        {
            TryClearNativeRefresh(mutation.WasNativeRefreshPending, mutation.IsNativeRefreshPending, "stable-threshold");
            return;
        }

        if (!CanApplyDelta(rule))
        {
            TerriasPerformanceCounters.Record("PresentationInvalidation.NativeFullRequired");
            return;
        }

        if (mutation.WasNativeRefreshPending)
        {
            return;
        }

        if (mutation.IsNativeRefreshPending
            && !TryClearNativeRefresh(
                mutation.WasNativeRefreshPending,
                mutation.IsNativeRefreshPending,
                "buff:" + mutation.BuffId))
        {
            return;
        }

        Queue(rule, "Buff:" + mutation.BuffId + ":" + mutation.BeforeLevel + "->" + mutation.AfterLevel);
    }

    private static void OnCheckCompleted(TerriasBuffCheckContext check)
    {
        var allKnown = true;
        var allDeltaSafe = true;
        for (var i = 0; i < check.Mutations.Count; i++)
        {
            var mutation = check.Mutations[i];
            if (!TerriasBuffPresentationDependencyCatalog.TryResolve(mutation.BuffId, out var rule))
            {
                allKnown = false;
                break;
            }

            if (!RuleApplies(rule, mutation.Status))
            {
                allDeltaSafe = false;
            }
            else if (rule.ShouldInvalidate(mutation.BeforeLevel, mutation.AfterLevel)
                     && !CanApplyDelta(rule))
            {
                allDeltaSafe = false;
            }
        }

        var decision = TerriasPresentationInvalidationPolicy.Decide(
            check.WasPending,
            check.IsPending,
            TerriasActiveCardPresentationIndex.AllActiveCardsManaged(),
            check.Mutations.Count,
            allKnown,
            allDeltaSafe);
        if (decision == TerriasPresentationInvalidationDecision.PreserveNative)
        {
            TerriasPerformanceCounters.Record("PresentationInvalidation.CheckFullPreserved");
            return;
        }

        if (!SetNativeRefreshPending(false))
        {
            return;
        }

        if (decision == TerriasPresentationInvalidationDecision.SuppressNoChange)
        {
            TerriasPerformanceCounters.Record("PresentationInvalidation.CheckNoChangeSuppressed");
            return;
        }

        for (var i = 0; i < check.Mutations.Count; i++)
        {
            var mutation = check.Mutations[i];
            if (TerriasBuffPresentationDependencyCatalog.TryResolve(mutation.BuffId, out var rule)
                && RuleApplies(rule, mutation.Status)
                && rule.ShouldInvalidate(mutation.BeforeLevel, mutation.AfterLevel))
            {
                Queue(rule, "CheckAllBuff:" + mutation.BuffId);
            }
        }

        TerriasPerformanceCounters.Record("PresentationInvalidation.CheckConvertedToDelta");
    }

    private static bool RuleApplies(TerriasBuffPresentationRule rule, IStatusManager? status)
    {
        if (rule.Scope == TerriasBuffPresentationScope.AnyStatus)
        {
            return true;
        }

        if (rule.Scope == TerriasBuffPresentationScope.Enemy)
        {
            return status?.fatherObject is Enemy;
        }

        var local = FightPlayer.Instance?.Status;
        return status != null
               && local != null
               && (ReferenceEquals(status, local)
                   || string.Equals(status.InstanceId, local.InstanceId, StringComparison.Ordinal));
    }

    private static bool CanApplyDelta(TerriasBuffPresentationRule rule)
    {
        return (rule.Fields & (TerriasPresentationDirtyFields.Skill
                               | TerriasPresentationDirtyFields.EnemyIntent
                               | TerriasPresentationDirtyFields.Structural)) == 0;
    }

    private static void Queue(TerriasBuffPresentationRule rule, string source)
    {
        if (rule.IsNoImpact)
        {
            TerriasPerformanceCounters.Record("PresentationInvalidation.NoImpact");
            return;
        }

        foreach (var card in TerriasActiveCardPresentationIndex.Snapshot(rule.CardIds))
        {
            if ((rule.Fields & (TerriasPresentationDirtyFields.Tags
                                | TerriasPresentationDirtyFields.Visual)) != 0)
            {
                TerriasCardRefreshQueue.RequestFullRefresh(card, source);
                continue;
            }


            if ((rule.Fields & TerriasPresentationDirtyFields.Usability) != 0)
            {
                TerriasCardRefreshQueue.RequestDataUpdate(card, source);
                continue;
            }

            if ((rule.Fields & TerriasPresentationDirtyFields.Description) != 0)
            {
                TerriasCardRefreshQueue.RequestDescriptionUpdate(card, source);
            }

            if ((rule.Fields & TerriasPresentationDirtyFields.Cost) != 0)
            {
                TerriasCardRefreshQueue.RequestCostUpdate(card, source);
            }
        }
    }

    private static bool TryClearNativeRefresh(bool wasPending, bool isPending, string source)
    {
        if (wasPending || !isPending || !TerriasActiveCardPresentationIndex.AllActiveCardsManaged())
        {
            return false;
        }

        if (!SetNativeRefreshPending(false))
        {
            return false;
        }

        TerriasPerformanceCounters.Record("PresentationInvalidation.NativeFullSuppressed");
        TerriasLog.Debug("[PresentationInvalidation] suppressed native full refresh from " + source + ".");
        return true;
    }

    private static bool SetNativeRefreshPending(bool value)
    {
        try
        {
            var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
            if (fightUi == null)
            {
                return false;
            }

            fightUi.NeedUpdateCardMsg = value;
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[PresentationInvalidation] native flag update failed: " + ex.Message);
            return false;
        }
    }
}
