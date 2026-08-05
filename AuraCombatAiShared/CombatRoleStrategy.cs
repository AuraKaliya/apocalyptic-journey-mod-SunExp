using System;

namespace AuraCombatAi.Shared;

/// <summary>
/// Consumer-owned, role-aware tactical guidance. Shared combat AI owns only
/// the registration and feature protocol; concrete role and card semantics
/// remain in the registering consumer.
/// </summary>
public interface ICombatRoleStrategyProvider
{
    bool TryEnrich(CombatStateObservation state);
}

public static class CombatRoleStrategyFeatureNames
{
    public const string Prefix = "roleStrategy:";

    public const string Active = Prefix + "active";

    public const string Phase = Prefix + "phase";

    public const string Synergy = Prefix + "synergy";

    public const string Continuation = Prefix + "continuation";

    public const string Scaling = Prefix + "scaling";

    public const string Risk = Prefix + "risk";

    public const string Coordination = Prefix + "coordination";

    public const string StrategicallyProhibited =
        Prefix + "strategically-prohibited";

    public const string SafeContinuationCertified =
        Prefix + "safe-continuation-certified";

    public const string TrainingQuotaPrefix = Prefix + "training-quota:";

    public static string MinimumTrainingShare(string strategy)
    {
        return TrainingQuotaPrefix
               + NormalizeTrainingStrategy(strategy)
               + ":minimum-share";
    }

    public static string MaximumTrainingShare(string strategy)
    {
        return TrainingQuotaPrefix
               + NormalizeTrainingStrategy(strategy)
               + ":maximum-share";
    }

    public static double Value(
        CombatActionObservation? action,
        string key)
    {
        if (action?.Features == null
            || !action.Features.TryGetValue(key, out var value)
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            return 0d;
        }
        return value;
    }

    private static string NormalizeTrainingStrategy(string strategy)
    {
        var normalized = (strategy ?? "")
            .Trim()
            .ToLowerInvariant();
        return normalized.StartsWith("strategy-", StringComparison.Ordinal)
            ? normalized
            : "strategy-" + normalized;
    }
}
