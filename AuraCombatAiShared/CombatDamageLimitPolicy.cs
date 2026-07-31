using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public sealed class CombatDamageProjection
{
    public double BlockDamage { get; set; }

    public double HpDamage { get; set; }

    public double DurabilityDamage => BlockDamage + HpDamage;

    public double PreventedHpDamage { get; set; }

    public bool LimitDamageActive { get; set; }

    public double RemainingHpDamageBudget { get; set; } =
        double.PositiveInfinity;
}

public static class CombatDamageLimitPolicy
{
    public const string ActiveFeature = "damageLimitActive";
    public const string RemainingFeature = "damageLimitLevel";
    public const string StatusFeature = "status:buff_limitdamage";

    public static CombatDamageProjection Project(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (action == null) throw new ArgumentNullException(nameof(action));

        var semantics = action.Semantics ?? new CombatActionSemantics();
        var normalDamage = Math.Max(0d, semantics.Damage)
                           * Math.Max(1d, semantics.HitCount);
        var bypassDamage = Math.Max(0d, semantics.TrueDamage)
                           + Math.Max(0d, semantics.DamageOverTime);
        var targets = ResolveTargets(state.Enemies, action.TargetRuntimeId);
        var result = new CombatDamageProjection();
        foreach (var target in targets)
        {
            var projected = Project(target, normalDamage, bypassDamage);
            result.BlockDamage += projected.BlockDamage;
            result.HpDamage += projected.HpDamage;
            result.PreventedHpDamage += projected.PreventedHpDamage;
            result.LimitDamageActive |= projected.LimitDamageActive;
            result.RemainingHpDamageBudget = Math.Min(
                result.RemainingHpDamageBudget,
                projected.RemainingHpDamageBudget);
        }
        if (!targets.Any())
        {
            result.HpDamage = normalDamage + bypassDamage;
        }
        return result;
    }

    public static CombatDamageProjection Project(
        CombatUnitObservation target,
        double normalDamage,
        double bypassDamage)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        return ProjectCore(
            Math.Max(0, target.CurrentHp),
            Math.Max(0, target.Defend),
            target.Features,
            normalDamage,
            bypassDamage);
    }

    public static CombatDamageProjection Project(
        CombatSimulationUnit target,
        double normalDamage,
        double bypassDamage)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        return ProjectCore(
            Math.Max(0, target.Hp),
            Math.Max(0, target.Defend),
            target.Features,
            normalDamage,
            bypassDamage);
    }

    public static bool TryGetRemainingBudget(
        IReadOnlyDictionary<string, double>? features,
        out double remaining)
    {
        remaining = double.PositiveInfinity;
        if (features == null)
        {
            return false;
        }
        var active = Feature(features, ActiveFeature) > 0.5d
                     || features.ContainsKey(StatusFeature);
        if (!active)
        {
            return false;
        }
        if (features.TryGetValue(RemainingFeature, out var level)
            && IsFinite(level))
        {
            remaining = Math.Max(0d, level);
            return true;
        }
        if (features.TryGetValue(StatusFeature, out level)
            && IsFinite(level))
        {
            remaining = Math.Max(0d, level);
            return true;
        }
        remaining = 0d;
        return true;
    }

    public static void ConsumeBudget(
        IDictionary<string, double> features,
        double hpDamage)
    {
        if (!TryGetRemainingBudget(
                features as IReadOnlyDictionary<string, double>,
                out var remaining))
        {
            return;
        }
        var next = Math.Max(0d, remaining - Math.Max(0d, hpDamage));
        features[ActiveFeature] = 1d;
        features[RemainingFeature] = next;
        if (features.ContainsKey(StatusFeature))
        {
            features[StatusFeature] = next;
        }
    }

    private static CombatDamageProjection ProjectCore(
        int hp,
        int defend,
        IReadOnlyDictionary<string, double>? features,
        double normalDamage,
        double bypassDamage)
    {
        normalDamage = Math.Max(0d, normalDamage);
        bypassDamage = Math.Max(0d, bypassDamage);
        var blockDamage = Math.Min(defend, normalDamage);
        var requestedHpDamage =
            Math.Max(0d, normalDamage - blockDamage) + bypassDamage;
        var limited = TryGetRemainingBudget(features, out var remaining);
        var hpDamage = Math.Min(hp, requestedHpDamage);
        if (limited)
        {
            hpDamage = Math.Min(hpDamage, remaining);
        }
        return new CombatDamageProjection
        {
            BlockDamage = blockDamage,
            HpDamage = hpDamage,
            PreventedHpDamage = Math.Max(0d, requestedHpDamage - hpDamage),
            LimitDamageActive = limited,
            RemainingHpDamageBudget = limited
                ? remaining
                : double.PositiveInfinity
        };
    }

    private static IEnumerable<CombatUnitObservation> ResolveTargets(
        IEnumerable<CombatUnitObservation> enemies,
        int targetRuntimeId)
    {
        var alive = enemies.Where(enemy => enemy != null && enemy.Alive);
        return targetRuntimeId == 0
            ? alive
            : alive.Where(enemy => enemy.RuntimeId == targetRuntimeId);
    }

    private static double Feature(
        IReadOnlyDictionary<string, double> values,
        string key)
    {
        return values.TryGetValue(key, out var value) && IsFinite(value)
            ? value
            : 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
