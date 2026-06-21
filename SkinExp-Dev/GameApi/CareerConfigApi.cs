using System;
using System.Collections.Generic;
using SkinExp.Dll.Infrastructure;

namespace SkinExp.Dll.GameApi;

public static class CareerConfigApi
{
    private const string OfficialCareerPrefix = "career_";

    public static string NormalizeId(string? careerId)
    {
        var value = (careerId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return IsUnsignedNumber(value) ? OfficialCareerPrefix + value : value;
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

            try
            {
                career = new DataConfig(candidate, DataType.Career);
                return true;
            }
            catch (Exception ex)
            {
                SkinLog.Warn("Career config exists but could not be created: " + candidate + ": " + ex.Message);
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

        try
        {
            return Singleton<GameConfigManager>.Instance?.GetOne(DataType.Career, careerId) != null;
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Career config lookup failed: " + careerId + ": " + ex.Message);
            return false;
        }
    }

    private static bool IsUnsignedNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
