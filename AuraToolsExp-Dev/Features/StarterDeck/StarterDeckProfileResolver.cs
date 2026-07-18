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

internal static class StarterDeckProfileResolver
{
    private const string Mode = "AuraTools.WorldSimulation";

    internal static StarterDeckResolvedProfile? ResolveEffectiveProfileForPreview(string roleId)
    {
        return ResolveEffectiveProfile(RoleCatalog.NormalizeRoleId(roleId));
    }

    internal static bool IsGlobalModeEnabled()
    {
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        settings.Normalize();
        return settings.Mode == StarterDeckModes.Global;
    }

    internal static string ConfiguredSelectedProfileIdForRole(string roleId)
    {
        return StarterDeckLocalProfileStore.ConfiguredSelectedProfileIdForRole(roleId);
    }

    internal static bool ProfileMatchesId(StarterDeckProfile profile, string profileId)
    {
        return IsSelectedProfile(profile, profileId);
    }

    internal static IReadOnlyList<StarterDeckProfile> BuildCandidateProfilesForRole(string roleId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        settings.Normalize();
        var registered = StarterDeckArbiterRuntime.GetRegisteredProfiles(AuraToolsIds.ModId);
        var roleOwner = ResolveRoleOwnerModId(normalizedRole, registered);
        var context = CreateResolutionContext(normalizedRole, roleOwner);
        var policy = CreateResolutionPolicy(settings);

        var profiles = registered
            .Where(profile => StarterDeckArbiterRuntime.IsProfileEligible(profile, context))
            .Select(profile => profile.Clone())
            .ToList();

        profiles.Add(CreateGlobalLocalProfile(settings.GlobalProfile));
        if (settings.Roles.TryGetValue(normalizedRole, out var roleSettings))
        {
            profiles.Add(CreateRoleLocalProfile(normalizedRole, roleSettings));
        }

        return StarterDeckArbiterRuntime.SortCandidateProfiles(profiles, context, policy);
    }

    internal static List<string> BuildDeckFromProfile(StarterDeckProfile profile)
    {
        IEnumerable<string> fallbackCardIds = profile.CandidatePackIds.Count > 0
            ? StarterDeckCardCatalog.BuildCandidateCardIds(profile.CandidatePackIds)
            : Array.Empty<string>();
        return StarterDeckDeckBuilder.Build(
            profile.CardIds,
            profile.DeckSize,
            StarterDeckCardCatalog.IsValidCard,
            StarterDeckCardCatalog.IsStarterDeckExcludedCard,
            fallbackCardIds);
    }

    internal static StarterDeckResolvedProfile? ResolveEffectiveProfile(string roleId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        settings.Normalize();
        if (settings.Mode == StarterDeckModes.Global)
        {
            var globalProfile = CreateGlobalLocalProfile(settings.GlobalProfile);
            return IsResolvable(globalProfile)
                ? new StarterDeckResolvedProfile(globalProfile, StarterDeckProfileResolutionReasons.LocalGlobal)
                : null;
        }

        var profiles = BuildCandidateProfilesForRole(normalizedRole);
        var registered = profiles.Where(profile => profile.SourceKind == StarterDeckProfileSourceKind.Registered).ToList();
        var roleOwner = ResolveRoleOwnerModId(normalizedRole, registered);
        var context = CreateResolutionContext(normalizedRole, roleOwner);
        var selected = profiles.FirstOrDefault(profile => IsSelectedProfile(profile, context.SelectedProfileId) && IsResolvable(profile));
        if (selected != null)
        {
            return new StarterDeckResolvedProfile(selected, StarterDeckProfileResolutionReasons.Selected);
        }

        if (settings.Mode == StarterDeckModes.RoleSpecific)
        {
            var localRole = profiles.FirstOrDefault(profile =>
                StarterDeckArbiterRuntime.IsLocalRoleProfile(profile, normalizedRole)
                && IsResolvable(profile));
            if (localRole != null)
            {
                return new StarterDeckResolvedProfile(localRole, StarterDeckProfileResolutionReasons.LocalRole);
            }
        }

        var result = StarterDeckArbiterRuntime.ResolveEffectiveProfile(
            profiles,
            context,
            CreateResolutionPolicy(settings),
            IsResolvable);
        return result.Profile == null ? null : new StarterDeckResolvedProfile(result.Profile, result.Reason);
    }

    private static bool IsResolvable(StarterDeckProfile profile)
    {
        return BuildDeckFromProfile(profile).Count == profile.DeckSize;
    }

    private static bool IsSelectedProfile(StarterDeckProfile profile, string selectedProfileId)
    {
        return !string.IsNullOrWhiteSpace(selectedProfileId)
               && (string.Equals(selectedProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(selectedProfileId, profile.QualifiedProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private static string SelectedProfileId(string roleId)
    {
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        settings.Normalize();
        return settings.Mode == StarterDeckModes.RoleSpecific ? StarterDeckLocalProfileStore.ConfiguredSelectedProfileIdForRole(roleId) : "";
    }

    private static string ConfiguredSelectedProfileId(string roleId)
    {
        return AuraToolsConfigService.MatchExperience.StarterDeck.SelectedProfileByRole.TryGetValue(RoleCatalog.NormalizeRoleId(roleId), out var selected)
            ? selected
            : "";
    }

    private static StarterDeckProfileContext CreateResolutionContext(string roleId, string roleOwner)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        return new StarterDeckProfileContext
        {
            ModeId = Mode,
            RoleId = normalizedRole,
            RoleOwnerModId = roleOwner,
            SelectedProfileId = SelectedProfileId(normalizedRole)
        };
    }

    private static StarterDeckProfileResolutionPolicy CreateResolutionPolicy(StarterDeckSettings settings)
    {
        return new StarterDeckProfileResolutionPolicy
        {
            PreferRoleModProfile = settings.PreferRoleModProfile,
            UseRoleSpecificLocalProfiles = settings.Mode == StarterDeckModes.RoleSpecific,
            AllowGlobalLocalProfileFallback = true,
            IncludeNonOwnerRegisteredFallback = false,
            RequireCompleteProfile = true
        };
    }

    private static StarterDeckProfile CreateGlobalLocalProfile(StarterDeckLocalProfileSettings settings)
    {
        var profile = new StarterDeckProfile
        {
            ProfileId = "local.global",
            OwnerModId = AuraToolsIds.ModId,
            DisplayName = string.IsNullOrWhiteSpace(settings.DisplayName) ? "全局自定义卡组" : settings.DisplayName,
            ModeIds = new List<string> { Mode },
            DeckSize = settings.DeckSize,
            CardIds = settings.CardIds.ToList(),
            SourceKind = StarterDeckProfileSourceKind.Local,
            Editable = true,
            Deletable = false,
            Enabled = settings.Enabled,
            Priority = -1000,
            DerivedFromProfileId = settings.DerivedFromProfileId
        };
        profile.Normalize(AuraToolsIds.ModId);
        return profile;
    }

    private static StarterDeckProfile CreateRoleLocalProfile(string roleId, StarterDeckLocalProfileSettings settings)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var profile = new StarterDeckProfile
        {
            ProfileId = "local.role." + normalizedRole,
            OwnerModId = AuraToolsIds.ModId,
            DisplayName = string.IsNullOrWhiteSpace(settings.DisplayName) ? RoleCatalog.GetDisplayName(normalizedRole) + " 自定义卡组" : settings.DisplayName,
            ModeIds = new List<string> { Mode },
            TargetRoleIds = new List<string> { normalizedRole },
            DeckSize = settings.DeckSize,
            CardIds = settings.CardIds.ToList(),
            SourceKind = StarterDeckProfileSourceKind.Local,
            Editable = true,
            Deletable = true,
            Enabled = settings.Enabled,
            Priority = -500,
            DerivedFromProfileId = settings.DerivedFromProfileId
        };
        profile.Normalize(AuraToolsIds.ModId);
        return profile;
    }

    private static string ResolveRoleOwnerModId(string roleId, IEnumerable<StarterDeckProfile> registeredProfiles)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var owner = StarterDeckArbiterRuntime.InferOwnerModId(
            normalizedRole,
            registeredProfiles.Select(profile => profile.OwnerModId));
        if (!string.IsNullOrWhiteSpace(owner))
        {
            return owner;
        }

        try
        {
            var role = RoleCatalog.GetRoles()
                .FirstOrDefault(item => string.Equals(item.Id, normalizedRole, StringComparison.OrdinalIgnoreCase));
            owner = OwnerFromResourcePath(role?.Icon) ?? OwnerFromResourcePath(role?.PackBelong) ?? "";
        }
        catch
        {
            owner = "";
        }

        return owner;
    }

    private static string? OwnerFromResourcePath(string? value)
    {
        var text = (value ?? "").Trim().Replace('\\', '/');
        const string prefix = "Mods/";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = text.Substring(prefix.Length);
        var slash = rest.IndexOf('/');
        return slash > 0 ? rest.Substring(0, slash) : rest;
    }

}
