using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraGameData.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;

namespace AuraSkin.Shared.GameApi;

public static class CareerConfigApi
{
    public static string NormalizeId(string? careerId)
    {
        return AuraSharedIdentity.NormalizeCareerId(careerId);
    }

    public static bool TryCreate(string? careerId, out DataConfig? career)
    {
        return TryCreate(careerId, out career, warnOnFailure: true);
    }

    internal static bool TryCreate(string? careerId, out DataConfig? career, bool warnOnFailure)
    {
        career = null;
        var candidates = CandidateIds(careerId).ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        var materialized = AuraGameDataHostApi.Materialize(DataType.Career, candidates);
        if (materialized.Success && materialized.Instance is DataConfig resolved)
        {
            career = resolved;
            return true;
        }

        if (warnOnFailure && !string.IsNullOrWhiteSpace(materialized.Message))
        {
            SkinLog.Warn("Career config could not be created: "
                         + string.Join(", ", candidates)
                         + ": "
                         + materialized.Message);
        }
        return false;
    }

    private static IEnumerable<string> CandidateIds(string? careerId)
    {
        var raw = (careerId ?? "").Trim();
        var normalized = NormalizeId(raw);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            yield return normalized;
        }

        if (!string.IsNullOrWhiteSpace(raw)
            && !string.Equals(raw, normalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return raw;
        }
    }

}
