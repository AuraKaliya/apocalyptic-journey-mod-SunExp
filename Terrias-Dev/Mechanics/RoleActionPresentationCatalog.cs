using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public enum RoleActionTargetMode
{
    Default,
    AllOpponents,
    SelfOnly
}

public static class RoleActionPresentationCatalog
{
    private static readonly HashSet<string> WunaAllOpponentCards = new(StringComparer.OrdinalIgnoreCase)
    {
        "blazing_crown_collapse",
        "crown_radiance",
        "canopy_return",
        "solar_ignition",
        "flamewheel_recurrence"
    };

    public static bool SupportsRole(string currentRoleId, string ownerRoleId)
    {
        return IsWunaRole(currentRoleId)
            || IsWunaRole(ownerRoleId)
            || IsColumbinaRole(currentRoleId)
            || IsColumbinaRole(ownerRoleId);
    }

    public static bool UsesWunaEffectNormalization(string currentRoleId, string ownerRoleId)
    {
        return IsWunaRole(currentRoleId) || IsWunaRole(ownerRoleId);
    }

    public static RoleActionTargetMode TargetMode(string cardId)
    {
        var normalized = NormalizeContentId(cardId);
        if (normalized.EndsWith("columbina_homesickness", StringComparison.OrdinalIgnoreCase))
        {
            return RoleActionTargetMode.AllOpponents;
        }

        if (normalized.EndsWith("columbina_eternal_tide", StringComparison.OrdinalIgnoreCase))
        {
            return RoleActionTargetMode.SelfOnly;
        }

        return WunaAllOpponentCards.Contains(normalized)
            ? RoleActionTargetMode.AllOpponents
            : RoleActionTargetMode.Default;
    }

    public static bool IsWunaRole(string roleId)
    {
        var normalized = NormalizeRoleId(roleId);
        return string.Equals(normalized, "wuna", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Terrias_wuna_wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(":wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".wuna", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsColumbinaRole(string roleId)
    {
        var normalized = NormalizeRoleId(roleId);
        return string.Equals(normalized, "columbina", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Terrias_columbina_columbina", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_columbina", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(":columbina", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".columbina", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoleId(string value)
    {
        return AuraShared.Core.AuraSharedIdentity.NormalizeRoleId(value).TrimStart('*').Trim();
    }

    private static string NormalizeContentId(string value)
    {
        var normalized = (value ?? "").Trim().TrimStart('*');
        const string fullPrefix = "Terrias_terrias_";
        if (normalized.StartsWith(fullPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(fullPrefix.Length);
        }

        return normalized.TrimStart('*');
    }
}
