using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

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
            EnemyCardId = TerriasIds.ProjectionActionWaitCardId,
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
    public static CompanionIntentPlan Create(OtherObj? projection, CompanionBattleState? state)
    {
        var isSpirit = string.Equals(state?.EntityKind, "SpiritAttachment", StringComparison.Ordinal);
        var started = isSpirit ? TerriasPerformanceCounters.Timestamp() : 0L;
        CompanionIntentPlan plan = CreateCore(projection, state)!;
        if (isSpirit)
        {
            TerriasPerformanceCounters.RecordHotspot(
                "Spirit.Intent.Plan",
                started,
                "status=" + (state?.StatusId ?? "<none>")
                + ", intent=" + plan.IntentId
                + ", wait=" + plan.IsWait,
                logFirstSample: true);
        }
        return plan;
    }

    private static CompanionIntentPlan CreateCore(OtherObj? projection, CompanionBattleState? state)
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
        var executor = projection.dataConfig?.scriptExecutor as ScriptExecutor;
        if (executor == null)
        {
            return CompanionSystemPlans.Wait(state);
        }

        var resolvedEffects = new List<CompanionResolvedEffect>();
        foreach (var effectSpec in CompanionIntentEffects.Expand(intent))
        {
            var effectIntent = CompanionIntentEffects.AsDefinition(intent, effectSpec);
            if (!CompanionIntentHandlerRegistry.TryGet(effectIntent.HandlerId, out var handler))
            {
                TerriasLog.Warn("[CompanionIntent] missing handler while planning: " + effectIntent.HandlerId);
                return CompanionSystemPlans.Wait(state);
            }

            var targets = CompanionTargetPolicyRegistry.Resolve(executor, state, effectIntent);
            if (targets.Count == 0)
            {
                return CompanionSystemPlans.Wait(state);
            }

            resolvedEffects.Add(handler.Resolve(state, effectIntent, targets));
        }

        if (resolvedEffects.Count == 0)
        {
            return CompanionSystemPlans.Wait(state);
        }

        var primaryEffect = resolvedEffects[0];
        SpiritTrainingBattleRuntime.ApplyPlanModifiers(
            state,
            intent,
            resolvedEffects,
            out var numericBonusPercent,
            out var appliedModifierKeys,
            out var effectiveCost);
        SpiritArtifactBattleRuntime.ApplyPlanModifiers(
            state,
            intent,
            resolvedEffects,
            ref effectiveCost,
            appliedModifierKeys);
        var plan = new CompanionIntentPlan
        {
            PlanId = CompanionSystemPlans.PlanId(state),
            StatusId = state.StatusId,
            TurnIndex = state.TurnIndex,
            IntentId = intent.Id,
            EnemyCardId = intent.EnemyCardId,
            OrderedTargetIds = resolvedEffects.SelectMany(effect => effect.TargetIds)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            ResolvedValue = primaryEffect.Value,
            Cost = effectiveCost,
            ReadyOnTurn = state.TurnIndex + Math.Max(0, intent.Cooldown) + 1,
            PreviewThreat = Math.Max(0, Math.Min(CompanionThreatService.MaxPreviewThreat, intent.Threat?.Preview ?? 0)),
            Priority = choice.Value.Priority,
            StateRevision = state.Revision + 1,
            IsWait = false,
            NumericBonusPercent = numericBonusPercent,
            AppliedModifierKeys = appliedModifierKeys,
            ResolvedEffects = resolvedEffects
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

        var intent = CompanionIntentResolver.Find(state, plan.IntentId);
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
        var values = string.Join(",", (plan.ResolvedEffects ?? new List<CompanionResolvedEffect>())
            .Select(effect => effect.HandlerId + "=" + effect.Value
                + (effect.BuffStacks > 0 ? "/stacks=" + effect.BuffStacks : "")
                + (effect.RepeatCount > 1 ? "/hits=" + effect.RepeatCount : "")));
        var targets = string.Join(",", plan.OrderedTargetIds ?? new List<string>());
        var intent = CompanionIntentResolver.Find(state, plan.IntentId);
        var friendlyRoster = string.Join(",", CompanionFriendlyRosterService.Snapshot()
            .Where(CompanionTargetPolicyRegistry.IsAlive)
            .Select(status => status.InstanceId));
        TerriasLog.Info("[ProjectionPlan] committed"
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
            + " effects=" + (values.Length == 0 ? "none" : values)
            + " magic=" + state.Stats.CurrentMagic
            + " cost=" + plan.Cost
            + " targets=" + (targets.Length == 0 ? "none" : targets)
            + " friendlyRoster=" + (friendlyRoster.Length == 0 ? "none" : friendlyRoster)
            + " priority=" + plan.Priority
            + " reason=" + (plan.IsWait ? "no-eligible-intent" : "priority-weighted"));
    }

}
