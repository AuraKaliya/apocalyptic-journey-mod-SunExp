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
            Effect = "Wait",
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
        return new CompanionIntentPlan
        {
            PlanId = CompanionSystemPlans.PlanId(state),
            StatusId = state.StatusId,
            TurnIndex = state.TurnIndex,
            IntentId = intent.Id,
            EnemyCardId = intent.EnemyCardId,
            Effect = intent.Effect ?? "",
            OrderedTargetIds = OrderedTargetIds(projection, state, intent, choice.Value.Target),
            ResolvedValue = CompanionIntentExecutor.ResolveValue(state, intent),
            Cost = Math.Max(0, intent.Cost),
            ReadyOnTurn = state.TurnIndex + Math.Max(0, intent.Cooldown) + 1,
            PreviewThreat = Math.Max(0, Math.Min(CompanionThreatService.MaxPreviewThreat, intent.Threat?.Preview ?? 0)),
            Priority = choice.Value.Priority,
            StateRevision = state.Revision + 1,
            IsWait = false
        };
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
        if (plan.IsWait)
        {
            CompanionThreatService.ClearPreview(state);
            return;
        }

        var intent = CompanionIntentRegistry.Find(plan.IntentId);
        if (intent != null)
        {
            CompanionThreatService.SetPreview(state, intent);
        }
    }

    private static List<string> OrderedTargetIds(
        ProjectionOtherObj projection,
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IStatusManager? primary)
    {
        var result = new List<string>();
        AddTarget(result, primary);
        var executor = projection.dataConfig?.scriptExecutor as ScriptExecutor;
        if (executor == null)
        {
            return result;
        }

        if (CompanionIntentRegistry.IntentType(intent) == CompanionIntentType.Attack
            || CompanionIntentRegistry.IntentType(intent) == CompanionIntentType.Interference)
        {
            foreach (var target in ExecutorApi.EnemyTargets(executor)
                         .Where(IsAlive)
                         .OrderBy(target => target.CurHp)
                         .ThenBy(target => target.InstanceId, StringComparer.Ordinal))
            {
                AddTarget(result, target);
            }

            return result;
        }

        var owner = StatusById(state.OwnerStatusId);
        var self = projection.Status;
        AddTarget(result, owner);
        AddTarget(result, self);
        return result;
    }

    private static void AddTarget(List<string> result, IStatusManager? target)
    {
        var id = target?.InstanceId ?? "";
        if (IsAlive(target) && !string.IsNullOrWhiteSpace(id) && !result.Contains(id))
        {
            result.Add(id);
        }
    }

    private static IStatusManager? StatusById(string statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
            && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
                ? status
                : null;
    }

    private static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }
}
