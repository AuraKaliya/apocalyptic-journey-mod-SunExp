using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.Mechanics;

public static class CompanionIntentSelector
{
    public static CompanionIntentChoice? Select(ProjectionOtherObj projection, CompanionBattleState state)
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
        var tendency = PickTendency(attackChoices, defenseChoices);
        var choices = tendency == CompanionIntentTendency.Attack ? attackChoices : defenseChoices;
        if (choices.Count == 0)
        {
            choices = tendency == CompanionIntentTendency.Attack ? defenseChoices : attackChoices;
        }

        if (choices.Count == 0)
        {
            return null;
        }

        var type = PickType(choices);
        var typedChoices = choices.Where(choice => CompanionIntentRegistry.IntentType(choice.Intent) == type).ToList();
        var top = typedChoices
            .OrderByDescending(choice => choice.Priority)
            .ThenBy(choice => choice.Intent.Id, StringComparer.Ordinal)
            .Take(3)
            .ToList();
        return PickWeighted(top);
    }

    public static bool CommitResolvedPlan(CompanionBattleState state, CompanionIntentPlan plan)
    {
        if (state == null || plan == null || plan.IsWait)
        {
            return false;
        }

        var intent = CompanionIntentRegistry.Find(plan.IntentId);
        if (intent == null || !state.Stats.TrySpendMagic(plan.Cost))
        {
            return false;
        }

        state.StartCooldown(intent.Id, intent.Cooldown);
        state.CurrentIntentId = intent.Id;
        CompanionThreatService.MarkIntentUsed(state, intent);
        return true;
    }

    private static List<CompanionIntentChoice> BuildChoices(
        ProjectionOtherObj projection,
        ScriptExecutor executor,
        CompanionBattleState state,
        CompanionIntentTendency tendency)
    {
        var result = new List<CompanionIntentChoice>();
        foreach (var intent in CompanionIntentRegistry.IntentsForRole(state.RoleId, tendency))
        {
            if (!state.IsReady(intent.Id) || state.Stats.CurrentMagic < intent.Cost)
            {
                continue;
            }

            var target = CompanionIntentExecutor.SelectTarget(executor, state, intent);
            if (TargetRequired(intent) && target == null)
            {
                continue;
            }

            result.Add(new CompanionIntentChoice(intent, target, DynamicPriority(executor, state, intent, target)));
        }

        return result;
    }

    private static CompanionIntentTendency PickTendency(
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

        var attackWeight = Math.Max(1, attackChoices.Sum(choice => choice.Priority));
        var defenseWeight = Math.Max(1, defenseChoices.Sum(choice => choice.Priority));
        return UnityEngine.Random.Range(0, attackWeight + defenseWeight) < attackWeight
            ? CompanionIntentTendency.Attack
            : CompanionIntentTendency.Defense;
    }

    private static CompanionIntentType PickType(List<CompanionIntentChoice> choices)
    {
        var groups = choices
            .GroupBy(choice => CompanionIntentRegistry.IntentType(choice.Intent))
            .Select(group => new
            {
                Type = group.Key,
                Weight = Math.Max(1, group.Max(choice => choice.Priority))
            })
            .ToList();
        var total = groups.Sum(group => group.Weight);
        var roll = UnityEngine.Random.Range(0, Math.Max(1, total));
        var cursor = 0;
        foreach (var group in groups)
        {
            cursor += group.Weight;
            if (roll < cursor)
            {
                return group.Type;
            }
        }

        return groups[0].Type;
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
                var damage = CompanionIntentExecutor.ResolveValue(state, intent);
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
        return CompanionIntentRegistry.IntentType(intent) switch
        {
            CompanionIntentType.Attack => 10 + EnemyPressure(executor) - HighThreatLowHpPenalty(state),
            CompanionIntentType.Defense => 8 + MissingBlock(target) + CompanionThreatService.ThreatPressurePercent(state) / 4,
            CompanionIntentType.Support => 6 + MissingBlock(target) / 2,
            CompanionIntentType.Recovery => 12 + Math.Max(0, 60 - HpPercent(target)) + CompanionThreatService.ThreatPressurePercent(state) / 5,
            CompanionIntentType.Interference => 9 + EnemyPressure(executor) / 2,
            _ => 5
        };
    }

    private static int HighThreatLowHpPenalty(CompanionBattleState state)
    {
        var status = FightManager.Instance?.statuses?.TryGetValue(state.StatusId, out var value) == true ? value : null;
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

    private static bool TargetRequired(CompanionIntentDefinition intent)
    {
        var effect = (intent.Effect ?? "").Trim();
        return effect.Equals("Damage", StringComparison.OrdinalIgnoreCase)
            || effect.Equals("Block", StringComparison.OrdinalIgnoreCase);
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
