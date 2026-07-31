using System;
using System.Linq;

namespace AuraCombatAi.Shared;

public enum CombatLoopClassification
{
    None,
    CertifiedLethal,
    SustainableControl,
    Fake,
    Blocked
}

public sealed class CombatLoopSafetyAssessment
{
    public CombatLoopClassification Classification { get; set; }

    public double EffectiveEnemyProgress { get; set; }

    public int PlayerHpDelta { get; set; }

    public int PlayerBlockDelta { get; set; }

    public double MonotonicStateGain { get; set; }

    public double ResourceStateLoss { get; set; }

    public int EnergyDelta { get; set; }

    public int RequiredCycles { get; set; }

    public int SafeCycles { get; set; }

    public bool EnemyLimitDamageActive { get; set; }

    public double EnemyDamageBudgetRemaining { get; set; }

    public double EnemyEscalationPressure { get; set; }

    public string Reason { get; set; } = "";
}

public static class CombatLoopSafetyAnalyzer
{
    public static CombatLoopSafetyAssessment Analyze(
        CombatSimulationState start,
        CombatSimulationState end,
        CombatDecisionProfile profile)
    {
        if (start == null) throw new ArgumentNullException(nameof(start));
        if (end == null) throw new ArgumentNullException(nameof(end));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var progress = EffectiveEnemyHealth(start)
                       - EffectiveEnemyHealth(end);
        var hpDelta = end.PlayerHp - start.PlayerHp;
        var attrition = Math.Max(0, -hpDelta);
        var blockDelta = end.PlayerDefend - start.PlayerDefend;
        var monotonicStateGain =
            PositiveDelta(start.SetupValue, end.SetupValue)
            + PositiveDelta(start.PersistentValue, end.PersistentValue)
            + PositiveDelta(start.DamageMultiplier, end.DamageMultiplier)
            + PositiveDelta(start.DrawnCardPotential, end.DrawnCardPotential)
            + PositiveFeatureGain(start.Features, end.Features);
        var energyDelta = end.Power - start.Power;
        var resourceStateLoss =
            FeatureLoss(start.Features, end.Features)
            + Math.Max(0, -energyDelta);
        var defensiveOrStateGain = blockDelta > 0 || monotonicStateGain > 0d;
        var limitDamage = end.Enemies
            .Where(enemy => enemy.Hp > 0)
            .Any(enemy => Feature(
                enemy,
                "damageLimitActive",
                "status:buff_limitdamage") > 0d);
        var remainingDamageBudget = end.Enemies
            .Where(enemy => enemy.Hp > 0)
            .Select(enemy =>
                CombatDamageLimitPolicy.TryGetRemainingBudget(
                    enemy.Features,
                    out var remaining)
                    ? remaining
                    : double.PositiveInfinity)
            .DefaultIfEmpty(double.PositiveInfinity)
            .Min();
        var escalation = end.Enemies
            .Where(enemy => enemy.Hp > 0)
            .Sum(enemy => Feature(
                enemy,
                "escalationPressure",
                "status:buff_frenzy",
                "status:buff_keenedge",
                "status:buff_counterattack",
                "status:buff_thorns"));
        var reserve = Math.Max(
            1,
            (int)Math.Ceiling(
                end.PlayerMaxHp
                * Math.Max(
                    0d,
                    Math.Min(0.5d, profile.LoopMinimumHpReserveRatio))));
        var safeCycles = attrition <= 0
            ? int.MaxValue
            : Math.Max(0, (end.PlayerHp - reserve) / attrition);
        if (escalation > 0d)
        {
            safeCycles = Math.Min(
                safeCycles,
                Math.Max(1, 8 - (int)Math.Ceiling(
                    Math.Min(7d, escalation))));
        }
        var remaining = EffectiveEnemyHealth(end);
        var minimumProgress = Math.Max(
            0.0001d,
            profile.LoopMinimumEffectiveProgress);
        var requiredCycles = progress >= minimumProgress
            ? Math.Max(0, (int)Math.Ceiling(remaining / progress))
            : int.MaxValue;
        var assessment = new CombatLoopSafetyAssessment
        {
            EffectiveEnemyProgress = progress,
            PlayerHpDelta = hpDelta,
            PlayerBlockDelta = blockDelta,
            MonotonicStateGain = monotonicStateGain,
            ResourceStateLoss = resourceStateLoss,
            EnergyDelta = energyDelta,
            RequiredCycles = requiredCycles,
            SafeCycles = safeCycles,
            EnemyLimitDamageActive = limitDamage,
            EnemyDamageBudgetRemaining = remainingDamageBudget,
            EnemyEscalationPressure = escalation
        };

        if (attrition > 0 || resourceStateLoss > 0d)
        {
            assessment.Classification = CombatLoopClassification.Fake;
            assessment.Reason = attrition > 0
                ? "repeatable structure consumes finite player hp"
                : "repeatable structure consumes finite player state";
            return assessment;
        }

        if (progress < minimumProgress)
        {
            assessment.Classification = defensiveOrStateGain
                ? CombatLoopClassification.SustainableControl
                : limitDamage || escalation > 0d
                    ? CombatLoopClassification.Blocked
                    : CombatLoopClassification.SustainableControl;
            assessment.Reason = defensiveOrStateGain
                ? "resources repeat while block or persistent state grows"
                : limitDamage || escalation > 0d
                    ? "enemy mechanic blocks lethal progress without compensating growth"
                    : energyDelta > 0
                        ? "repeatable structure grows energy but has no current lethal progress"
                        : "resource cycle is stable but has no lethal progress";
            return assessment;
        }

        if (limitDamage
            && requiredCycles > Math.Max(
                1,
                profile.LoopLimitDamageMaximumCycles))
        {
            assessment.Classification = defensiveOrStateGain
                ? CombatLoopClassification.SustainableControl
                : CombatLoopClassification.Blocked;
            assessment.Reason = defensiveOrStateGain
                ? "limit-damage slows lethal progress while persistent defense or state grows"
                : "limit-damage makes the projected lethal loop too slow";
            return assessment;
        }
        if (escalation > 0d && requiredCycles > safeCycles)
        {
            assessment.Classification = CombatLoopClassification.Blocked;
            assessment.Reason =
                "enemy escalation outpaces the projected loop";
            return assessment;
        }
        if (requiredCycles > Math.Max(
                1,
                profile.LoopMaximumCertifiedCycles))
        {
            assessment.Classification = defensiveOrStateGain
                ? CombatLoopClassification.SustainableControl
                : CombatLoopClassification.Blocked;
            assessment.Reason = defensiveOrStateGain
                ? "resources repeat with growth but current lethal progress is not certifiable"
                : "projected lethal requires too many repeated cycles";
            return assessment;
        }

        assessment.Classification =
            CombatLoopClassification.CertifiedLethal;
        assessment.Reason =
            "resources repeat with safe hp reserve and effective lethal progress";
        return assessment;
    }

    private static double PositiveDelta(double start, double end)
    {
        return Math.Max(0d, Finite(end) - Finite(start));
    }

    private static double PositiveFeatureGain(
        System.Collections.Generic.IReadOnlyDictionary<string, double> start,
        System.Collections.Generic.IReadOnlyDictionary<string, double> end)
    {
        return end.Sum(pair =>
        {
            var before = start.TryGetValue(pair.Key, out var value)
                ? Finite(value)
                : 0d;
            return Math.Max(0d, Finite(pair.Value) - before);
        });
    }

    private static double FeatureLoss(
        System.Collections.Generic.IReadOnlyDictionary<string, double> start,
        System.Collections.Generic.IReadOnlyDictionary<string, double> end)
    {
        return start.Sum(pair =>
        {
            var after = end.TryGetValue(pair.Key, out var value)
                ? Finite(value)
                : 0d;
            return Math.Max(0d, Finite(pair.Value) - after);
        });
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }

    private static double EffectiveEnemyHealth(CombatSimulationState state)
    {
        return state.Enemies.Sum(enemy =>
            Math.Max(0, enemy.Hp) + Math.Max(0, enemy.Defend));
    }

    private static double Feature(
        CombatSimulationUnit enemy,
        params string[] keys)
    {
        var total = 0d;
        foreach (var key in keys)
        {
            if (enemy.Features.TryGetValue(key, out var value)
                && !double.IsNaN(value)
                && !double.IsInfinity(value))
            {
                total += Math.Max(0d, value);
            }
        }
        return total;
    }
}
