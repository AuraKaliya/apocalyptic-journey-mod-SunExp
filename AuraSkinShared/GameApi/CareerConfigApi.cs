using System;
using System.Collections.Generic;
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
        career = null;
        foreach (var candidate in CandidateIds(careerId))
        {
            if (!Exists(candidate))
            {
                continue;
            }

            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Career, candidate);
            var materialized = handle == null
                ? AuraGameDataHostMutationResult.Fail("resolve", "Career definition was not found.")
                : AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest { Definition = handle });
            if (materialized.Success && materialized.Instance is DataConfig resolved)
            {
                career = resolved;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(materialized.Message))
            {
                SkinLog.Warn("Career config exists but could not be created: " + candidate + ": " + materialized.Message);
            }
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

    private static bool Exists(string careerId)
    {
        if (string.IsNullOrWhiteSpace(careerId))
        {
            return false;
        }

        return AuraGameDataHostApi.Resolve(DataType.Career, careerId) != null;
    }

}
