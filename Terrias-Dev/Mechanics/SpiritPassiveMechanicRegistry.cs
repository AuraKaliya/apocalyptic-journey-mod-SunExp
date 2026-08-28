using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class SpiritVisibleStatusSnapshot
{
    public string Kind { get; set; } = "Buff";

    public string Id { get; set; } = "";

    public int Stacks { get; set; }

    public int Value { get; set; }

    public int Maximum { get; set; }

    public SpiritVisibleStatusSnapshot Clone()
    {
        return new SpiritVisibleStatusSnapshot
        {
            Kind = Kind,
            Id = Id,
            Stacks = Stacks,
            Value = Value,
            Maximum = Maximum
        };
    }
}

public static class SpiritPassiveMechanicRegistry
{
    private const string Progress = "species.progress";
    private const string Armed = "species.armed";
    private const string PreviousType = "species.previous-type";
    private const string ReadyTurn = "species.ready-turn";
    private const string HitCount = "species.hit-count";
    private const string Stacks = "species.stacks";

    private static readonly IReadOnlyDictionary<string, SpeciesMechanic> Handlers =
        new Dictionary<string, SpeciesMechanic>(StringComparer.Ordinal)
        {
            ["species.rhythm"] = new RhythmMechanic(),
            ["species.interference-feedback"] = new InterferenceFeedbackMechanic(),
            ["species.guard-cycle"] = new GuardCycleMechanic(),
            ["species.low-health-drive"] = new LowHealthDriveMechanic(),
            ["species.alternating-drive"] = new AlternatingDriveMechanic(),
            ["species.waiting-drive"] = new WaitingDriveMechanic(),
            ["species.shielded-drive"] = new ShieldedDriveMechanic(),
            ["species.debuff-hunter"] = new DebuffHunterMechanic(),
            ["species.owner-guard"] = new OwnerGuardMechanic(),
            ["species.first-hit-ward"] = new FirstHitWardMechanic(),
            ["species.mana-balance"] = new ManaBalanceMechanic(),
            ["species.momentum"] = new MomentumMechanic()
        };

    public static bool IsSpeciesMechanic(SpiritPassiveDefinition? passive)
    {
        return passive != null
               && string.Equals(passive.Pool, "Species", StringComparison.Ordinal)
               && Handlers.ContainsKey(HandlerId(passive));
    }

    public static bool Supports(string handlerId) => Handlers.ContainsKey(handlerId ?? "");

    public static bool Validate(SpiritPassiveDefinition? passive, out string reason)
    {
        if (passive == null || !Handlers.TryGetValue(HandlerId(passive), out var handler))
        {
            reason = "missing species passive handler";
            return false;
        }
        return handler.Validate(passive, out reason);
    }

    public static string Signature(SpiritPassiveDefinition passive)
    {
        return HandlerId(passive) + "|" + passive.IntentType + "|" + passive.NumericBonusPercent
               + "|" + passive.Threshold + "|" + passive.Value + "|" + passive.SecondaryValue
               + "|" + passive.MaximumStacks;
    }

    public static void BeforePlan(OtherObj actor, CompanionBattleState state, SpiritPassiveDefinition passive)
    {
        Handler(passive)?.BeforePlan(actor, state, passive);
    }

    public static void ApplyPlanModifiers(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        IReadOnlyList<CompanionResolvedEffect> effects,
        SpiritPassiveDefinition passive,
        ref int bonusPercent,
        ICollection<string> modifierKeys)
    {
        Handler(passive)?.ApplyPlanModifiers(state, intent, effects, passive, ref bonusPercent, modifierKeys);
    }

    public static void OnIntentExecuted(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        CompanionIntentPlan plan,
        SpiritPassiveDefinition passive)
    {
        Handler(passive)?.OnIntentExecuted(state, intent, plan, passive);
    }

    public static int OnWait(CompanionBattleState state, SpiritPassiveDefinition passive)
    {
        return Handler(passive)?.OnWait(state, passive) ?? 0;
    }

    public static void OnStatusHit(
        IStatusManager target,
        CompanionBattleState state,
        SpiritPassiveDefinition passive)
    {
        Handler(passive)?.OnStatusHit(target, state, passive);
    }

    public static SpiritVisibleStatusSnapshot VisibleStatus(
        CompanionBattleState state,
        SpiritPassiveDefinition passive)
    {
        return Handler(passive)?.VisibleStatus(state, passive)
               ?? Status(passive, 0, 0);
    }

    private static SpeciesMechanic? Handler(SpiritPassiveDefinition passive)
    {
        return passive != null && Handlers.TryGetValue(HandlerId(passive), out var handler) ? handler : null;
    }

    private static string HandlerId(SpiritPassiveDefinition passive)
    {
        if (passive == null) return "";
        return string.IsNullOrWhiteSpace(passive.HandlerId) ? passive.EffectKind ?? "" : passive.HandlerId.Trim();
    }

    private static bool HasDirectNumeric(IEnumerable<CompanionResolvedEffect> effects)
    {
        return effects.Any(effect =>
        {
            var handler = effect?.HandlerId ?? "";
            return handler.StartsWith("damage.", StringComparison.Ordinal)
                   || handler.StartsWith("block.", StringComparison.Ordinal)
                   || handler.StartsWith("heal.", StringComparison.Ordinal) && handler != "heal.delayed"
                   || handler == "magic.recover";
        });
    }

    private static bool HasDebuffedTarget(IEnumerable<CompanionResolvedEffect> effects)
    {
        return effects.SelectMany(effect => effect.TargetIds ?? new List<string>())
            .Distinct(StringComparer.Ordinal)
            .Select(StatusById)
            .Where(status => status != null)
            .Any(status => ExecutorApi.StatusBuffLevel(status, "buff_weak") > 0
                           || ExecutorApi.StatusBuffLevel(status, "buff_vulnerability") > 0
                           || ExecutorApi.StatusBuffLevel(status, "buff_toxin") > 0
                           || ExecutorApi.StatusBuffLevel(status, "buff_burn") > 0);
    }

    private static IStatusManager? StatusById(string? id)
    {
        return !string.IsNullOrWhiteSpace(id)
               && FightManager.Instance?.statuses?.TryGetValue(id, out var status) == true
            ? status
            : null;
    }

    private static int TypeCode(string type)
    {
        return Enum.TryParse(type, true, out CompanionIntentType parsed) ? (int)parsed + 1 : 0;
    }

    private static void AddBonus(
        string key,
        int value,
        ref int total,
        ICollection<string> modifierKeys)
    {
        if (value <= 0) return;
        total += value;
        modifierKeys.Add(key);
    }

    private static SpiritVisibleStatusSnapshot Status(SpiritPassiveDefinition passive, int value, int maximum)
    {
        return new SpiritVisibleStatusSnapshot
        {
            Kind = "Mechanic",
            Id = passive?.Id ?? "",
            Value = Math.Max(0, value),
            Maximum = Math.Max(0, maximum)
        };
    }

    private abstract class SpeciesMechanic
    {
        public virtual bool Validate(SpiritPassiveDefinition passive, out string reason)
        {
            if (passive.Threshold < 0 || passive.Value < 0 || passive.SecondaryValue < 0
                || passive.MaximumStacks < 0 || passive.NumericBonusPercent < 0
                || passive.NumericBonusPercent > 75)
            {
                reason = "species passive parameters are outside their bounds";
                return false;
            }
            reason = "";
            return true;
        }

        public virtual void BeforePlan(OtherObj actor, CompanionBattleState state, SpiritPassiveDefinition passive) { }

        public virtual void ApplyPlanModifiers(
            CompanionBattleState state,
            CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects,
            SpiritPassiveDefinition passive,
            ref int bonusPercent,
            ICollection<string> modifierKeys) { }

        public virtual void OnIntentExecuted(
            CompanionBattleState state,
            CompanionIntentDefinition intent,
            CompanionIntentPlan plan,
            SpiritPassiveDefinition passive) { }

        public virtual int OnWait(CompanionBattleState state, SpiritPassiveDefinition passive) => 0;

        public virtual void OnStatusHit(
            IStatusManager target,
            CompanionBattleState state,
            SpiritPassiveDefinition passive) { }

        public virtual SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
        {
            return Status(passive, 0, 0);
        }
    }

    private sealed class RhythmMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            if (state.PassiveValue(Armed) > 0 && HasDirectNumeric(effects))
                AddBonus("species.rhythm", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }

        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritPassiveDefinition passive)
        {
            if (plan.AppliedModifierKeys.Contains("species.rhythm", StringComparer.Ordinal)) state.SetPassiveValue(Armed, 0);
            var next = state.PassiveValue(Progress) + 1;
            if (next >= Math.Max(2, passive.Threshold))
            {
                state.SetPassiveValue(Progress, 0);
                state.SetPassiveValue(Armed, 1);
            }
            else state.SetPassiveValue(Progress, next);
        }

        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
            => Status(passive, state.PassiveValue(Armed) > 0 ? Math.Max(2, passive.Threshold) : state.PassiveValue(Progress), Math.Max(2, passive.Threshold));
    }

    private sealed class InterferenceFeedbackMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            if (state.PassiveValue(Armed) > 0 && HasDirectNumeric(effects))
                AddBonus("species.interference-feedback", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }

        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritPassiveDefinition passive)
        {
            if (plan.AppliedModifierKeys.Contains("species.interference-feedback", StringComparer.Ordinal))
                state.SetPassiveValue(Armed, 0);
            if (!string.Equals(intent.Type, "Interference", StringComparison.Ordinal)) return;
            var next = state.PassiveValue(Progress) + 1;
            if (next >= Math.Max(1, passive.Threshold))
            {
                state.SetPassiveValue(Progress, 0);
                state.SetPassiveValue(Armed, 1);
                state.Stats.RecoverMagic(Math.Max(1, passive.Value));
            }
            else state.SetPassiveValue(Progress, next);
        }

        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
            => Status(passive, state.PassiveValue(Armed) > 0 ? Math.Max(1, passive.Threshold) : state.PassiveValue(Progress), Math.Max(1, passive.Threshold));
    }

    private sealed class GuardCycleMechanic : SpeciesMechanic
    {
        public override void BeforePlan(OtherObj actor, CompanionBattleState state, SpiritPassiveDefinition passive)
        {
            var self = StatusById(state.StatusId);
            var executor = actor?.dataConfig?.scriptExecutor as ScriptExecutor;
            if (self == null || executor == null || self.Defend > 0 || state.TurnIndex < state.PassiveValue(ReadyTurn)) return;
            var block = Math.Max(1, passive.Value + state.Stats.Armor * passive.SecondaryValue / 100);
            if (CompanionEffectCommitService.Block(executor, self, block))
                state.SetPassiveValue(ReadyTurn, state.TurnIndex + Math.Max(2, passive.Threshold));
        }

        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
            => Status(passive, Math.Max(0, state.PassiveValue(ReadyTurn) - state.TurnIndex), Math.Max(2, passive.Threshold));
    }

    private sealed class LowHealthDriveMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            var self = StatusById(state.StatusId);
            if (self != null && self.MaxHp > 0 && self.CurHp * 100 / self.MaxHp <= Math.Max(10, passive.Threshold)
                && HasDirectNumeric(effects))
                AddBonus("species.low-health-drive", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }
    }

    private sealed class AlternatingDriveMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            if (state.PassiveValue(Armed) > 0 && HasDirectNumeric(effects))
                AddBonus("species.alternating-drive", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }

        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritPassiveDefinition passive)
        {
            if (plan.AppliedModifierKeys.Contains("species.alternating-drive", StringComparer.Ordinal)) state.SetPassiveValue(Armed, 0);
            var type = TypeCode(intent.Type);
            var progress = state.PassiveValue(PreviousType) > 0 && state.PassiveValue(PreviousType) != type
                ? state.PassiveValue(Progress) + 1
                : 1;
            state.SetPassiveValue(PreviousType, type);
            if (progress >= Math.Max(2, passive.Threshold))
            {
                state.SetPassiveValue(Progress, 0);
                state.SetPassiveValue(Armed, 1);
            }
            else state.SetPassiveValue(Progress, progress);
        }

        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
            => Status(passive, state.PassiveValue(Armed) > 0 ? Math.Max(2, passive.Threshold) : state.PassiveValue(Progress), Math.Max(2, passive.Threshold));
    }

    private sealed class WaitingDriveMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            if (state.PassiveValue(Armed) > 0 && HasDirectNumeric(effects))
                AddBonus("species.waiting-drive", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }

        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritPassiveDefinition passive)
        {
            if (plan.AppliedModifierKeys.Contains("species.waiting-drive", StringComparer.Ordinal)) state.SetPassiveValue(Armed, 0);
        }

        public override int OnWait(CompanionBattleState state, SpiritPassiveDefinition passive)
        {
            var next = state.PassiveValue(Progress) + 1;
            if (next >= Math.Max(1, passive.Threshold))
            {
                state.SetPassiveValue(Progress, 0);
                state.SetPassiveValue(Armed, 1);
            }
            else state.SetPassiveValue(Progress, next);
            return Math.Max(0, passive.Value);
        }

        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
            => Status(passive, state.PassiveValue(Armed) > 0 ? Math.Max(1, passive.Threshold) : state.PassiveValue(Progress), Math.Max(1, passive.Threshold));
    }

    private sealed class ShieldedDriveMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            if (StatusById(state.StatusId)?.Defend > 0 && HasDirectNumeric(effects))
                AddBonus("species.shielded-drive", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }
    }

    private sealed class DebuffHunterMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            if (HasDirectNumeric(effects) && HasDebuffedTarget(effects))
                AddBonus("species.debuff-hunter", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }
    }

    private sealed class OwnerGuardMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            var matchingType = string.IsNullOrWhiteSpace(passive.IntentType)
                ? intent.Type is "Defense" or "Recovery" or "Support"
                : string.Equals(intent.Type, passive.IntentType, StringComparison.Ordinal);
            if (state.PassiveValue(Armed) > 0 && matchingType && HasDirectNumeric(effects))
                AddBonus("species.owner-guard", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }

        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritPassiveDefinition passive)
        {
            if (plan.AppliedModifierKeys.Contains("species.owner-guard", StringComparer.Ordinal)) state.SetPassiveValue(Armed, 0);
        }

        public override void OnStatusHit(IStatusManager target, CompanionBattleState state, SpiritPassiveDefinition passive)
        {
            if (string.Equals(target.InstanceId, state.OwnerStatusId, StringComparison.Ordinal)) state.SetPassiveValue(Armed, 1);
        }

        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
            => Status(passive, state.PassiveValue(Armed), 1);
    }

    private sealed class FirstHitWardMechanic : SpeciesMechanic
    {
        public override void OnStatusHit(IStatusManager target, CompanionBattleState state, SpiritPassiveDefinition passive)
        {
            if (!string.Equals(target.InstanceId, state.StatusId, StringComparison.Ordinal)) return;
            var next = state.PassiveValue(HitCount) + 1;
            if (next < Math.Max(1, passive.Threshold))
            {
                state.SetPassiveValue(HitCount, next);
                return;
            }
            state.SetPassiveValue(HitCount, 0);
            var spirit = SpiritStateStore.Find(state.StatusId)?.Spirit;
            var executor = spirit?.dataConfig?.scriptExecutor as ScriptExecutor;
            if (executor != null)
            {
                var block = Math.Max(1, passive.Value + state.Stats.Armor * passive.SecondaryValue / 100);
                CompanionEffectCommitService.Block(executor, target, block);
            }
        }

        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
            => Status(passive, state.PassiveValue(HitCount), Math.Max(1, passive.Threshold));
    }

    private sealed class ManaBalanceMechanic : SpeciesMechanic
    {
        public override void BeforePlan(OtherObj actor, CompanionBattleState state, SpiritPassiveDefinition passive)
        {
            if (state.Stats.CurrentMagic == 0) state.Stats.RecoverMagic(Math.Max(1, passive.Value));
        }

        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            if (state.Stats.CurrentMagic >= state.Stats.MaxMagic && HasDirectNumeric(effects))
                AddBonus("species.mana-balance", passive.NumericBonusPercent, ref bonusPercent, modifierKeys);
        }
    }

    private sealed class MomentumMechanic : SpeciesMechanic
    {
        public override void ApplyPlanModifiers(CompanionBattleState state, CompanionIntentDefinition intent,
            IReadOnlyList<CompanionResolvedEffect> effects, SpiritPassiveDefinition passive, ref int bonusPercent,
            ICollection<string> modifierKeys)
        {
            if (HasDirectNumeric(effects))
                AddBonus("species.momentum", state.PassiveValue(Stacks) * Math.Max(1, passive.Value), ref bonusPercent, modifierKeys);
        }

        public override void OnIntentExecuted(CompanionBattleState state, CompanionIntentDefinition intent,
            CompanionIntentPlan plan, SpiritPassiveDefinition passive)
        {
            state.SetPassiveValue(Stacks, Math.Min(Math.Max(1, passive.MaximumStacks), state.PassiveValue(Stacks) + 1));
        }

        public override SpiritVisibleStatusSnapshot VisibleStatus(CompanionBattleState state, SpiritPassiveDefinition passive)
            => Status(passive, state.PassiveValue(Stacks), Math.Max(1, passive.MaximumStacks));
    }
}

public static class SpiritVisibleStatusService
{
    public static IReadOnlyList<SpiritVisibleStatusSnapshot> Capture(SpiritOtherObj? spirit)
    {
        var result = new List<SpiritVisibleStatusSnapshot>();
        var status = spirit?.Status as StatusManager;
        if (status != null)
        {
            foreach (var entry in BuffApi.SnapshotLevels(status).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                result.Add(new SpiritVisibleStatusSnapshot
                {
                    Kind = "Buff",
                    Id = entry.Key,
                    Stacks = entry.Value
                });
                if (result.Count >= SpiritSystemContract.MaximumVisibleStatuses) break;
            }
        }

        var state = spirit == null ? null : CompanionBattleStateStore.Find(spirit.InstanceId);
        var passive = state == null ? null : SpiritTrainingRegistry.FindPassive(state.EquippedPassiveId);
        if (state != null && passive != null && result.Count < SpiritSystemContract.MaximumVisibleStatuses)
        {
            result.Add(SpiritPassiveMechanicRegistry.IsSpeciesMechanic(passive)
                ? SpiritPassiveMechanicRegistry.VisibleStatus(state, passive)
                : new SpiritVisibleStatusSnapshot { Kind = "Mechanic", Id = passive.Id });
        }
        if (state != null && result.Count < SpiritSystemContract.MaximumVisibleStatuses)
        {
            result.AddRange(SpiritArtifactBattleRuntime.VisibleStatuses(state)
                .Take(SpiritSystemContract.MaximumVisibleStatuses - result.Count));
        }
        return result.Take(SpiritSystemContract.MaximumVisibleStatuses).Select(item => item.Clone()).ToArray();
    }
}
