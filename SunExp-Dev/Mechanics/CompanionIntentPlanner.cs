using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class CompanionSystemPlans
{
    public const string WaitIntentId = "system.wait";

    public static CompanionIntentPlan Wait(CompanionBattleState state)
    {
        return new CompanionIntentPlan
        {
            PlanId = PlanId(state),
            StatusId = state.StatusId,
            TurnIndex = state.TurnIndex,
            IntentId = WaitIntentId,
            EnemyCardId = SunExpIds.ProjectionActionWaitCardId,
            ResolvedValue = 0,
            Cost = 0,
            ReadyOnTurn = state.TurnIndex,
            PreviewThreat = 0,
            Priority = 1,
            StateRevision = state.Revision + 1,
            IsWait = true
        };
    }

    internal static string PlanId(CompanionBattleState state)
    {
        return CompanionAuthorityService.BattleEpoch
            + ":" + state.StatusId
            + ":" + state.TurnIndex
            + ":" + (state.Revision + 1);
    }
}

public static class CompanionIntentPlanner
{
    public static CompanionIntentPlan Create(ProjectionOtherObj projection, CompanionBattleState state)
    {
        if (projection == null || state == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return state?.CurrentPlan ?? (state == null
                ? new CompanionIntentPlan { IsWait = true, IntentId = CompanionSystemPlans.WaitIntentId }
                : CompanionSystemPlans.Wait(state));
        }

        var choice = CompanionIntentSelector.Select(projection, state);
        if (choice == null)
        {
            return CompanionSystemPlans.Wait(state);
        }

        var intent = choice.Value.Intent;
        if (!CompanionIntentHandlerRegistry.TryGet(intent.HandlerId, out var handler))
        {
            SunExpLog.Warn("[CompanionIntent] missing handler while planning: " + intent.HandlerId);
            return CompanionSystemPlans.Wait(state);
        }

        var executor = projection.dataConfig?.scriptExecutor as ScriptExecutor;
        if (executor == null)
        {
            return CompanionSystemPlans.Wait(state);
        }

        var targets = CompanionTargetPolicyRegistry.Resolve(executor, state, intent);
        if (targets.Count == 0)
        {
            return CompanionSystemPlans.Wait(state);
        }

        var resolvedEffect = handler.Resolve(state, intent, targets);
        var plan = new CompanionIntentPlan
        {
            PlanId = CompanionSystemPlans.PlanId(state),
            StatusId = state.StatusId,
            TurnIndex = state.TurnIndex,
            IntentId = intent.Id,
            EnemyCardId = intent.EnemyCardId,
            OrderedTargetIds = new List<string>(resolvedEffect.TargetIds),
            ResolvedValue = resolvedEffect.Value,
            Cost = Math.Max(0, intent.Cost),
            ReadyOnTurn = state.TurnIndex + Math.Max(0, intent.Cooldown) + 1,
            PreviewThreat = Math.Max(0, Math.Min(CompanionThreatService.MaxPreviewThreat, intent.Threat?.Preview ?? 0)),
            Priority = choice.Value.Priority,
            StateRevision = state.Revision + 1,
            IsWait = false,
            ResolvedEffects = new List<CompanionResolvedEffect> { resolvedEffect }
        };
        return ProjectionEffectContextService.RefreshLockedPlan(projection, state, plan);
    }

    public static void Commit(CompanionBattleState state, CompanionIntentPlan plan)
    {
        if (state == null || plan == null)
        {
            return;
        }

        state.TouchRevision();
        state.CurrentPlan = plan.Snapshot();
        state.CurrentIntentId = plan.IntentId;
        LogAuthoritativeCommit(state, plan);
        if (plan.IsWait)
        {
            CompanionThreatService.ClearPreview(state);
            return;
        }

        var intent = CompanionIntentRegistry.Find(plan.IntentId);
        if (intent != null)
        {
            CompanionThreatService.SetPreview(state, intent, plan.ResolvedValue,
                plan.ResolvedEffects.Count == 0 ? 1 : plan.ResolvedEffects[0].RepeatCount);
        }
    }

    private static void LogAuthoritativeCommit(CompanionBattleState state, CompanionIntentPlan plan)
    {
        if (!CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }

        var handlers = string.Join(",", (plan.ResolvedEffects ?? new List<CompanionResolvedEffect>())
            .Select(effect => effect.HandlerId)
            .Where(handlerId => !string.IsNullOrWhiteSpace(handlerId))
            .Distinct(StringComparer.Ordinal));
        var targets = string.Join(",", plan.OrderedTargetIds ?? new List<string>());
        var intent = CompanionIntentRegistry.Find(plan.IntentId);
        var friendlyRoster = string.Join(",", CompanionFriendlyRosterService.Snapshot(includeControlled: true)
            .Where(CompanionTargetPolicyRegistry.IsAlive)
            .Select(status => status.InstanceId));
        SunExpLog.Info("[ProjectionPlan] committed"
            + " battleEpoch=" + CompanionAuthorityService.BattleEpoch
            + " projection=" + state.StatusId
            + " owner=" + state.OwnerStatusId
            + " turn=" + plan.TurnIndex
            + " revision=" + plan.StateRevision
            + " plan=" + plan.PlanId
            + " status=" + (plan.IsWait ? "WaitingForMagicOrIntent" : "Ready")
            + " intent=" + plan.IntentId
            + " scope=" + (intent?.Target?.Scope ?? "none")
            + " handler=" + (handlers.Length == 0 ? "none" : handlers)
            + " magic=" + state.Stats.CurrentMagic
            + " cost=" + plan.Cost
            + " targets=" + (targets.Length == 0 ? "none" : targets)
            + " friendlyRoster=" + (friendlyRoster.Length == 0 ? "none" : friendlyRoster)
            + " priority=" + plan.Priority
            + " reason=" + (plan.IsWait ? "no-eligible-intent" : "priority-weighted"));
    }

}
