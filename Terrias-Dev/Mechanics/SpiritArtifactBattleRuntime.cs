using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public static class SpiritArtifactBattleRuntime
{
    public static void BeforePlan(OtherObj actor, CompanionBattleState state)
    {
        if (!IsSpirit(state)) return;
        SpiritArtifactEffectHandlerRegistry.BeforePlan(actor, state);
    }

    public static void ApplyPlanModifiers(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IList<CompanionResolvedEffect> effects,
        ref int effectiveCost,
        ICollection<string> modifierKeys)
    {
        if (!IsSpirit(state) || intent == null || effects == null || effects.Count == 0) return;
        var context = SpiritArtifactEffectHandlerRegistry.ApplyPlan(state, intent, effects.ToArray(), effectiveCost);
        effectiveCost = Math.Max(0, context.EffectiveCost);
        foreach (var key in context.ModifierKeys)
            if (!modifierKeys.Contains(key)) modifierKeys.Add(key);

        var damageBasisPoints = Math.Max(0, Math.Min(
            SpiritArtifactRegistry.MaximumArtifactSetDamageBonusPercent * 100,
            context.DamageBonusBasisPoints));
        var firstBuffApplied = false;
        foreach (var effect in effects)
        {
            if (effect == null) continue;
            var handlerId = effect.HandlerId ?? "";
            if (handlerId.StartsWith("damage.", StringComparison.Ordinal))
            {
                effect.PreArtifactValue = Math.Max(0, effect.Value);
                effect.ArtifactDamageBonusBasisPoints = damageBasisPoints;
                effect.Value = SpiritArtifactMath.ApplyDamageMultiplier(effect.PreArtifactValue, damageBasisPoints);
                continue;
            }
            if (handlerId.StartsWith("heal.", StringComparison.Ordinal))
            {
                effect.Value = ApplyPercent(effect.Value, context.HealBonusPercent);
                continue;
            }
            if (handlerId.StartsWith("block.", StringComparison.Ordinal))
            {
                effect.Value = ApplyPercent(effect.Value, context.BlockBonusPercent);
                continue;
            }
            if (handlerId == CompanionIntentHandlerRegistry.ApplyBuff)
            {
                var bonus = BuffApi.IsNegativeBuffId(effect.BuffId) ? context.NegativeBuffStackBonus : 0;
                if (!firstBuffApplied && context.FirstBuffStackBonus > 0)
                {
                    bonus += context.FirstBuffStackBonus;
                    firstBuffApplied = true;
                }
                effect.BuffStacks = Math.Max(0, effect.BuffStacks + bonus);
                effect.Value = effect.BuffStacks;
            }
        }
    }

    public static void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent, CompanionIntentPlan plan)
    {
        if (IsSpirit(state)) SpiritArtifactEffectHandlerRegistry.OnIntentExecuted(state, intent, plan);
    }

    public static int WaitRecoveryBonus(CompanionBattleState state)
        => IsSpirit(state) ? SpiritArtifactEffectHandlerRegistry.OnWait(state) : 0;

    public static void OnStatusHit(IStatusManager target)
    {
        if (!CompanionAuthorityService.IsAuthoritative() || target == null) return;
        foreach (var state in CompanionBattleStateStore.Snapshot().Where(IsSpirit))
        {
            if (!SpiritArtifactEffectHandlerRegistry.OnStatusHit(target, state)) continue;
            var spirit = SpiritStateStore.Find(state.StatusId)?.Spirit;
            if (spirit != null) SpiritSummonService.BroadcastRuntimeState(spirit, "Artifact.OnStatusHit");
        }
    }

    public static IReadOnlyList<SpiritVisibleStatusSnapshot> VisibleStatuses(CompanionBattleState state)
        => IsSpirit(state) ? SpiritArtifactEffectHandlerRegistry.VisibleStatuses(state) : Array.Empty<SpiritVisibleStatusSnapshot>();

    private static int ApplyPercent(int value, int percent)
    {
        var normalized = Math.Max(0, value);
        return Math.Max(normalized > 0 ? 1 : 0, (int)Math.Round(
            normalized * (100 + Math.Max(0, percent)) / 100d,
            MidpointRounding.AwayFromZero));
    }

    private static bool IsSpirit(CompanionBattleState? state)
        => state != null && string.Equals(state.EntityKind, "SpiritAttachment", StringComparison.Ordinal);
}
