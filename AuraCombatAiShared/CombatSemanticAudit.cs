using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatSemanticAuditComparison
{
    public string Kind { get; set; } = "";

    public double Projected { get; set; }

    public double EffectiveProjected { get; set; }

    public double Actual { get; set; }

    public string Classification { get; set; } = "";

    public string Explanation { get; set; } = "";
}

public sealed class CombatSemanticAuditResult
{
    public List<string> AuditedKinds { get; set; } = new();

    public List<string> MismatchKinds { get; set; } = new();

    public List<string> ExplainedKinds { get; set; } = new();

    public List<CombatSemanticAuditComparison> Comparisons { get; set; } = new();

    public bool Mismatch => MismatchKinds.Count > 0;

    public bool ExplainedDifference => ExplainedKinds.Count > 0;

    public string Describe(string sourceId)
    {
        var details = Comparisons
            .Where(item => string.Equals(
                item.Classification,
                "unexplained",
                StringComparison.Ordinal))
            .Take(4)
            .Select(item =>
                item.Kind
                + ":projected="
                + Format(item.Projected)
                + ",effective="
                + Format(item.EffectiveProjected)
                + ",actual="
                + Format(item.Actual));
        return (string.IsNullOrWhiteSpace(sourceId) ? "unknown" : sourceId)
               + "|"
               + string.Join(";", details);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

public sealed class CombatEffectiveActionProjection
{
    public double Damage { get; set; }

    public double DurabilityDamage { get; set; }

    public double IntrinsicDefend { get; set; }

    public double Defend { get; set; }

    public double Heal { get; set; }
}

public static class CombatSemanticAuditor
{
    public static CombatEffectiveActionProjection ProjectEffective(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected)
    {
        return new CombatEffectiveActionProjection
        {
            Damage = EffectiveDamage(before, action, projected),
            DurabilityDamage =
                EffectiveDurabilityDamage(before, action, projected),
            IntrinsicDefend =
                EffectiveBlock(before.Player, projected.Defend),
            Defend = NetEffectiveBlock(before.Player, projected.Defend),
            Heal = EffectiveHeal(before.Player, projected.Heal)
        };
    }

    public static CombatSemanticAuditResult Audit(
        CombatBattleState before,
        IReadOnlyList<CombatSimulationEvent> events,
        CombatActionSemantics? projected,
        CombatSimulationAction action)
    {
        return Audit(before, before, events, projected, action, null);
    }

    public static CombatSemanticAuditResult Audit(
        CombatBattleState before,
        CombatBattleState after,
        IReadOnlyList<CombatSimulationEvent> events,
        CombatActionSemantics? projected,
        CombatSimulationAction action,
        CombatRuleset? ruleset)
    {
        var result = new CombatSemanticAuditResult();
        if (projected == null)
        {
            result.AuditedKinds.Add("projection");
            result.MismatchKinds.Add("projection-missing");
            result.Comparisons.Add(new CombatSemanticAuditComparison
            {
                Kind = "projection",
                Classification = "unexplained",
                Explanation = "no projected semantics were available"
            });
            return result;
        }

        before ??= new CombatBattleState();
        after ??= before;
        action ??= new CombatSimulationAction();
        var actionEvents = ScopeActionEvents(
            before,
            events ?? Array.Empty<CombatSimulationEvent>());
        var intrinsicEvents = actionEvents
            .Where(item => IsIntrinsic(item, action))
            .ToList();
        var contextualEvents = actionEvents
            .Where(item => !IsIntrinsic(item, action))
            .ToList();
        var playerId = before.PlayerActorId;
        var target = before.FindActor(action.TargetActorId);

        var actualDamage = intrinsicEvents
            .Where(item => item.Kind == CombatSimulationEventKind.DamageDealt
                           && IsEnemy(before, item.TargetActorId))
            .Sum(item => Math.Max(0, item.Amount));
        var effectiveProjection = ProjectEffective(before, action, projected);
        var effectiveDamage = effectiveProjection.Damage;
        Compare(
            result,
            "damage",
            projected.Damage + projected.TrueDamage,
            effectiveDamage,
            actualDamage,
            ExplainDamage(before, action, projected, effectiveDamage));

        var actualBlock = intrinsicEvents
            .Where(item =>
                item.TargetActorId == playerId
                && (item.Kind == CombatSimulationEventKind.BlockGained
                    || item.Kind == CombatSimulationEventKind.BlockChanged))
            .Sum(item => Math.Max(0, item.Amount));
        var effectiveBlock = effectiveProjection.IntrinsicDefend;
        Compare(
            result,
            "defend",
            projected.Defend,
            effectiveBlock,
            actualBlock,
            Different(projected.Defend, effectiveBlock)
                ? "attribute-or-status-modified"
                : "");
        if (Different(
                effectiveProjection.IntrinsicDefend,
                effectiveProjection.Defend))
        {
            AddExplained(
                result,
                "defend-net-value",
                "post-action-status-nullified");
        }

        var actualHeal = intrinsicEvents
            .Where(item => item.Kind == CombatSimulationEventKind.Healed
                           && item.TargetActorId == playerId)
            .Sum(item => Math.Max(0, item.Amount));
        var effectiveHeal = effectiveProjection.Heal;
        Compare(
            result,
            "heal",
            projected.Heal,
            effectiveHeal,
            actualHeal,
            Different(projected.Heal, effectiveHeal)
                ? "missing-hp-or-heal-modifier"
                : "");

        var actualDraw = intrinsicEvents.Count(item =>
            item.Kind == CombatSimulationEventKind.CardDrawn
            && (item.TargetActorId == 0 || item.TargetActorId == playerId));
        Compare(
            result,
            "draw",
            projected.Draw,
            projected.Draw,
            actualDraw,
            projected.Draw > actualDraw ? "draw-cap-or-empty-pile" : "");

        var actualEnergy = intrinsicEvents
            .Where(item => item.Kind == CombatSimulationEventKind.EnergyChanged
                           && item.TargetActorId == playerId)
            .Sum(item => Math.Max(0, item.Amount));
        Compare(
            result,
            "energy-gain",
            projected.EnergyGain,
            projected.EnergyGain,
            actualEnergy,
            "");

        var actualGenerated = intrinsicEvents.Count(item =>
            item.Kind == CombatSimulationEventKind.CardCreated);
        Compare(
            result,
            "card-generation",
            projected.CardGeneration,
            projected.CardGeneration,
            actualGenerated,
            "");

        var (actualBuff, actualDebuff, hasExactStatusProjection) =
            StatusDeltas(
                before,
                action,
                ruleset,
                intrinsicEvents);
        if (!hasExactStatusProjection)
        {
            actualBuff = intrinsicEvents.Any(item =>
                item.Kind == CombatSimulationEventKind.StatusAdded
                && item.TargetActorId == playerId)
                ? Math.Max(1d, projected.Buff)
                : 0d;
            actualDebuff = intrinsicEvents.Any(item =>
                item.Kind == CombatSimulationEventKind.StatusAdded
                && IsEnemy(before, item.TargetActorId))
                ? Math.Max(1d, projected.Debuff)
                : 0d;
        }
        Compare(
            result,
            "buff",
            projected.Buff,
            projected.Buff,
            actualBuff,
            projected.Buff > actualBuff ? "status-cap-or-no-op" : "");
        Compare(
            result,
            "debuff",
            projected.Debuff,
            projected.Debuff,
            actualDebuff,
            projected.Debuff > actualDebuff ? "status-cap-or-no-op" : "");

        if (intrinsicEvents.Any(item =>
                item.Kind == CombatSimulationEventKind.ActorSummoned))
        {
            result.AuditedKinds.Add("summon");
            result.MismatchKinds.Add("summon-unrepresented");
            result.Comparisons.Add(new CombatSemanticAuditComparison
            {
                Kind = "summon",
                Actual = 1d,
                Classification = "unexplained",
                Explanation = "summon semantics are not represented"
            });
        }
        var actualRandom = intrinsicEvents.Any(item =>
            item.Kind == CombatSimulationEventKind.RandomResolved
            || item.Kind == CombatSimulationEventKind.DiceChecked);
        if (projected.RandomOutcome || actualRandom)
        {
            result.AuditedKinds.Add("random-outcome");
            if (projected.RandomOutcome != actualRandom)
            {
                result.MismatchKinds.Add("random-outcome");
                result.Comparisons.Add(new CombatSemanticAuditComparison
                {
                    Kind = "random-outcome",
                    Projected = projected.RandomOutcome ? 1d : 0d,
                    EffectiveProjected = projected.RandomOutcome ? 1d : 0d,
                    Actual = actualRandom ? 1d : 0d,
                    Classification = "unexplained",
                    Explanation = "intrinsic random event presence differs"
                });
            }
        }

        foreach (var kind in ContextualKinds(before, contextualEvents))
        {
            AddExplained(result, kind, "trigger-side-effect");
        }
        return result;
    }

    private static IReadOnlyList<CombatSimulationEvent> ScopeActionEvents(
        CombatBattleState before,
        IReadOnlyList<CombatSimulationEvent> events)
    {
        var sourceActionId = Math.Max(1L, before.ActionSequence + 1L);
        return events
            .Where(item => item.SourceActionId == 0
                           || item.SourceActionId == sourceActionId)
            .ToList();
    }

    private static bool IsIntrinsic(
        CombatSimulationEvent item,
        CombatSimulationAction action)
    {
        var actionReward = !string.IsNullOrWhiteSpace(item.SourceRewardId)
                           && string.Equals(
                               item.SourceRewardId,
                               action.DefinitionId,
                               StringComparison.OrdinalIgnoreCase);
        if ((!string.IsNullOrWhiteSpace(item.HandlerId)
             || !string.IsNullOrWhiteSpace(item.SourceRewardId))
            && !actionReward)
        {
            return false;
        }
        return item.CardInstanceId == 0
               || action.CardInstanceId == 0
               || item.CardInstanceId == action.CardInstanceId;
    }

    private static double EffectiveDamage(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected)
    {
        var source = before.FindActor(action.ActorId) ?? before.Player;
        var target = before.FindActor(action.TargetActorId);
        if (target == null)
        {
            return Math.Max(0d, projected.Damage + projected.TrueDamage);
        }
        var hp = Math.Max(0, target.Hp);
        var normal = ModifiedDamage(
            source,
            target,
            projected.Damage,
            applyStrength: source?.Kind == CombatSimulationActorKind.Player,
            damageType: "Normal");
        var blocked = Math.Min(Math.Max(0, target.Block), normal);
        var hpDamage = Math.Min(hp, Math.Max(0, normal - blocked));
        hp -= hpDamage;
        var trueDamage = ModifiedDamage(
            source,
            target,
            projected.TrueDamage,
            applyStrength: false,
            damageType: "True");
        var totalHpDamage =
            hpDamage + Math.Min(hp, Math.Max(0, trueDamage));
        return ApplyHpLossLimit(target, totalHpDamage);
    }

    private static double EffectiveDurabilityDamage(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected)
    {
        var source = before.FindActor(action.ActorId) ?? before.Player;
        var target = before.FindActor(action.TargetActorId);
        if (target == null)
        {
            return Math.Max(0d, projected.Damage + projected.TrueDamage);
        }
        var durability = Math.Max(0, target.Hp) + Math.Max(0, target.Block);
        var normal = ModifiedDamage(
            source,
            target,
            projected.Damage,
            applyStrength: source?.Kind == CombatSimulationActorKind.Player,
            damageType: "Normal");
        var blockDamage = Math.Min(
            Math.Max(0, target.Block),
            Math.Max(0, normal));
        var normalHpDamage = Math.Min(
            Math.Max(0, target.Hp),
            Math.Max(0, normal - blockDamage));
        var hpAfterNormal = Math.Max(
            0,
            target.Hp - normalHpDamage);
        var trueDamage = ModifiedDamage(
            source,
            target,
            projected.TrueDamage,
            applyStrength: false,
            damageType: "True");
        var hpDamage = normalHpDamage
                       + Math.Min(
                           hpAfterNormal,
                           Math.Max(0, trueDamage));
        return Math.Min(
            durability,
            blockDamage + ApplyHpLossLimit(target, hpDamage));
    }

    private static int ModifiedDamage(
        CombatActorState? source,
        CombatActorState target,
        double amount,
        bool applyStrength,
        string damageType)
    {
        if (amount <= 0d)
        {
            return 0;
        }
        var outgoingMultiplier = Variable(source, "PercentDamage", 1d);
        var outgoingFlat = Variable(source, "DefaultDamage", 0d);
        var incomingMultiplier = Variable(target, "AttackedPercentDamage", 1d);
        var incomingFlat = Variable(target, "AttackedDefaultDamage", 0d);
        var attributeMultiplier = applyStrength
            ? 1d + Math.Max(0d, Variable(source, "Strength", 0d)) * 0.03d
            : 1d;
        var outgoing = WitchRounded(
            (amount * outgoingMultiplier + outgoingFlat) * attributeMultiplier);
        var typedMultiplier = Math.Max(
            0d,
            Variable(
                target,
                "DamageTakenMultiplier." + damageType,
                1d));
        var filterReduction = Math.Max(
            0d,
            Variable(
                target,
                "DamageFilter." + damageType,
                0d));
        return Math.Max(
            0,
            (int)((outgoing + incomingFlat)
                  * incomingMultiplier
                  * typedMultiplier
                  * Math.Max(0d, 1d - filterReduction / 100d)));
    }

    private static double EffectiveBlock(
        CombatActorState? player,
        double amount)
    {
        if (player == null || amount <= 0d)
        {
            return Math.Max(0d, amount);
        }
        return Math.Max(
            0,
            WitchRounded(
                amount
                * Variable(player, "DefendPercent", 1d)
                * (1d + Math.Max(0d, Variable(player, "Perceive", 0d)) * 0.04d)));
    }

    private static double NetEffectiveBlock(
        CombatActorState? player,
        double amount)
    {
        var intrinsic = EffectiveBlock(player, amount);
        return StatusStacks(player, "buff_rotten") > 0
            ? 0d
            : intrinsic;
    }

    private static double ApplyHpLossLimit(
        CombatActorState target,
        double requested)
    {
        requested = Math.Max(0d, requested);
        if (!target.Variables.TryGetValue(
                "MaxChangeHp",
                out var maximumChangeRatio))
        {
            return requested;
        }
        var maximumLoss = Math.Floor(
            target.MaxHp
            * Math.Max(0d, Math.Min(1d, maximumChangeRatio)));
        var alreadyLost = Math.Max(
            0d,
            Variable(target, "HpLossThisAction", 0d));
        return Math.Min(
            requested,
            Math.Max(0d, maximumLoss - alreadyLost));
    }

    private static double EffectiveHeal(
        CombatActorState? player,
        double amount)
    {
        if (player == null || amount <= 0d)
        {
            return Math.Max(0d, amount);
        }
        var modified = Math.Max(
            0,
            (int)Math.Round(
                amount * Variable(player, "HealMultiplier", 1d)));
        return Math.Min(modified, Math.Max(0, player.MaxHp - player.Hp));
    }

    private static (
        double Buff,
        double Debuff,
        bool Exact) StatusDeltas(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatRuleset? ruleset,
        IReadOnlyList<CombatSimulationEvent> intrinsicEvents)
    {
        if (ruleset == null
            || !ruleset.TryGetCard(action.DefinitionId, out var card))
        {
            return (0d, 0d, false);
        }
        var buff = 0d;
        var debuff = 0d;
        var exact = false;
        var targets = card.Effects
            .Where(item => item.Kind == CombatSimulationEffectKind.AddStatus)
            .SelectMany(effect => TargetActorIds(before, action, effect.Target)
                .Select(targetId => new
                {
                    TargetId = targetId,
                    StatusId = effect.DefinitionId
                }))
            .Distinct()
            .ToList();
        foreach (var target in targets)
        {
            exact = true;
            var current = StatusStacks(
                before.FindActor(target.TargetId),
                target.StatusId);
            var maximum = ruleset.TryGetStatus(
                target.StatusId,
                out var statusDefinition)
                ? Math.Max(1, statusDefinition.MaximumStacks)
                : int.MaxValue;
            var delta = 0;
            foreach (var item in intrinsicEvents.Where(item =>
                         item.Kind == CombatSimulationEventKind.StatusAdded
                         && item.TargetActorId == target.TargetId
                         && string.Equals(
                             item.DefinitionId,
                             target.StatusId,
                             StringComparison.OrdinalIgnoreCase)))
            {
                var next = Math.Min(
                    maximum,
                    current + Math.Max(1, item.Amount));
                delta += Math.Max(0, next - current);
                current = next;
            }
            if (before.FindActor(target.TargetId)?.Kind
                == CombatSimulationActorKind.Enemy)
            {
                debuff += delta;
            }
            else
            {
                buff += delta;
            }
        }
        return (buff, debuff, exact);
    }

    private static IEnumerable<int> TargetActorIds(
        CombatBattleState state,
        CombatSimulationAction action,
        CombatSimulationTarget target)
    {
        return target switch
        {
            CombatSimulationTarget.Self
                or CombatSimulationTarget.Player =>
                new[] { state.PlayerActorId },
            CombatSimulationTarget.AllEnemies =>
                state.LivingEnemies.Select(item => item.ActorId),
            CombatSimulationTarget.AllAllies =>
                state.Actors
                    .Where(item => item.Alive
                                   && item.Kind
                                   != CombatSimulationActorKind.Enemy)
                    .Select(item => item.ActorId),
            _ => new[] { action.TargetActorId }
        };
    }

    private static int StatusStacks(
        CombatActorState? actor,
        string statusId)
    {
        return actor?.Statuses.FirstOrDefault(item => string.Equals(
            item.StatusId,
            statusId,
            StringComparison.OrdinalIgnoreCase))?.Stacks ?? 0;
    }

    private static IEnumerable<string> ContextualKinds(
        CombatBattleState before,
        IReadOnlyList<CombatSimulationEvent> events)
    {
        var playerId = before.PlayerActorId;
        if (events.Any(item => item.Kind == CombatSimulationEventKind.DamageDealt
                              && IsEnemy(before, item.TargetActorId)))
        {
            yield return "damage";
        }
        if (events.Any(item =>
                item.TargetActorId == playerId
                && (item.Kind == CombatSimulationEventKind.BlockGained
                    || item.Kind == CombatSimulationEventKind.BlockChanged)))
        {
            yield return "defend";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.Healed
                              && item.TargetActorId == playerId))
        {
            yield return "heal";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.StatusAdded
                              && item.TargetActorId == playerId))
        {
            yield return "buff";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.StatusAdded
                              && IsEnemy(before, item.TargetActorId)))
        {
            yield return "debuff";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.CardDrawn))
        {
            yield return "draw";
        }
    }

    private static string ExplainDamage(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected,
        double effective)
    {
        var raw = projected.Damage + projected.TrueDamage;
        if (!Different(raw, effective))
        {
            return "";
        }
        var target = before.FindActor(action.TargetActorId);
        if (target?.Block > 0 && projected.Damage > 0d)
        {
            return "absorbed-by-block";
        }
        if (target != null && target.Hp < raw)
        {
            return "overkill-clamped";
        }
        return "attribute-or-status-modified";
    }

    private static void Compare(
        CombatSemanticAuditResult result,
        string kind,
        double projected,
        double effectiveProjected,
        double actual,
        string explanation)
    {
        if (Math.Abs(projected) <= 0.000001d
            && Math.Abs(effectiveProjected) <= 0.000001d
            && Math.Abs(actual) <= 0.000001d)
        {
            return;
        }
        result.AuditedKinds.Add(kind);
        if (Different(projected, effectiveProjected))
        {
            AddExplained(
                result,
                kind,
                string.IsNullOrWhiteSpace(explanation)
                    ? "context-adjusted"
                    : explanation);
        }
        var tolerance = Math.Max(
            2d,
            Math.Max(
                Math.Abs(effectiveProjected),
                Math.Abs(actual)) * 0.25d);
        if (Math.Abs(effectiveProjected - actual) <= tolerance)
        {
            result.Comparisons.Add(new CombatSemanticAuditComparison
            {
                Kind = kind,
                Projected = projected,
                EffectiveProjected = effectiveProjected,
                Actual = actual,
                Classification = Different(projected, effectiveProjected)
                    ? "explained"
                    : "matched",
                Explanation = explanation
            });
            return;
        }
        result.MismatchKinds.Add(kind);
        result.Comparisons.Add(new CombatSemanticAuditComparison
        {
            Kind = kind,
            Projected = projected,
            EffectiveProjected = effectiveProjected,
            Actual = actual,
            Classification = "unexplained",
            Explanation = explanation
        });
    }

    private static void AddExplained(
        CombatSemanticAuditResult result,
        string kind,
        string explanation)
    {
        if (!result.ExplainedKinds.Contains(
                kind,
                StringComparer.OrdinalIgnoreCase))
        {
            result.ExplainedKinds.Add(kind);
        }
        if (result.Comparisons.Any(item =>
                string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Classification,
                    "explained",
                    StringComparison.Ordinal)))
        {
            return;
        }
        result.Comparisons.Add(new CombatSemanticAuditComparison
        {
            Kind = kind,
            Classification = "explained",
            Explanation = explanation
        });
    }

    private static bool Different(double left, double right)
    {
        return Math.Abs(left - right) > 0.000001d;
    }

    private static bool IsEnemy(CombatBattleState state, int actorId)
    {
        return state.FindActor(actorId)?.Kind
               == CombatSimulationActorKind.Enemy;
    }

    private static double Variable(
        CombatActorState? actor,
        string key,
        double fallback)
    {
        return actor?.Variables.TryGetValue(key, out var value) == true
            ? value
            : fallback;
    }

    private static int WitchRounded(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }
        if (value <= int.MinValue)
        {
            return int.MinValue;
        }
        var ceiling = Math.Ceiling(value);
        return (int)(ceiling - value <= 0.01d
            ? ceiling
            : Math.Floor(value));
    }
}
