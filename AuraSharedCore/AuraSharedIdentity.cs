using System;
using System.IO;
using System.Linq;

namespace AuraShared.Core;

public static class AuraSharedIdentity
{
    public const string OfficialCareerPrefix = "career_";
    private const int RuntimeNumericIdLength = 8;

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

    public static string SelectRoleId(params string?[] candidates)
    {
        foreach (var candidate in candidates ?? Array.Empty<string?>())
        {
            var value = (candidate ?? "").Trim();
            if (IsUsableRoleId(value))
            {
                return NormalizeRoleId(value);
            }
        }

        return "";
    }

    public static bool IsUsableRoleId(string? roleId)
    {
        var value = (roleId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.TrimStart('*');
        return !IsRuntimeNumericId(value);
    }

    public static bool IsRuntimeNumericId(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Length >= RuntimeNumericIdLength && IsUnsignedNumber(text);
    }

    public static string SafeId(string? value, string fallback = "unknown")
    {
        fallback = string.IsNullOrWhiteSpace(fallback) ? "unknown" : fallback.Trim();
        var candidate = value == null || string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
    }

    public static bool IsUnsignedNumber(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.All(char.IsDigit);
    }
}
