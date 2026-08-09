using System;
using System.Collections.Generic;

namespace AuraCombatAi.Shared;

/// <summary>
/// Mirrors the public damage variables used by the game runtime. Keeping the
/// arithmetic in one place prevents observation, search and semantic auditing
/// from silently using different damage models.
/// </summary>
public static class CombatDynamicDamageProjection
{
    public const string PercentDamage = "PercentDamage";
    public const string DefaultDamage = "DefaultDamage";
    public const string AttackedPercentDamage = "AttackedPercentDamage";
    public const string AttackedDefaultDamage = "AttackedDefaultDamage";
    public const string TruePercentDamage = "TruePercentDamage";
    public const string Strength = "Strength";

    public static int ResolveNormal(
        double baseDamage,
        IReadOnlyDictionary<string, double>? sourceFeatures,
        IReadOnlyDictionary<string, double>? targetFeatures,
        bool applyStrength)
    {
        return ResolveNormal(
            baseDamage,
            Value(sourceFeatures, PercentDamage, 1d),
            Value(sourceFeatures, DefaultDamage, 0d),
            Value(sourceFeatures, Strength, 0d),
            Value(targetFeatures, AttackedPercentDamage, 1d),
            Value(targetFeatures, AttackedDefaultDamage, 0d),
            applyStrength);
    }

    public static int ResolveNormal(
        double baseDamage,
        double outgoingMultiplier,
        double outgoingFlat,
        double strength,
        double incomingMultiplier,
        double incomingFlat,
        bool applyStrength)
    {
        if (!Finite(baseDamage) || baseDamage <= 0d)
        {
            return 0;
        }

        var attributeMultiplier = applyStrength
            ? 1d + strength * 0.03d
            : 1d;
        var outgoing = WitchRound(
            (baseDamage * FiniteOr(outgoingMultiplier, 1d)
             + FiniteOr(outgoingFlat, 0d))
            * FiniteOr(attributeMultiplier, 1d));
        var incoming = (outgoing + FiniteOr(incomingFlat, 0d))
                       * FiniteOr(incomingMultiplier, 1d);
        return ClampNonNegativeTruncated(incoming);
    }

    public static int ResolveTrue(
        double baseDamage,
        IReadOnlyDictionary<string, double>? sourceFeatures)
    {
        return ResolveTrue(
            baseDamage,
            Value(sourceFeatures, TruePercentDamage, 1d));
    }

    public static int ResolveTrue(double baseDamage, double outgoingMultiplier)
    {
        if (!Finite(baseDamage) || baseDamage <= 0d)
        {
            return 0;
        }
        return Math.Max(
            0,
            WitchRound(baseDamage * FiniteOr(outgoingMultiplier, 1d)));
    }

    public static int WitchRound(double value)
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

    private static int ClampNonNegativeTruncated(double value)
    {
        if (!Finite(value) || value <= 0d)
        {
            return 0;
        }
        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    private static double Value(
        IReadOnlyDictionary<string, double>? values,
        string key,
        double fallback)
    {
        return values != null
               && values.TryGetValue(key, out var value)
               && Finite(value)
            ? value
            : fallback;
    }

    private static double FiniteOr(double value, double fallback)
    {
        return Finite(value) ? value : fallback;
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public static class CombatEnemyPriorityPolicy
{
    public const string SummonPotentialFeature = "summonPotential";
    public const string SupportPotentialFeature = "supportPotential";
    public const string StrategicPriorityFeature = "strategicPriority";
    public const string ExpectedThreatFeature = "expectedThreat";

    public static double Calculate(IReadOnlyDictionary<string, double>? features)
    {
        var summon = Value(features, SummonPotentialFeature);
        var support = Value(features, SupportPotentialFeature);
        var escalation = Value(features, "escalationPressure");
        var threat = Value(features, ExpectedThreatFeature);
        var priority = summon * 2.5d
                       + support * 1.5d
                       + Math.Min(2d, escalation * 0.20d)
                       + Math.Min(1.5d, threat / 20d);
        return Math.Max(0d, Math.Min(4d, priority));
    }

    public static double Weight(IReadOnlyDictionary<string, double>? features)
    {
        var observed = Value(features, StrategicPriorityFeature);
        return 1d + Math.Max(
            observed,
            Calculate(features));
    }

    private static double Value(
        IReadOnlyDictionary<string, double>? features,
        string key)
    {
        return features != null
               && features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? Math.Max(0d, value)
            : 0d;
    }
}
