using System;
using System.Linq;
using System.Text;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class CompanionIntentExecutor
{
    internal const string PresentedPlanVar = "TerriasCompanionPresentedPlan";
    private const string PresentedFingerprintVar = "TerriasCompanionPresentedFingerprint";

    public static void InitAction(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        var state = CompanionBattleStateStore.Find(executor.Self?.InstanceId);
        var plan = state?.CurrentPlan;
        if (state == null || plan == null)
        {
            return;
        }

        DictionaryUtil.Set(executor.Vars, "CD", "0");
        DictionaryUtil.Set(executor.Vars, "priority", Math.Max(1, plan.Priority).ToString());
        if (!plan.IsWait)
        {
            var intent = CompanionIntentResolver.Find(state, plan.IntentId);
            var specs = intent == null
                ? Array.Empty<CompanionIntentEffectSpec>()
                : CompanionIntentEffects.Expand(intent);
            System.Collections.Generic.IReadOnlyList<CompanionResolvedEffect> effects = plan.ResolvedEffects;
            var fingerprint = CompanionIntentPresentationSnapshot.Fingerprint(effects, specs);
            var isCurrentSnapshot = string.Equals(
                    DictionaryUtil.Get(executor.Vars, PresentedPlanVar),
                    plan.PlanId,
                    StringComparison.Ordinal)
                && int.TryParse(DictionaryUtil.Get(executor.Vars, PresentedFingerprintVar), out var previousFingerprint)
                && previousFingerprint == fingerprint;
            if (isCurrentSnapshot)
            {
                return;
            }

            for (var index = 1; index <= CompanionIntentPresentationSnapshot.ClearedDescriptionSlots; index++)
            {
                DictionaryUtil.Set(executor.Vars, "DesVal" + index, "");
            }

            var diagnostic = new StringBuilder(192);
            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];
                var displayIndex = index < specs.Count
                    ? Math.Max(1, specs[index].DisplayIndex)
                    : index + 1;
                var snapshot = CompanionIntentPresentationSnapshot.Resolve(effect, displayIndex);
                DictionaryUtil.Set(executor.Vars, "DesVal" + snapshot.DisplayIndex, snapshot.DisplayText);
                if (diagnostic.Length > 0)
                {
                    diagnostic.Append(',');
                }

                diagnostic.Append("DesVal").Append(snapshot.DisplayIndex)
                    .Append('=').Append(snapshot.DisplayText)
                    .Append("/base=").Append(snapshot.AuthoritativeValue)
                    .Append("/handler=").Append(snapshot.HandlerId);
                if (snapshot.RepeatCount > 1)
                {
                    diagnostic.Append("/hits=").Append(snapshot.RepeatCount);
                }
            }

            DictionaryUtil.Set(executor.Vars, PresentedPlanVar, plan.PlanId);
            DictionaryUtil.Set(executor.Vars, PresentedFingerprintVar, fingerprint.ToString());
            TerriasLog.InfoAlways("[CompanionIntentPresentation] status=" + state.StatusId
                + ", plan=" + plan.PlanId
                + ", intent=" + plan.IntentId
                + ", fingerprint=" + fingerprint
                + ", values=" + (diagnostic.Length > 0 ? diagnostic.ToString() : "none"));

            return;
        }

        for (var index = 1; index <= CompanionIntentPresentationSnapshot.ClearedDescriptionSlots; index++)
        {
            DictionaryUtil.Set(executor.Vars, "DesVal" + index, "");
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
        var type = CompanionIntentResolver.IntentType(state, CompanionIntentResolver.Find(state, plan.IntentId));
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
                TerriasLog.Warn("[CompanionIntent] rejected unknown execution handler: " + effect.HandlerId);
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
