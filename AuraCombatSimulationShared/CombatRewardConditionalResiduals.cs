using System;
using System.Collections.Generic;

namespace AuraCombatSimulation.Shared;

public static class CombatRewardConditionalResidualProtocol
{
    public const string Version = "reward-conditional-residual-v1";

    public static string Key(
        string? rewardId,
        string? difficultyId,
        int encounterIndex,
        string? archetype)
    {
        return Normalize(rewardId, "unknown")
               + "|"
               + NormalizeDifficulty(difficultyId)
               + "|"
               + EncounterBucket(encounterIndex)
               + "|"
               + Normalize(archetype, "*");
    }

    public static double Resolve(
        IReadOnlyDictionary<string, double>? residuals,
        string? rewardId,
        string? difficultyId,
        int encounterIndex,
        string? archetype)
    {
        if (residuals == null || residuals.Count == 0)
        {
            return 0d;
        }
        var reward = Normalize(rewardId, "unknown");
        var difficulty = NormalizeDifficulty(difficultyId);
        var bucket = EncounterBucket(encounterIndex);
        var build = Normalize(archetype, "*");
        var keys = new[]
        {
            reward + "|" + difficulty + "|" + bucket + "|" + build,
            reward + "|" + difficulty + "|" + bucket + "|*",
            reward + "|" + difficulty + "|*|" + build,
            reward + "|" + difficulty + "|*|*",
            reward + "|*|*|*"
        };
        foreach (var key in keys)
        {
            if (residuals.TryGetValue(key, out var value)
                && !double.IsNaN(value)
                && !double.IsInfinity(value))
            {
                return value;
            }
        }
        return 0d;
    }

    public static string EncounterBucket(int encounterIndex)
    {
        return encounterIndex switch
        {
            <= 0 => "opening",
            <= 3 => "local-1-3",
            <= 10 => "early",
            <= 20 => "middle",
            <= 30 => "late",
            _ => "finale"
        };
    }

    public static string NormalizeDifficulty(string? value)
    {
        return string.Equals(
            (value ?? "").Trim(),
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}
