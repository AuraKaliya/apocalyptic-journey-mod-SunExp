using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public static class SpiritTrainingBattleRuntime
{
    private const string PendingPercent = "numeric.pending.percent";
    private const string PendingCharges = "numeric.pending.charges";
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, List<DelayedHeal>> DelayedHeals = new(StringComparer.Ordinal);

    public static bool IsEligible(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IReadOnlyList<IStatusManager> targets)
    {
        switch ((intent.EligibilityPolicy ?? "").Trim())
        {
            case "missing-magic-at-least-2":
                return state.Stats.MaxMagic - state.Stats.CurrentMagic >= 2;
            case "no-pending-numeric-bonus":
                return state.PassiveValue(PendingCharges) <= 0;
            case "full-magic-and-no-overflow":
                return state.Stats.CurrentMagic >= state.Stats.MaxMagic && state.PassiveValue(PendingCharges) <= 0;
            case "life-echo-needed":
                var wounded = targets.Count(target => target.CurHp < target.MaxHp);
                return wounded >= 2 || targets.Any(target => target.MaxHp > 0 && target.CurHp * 100 / target.MaxHp <= 25);
            default:
                return true;
        }
    }

    public static void BeforePlan(OtherObj actor, CompanionBattleState state)
    {
        if (!IsSpirit(state)) return;
        var passive = SpiritTrainingRegistry.FindPassive(state.EquippedPassiveId);
        if (passive == null) return;
        if (SpiritPassiveMechanicRegistry.IsSpeciesMechanic(passive))
        {
            SpiritPassiveMechanicRegistry.BeforePlan(actor, state, passive);
            return;
        }
        var executor = actor?.dataConfig?.scriptExecutor as ScriptExecutor;
        var self = Status(state.StatusId);
        switch (passive.EffectKind)
        {
            case "mana-tide" when state.Stats.CurrentMagic == 0:
                state.Stats.RecoverMagic(1);
                break;
            case "stable-structure" when self != null && self.Defend <= 0
                                                && state.TurnIndex >= state.PassiveValue("stable.ready-turn"):
                if (executor != null)
                {
                    CompanionEffectCommitService.Block(executor, self, Math.Max(1,
                        (int)Math.Round(2 + state.Stats.Armor * 0.4, MidpointRounding.AwayFromZero)));
                    state.SetPassiveValue("stable.ready-turn", state.TurnIndex + 3);
                }
                break;
        }
    }

    public static int SpeedContribution(CompanionBattleState state, CompanionIntentDefinition intent)
    {
        var value = state.Stats.Speed * intent.SpeedScale;
        var passive = SpiritTrainingRegistry.FindPassive(state.EquippedPassiveId);
        if (passive?.EffectKind == "swift-calculation" && intent.SpeedScale > 0f) value *= 1.5f;
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    public static void ApplyPlanModifiers(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IList<CompanionResolvedEffect> effects,
        out int bonusPercent,
        out List<string> modifierKeys,
        out int effectiveCost)
    {
        bonusPercent = 0;
        modifierKeys = new List<string>();
        effectiveCost = Math.Max(0, intent.Cost);
        if (!IsSpirit(state)) return;

        var passive = SpiritTrainingRegistry.FindPassive(state.EquippedPassiveId);
        if (passive?.EffectKind == "efficient-casting" && intent.Cost >= 2
            && state.PassiveValue("efficient.used") == 0)
        {
            effectiveCost = Math.Max(0, intent.Cost - 1);
            modifierKeys.Add("efficient-casting");
        }
        if (!effects.Any(IsDirectNumeric)) return;

        if (state.PassiveValue(PendingCharges) > 0)
        {
            AddModifier("pending-numeric", state.PassiveValue(PendingPercent), ref bonusPercent, modifierKeys);
        }

        if (passive != null)
        {
            if (SpiritPassiveMechanicRegistry.IsSpeciesMechanic(passive))
            {
                SpiritPassiveMechanicRegistry.ApplyPlanModifiers(
                    state,
                    intent,
                    effects.ToArray(),
                    passive,
                    ref bonusPercent,
                    modifierKeys);
            }
            else switch (passive.EffectKind)
            {
                case "opening-calibration" when state.PassiveValue("opening.used") == 0:
                    AddModifier("opening-calibration", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
                    break;
                case "alternating-tactics":
                    var currentType = TypeCode(intent.Type);
                    var previousType = state.PassiveValue("last.intent.type");
                    if (previousType > 0 && previousType != currentType)
                        AddModifier("alternating-tactics", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
                    break;
                case "guardian-contract" when state.PassiveValue("guardian.armed") > 0
                                                   && (intent.Type == "Defense" || intent.Type == "Recovery"):
                    AddModifier("guardian-contract", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
                    break;
                case "mana-tide" when state.Stats.CurrentMagic >= state.Stats.MaxMagic:
                    AddModifier("mana-tide-full", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
                    break;
                case "desperate-echo":
                    var self = Status(state.StatusId);
                    if (self != null && self.MaxHp > 0 && self.CurHp * 100 / self.MaxHp <= 30)
                        AddModifier("desperate-echo", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
                    break;
                case "combo-resonance" when state.PassiveValue("combo.armed") > 0
                                               && (intent.Type == "Attack" || intent.Type == "Defense"):
                    AddModifier("combo-resonance", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
                    break;
                case "exploit-opening" when intent.Type == "Attack" && HasDebuffedTarget(effects):
                    AddModifier("exploit-opening", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
                    break;
            }
        }

        bonusPercent = Math.Min(75, bonusPercent);
        if (bonusPercent <= 0) return;
        foreach (var effect in effects.Where(IsDirectNumeric))
        {
            effect.Value = Math.Max(1, (int)Math.Round(
                effect.Value * (100 + bonusPercent) / 100d,
                MidpointRounding.AwayFromZero));
        }
    }

    public static int PreviewCost(CompanionBattleState state, CompanionIntentDefinition intent)
    {
        var passive = SpiritTrainingRegistry.FindPassive(state.EquippedPassiveId);
        return passive?.EffectKind == "efficient-casting" && intent.Cost >= 2
               && state.PassiveValue("efficient.used") == 0
            ? intent.Cost - 1
            : intent.Cost;
    }

    public static void OnIntentExecuted(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        CompanionIntentPlan plan)
    {
        foreach (var key in plan.AppliedModifierKeys ?? new List<string>())
        {
            switch (key)
            {
                case "pending-numeric":
                    var charges = Math.Max(0, state.PassiveValue(PendingCharges) - 1);
                    state.SetPassiveValue(PendingCharges, charges);
                    if (charges == 0) state.SetPassiveValue(PendingPercent, 0);
                    break;
                case "opening-calibration": state.SetPassiveValue("opening.used", 1); break;
                case "efficient-casting": state.SetPassiveValue("efficient.used", 1); break;
                case "guardian-contract": state.SetPassiveValue("guardian.armed", 0); break;
                case "combo-resonance": state.SetPassiveValue("combo.armed", 0); break;
            }
        }
        state.SetPassiveValue("last.intent.type", TypeCode(intent.Type));
        var passive = SpiritTrainingRegistry.FindPassive(state.EquippedPassiveId);
        if (passive != null && SpiritPassiveMechanicRegistry.IsSpeciesMechanic(passive))
        {
            SpiritPassiveMechanicRegistry.OnIntentExecuted(state, intent, plan, passive);
            return;
        }
        if (passive?.EffectKind == "combo-resonance" && (intent.Type == "Support" || intent.Type == "Recovery"))
        {
            state.SetPassiveValue("combo.armed", 1);
        }
    }

    public static int WaitRecoveryBonus(CompanionBattleState state)
    {
        var passive = SpiritTrainingRegistry.FindPassive(state.EquippedPassiveId);
        if (passive != null && SpiritPassiveMechanicRegistry.IsSpeciesMechanic(passive))
            return SpiritPassiveMechanicRegistry.OnWait(state, passive);
        return passive?.EffectKind == "recovery-loop" ? 1 : 0;
    }

    public static void PrepareNumeric(CompanionBattleState state, int percent, int charges)
    {
        if (state == null) return;
        state.SetPassiveValue(PendingPercent, Math.Max(state.PassiveValue(PendingPercent), Math.Max(0, Math.Min(75, percent))));
        state.SetPassiveValue(PendingCharges, Math.Max(state.PassiveValue(PendingCharges), Math.Max(1, charges)));
    }

    public static void ScheduleDelayedHeal(string sourceStatusId, string targetStatusId, int value)
    {
        if (string.IsNullOrWhiteSpace(sourceStatusId) || string.IsNullOrWhiteSpace(targetStatusId) || value <= 0) return;
        lock (SyncRoot)
        {
            if (!DelayedHeals.TryGetValue(targetStatusId, out var pending))
            {
                pending = new List<DelayedHeal>();
                DelayedHeals[targetStatusId] = pending;
            }
            pending.Add(new DelayedHeal(sourceStatusId, targetStatusId, value));
        }
    }

    public static void OnActorTurnCompleted(FightObject? actor)
    {
        if (!CompanionAuthorityService.IsAuthoritative() || actor?.Status == null) return;
        List<DelayedHeal>? pending;
        lock (SyncRoot)
        {
            if (!DelayedHeals.TryGetValue(actor.Status.InstanceId, out pending)) return;
            DelayedHeals.Remove(actor.Status.InstanceId);
        }
        foreach (var heal in pending)
        {
            var source = SpiritStateStore.Find(heal.SourceStatusId)?.Spirit;
            var executor = source?.dataConfig?.scriptExecutor as ScriptExecutor;
            var target = Status(heal.TargetStatusId);
            if (executor != null && target != null && CompanionTargetPolicyRegistry.IsAlive(target))
            {
                CompanionEffectCommitService.Heal(executor, target, heal.Value);
            }
        }
    }

    public static void OnStatusHit(IStatusManager? target)
    {
        if (!CompanionAuthorityService.IsAuthoritative() || target == null) return;
        foreach (var state in CompanionBattleStateStore.Snapshot().Where(IsSpirit))
        {
            var passive = SpiritTrainingRegistry.FindPassive(state.EquippedPassiveId);
            if (passive != null && SpiritPassiveMechanicRegistry.IsSpeciesMechanic(passive))
            {
                SpiritPassiveMechanicRegistry.OnStatusHit(target, state, passive);
                var speciesSpirit = SpiritStateStore.Find(state.StatusId)?.Spirit;
                if (speciesSpirit != null)
                    SpiritSummonService.BroadcastRuntimeState(speciesSpirit, "SpeciesPassive.StatusHit");
                continue;
            }
            if (passive?.EffectKind == "guardian-contract"
                && string.Equals(state.OwnerStatusId, target.InstanceId, StringComparison.Ordinal))
            {
                state.SetPassiveValue("guardian.armed", 1);
                var guardianSpirit = SpiritStateStore.Find(state.StatusId)?.Spirit;
                if (guardianSpirit != null)
                    SpiritSummonService.BroadcastRuntimeState(guardianSpirit, "CommonPassive.GuardianArmed");
            }
            if (passive?.EffectKind == "emergency-barrier"
                && string.Equals(state.StatusId, target.InstanceId, StringComparison.Ordinal)
                && state.PassiveValue("emergency.used") == 0
                && target.MaxHp > 0 && target.CurHp * 100 / target.MaxHp <= 40)
            {
                var source = SpiritStateStore.Find(state.StatusId)?.Spirit;
                var executor = source?.dataConfig?.scriptExecutor as ScriptExecutor;
                if (executor != null)
                {
                    CompanionEffectCommitService.Block(executor, target, Math.Max(1,
                        (int)Math.Round(3 + state.Stats.Armor * 0.8, MidpointRounding.AwayFromZero)));
                    state.SetPassiveValue("emergency.used", 1);
                }
            }
        }
    }

    public static void Clear()
    {
        lock (SyncRoot) DelayedHeals.Clear();
    }

    private static bool IsDirectNumeric(CompanionResolvedEffect effect)
    {
        var handler = effect?.HandlerId ?? "";
        return handler.StartsWith("damage.", StringComparison.Ordinal)
               || handler.StartsWith("block.", StringComparison.Ordinal)
               || (handler.StartsWith("heal.", StringComparison.Ordinal) && handler != "heal.delayed")
               || handler == "magic.recover";
    }

    private static bool HasDebuffedTarget(IEnumerable<CompanionResolvedEffect> effects)
    {
        return effects.SelectMany(effect => effect.TargetIds ?? new List<string>()).Distinct(StringComparer.Ordinal)
            .Select(Status).Where(value => value != null)
            .Any(value => ExecutorApi.StatusBuffLevel(value, "buff_weak") > 0
                          || ExecutorApi.StatusBuffLevel(value, "buff_vulnerability") > 0
                          || ExecutorApi.StatusBuffLevel(value, "buff_toxin") > 0
                          || ExecutorApi.StatusBuffLevel(value, "buff_burn") > 0);
    }

    private static void AddModifier(string key, int value, ref int total, ICollection<string> keys)
    {
        if (value <= 0) return;
        total += value;
        keys.Add(key);
    }

    private static int TypeCode(string type)
    {
        return Enum.TryParse(type, true, out CompanionIntentType parsed) ? (int)parsed + 1 : 0;
    }

    private static bool IsSpirit(CompanionBattleState? state)
    {
        return string.Equals(state?.EntityKind, "SpiritAttachment", StringComparison.Ordinal);
    }

    private static IStatusManager? Status(string? id)
    {
        return !string.IsNullOrWhiteSpace(id) && FightManager.Instance?.statuses?.TryGetValue(id, out var value) == true
            ? value
            : null;
    }

    private sealed class DelayedHeal
    {
        public DelayedHeal(string sourceStatusId, string targetStatusId, int value)
        {
            SourceStatusId = sourceStatusId;
            TargetStatusId = targetStatusId;
            Value = value;
        }
        public string SourceStatusId { get; }
        public string TargetStatusId { get; }
        public int Value { get; }
    }
}
