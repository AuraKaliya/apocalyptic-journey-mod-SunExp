using System;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckLocalProfileStore
{
    internal static StarterDeckLocalProfileSettings EnsureRoleSettings(string roleId, string displayName = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        if (!settings.Roles.TryGetValue(normalizedRole, out var roleSettings))
        {
            roleSettings = StarterDeckLocalProfileSettings.CreateRole(
                normalizedRole,
                string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName);
            settings.Roles[normalizedRole] = roleSettings;
        }

        roleSettings.Normalize(
            normalizedRole,
            string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName);
        return roleSettings;
    }

    internal static void DeleteRoleSettings(string roleId)
    {
        AuraToolsConfigService.MatchExperience.StarterDeck.Roles.Remove(RoleCatalog.NormalizeRoleId(roleId));
        AuraToolsConfigService.SaveStarterDeck();
    }

    internal static void RestoreCardsFromGlobal(string roleId)
    {
        var role = EnsureRoleSettings(roleId, RoleCatalog.GetDisplayName(roleId));
        role.InheritCards = true;
        role.CardIds.Clear();
        SaveOrPrune(roleId, role);
    }

    internal static void RestoreRelicsFromGlobal(string roleId)
    {
        var role = EnsureRoleSettings(roleId, RoleCatalog.GetDisplayName(roleId));
        role.InheritRelics = true;
        role.RelicIds.Clear();
        SaveOrPrune(roleId, role);
    }

    private static void SaveOrPrune(string roleId, StarterDeckLocalProfileSettings role)
    {
        if (role.InheritCards && role.InheritRelics)
        {
            AuraToolsConfigService.MatchExperience.StarterDeck.Roles.Remove(RoleCatalog.NormalizeRoleId(roleId));
        }

        AuraToolsConfigService.SaveStarterDeck();
    }
}
