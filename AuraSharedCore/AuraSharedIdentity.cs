using System;
using System.Linq;

namespace AuraShared.Core;

public static class AuraSharedIdentity
{
    public const string OfficialCareerPrefix = "career_";

    public static string NormalizeCareerId(string? careerId)
    {
        return NormalizeRoleId(careerId);
    }

    public static string NormalizeRoleId(string? roleId)
    {
        var value = (roleId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "*", StringComparison.Ordinal))
        {
            return value;
        }

        value = value.TrimStart('*');
        if (value.StartsWith(OfficialCareerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = value.Substring(OfficialCareerPrefix.Length);
            return IsUnsignedNumber(suffix) ? OfficialCareerPrefix + suffix : value;
        }

        return IsUnsignedNumber(value) ? OfficialCareerPrefix + value : value;
    }

    public static string SafeId(string? value, string fallback = "unknown")
    {
        return AuraSharedPaths.SafeSegment(value ?? "", fallback);
    }

    public static bool IsUnsignedNumber(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.All(char.IsDigit);
    }
}
