using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.Mechanics;

public static class CompanionIntentSelector
{
    public static CompanionIntentChoice? Select(OtherObj projection, CompanionBattleState state)
    {
        if (projection?.dataConfig?.scriptExecutor == null || state == null)
        {
            return null;
        }

        var executor = projection.dataConfig.scriptExecutor as ScriptExecutor;
        if (executor == null)
        {
            return null;
        }

        var attackChoices = BuildChoices(projection, executor, state, CompanionIntentTendency.Attack);
        var defenseChoices = BuildChoices(projection, executor, state, CompanionIntentTendency.Defense);
        var tendency = PickTendency(state, attackChoices, defenseChoices);
        var choices = tendency == CompanionIntentTendency.Attack ? attackChoices : defenseChoices;
        if (choices.Count == 0)
        {
            choices = tendency == CompanionIntentTendency.Attack ? defenseChoices : attackChoices;
        }

        if (choices.Count == 0)
        {
            return null;
        }

        var weightedChoices = choices
            .OrderByDescending(choice => choice.Priority)
            .ThenBy(choice => choice.Intent.Id, StringComparer.Ordinal)
            .ToList();
        return PickWeighted(weightedChoices);
    }

    public static bool CommitResolvedPlan(CompanionBattleState state, CompanionIntentPlan plan)
    {
        if (state == null || plan == null || plan.IsWait)
        {
            return false;
        }

        var intent = CompanionIntentResolver.Find(state, plan.IntentId);
        if (intent == null || !state.Stats.TrySpendMagic(plan.Cost))
        {
            return false;
        }

        state.StartCooldown(intent.Id, intent.Cooldown);
        state.CurrentIntentId = intent.Id;
        SpiritTrainingBattleRuntime.OnIntentExecuted(state, intent, plan);
        CompanionThreatService.MarkIntentUsed(
            state,
            intent,
            plan.ResolvedValue,
            plan.ResolvedEffects.Count == 0 ? 1 : plan.ResolvedEffects[0].RepeatCount);
        return true;
    }

    private static List<CompanionIntentChoice> BuildChoices(
        OtherObj projection,
        ScriptExecutor executor,
        CompanionBattleState state,
        CompanionIntentTendency tendency)
    {
        var result = new List<CompanionIntentChoice>();
        foreach (var intent in CompanionIntentResolver.IntentsFor(state, tendency))
        {
            if (!state.IsReady(intent.Id) || state.Stats.CurrentMagic < SpiritTrainingBattleRuntime.PreviewCost(state, intent))
            {
                continue;
            }

            var targets = CompanionTargetPolicyRegistry.Resolve(executor, state, intent);
            var target = targets.FirstOrDefault();
            if (targets.Count == 0)
            {
                continue;
            }

            if (!SpiritTrainingBattleRuntime.IsEligible(state, intent, targets))
            {
                continue;
            }

            var priority = DynamicPriority(executor, state, intent, target);
            if (IsRedundantBuff(intent, targets))
            {
                priority = Math.Max(1, priority - 25);
            }

            result.Add(new CompanionIntentChoice(intent, target, priority));
        }

        return result;
    }

    private static CompanionIntentTendency PickTendency(
        CompanionBattleState state,
        List<CompanionIntentChoice> attackChoices,
        List<CompanionIntentChoice> defenseChoices)
    {
        if (attackChoices.Count == 0)
        {
            return CompanionIntentTendency.Defense;
        }

        if (defenseChoices.Count == 0)
        {
            return CompanionIntentTendency.Attack;
        }

        var weights = CompanionIntentResolver.TendencyWeightsFor(state);
        var attackWeight = weights.Attack;
        var defenseWeight = weights.Defense + RecoveryUrgency(defenseChoices);
        return UnityEngine.Random.Range(0, attackWeight + defenseWeight) < attackWeight
            ? CompanionIntentTendency.Attack
            : CompanionIntentTendency.Defense;
    }

    private static CompanionIntentChoice PickWeighted(List<CompanionIntentChoice> choices)
    {
        if (choices.Count == 1)
        {
            return choices[0];
        }

        var total = choices.Sum(choice => Math.Max(1, choice.Priority));
        var roll = UnityEngine.Random.Range(0, Math.Max(1, total));
        var cursor = 0;
        foreach (var choice in choices)
        {
            cursor += Math.Max(1, choice.Priority);
            if (roll < cursor)
            {
                return choice;
            }
        }

        return choices[choices.Count - 1];
    }

    private static int DynamicPriority(
        ScriptExecutor executor,
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IStatusManager? target)
    {
        var priority = intent.BasePriority + TypePriority(executor, state, intent, target);
        switch ((intent.PriorityBonus ?? "").Trim())
        {
            case "execute_low_hp":
                var damage = CompanionIntentExecutor.ResolveValue(state, intent) * Math.Max(1, intent.HitCount);
                if (target != null && target.CurHp <= damage)
                {
                    priority += 40;
                }
                else if (HpPercent(target) <= 30)
                {
                    priority += 15;
                }

                break;
            case "low_hp_or_no_block":
                if (HpPercent(target) <= 35)
                {
                    priority += 35;
                }

                if (target != null && target.Defend <= 0)
                {
                    priority += 10;
                }

                break;
        }

        return Math.Max(1, priority);
    }

    private static int TypePriority(
        ScriptExecutor executor,
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IStatusManager? target)
    {
        return CompanionIntentResolver.IntentType(state, intent) switch
        {
            CompanionIntentType.Attack => 16 + EnemyPressure(executor) - HighThreatLowHpPenalty(state),
            CompanionIntentType.Defense => 8 + MissingBlock(target) + CompanionThreatService.ThreatPressurePercent(state) / 4,
            CompanionIntentType.Support => 6 + MissingBlock(target) / 2,
            CompanionIntentType.Recovery => 12 + Math.Max(0, 60 - HpPercent(target)) + CompanionThreatService.ThreatPressurePercent(state) / 5,
            CompanionIntentType.Interference => 9 + EnemyPressure(executor) / 2,
            _ => 5
        };
    }

    private static int HighThreatLowHpPenalty(CompanionBattleState state)
    {
        var statusId = string.Equals(state.EntityKind, "SpiritAttachment", StringComparison.Ordinal)
            ? state.StatusId
            : state.OwnerStatusId;
        var status = FightManager.Instance?.statuses?.TryGetValue(statusId, out var value) == true ? value : null;
        if (HpPercent(status) > 35)
        {
            return 0;
        }

        return CompanionThreatService.ThreatPressurePercent(state) / 5;
    }

    private static int EnemyPressure(ScriptExecutor executor)
    {
        return ExecutorApi.EnemyTargets(executor)
            .Where(IsAlive)
            .Count() * 5;
    }

    private static int MissingBlock(IStatusManager? target)
    {
        return target == null ? 0 : Math.Max(0, 12 - target.Defend);
    }

    private static int RecoveryUrgency(IEnumerable<CompanionIntentChoice> choices)
    {
        var wounded = choices
            .Where(choice => CompanionIntentResolver.IntentType(null, choice.Intent) == CompanionIntentType.Recovery)
            .Select(choice => HpPercent(choice.Target))
            .DefaultIfEmpty(100)
            .Min();
        return wounded <= 20 ? 60 : wounded <= 35 ? 30 : 0;
    }

    private static bool IsRedundantBuff(
        CompanionIntentDefinition intent,
        IReadOnlyList<IStatusManager> targets)
    {
        return string.Equals(intent.HandlerId, CompanionIntentHandlerRegistry.ApplyBuff, StringComparison.Ordinal)
            && targets.Count > 0
            && targets.All(target => ExecutorApi.StatusBuffLevel(target, intent.BuffId) >= intent.BuffStacks);
    }

    private static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }

    private static int HpPercent(IStatusManager? status)
    {
        if (status == null || status.MaxHp <= 0)
        {
            return 100;
        }

        return Math.Max(0, Math.Min(100, status.CurHp * 100 / Math.Max(1, status.MaxHp)));
    }
}
