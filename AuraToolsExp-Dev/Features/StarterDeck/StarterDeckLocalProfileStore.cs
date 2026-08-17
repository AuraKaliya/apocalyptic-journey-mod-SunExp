using System;
using System.Collections.Generic;
using System.Linq;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Data.Save;
using StarterDeckArbiter.Shared;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class StarterDeckLocalProfileStore
{
    internal static string LocalGlobalProfileId()
    {
        return StarterDeckProfile.QualifyProfileId(AuraToolsIds.ModId, "local.global");
    }

    internal static string LocalRoleProfileId(string roleId)
    {
        return StarterDeckProfile.QualifyProfileId(AuraToolsIds.ModId, "local.role." + RoleCatalog.NormalizeRoleId(roleId));
    }

    internal static bool IsLocalRoleProfileId(string roleId, string profileId)
    {
        return string.Equals(profileId, LocalRoleProfileId(roleId), StringComparison.OrdinalIgnoreCase)
               || string.Equals(profileId, "local.role." + RoleCatalog.NormalizeRoleId(roleId), StringComparison.OrdinalIgnoreCase);
    }

    internal static StarterDeckLocalProfileSettings EnsureRoleProfileSettings(string roleId, string displayName = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        if (!settings.Roles.TryGetValue(normalizedRole, out var roleSettings))
        {
            roleSettings = new StarterDeckLocalProfileSettings
            {
                Enabled = true,
                RoleId = normalizedRole,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName,
                DeckSize = settings.GlobalProfile.DeckSize
            };
            settings.Roles[normalizedRole] = roleSettings;
        }

        roleSettings.Normalize(normalizedRole, string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName);
        return roleSettings;
    }

    internal static void DeleteRoleProfileSettings(string roleId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        settings.Roles.Remove(normalizedRole);
        if (settings.SelectedProfileByRole.TryGetValue(normalizedRole, out var selected)
            && IsLocalRoleProfileId(normalizedRole, selected))
        {
            settings.SelectedProfileByRole.Remove(normalizedRole);
        }

        AuraToolsConfigService.SaveStarterDeck();
    }

    internal static void SelectProfileForRole(string roleId, string profileId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole) || string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        AuraToolsConfigService.MatchExperience.StarterDeck.SelectedProfileByRole[normalizedRole] = profileId.Trim();
        AuraToolsConfigService.SaveStarterDeck();
    }

    internal static void ClearSelectedProfileForRole(string roleId)
    {
        AuraToolsConfigService.MatchExperience.StarterDeck.SelectedProfileByRole.Remove(RoleCatalog.NormalizeRoleId(roleId));
        AuraToolsConfigService.SaveStarterDeck();
    }
    internal static string ConfiguredSelectedProfileIdForRole(string roleId)
    {
        return AuraToolsConfigService.MatchExperience.StarterDeck.SelectedProfileByRole.TryGetValue(RoleCatalog.NormalizeRoleId(roleId), out var selected)
            ? selected
            : "";
    }
}
