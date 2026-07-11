using System;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class CompanionIntentExecutor
{
    public static void InitAction(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        var state = CompanionBattleStateStore.Find(executor.Self?.InstanceId);
        var plan = state?.CurrentPlan;
        if (plan == null)
        {
            return;
        }

        DictionaryUtil.Set(executor.Vars, "CD", "0");
        DictionaryUtil.Set(executor.Vars, "priority", Math.Max(1, plan.Priority).ToString());
        if (!plan.IsWait)
        {
            foreach (var effect in plan.ResolvedEffects ?? new System.Collections.Generic.List<CompanionResolvedEffect>())
            {
                if (CompanionIntentHandlerRegistry.TryGet(effect.HandlerId, out var handler))
                {
                    handler.AddDescription(executor, effect);
                }
            }
        }
    }

    public static void Target(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        var state = CompanionBattleStateStore.Find(executor.Self?.InstanceId);
        var plan = state?.CurrentPlan;
        if (plan == null || plan.IsWait)
        {
            return;
        }

        var target = ResolveCommittedTarget(plan);
        var type = CompanionIntentRegistry.IntentType(CompanionIntentRegistry.Find(plan.IntentId));
        ExecutorApi.SetStatusForTarget(
            executor,
            target,
            type == CompanionIntentType.Attack || type == CompanionIntentType.Interference ? "Target" : "Self");
    }

    public static void UseAction(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        var state = CompanionBattleStateStore.Find(executor.Self?.InstanceId);
        var plan = state?.CurrentPlan;
        if (plan == null || plan.IsWait)
        {
            return;
        }

        foreach (var effect in plan.ResolvedEffects ?? new System.Collections.Generic.List<CompanionResolvedEffect>())
        {
            if (!CompanionIntentHandlerRegistry.TryGet(effect.HandlerId, out var handler))
            {
                SunExpLog.Warn("[CompanionIntent] rejected unknown execution handler: " + effect.HandlerId);
                continue;
            }

            handler.Execute(executor, effect);
        }
    }

    public static IStatusManager? ResolveCommittedTarget(CompanionIntentPlan? plan)
    {
        if (plan == null)
        {
            return null;
        }

        foreach (var effect in plan.ResolvedEffects ?? new System.Collections.Generic.List<CompanionResolvedEffect>())
        {
            var target = CompanionTargetPolicyRegistry.FirstAlive(effect.TargetIds);
            if (target != null)
            {
                return target;
            }
        }

        return CompanionTargetPolicyRegistry.FirstAlive(plan.OrderedTargetIds);
    }

    public static bool CanExecute(CompanionIntentPlan? plan)
    {
        if (plan == null || plan.IsWait || plan.ResolvedEffects == null || plan.ResolvedEffects.Count == 0)
        {
            return false;
        }

        return plan.ResolvedEffects.All(effect =>
            CompanionIntentHandlerRegistry.TryGet(effect.HandlerId, out _)
            && CompanionTargetPolicyRegistry.FirstAlive(effect.TargetIds) != null);
    }

    public static IStatusManager? SelectTarget(ScriptExecutor self, CompanionBattleState? state, CompanionIntentDefinition intent)
    {
        return state == null || intent == null
            ? null
            : CompanionTargetPolicyRegistry.Resolve(self, state, intent).FirstOrDefault();
    }

    public static int ResolveValue(CompanionBattleState state, CompanionIntentDefinition intent)
    {
        if (state == null || intent == null)
        {
            return 1;
        }

        var stats = state.Stats;
        var value = intent.FlatValue
            + stats.Attack * intent.AttackScale
            + stats.Armor * intent.ArmorScale
            + stats.MaxMagic * intent.MagicScale;
        return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

}
