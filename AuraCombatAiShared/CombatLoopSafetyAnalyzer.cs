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

    public int RequiredCycles { get; set; }

    public int SafeCycles { get; set; }

    public bool EnemyLimitDamageActive { get; set; }

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
        var limitDamage = end.Enemies
            .Where(enemy => enemy.Hp > 0)
            .Any(enemy => Feature(
                enemy,
                "damageLimitActive",
                "status:buff_limitdamage") > 0d);
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
            RequiredCycles = requiredCycles,
            SafeCycles = safeCycles,
            EnemyLimitDamageActive = limitDamage,
            EnemyEscalationPressure = escalation
        };

        if (progress < minimumProgress)
        {
            assessment.Classification = attrition > 0
                ? CombatLoopClassification.Fake
                : limitDamage || escalation > 0d
                    ? CombatLoopClassification.Blocked
                    : CombatLoopClassification.SustainableControl;
            assessment.Reason = attrition > 0
                ? "resource cycle loses player hp without effective enemy progress"
                : limitDamage || escalation > 0d
                    ? "enemy mechanic blocks lethal progress"
                    : "resource cycle is stable but has no lethal progress";
            return assessment;
        }

        if (attrition > 0 && requiredCycles > safeCycles)
        {
            assessment.Classification = CombatLoopClassification.Fake;
            assessment.Reason =
                "projected player hp reserve expires before the loop can kill";
            return assessment;
        }
        if (limitDamage
            && requiredCycles > Math.Max(
                1,
                profile.LoopLimitDamageMaximumCycles))
        {
            assessment.Classification = CombatLoopClassification.Blocked;
            assessment.Reason =
                "limit-damage makes the projected lethal loop too slow";
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
            assessment.Classification = CombatLoopClassification.Blocked;
            assessment.Reason =
                "projected lethal requires too many repeated cycles";
            return assessment;
        }

        assessment.Classification =
            CombatLoopClassification.CertifiedLethal;
        assessment.Reason =
            "resources repeat with safe hp reserve and effective lethal progress";
        return assessment;
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
