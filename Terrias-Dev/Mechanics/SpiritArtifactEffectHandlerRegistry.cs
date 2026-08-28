using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritArtifactBonusBuilder
{
    public int OriginMagic { get; set; }
    public int OriginSpirit { get; set; }
    public int OriginLuck { get; set; }
    public int OriginPerception { get; set; }
    public int FlatLife { get; set; }
    public int FlatArmor { get; set; }
    public int MaxMagic { get; set; }
    public int Speed { get; set; }
    public int StartExtraordinary { get; set; }
}

public sealed class SpiritArtifactPlanModifierContext
{
    private readonly List<string> modifierKeys = new();

    public SpiritArtifactPlanModifierContext(int effectiveCost)
    {
        EffectiveCost = Math.Max(0, effectiveCost);
    }

    public int EffectiveCost { get; set; }
    public int DamageBonusBasisPoints { get; private set; }
    public int HealBonusPercent { get; private set; }
    public int BlockBonusPercent { get; private set; }
    public int NegativeBuffStackBonus { get; private set; }
    public int FirstBuffStackBonus { get; private set; }
    public IReadOnlyList<string> ModifierKeys => modifierKeys;

    public void AddDamage(string key, int percent)
    {
        if (percent <= 0) return;
        DamageBonusBasisPoints += percent * 100;
        AddKey(key);
    }

    public void AddHeal(string key, int percent)
    {
        if (percent <= 0) return;
        HealBonusPercent += percent;
        AddKey(key);
    }

    public void AddBlock(string key, int percent)
    {
        if (percent <= 0) return;
        BlockBonusPercent += percent;
        AddKey(key);
    }

    public void AddNegativeBuffStacks(string key, int value)
    {
        if (value <= 0) return;
        NegativeBuffStackBonus += value;
        AddKey(key);
    }

    public void AddFirstBuffStacks(string key, int value)
    {
        if (value <= 0) return;
        FirstBuffStackBonus += value;
        AddKey(key);
    }

    public void AddKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key) && !modifierKeys.Contains(key, StringComparer.Ordinal))
            modifierKeys.Add(key);
    }
}

internal abstract class SpiritArtifactEffectHandler
{
    protected SpiritArtifactEffectHandler(string handlerId)
    {
        HandlerId = handlerId;
    }

    public string HandlerId { get; }

    public virtual void ApplyStatic(SpiritArtifactActiveEffectSnapshot effect, SpiritArtifactBonusBuilder bonuses) { }

    public virtual void BeforePlan(OtherObj actor, CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect) { }

    public virtual void ContributePlan(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IReadOnlyList<CompanionResolvedEffect> effects,
        SpiritArtifactActiveEffectSnapshot effect,
        SpiritArtifactPlanModifierContext context) { }

    public virtual void OnIntentExecuted(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        CompanionIntentPlan plan,
        SpiritArtifactActiveEffectSnapshot effect) { }

    public virtual int OnWait(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect) => 0;

    public virtual bool OnStatusHit(IStatusManager target, CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect) => false;

    public virtual SpiritVisibleStatusSnapshot? VisibleStatus(
        CompanionBattleState state,
        SpiritArtifactActiveEffectSnapshot effect) => null;

    protected static string Key(SpiritArtifactActiveEffectSnapshot effect, string suffix)
        => "artifact." + (effect?.EffectId ?? "unknown") + "." + suffix;

    protected static bool IsType(CompanionIntentDefinition intent, params string[] types)
        => types.Contains(intent?.Type ?? "", StringComparer.Ordinal);

    internal static bool HasDamage(IEnumerable<CompanionResolvedEffect> effects)
        => effects.Any(value => (value?.HandlerId ?? "").StartsWith("damage.", StringComparison.Ordinal));

    internal static bool HasUtility(IEnumerable<CompanionResolvedEffect> effects)
        => effects.Any(value => IsHeal(value) || IsBlock(value));

    internal static bool IsHeal(CompanionResolvedEffect? effect)
        => (effect?.HandlerId ?? "").StartsWith("heal.", StringComparison.Ordinal);

    internal static bool IsBlock(CompanionResolvedEffect? effect)
        => (effect?.HandlerId ?? "").StartsWith("block.", StringComparison.Ordinal);

    protected static SpiritVisibleStatusSnapshot Status(SpiritArtifactActiveEffectSnapshot effect, int value, int maximum)
    {
        return new SpiritVisibleStatusSnapshot
        {
            Kind = "ArtifactSet",
            Id = effect?.EffectId ?? "",
            Value = Math.Max(0, value),
            Maximum = Math.Max(0, maximum)
        };
    }
}

public static class SpiritArtifactEffectHandlerRegistry
{
    private static readonly IReadOnlyDictionary<string, SpiritArtifactEffectHandler> Handlers = Build();

    public static bool Supports(string? handlerId) => Handlers.ContainsKey((handlerId ?? "").Trim());

    public static void ApplyStatic(SpiritArtifactActiveEffectSnapshot effect, SpiritArtifactBonusBuilder bonuses)
    {
        if (effect != null && bonuses != null && Handlers.TryGetValue(effect.HandlerId ?? "", out var handler))
            handler.ApplyStatic(effect, bonuses);
    }

    public static void BeforePlan(OtherObj actor, CompanionBattleState state)
    {
        foreach (var effect in Effects(state))
            if (Handlers.TryGetValue(effect.HandlerId, out var handler)) handler.BeforePlan(actor, state, effect);
    }

    public static SpiritArtifactPlanModifierContext ApplyPlan(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IReadOnlyList<CompanionResolvedEffect> effects,
        int effectiveCost)
    {
        var context = new SpiritArtifactPlanModifierContext(effectiveCost);
        foreach (var effect in Effects(state))
            if (Handlers.TryGetValue(effect.HandlerId, out var handler))
                handler.ContributePlan(state, intent, effects, effect, context);
        return context;
    }

    public static void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent, CompanionIntentPlan plan)
    {
        foreach (var effect in Effects(state))
            if (Handlers.TryGetValue(effect.HandlerId, out var handler))
                handler.OnIntentExecuted(state, intent, plan, effect);
    }

    public static int OnWait(CompanionBattleState state)
    {
        var value = 0;
        foreach (var effect in Effects(state))
            if (Handlers.TryGetValue(effect.HandlerId, out var handler)) value += Math.Max(0, handler.OnWait(state, effect));
        return value;
    }

    public static bool OnStatusHit(IStatusManager target, CompanionBattleState state)
    {
        var changed = false;
        foreach (var effect in Effects(state))
            if (Handlers.TryGetValue(effect.HandlerId, out var handler)) changed |= handler.OnStatusHit(target, state, effect);
        return changed;
    }

    public static IReadOnlyList<SpiritVisibleStatusSnapshot> VisibleStatuses(CompanionBattleState state)
    {
        var result = new List<SpiritVisibleStatusSnapshot>();
        foreach (var effect in Effects(state))
        {
            if (!Handlers.TryGetValue(effect.HandlerId, out var handler)) continue;
            var status = handler.VisibleStatus(state, effect);
            if (status != null && (status.Value > 0 || status.Maximum > 0)) result.Add(status);
        }
        return result;
    }

    private static IReadOnlyList<SpiritArtifactActiveEffectSnapshot> Effects(CompanionBattleState? state)
        => state?.ArtifactBattle?.ActiveEffects is { } values
            ? values
            : Array.Empty<SpiritArtifactActiveEffectSnapshot>();

    private static IReadOnlyDictionary<string, SpiritArtifactEffectHandler> Build()
    {
        var result = new Dictionary<string, SpiritArtifactEffectHandler>(StringComparer.Ordinal);
        Add(result, new StaticStatHandler("stat.origin.magic", (value, b) => b.OriginMagic += value));
        Add(result, new StaticStatHandler("stat.max-magic", (value, b) => b.MaxMagic += value));
        Add(result, new StaticStatHandler("stat.origins.all", (value, b) =>
        {
            b.OriginMagic += value; b.OriginSpirit += value; b.OriginLuck += value; b.OriginPerception += value;
        }));
        Add(result, new StaticStatHandler("stat.max-life", (value, b) => b.FlatLife += value));
        Add(result, new StaticStatHandler("stat.armor", (value, b) => b.FlatArmor += value));
        Add(result, new StaticStatHandler("stat.speed", (value, b) => b.Speed += value));
        Add(result, new AttackDamageHandler());
        Add(result, new TriumphHandler());
        Add(result, new ExtraCostAttackHandler());
        Add(result, new CooldownNumericHandler());
        Add(result, new VariationHandler());
        Add(result, new LowHealthDamageHandler());
        Add(result, new HuntHandler());
        Add(result, new CostNumericHandler());
        Add(result, new DiversityNumericHandler());
        Add(result, new RecoveryHealHandler());
        Add(result, new FoamHandler());
        Add(result, new WaitMagicHandler());
        Add(result, new OathHandler());
        Add(result, new OwnerHitGuardHandler());
        Add(result, new ShieldCycleHandler());
        Add(result, new InterferenceDebuffHandler());
        Add(result, new DebuffedTargetDamageHandler());
        Add(result, new GaleAlternationHandler());
        return result;
    }

    private static void Add(IDictionary<string, SpiritArtifactEffectHandler> target, SpiritArtifactEffectHandler handler)
        => target.Add(handler.HandlerId, handler);

    private sealed class StaticStatHandler : SpiritArtifactEffectHandler
    {
        private readonly Action<int, SpiritArtifactBonusBuilder> apply;
        public StaticStatHandler(string id, Action<int, SpiritArtifactBonusBuilder> action) : base(id) => apply = action;
        public override void ApplyStatic(SpiritArtifactActiveEffectSnapshot effect, SpiritArtifactBonusBuilder bonuses)
            => apply(Math.Max(0, effect.Amount), bonuses);
    }

    private sealed class AttackDamageHandler : SpiritArtifactEffectHandler
    {
        public AttackDamageHandler() : base("intent.attack.damage-percent") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (IsType(intent, "Attack") && HasDamage(effects)) context.AddDamage(effect.EffectId, effect.Amount);
        }
    }

    private sealed class TriumphHandler : SpiritArtifactEffectHandler
    {
        public TriumphHandler() : base("intent.attack.triumph") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (IsType(intent, "Attack") && HasDamage(effects))
                context.AddDamage(effect.EffectId, state.PassiveValue(Key(effect, "stacks")) * effect.Amount);
        }
        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritArtifactActiveEffectSnapshot effect)
        {
            var key = Key(effect, "stacks");
            state.SetPassiveValue(key, IsType(intent, "Attack")
                ? Math.Min(Math.Max(1, effect.Maximum), state.PassiveValue(key) + 1)
                : 0);
        }
        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
            => Status(effect, state.PassiveValue(Key(effect, "stacks")), Math.Max(1, effect.Maximum));
    }

    private sealed class ExtraCostAttackHandler : SpiritArtifactEffectHandler
    {
        public ExtraCostAttackHandler() : base("intent.attack.extra-cost") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            var extra = Math.Max(1, effect.SecondaryAmount);
            if (!IsType(intent, "Attack") || !HasDamage(effects) || state.Stats.CurrentMagic < context.EffectiveCost + extra) return;
            context.EffectiveCost += extra;
            context.AddDamage(effect.EffectId, effect.Amount);
        }
    }

    private sealed class CooldownNumericHandler : SpiritArtifactEffectHandler
    {
        public CooldownNumericHandler() : base("intent.cooldown.numeric-percent") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (intent.Cooldown < Math.Max(1, effect.SecondaryAmount)) return;
            AddNumeric(effect, effects, context, effect.Amount);
        }
    }

    private sealed class VariationHandler : SpiritArtifactEffectHandler
    {
        public VariationHandler() : base("intent.alternating.variation") { }
        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritArtifactActiveEffectSnapshot effect)
        {
            var type = TypeCode(intent.Type);
            var previousKey = Key(effect, "previous-type");
            var progressKey = Key(effect, "progress");
            var previous = state.PassiveValue(previousKey);
            state.SetPassiveValue(previousKey, type);
            if (previous <= 0 || previous == type) return;
            var next = state.PassiveValue(progressKey) + 1;
            if (next >= Math.Max(2, effect.Maximum))
            {
                state.SetPassiveValue(progressKey, 0);
                state.Stats.RecoverMagic(Math.Max(1, effect.Amount));
            }
            else state.SetPassiveValue(progressKey, next);
        }
        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
            => Status(effect, state.PassiveValue(Key(effect, "progress")), Math.Max(2, effect.Maximum));
    }

    private sealed class LowHealthDamageHandler : SpiritArtifactEffectHandler
    {
        public LowHealthDamageHandler() : base("intent.low-health.damage-percent") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            var self = StatusById(state.StatusId);
            if (self != null && self.MaxHp > 0 && self.CurHp * 100 / self.MaxHp <= Math.Max(1, effect.SecondaryAmount)
                && IsType(intent, "Attack", "Interference") && HasDamage(effects))
                context.AddDamage(effect.EffectId, effect.Amount);
        }
    }

    private sealed class HuntHandler : SpiritArtifactEffectHandler
    {
        public HuntHandler() : base("status.hit.hunt") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            var stacks = state.PassiveValue(Key(effect, "stacks"));
            if (stacks > 0 && IsType(intent, "Attack", "Interference") && HasDamage(effects))
            {
                context.AddDamage(effect.EffectId, stacks * effect.Amount);
                context.AddKey(Key(effect, "consume"));
            }
        }
        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritArtifactActiveEffectSnapshot effect)
        {
            if (plan.AppliedModifierKeys.Contains(Key(effect, "consume"), StringComparer.Ordinal))
                state.SetPassiveValue(Key(effect, "stacks"), 0);
        }
        public override bool OnStatusHit(IStatusManager target, CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
        {
            if (!string.Equals(target?.InstanceId, state.StatusId, StringComparison.Ordinal)) return false;
            var key = Key(effect, "stacks");
            var before = state.PassiveValue(key);
            var next = Math.Min(Math.Max(1, effect.Maximum), before + 1);
            state.SetPassiveValue(key, next);
            return next != before;
        }
        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
            => Status(effect, state.PassiveValue(Key(effect, "stacks")), Math.Max(1, effect.Maximum));
    }

    private sealed class CostNumericHandler : SpiritArtifactEffectHandler
    {
        public CostNumericHandler() : base("intent.cost.numeric-percent") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
            => AddNumeric(effect, effects, context, Math.Min(Math.Max(0, effect.Maximum), context.EffectiveCost * effect.Amount));
    }

    private sealed class DiversityNumericHandler : SpiritArtifactEffectHandler
    {
        public DiversityNumericHandler() : base("loadout.intent-diversity.numeric-percent") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            var count = state.EquippedIntentIds.Select(id => CompanionIntentResolver.Find(state, id)?.Type ?? "")
                .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).Count();
            AddNumeric(effect, effects, context, Math.Min(Math.Max(0, effect.Maximum), count * effect.Amount));
        }
    }

    private sealed class RecoveryHealHandler : SpiritArtifactEffectHandler
    {
        public RecoveryHealHandler() : base("intent.recovery.heal-percent") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (IsType(intent, "Recovery") && effects.Any(IsHeal)) context.AddHeal(effect.EffectId, effect.Amount);
        }
    }

    private sealed class FoamHandler : SpiritArtifactEffectHandler
    {
        public FoamHandler() : base("intent.recovery.foam") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (state.PassiveValue(Key(effect, "armed")) > 0 && IsType(intent, "Attack", "Interference") && HasDamage(effects))
            {
                context.AddDamage(effect.EffectId, effect.Amount);
                context.AddKey(Key(effect, "consume"));
            }
        }
        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritArtifactActiveEffectSnapshot effect)
        {
            if (plan.AppliedModifierKeys.Contains(Key(effect, "consume"), StringComparer.Ordinal))
                state.SetPassiveValue(Key(effect, "armed"), 0);
            if (IsType(intent, "Recovery")) state.SetPassiveValue(Key(effect, "armed"), 1);
        }
        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
            => Status(effect, state.PassiveValue(Key(effect, "armed")), 1);
    }

    private sealed class WaitMagicHandler : SpiritArtifactEffectHandler
    {
        public WaitMagicHandler() : base("wait.magic-recovery") { }
        public override int OnWait(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect) => effect.Amount;
    }

    private sealed class OathHandler : SpiritArtifactEffectHandler
    {
        public OathHandler() : base("wait.oath") { }
        public override int OnWait(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
        {
            state.SetPassiveValue(Key(effect, "armed"), 1);
            return 0;
        }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (state.PassiveValue(Key(effect, "armed")) <= 0 || (!HasDamage(effects) && !HasUtility(effects))) return;
            AddNumeric(effect, effects, context, effect.Amount);
            context.AddKey(Key(effect, "consume"));
        }
        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritArtifactActiveEffectSnapshot effect)
        {
            if (plan.AppliedModifierKeys.Contains(Key(effect, "consume"), StringComparer.Ordinal))
                state.SetPassiveValue(Key(effect, "armed"), 0);
        }
        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
            => Status(effect, state.PassiveValue(Key(effect, "armed")), 1);
    }

    private sealed class OwnerHitGuardHandler : SpiritArtifactEffectHandler
    {
        public OwnerHitGuardHandler() : base("status.owner-hit.guard") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            var stacks = state.PassiveValue(Key(effect, "stacks"));
            if (stacks <= 0 || !IsType(intent, "Defense", "Recovery")) return;
            if (effects.Any(IsBlock)) context.AddBlock(effect.EffectId, stacks * effect.Amount);
            if (effects.Any(IsHeal)) context.AddHeal(effect.EffectId, stacks * effect.Amount);
            context.AddKey(Key(effect, "consume"));
        }
        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritArtifactActiveEffectSnapshot effect)
        {
            if (plan.AppliedModifierKeys.Contains(Key(effect, "consume"), StringComparer.Ordinal))
                state.SetPassiveValue(Key(effect, "stacks"), 0);
        }
        public override bool OnStatusHit(IStatusManager target, CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
        {
            if (!string.Equals(target?.InstanceId, state.StatusId, StringComparison.Ordinal)
                && !string.Equals(target?.InstanceId, state.OwnerStatusId, StringComparison.Ordinal)) return false;
            var key = Key(effect, "stacks");
            var before = state.PassiveValue(key);
            var next = Math.Min(Math.Max(1, effect.Maximum), before + 1);
            state.SetPassiveValue(key, next);
            return next != before;
        }
        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
            => Status(effect, state.PassiveValue(Key(effect, "stacks")), Math.Max(1, effect.Maximum));
    }

    private sealed class ShieldCycleHandler : SpiritArtifactEffectHandler
    {
        public ShieldCycleHandler() : base("turn.shield-cycle") { }
        public override void BeforePlan(OtherObj actor, CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
        {
            var self = StatusById(state.StatusId);
            var executor = actor?.dataConfig?.scriptExecutor as ScriptExecutor;
            var readyKey = Key(effect, "ready-turn");
            if (self == null || executor == null || self.Defend > 0 || state.TurnIndex < state.PassiveValue(readyKey)) return;
            var block = Math.Max(1, state.Stats.Armor * Math.Max(1, effect.Amount) / 100);
            if (CompanionEffectCommitService.Block(executor, self, block))
                state.SetPassiveValue(readyKey, state.TurnIndex + Math.Max(1, effect.SecondaryAmount));
        }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (StatusById(state.StatusId)?.Defend > 0 && IsType(intent, "Defense") && effects.Any(IsBlock))
                context.AddBlock(effect.EffectId, effect.Maximum);
        }
        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
            => Status(effect, Math.Max(0, state.PassiveValue(Key(effect, "ready-turn")) - state.TurnIndex), Math.Max(1, effect.SecondaryAmount));
    }

    private sealed class InterferenceDebuffHandler : SpiritArtifactEffectHandler
    {
        public InterferenceDebuffHandler() : base("intent.interference.debuff-stacks") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (IsType(intent, "Interference")) context.AddNegativeBuffStacks(effect.EffectId, effect.Amount);
        }
    }

    private sealed class DebuffedTargetDamageHandler : SpiritArtifactEffectHandler
    {
        public DebuffedTargetDamageHandler() : base("target.debuffed.damage-percent") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (HasDamage(effects) && effects.SelectMany(value => value.TargetIds ?? new List<string>())
                    .Distinct(StringComparer.Ordinal).Select(StatusById).Any(value => BuffApi.NegativeKindCount(value) > 0))
                context.AddDamage(effect.EffectId, effect.Amount);
        }
    }

    private sealed class GaleAlternationHandler : SpiritArtifactEffectHandler
    {
        public GaleAlternationHandler() : base("intent.gale-alternation") { }
        public override void ContributePlan(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritArtifactActiveEffectSnapshot effect,
            SpiritArtifactPlanModifierContext context)
        {
            if (IsType(intent, "Attack") && state.PassiveValue(Key(effect, "attack-armed")) > 0 && HasDamage(effects))
            {
                context.AddDamage(effect.EffectId, effect.Amount);
                context.AddKey(Key(effect, "consume-attack"));
            }
            if (IsType(intent, "Support", "Interference") && state.PassiveValue(Key(effect, "buff-armed")) > 0)
            {
                context.AddFirstBuffStacks(effect.EffectId, Math.Max(1, effect.SecondaryAmount));
                context.AddKey(Key(effect, "consume-buff"));
            }
        }
        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritArtifactActiveEffectSnapshot effect)
        {
            if (plan.AppliedModifierKeys.Contains(Key(effect, "consume-attack"), StringComparer.Ordinal))
                state.SetPassiveValue(Key(effect, "attack-armed"), 0);
            if (plan.AppliedModifierKeys.Contains(Key(effect, "consume-buff"), StringComparer.Ordinal))
                state.SetPassiveValue(Key(effect, "buff-armed"), 0);
            if (IsType(intent, "Attack")) state.SetPassiveValue(Key(effect, "buff-armed"), 1);
            if (IsType(intent, "Support", "Interference")) state.SetPassiveValue(Key(effect, "attack-armed"), 1);
        }
        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritArtifactActiveEffectSnapshot effect)
        {
            var value = state.PassiveValue(Key(effect, "attack-armed"))
                        + state.PassiveValue(Key(effect, "buff-armed")) * 2;
            return Status(effect, value, 3);
        }
    }

    private static void AddNumeric(
        SpiritArtifactActiveEffectSnapshot effect,
        IReadOnlyList<CompanionResolvedEffect> effects,
        SpiritArtifactPlanModifierContext context,
        int percent)
    {
        if (SpiritArtifactEffectHandler.HasDamage(effects)) context.AddDamage(effect.EffectId, percent);
        if (effects.Any(SpiritArtifactEffectHandler.IsHeal)) context.AddHeal(effect.EffectId, percent);
        if (effects.Any(SpiritArtifactEffectHandler.IsBlock)) context.AddBlock(effect.EffectId, percent);
    }

    private static IStatusManager? StatusById(string? id)
        => !string.IsNullOrWhiteSpace(id) && FightManager.Instance?.statuses?.TryGetValue(id, out var value) == true ? value : null;

    private static int TypeCode(string? type)
        => Enum.TryParse(type, true, out CompanionIntentType parsed) ? (int)parsed + 1 : 0;
}
