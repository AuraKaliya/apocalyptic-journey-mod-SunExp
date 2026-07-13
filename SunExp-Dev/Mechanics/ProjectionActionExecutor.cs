using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

/// <summary>
/// Executes an already committed friendly-AI plan without routing through
/// ObjectAction.ActionExecute, whose native target setup assumes Enemy actors.
/// </summary>
public static class ProjectionActionExecutor
{
    public static bool Execute(OtherObj actor, CompanionBattleState state, ObjectCard? action)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            return ExecuteCore(actor, state, action);
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("ProjectionAction.Execute", start);
        }
    }

    private static bool ExecuteCore(OtherObj actor, CompanionBattleState state, ObjectCard? action)
    {
        var plan = state?.CurrentPlan;
        if (actor == null || state == null || plan == null || plan.IsWait)
        {
            return false;
        }

        plan = ProjectionEffectContextService.RefreshLockedPlan(actor, state, plan);
        state.CurrentPlan = plan;
        if (!CompanionIntentExecutor.CanExecute(plan))
        {
            return false;
        }

        var executor = actor.dataConfig?.scriptExecutor as ScriptExecutor;
        if (executor == null)
        {
            return false;
        }

        var intent = CompanionIntentResolver.Find(state, plan.IntentId);
        if (intent == null || !CommittedTargetsAreValid(state, intent, plan.ResolvedEffects))
        {
            SunExpLog.Warn("[ProjectionAction] rejected committed target outside intent scope: " + plan.IntentId);
            return false;
        }

        foreach (var effect in plan.ResolvedEffects)
        {
            if (!CompanionIntentHandlerRegistry.TryGet(effect.HandlerId, out var handler))
            {
                SunExpLog.Warn("[ProjectionAction] rejected unknown handler: " + effect.HandlerId);
                continue;
            }

            handler.Execute(executor, effect);
        }

        FightActionPresentationApi.PresentCommittedAction(
            action?.dataConfig?.scriptExecutor as ScriptExecutor,
            actor.Status,
            PresentationTargets(plan),
            "ProjectionActionExecutor.Execute");

        if (intent != null)
        {
            CompanionIntentSelector.CommitResolvedPlan(state, plan);
        }

        SunExpPerformanceCounters.Record("ProjectionAction.DedicatedExecuted");
        return true;
    }

    private static bool CommittedTargetsAreValid(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IReadOnlyList<CompanionResolvedEffect> resolvedEffects)
    {
        var specs = CompanionIntentEffects.Expand(intent);
        if (resolvedEffects == null || resolvedEffects.Count != specs.Count)
        {
            return false;
        }

        for (var index = 0; index < resolvedEffects.Count; index++)
        {
            var effectIntent = CompanionIntentEffects.AsDefinition(intent, specs[index]);
            if (CompanionTargetPolicyRegistry.Alive(resolvedEffects[index].TargetIds).Any(target =>
                    !CompanionTargetPolicyRegistry.IsValidCommittedTarget(state, effectIntent, target)))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<IStatusManager> PresentationTargets(CompanionIntentPlan plan)
    {
        var result = new List<IStatusManager>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in plan.ResolvedEffects ?? new List<CompanionResolvedEffect>())
        {
            AddAlive(effect.TargetIds, result, seen);
        }

        AddAlive(plan.OrderedTargetIds, result, seen);
        return result;
    }

    private static void AddAlive(
        IEnumerable<string>? targetIds,
        ICollection<IStatusManager> result,
        ISet<string> seen)
    {
        foreach (var target in CompanionTargetPolicyRegistry.Alive(targetIds))
        {
            if (seen.Add(target.InstanceId))
            {
                result.Add(target);
            }
        }
    }
}
