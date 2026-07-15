using System;
using System.Collections.Generic;
using System.Linq;
using AuraJourney.Shared;
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

public static class AuraToolsStarterDeckRuntime
{
    private const string AppliedKey = "AuraTools.StarterDeckApplied";
    private const string AppliedRoleKey = AppliedKey + ".Role";
    private const string AppliedProfileKey = AppliedKey + ".Profile";
    private const string AppliedRoleSourceKey = AppliedKey + ".RoleSource";
    private const string AppliedRoleTableRoleKey = AppliedKey + ".RoleTableRole";
    private const string AppliedSelectedRoleKey = AppliedKey + ".SelectedRole";
    private const string Owner = "AuraTools.StarterDeck";
    private const string Scope = "AuraTools.WorldSimulation";
    private const string Mode = "AuraTools.WorldSimulation";
    private const string LegacyMode = "aura-world-simulation";
    public const float CardInfoHeaderHeight = 40f;
    public const float CardImageColumnWidth = 44f;
    public const float CardIconSize = 34f;
    public const float CardRarityColumnWidth = 70f;
    public const float CardCostColumnWidth = 56f;
    public const float CardActionColumnWidth = 84f;
    private static readonly Dictionary<string, Sprite?> cardIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object cardCatalogSync = new();
    private static StarterDeckCardCatalogSnapshot? cardCatalogSnapshot;
    private static int lastForeignRoleTableSkipLogFrame = -100000;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "GameEntryUI.Init", _ =>
        {
            InvalidateStarterDeckCardCatalog("GameEntryUI.Init");
            WarmStarterDeckCardCatalog("GameEntryUI.Init");
        });
        RegisterAfter(modConfig, "GameEntryUI.ShowCareer", _ => WarmStarterDeckCardCatalog("GameEntryUI.ShowCareer"));
        RegisterBefore(modConfig, "GameEntryUI.StartGame", ApplyStarterDeckBeforeGameStart);
        // Clients submit their own RoleTable through this native command after the
        // server asks for role tables.  The Rpc wrapper is not the client-side
        // serialization point, so applying there leaves non-host decks unchanged.
        RegisterBefore(modConfig, "PlayerManager.CmdSyncRoleTable", ApplyStarterDeckBeforeRoleSubmit);
    }

    public static List<string> BuildAllCandidateCardIds()
    {
        return GetCardCatalogSnapshot("all-candidates").SelectableCardIds.ToList();
    }

    public static List<string> BuildCandidateCardIds(IEnumerable<string> packIds)
    {
        var requestedPacks = new HashSet<string>(
            (packIds ?? Array.Empty<string>())
            .Where(id => string.Equals(id, StarterDeckCardPackGroup.OtherGroupId, StringComparison.OrdinalIgnoreCase)
                         || IsValidPackForCurrentLobby(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => id.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (requestedPacks.Count == 0)
        {
            return new List<string>();
        }

        return GetCardCatalogSnapshot("pack-candidates")
            .SelectableGroups
            .Where(group => requestedPacks.Contains(group.PackId))
            .SelectMany(group => group.CardIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    public static List<StarterDeckCardPackGroup> BuildCandidateCardPackGroups()
    {
        return GetCardCatalogSnapshot("pack-groups").CloneSelectableGroups();
    }

    public static List<string> BuildRegisteredCardIds(bool includeSystemSkillCards = false)
    {
        return GetCardCatalogSnapshot("registered-cards")
            .AllCards
            .Where(card => !card.IsExcludedDerivedCard)
            .Where(card => includeSystemSkillCards || !card.IsSystemSkillCard)
            .Select(card => card.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    public static List<string> BuildRegisteredExplicitCardIds(bool includeSystemSkillCards = false)
    {
        return GetCardCatalogSnapshot("explicit-cards")
            .AllCards
            .Where(card => !card.IsHidden)
            .Where(card => !card.IsExcludedDerivedCard)
            .Where(card => includeSystemSkillCards || !card.IsSystemSkillCard)
            .Select(card => card.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    public static List<string> BuildRegisteredHiddenCardIds(bool includeSystemSkillCards = false)
    {
        return GetCardCatalogSnapshot("hidden-cards")
            .AllCards
            .Where(card => card.IsHidden)
            .Where(card => !card.IsExcludedDerivedCard)
            .Where(card => includeSystemSkillCards || !card.IsSystemSkillCard)
            .Select(card => card.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    public static List<string> BuildRegisteredSkillCardIds()
    {
        return GetCardCatalogSnapshot("skill-cards").SkillCardIds.ToList();
    }

    public static List<string> BuildRegisteredSystemSkillCardIds()
    {
        return GetCardCatalogSnapshot("system-skill-cards").SystemSkillCardIds.ToList();
    }

    internal static void WarmStarterDeckCardCatalog(string source)
    {
        _ = GetCardCatalogSnapshot(source);
    }

    internal static void InvalidateStarterDeckCardCatalog(string source)
    {
        lock (cardCatalogSync)
        {
            cardCatalogSnapshot = null;
            cardIconCache.Clear();
        }

        AuraToolsLog.Info("[StarterDeck] invalidated card catalog from " + source);
    }

    private static void ApplyStarterDeckBeforeGameStart(ModHookContext context)
    {
        try
        {
            ApplyStarterDeck(RoleTable.Instance, context, "GameEntryUI.StartGame");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[StarterDeck] failed to reconcile preset before start", ex);
        }
    }

    private static void ApplyStarterDeckBeforeRoleSubmit(ModHookContext context)
    {
        try
        {
            var roleTable = context.Arguments?.OfType<RoleTable>().FirstOrDefault() ?? RoleTable.Instance;
            ApplyStarterDeck(roleTable, context, "PlayerManager.CmdSyncRoleTable");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[StarterDeck] failed to reconcile preset before local role submission", ex);
        }
    }

    private static void ApplyStarterDeck(
        RoleTable? roleTable,
        ModHookContext context,
        string source)
    {
        if (!AuraToolsConfigService.Root.MatchExperience.Enabled
            || !AuraToolsConfigService.MatchExperience.StarterDeck.Enabled
            || roleTable == null)
        {
            return;
        }

        if (!IsWorldSimulationRun())
        {
            AuraToolsLog.Info("[StarterDeck] skipped: not a confirmed world-simulation run. source=" + source + ".");
            return;
        }

        if (!IsLocalPlayerRoleTable(roleTable, source))
        {
            return;
        }

        if (ShouldSkipForExternalOwner(roleTable))
        {
            return;
        }

        var role = ResolveRuntimeRole(roleTable);
        if (string.IsNullOrWhiteSpace(role.RoleId))
        {
            AuraToolsLog.Warn("[StarterDeck] skipped: local role table has no career. source="
                              + source
                              + ", roleTable="
                              + ReadRoleTableId(roleTable)
                              + ".");
            return;
        }

        if (IsApplied(roleTable, role))
        {
            return;
        }

        var selection = ResolveEffectiveProfile(role.RoleId);
        if (selection == null)
        {
            AuraToolsLog.Warn("[StarterDeck] skipped: no complete profile for role=" + role.RoleId + ".");
            return;
        }

        var deck = BuildDeckFromProfile(selection.Profile);
        if (deck.Count != selection.Profile.DeckSize)
        {
            AuraToolsLog.Warn("[StarterDeck] skipped: profile is incomplete. profile="
                              + selection.Profile.QualifiedProfileId
                              + ", role=" + role.RoleId
                              + ", deck=" + deck.Count + "/" + selection.Profile.DeckSize);
            return;
        }

        var originalDeckCount = roleTable.cardList.Count;
        if (!StarterDeckArbiterRuntime.ApplyDeck(roleTable, deck, CreateClaim(selection.Profile), sync: false))
        {
            return;
        }

        WriteAppliedRoleMetadata(roleTable, role, selection.Profile);

        AuraToolsLog.Info("[StarterDeck] applied local role-table profile; role="
                          + role.RoleId
                          + ", roleSource=" + role.Source
                          + ", roleTableRole=" + role.RoleTableRoleId
                          + ", selectedRole=" + role.SelectedRoleId
                          + ", profile=" + selection.Profile.QualifiedProfileId
                          + ", reason=" + selection.Reason
                          + ", originalDeck="
                          + originalDeckCount
                          + ", deck=" + roleTable.cardList.Count
                          + ", cards=" + string.Join("|", deck));
    }

    private static bool IsWorldSimulationRun()
    {
        var modeType = ReadLobbyModeType();
        if (!string.IsNullOrWhiteSpace(modeType))
        {
            return string.Equals(modeType, "Normal", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            if (IsNormalMapManager(MapManager.Instance?.ModeMapManager))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsLocalPlayerRoleTable(RoleTable roleTable, string source)
    {
        try
        {
            var playerManager = PlayerManager.Instance;
            if (playerManager == null)
            {
                return true;
            }

            var localPlayerId = (playerManager.PlayerId ?? "").Trim();
            var roleTableId = ReadRoleTableId(roleTable);
            if (string.IsNullOrWhiteSpace(localPlayerId) || string.IsNullOrWhiteSpace(roleTableId))
            {
                return true;
            }

            if (string.Equals(localPlayerId, roleTableId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            LogForeignRoleTableSkipped(source, localPlayerId, roleTableId);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static void LogForeignRoleTableSkipped(string source, string localPlayerId, string roleTableId)
    {
        var frame = SafeFrameCount();
        if (frame - lastForeignRoleTableSkipLogFrame < 300)
        {
            return;
        }

        lastForeignRoleTableSkipLogFrame = frame;
        AuraToolsLog.Info("[StarterDeck] skipped: role table belongs to another player; local="
                          + localPlayerId
                          + ", roleTable="
                          + roleTableId
                          + ", source="
                          + source + ".");
    }

    private static int SafeFrameCount()
    {
        try
        {
            return Time.frameCount;
        }
        catch
        {
            return int.MaxValue;
        }
    }

    private static bool IsNormalMapManager(object? value)
    {
        return string.Equals(value?.GetType().Name, "NormalMapManager", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRoleTableId(RoleTable roleTable)
    {
        return (ReflectionUtil.ReadString(roleTable, "Id", "id") ?? "").Trim();
    }

    private static bool ShouldSkipForExternalOwner(RoleTable roleTable)
    {
        if (IsSunExpSolarMemoryRun())
        {
            AuraToolsLog.Info("[StarterDeck] skipped: SunExp Solar Memory owns this run.");
            return true;
        }

        if (roleTable.SpecialVarMap == null)
        {
            return false;
        }

        if (StarterDeckArbiterRuntime.IsOwnedByOther(roleTable, Owner, out var owner))
        {
            if (IsAuraToolsApplied(roleTable))
            {
                return false;
            }

            AuraToolsLog.Info("[StarterDeck] skipped: starter deck owner=" + owner + ".");
            return true;
        }

        if (roleTable.SpecialVarMap.TryGetValue(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey + ".Mode", out var legacyMode)
            && string.Equals(legacyMode, "sunexp-solar-memory", StringComparison.OrdinalIgnoreCase))
        {
            AuraToolsLog.Info("[StarterDeck] skipped: CardPackExp compatibility owner is SunExp Solar Memory.");
            return true;
        }

        return false;
    }

    private static bool IsSunExpSolarMemoryRun()
    {
        try
        {
            return AuraJourneyRuntime.IsJourneyActive("AuraTools", "SunExp", "SunExp.SolarMemory");
        }
        catch
        {
            return false;
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, message => AuraToolsLog.Info(message), AuraToolsLog.Warn);
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, message => AuraToolsLog.Info(message), AuraToolsLog.Warn);
    }

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
        return ConfiguredSelectedProfileId(roleId);
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
        var deck = profile.CardIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(IsValidCard)
            .Where(id => !IsStarterDeckExcludedCard(id))
            .Take(profile.DeckSize)
            .ToList();

        if (deck.Count < profile.DeckSize && profile.CandidatePackIds.Count > 0)
        {
            foreach (var cardId in BuildCandidateCardIds(profile.CandidatePackIds))
            {
                if (deck.Count >= profile.DeckSize)
                {
                    break;
                }

                deck.Add(cardId);
            }
        }

        return deck.Take(profile.DeckSize).ToList();
    }

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

        AuraToolsConfigService.SaveMatchExperience();
    }

    internal static void SelectProfileForRole(string roleId, string profileId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole) || string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        AuraToolsConfigService.MatchExperience.StarterDeck.SelectedProfileByRole[normalizedRole] = profileId.Trim();
        AuraToolsConfigService.SaveMatchExperience();
    }

    internal static void ClearSelectedProfileForRole(string roleId)
    {
        AuraToolsConfigService.MatchExperience.StarterDeck.SelectedProfileByRole.Remove(RoleCatalog.NormalizeRoleId(roleId));
        AuraToolsConfigService.SaveMatchExperience();
    }

    private static StarterDeckResolvedProfile? ResolveEffectiveProfile(string roleId)
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
        return settings.Mode == StarterDeckModes.RoleSpecific ? ConfiguredSelectedProfileId(roleId) : "";
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
            registeredProfiles.Select(profile => profile.OwnerModId).Concat(new[] { "SunExp", "SanGuoShaExp" }));
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

    private static StarterDeckRuntimeRole ResolveRuntimeRole(RoleTable roleTable)
    {
        var roleTableRole = RoleCatalog.NormalizeRoleId(ReadDataId(roleTable.Career));
        return new StarterDeckRuntimeRole(
            roleTableRole,
            roleTableRole,
            "",
            string.IsNullOrWhiteSpace(roleTableRole) ? "missing-role-table-career" : "RoleTable.Career");
    }

    private static string ReadLobbyModeType()
    {
        try
        {
            return LobbyManager.Instance?.CurrentLobbyModeType ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadDataId(IDataConfig? dataConfig)
    {
        try
        {
            if (dataConfig?.data != null && dataConfig.data.TryGetValue("Id", out var id))
            {
                return id ?? "";
            }

            return dataConfig?.InstanceID ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsApplied(RoleTable roleTable, StarterDeckRuntimeRole role)
    {
        if (!IsAuraToolsApplied(roleTable))
        {
            return false;
        }

        var appliedRole = ReadSpecialVar(roleTable, AppliedRoleKey);
        if (string.IsNullOrWhiteSpace(appliedRole))
        {
            if (role.HasSelectedRoleConflict)
            {
                AuraToolsLog.Info("[StarterDeck] correcting legacy starter deck without role marker; roleTableRole="
                                  + role.RoleTableRoleId
                                  + ", selectedRole=" + role.SelectedRoleId
                                  + ".");
                return false;
            }

            return true;
        }

        if (string.Equals(RoleCatalog.NormalizeRoleId(appliedRole), role.RoleId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        AuraToolsLog.Info("[StarterDeck] correcting stale starter deck; appliedRole="
                          + appliedRole
                          + ", resolvedRole=" + role.RoleId
                          + ", roleSource=" + role.Source + ".");
        return false;
    }

    private static bool IsAuraToolsApplied(RoleTable roleTable)
    {
        if (StarterDeckArbiterRuntime.HasApplied(roleTable, AppliedKey, Owner))
        {
            return true;
        }

        return roleTable.SpecialVarMap != null
               && roleTable.SpecialVarMap.TryGetValue(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey, out var oldValue)
               && oldValue == "1"
               && roleTable.SpecialVarMap.TryGetValue(StarterDeckArbiterRuntime.LegacyCardPackAppliedKey + ".Mode", out var legacyMode)
               && legacyMode.StartsWith("aura-", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSpecialVar(RoleTable roleTable, string key)
    {
        return roleTable.SpecialVarMap != null && roleTable.SpecialVarMap.TryGetValue(key, out var value)
            ? value ?? ""
            : "";
    }

    private static void WriteAppliedRoleMetadata(RoleTable roleTable, StarterDeckRuntimeRole role, StarterDeckProfile profile)
    {
        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        roleTable.SpecialVarMap[AppliedRoleKey] = role.RoleId;
        roleTable.SpecialVarMap[AppliedProfileKey] = profile.QualifiedProfileId;
        roleTable.SpecialVarMap[AppliedRoleSourceKey] = role.Source;
        roleTable.SpecialVarMap[AppliedRoleTableRoleKey] = role.RoleTableRoleId;
        roleTable.SpecialVarMap[AppliedSelectedRoleKey] = role.SelectedRoleId;
    }

    private static StarterDeckClaim CreateClaim(StarterDeckProfile profile)
    {
        var registered = profile.SourceKind == StarterDeckProfileSourceKind.Registered;
        return new StarterDeckClaim
        {
            // The profile remains owned by its registering content mod.  This
            // claim records the AuraTools effective overlay that applies it.
            Owner = Owner,
            Scope = Scope,
            ModeId = Mode,
            Source = (registered ? "registered:" : "local:") + profile.QualifiedProfileId,
            State = StarterDeckArbiterRuntime.StateApplied,
            AppliedKey = AppliedKey,
            AppliedModeKey = AppliedKey + ".Mode",
            AppliedMode = LegacyMode,
            LegacyMode = LegacyMode,
            DeckSize = profile.DeckSize,
            SourceName = "AuraTools.WorldSimulation.StarterDeck"
        };
    }

    private static StarterDeckCardCatalogSnapshot GetCardCatalogSnapshot(string source)
    {
        lock (cardCatalogSync)
        {
            if (cardCatalogSnapshot != null)
            {
                return cardCatalogSnapshot;
            }

            cardCatalogSnapshot = BuildCardCatalogSnapshot(source);
            return cardCatalogSnapshot;
        }
    }

    private static StarterDeckCardCatalogSnapshot BuildCardCatalogSnapshot(string source)
    {
        try
        {
            var gameConfig = Singleton<GameConfigManager>.Instance;
            var packDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var existingPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectablePacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in gameConfig.GetTable(DataType.CardPack).Getlines())
            {
                if (!row.TryGetValue("Id", out var packId) || string.IsNullOrWhiteSpace(packId) || !IsValidPackForCurrentLobby(packId))
                {
                    continue;
                }

                packId = packId.Trim();
                existingPacks.Add(packId);
                packDisplayNames[packId] = RowDisplayName(row, packId);
                if (!IsRuntimeLocked(packId))
                {
                    selectablePacks.Add(packId);
                }
            }

            var groupCards = selectablePacks
                .ToDictionary(packId => packId, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            var allCards = new List<StarterDeckCardCatalogEntry>();
            var hiddenCards = new List<string>();
            var skillCards = new List<string>();
            var systemSkillCards = new List<string>();
            var excludedDerivedCards = new List<string>();
            var otherCards = new List<string>();
            var careerSkillCardIds = StarterDeckCardClassification.BuildCareerSkillCardIds(
                gameConfig.GetTable(DataType.Career).Getlines());

            foreach (var row in gameConfig.GetTable(DataType.Card).Getlines())
            {
                if (!row.TryGetValue("Id", out var id) || string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                id = id.Trim();
                row.TryGetValue("Action", out var action);
                row.TryGetValue("Icon", out var iconPath);
                row.TryGetValue("Rarity", out var rarity);
                row.TryGetValue("Expend", out var cost);

                var packId = StarterDeckCardClassification.ResolveEffectivePackId(row, gameConfig.GetPackBelong);
                var isHidden = IsSpecialCardId(id);
                var isSkillCard = StarterDeckCardClassification.IsCareerSkillCard(id, careerSkillCardIds);
                var isSystemSkillCard = isSkillCard;
                var isExcludedDerivedCard = StarterDeckCardClassification.IsExcludedDerivedCard(row);
                var isLocked = IsRuntimeLocked(id);
                var displayName = RowDisplayName(row, id);

                var entry = new StarterDeckCardCatalogEntry(
                    id,
                    packId,
                    displayName,
                    rarity ?? "",
                    cost ?? "",
                    iconPath ?? "",
                    action ?? "",
                    isHidden,
                    isSkillCard,
                    isSystemSkillCard,
                    isExcludedDerivedCard,
                    isLocked);
                allCards.Add(entry);

                if (isHidden)
                {
                    hiddenCards.Add(id);
                }

                if (isSkillCard)
                {
                    skillCards.Add(id);
                }

                if (isSystemSkillCard)
                {
                    systemSkillCards.Add(id);
                    continue;
                }

                if (isExcludedDerivedCard)
                {
                    excludedDerivedCards.Add(id);
                    continue;
                }

                if (isLocked)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(packId) && groupCards.TryGetValue(packId, out var group))
                {
                    group.Add(id);
                }
                else if (string.IsNullOrWhiteSpace(packId) || !existingPacks.Contains(packId))
                {
                    otherCards.Add(id);
                }
            }

            var groups = groupCards
                .Select(pair => new StarterDeckCardPackGroup(pair.Key, PackDisplayName(packDisplayNames, pair.Key), SortedDistinctCards(pair.Value)))
                .Where(group => group.CardIds.Count > 0)
                .OrderBy(group => group.DisplayName)
                .ThenBy(group => group.PackId)
                .ToList();
            var sortedOtherCards = SortedDistinctCards(otherCards);
            if (sortedOtherCards.Count > 0)
            {
                groups.Add(new StarterDeckCardPackGroup(StarterDeckCardPackGroup.OtherGroupId, "\u5176\u5b83", sortedOtherCards));
            }

            var selectableCards = groups
                .SelectMany(group => group.CardIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(CardSortKey)
                .ToList();
            var snapshot = new StarterDeckCardCatalogSnapshot(
                allCards
                    .GroupBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(card => CardSortKey(card.Id))
                    .ToList(),
                groups,
                selectableCards,
                SortedDistinctCards(hiddenCards),
                SortedDistinctCards(skillCards),
                SortedDistinctCards(systemSkillCards),
                SortedDistinctCards(excludedDerivedCards));
            AuraToolsLog.Info(
                "[StarterDeck] built card catalog from " + source
                + ": cards=" + snapshot.AllCards.Count
                + ", selectable=" + snapshot.SelectableCardIds.Count
                + ", groups=" + snapshot.SelectableGroups.Count
                + ", skills=" + snapshot.SkillCardIds.Count
                + ", systemSkills=" + snapshot.SystemSkillCardIds.Count
                + ", excludedDerived=" + snapshot.ExcludedDerivedCardIds.Count);
            return snapshot;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[StarterDeck] failed to build card catalog from " + source + ": " + ex.Message);
            return StarterDeckCardCatalogSnapshot.Empty;
        }
    }

    private static string PackDisplayName(Dictionary<string, string> packDisplayNames, string packId)
    {
        return packDisplayNames.TryGetValue(packId, out var displayName) && !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : CardPackDisplayName(packId);
    }

    private static string RowDisplayName(Dictionary<string, string> row, string fallback)
    {
        try
        {
            var localized = row.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }
        }
        catch
        {
            // Fall back to the raw row value below.
        }

        return row.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : fallback;
    }

    private static bool IsStarterDeckExcludedCard(string cardId)
    {
        return GetCardCatalogSnapshot("starter-deck-exclusion-check").IsStarterDeckExcluded(cardId);
    }

    private static bool IsRuntimeLocked(string id)
    {
        try
        {
            return Singleton<GameRuntimeData>.Instance.IsLocked(id);
        }
        catch
        {
            return false;
        }
    }

    private static List<string> BuildSelectablePacks()
    {
        try
        {
            return Singleton<GameConfigManager>.Instance.GetTable(DataType.CardPack)
                .Getlines()
                .Where(row => row.TryGetValue("Id", out var id)
                              && IsValidPackForCurrentLobby(id)
                              && !Singleton<GameRuntimeData>.Instance.IsLocked(id))
                .Select(row => row["Id"])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id)
                .ToList();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[StarterDeck] failed to list card packs: " + ex.Message);
            return new List<string>();
        }
    }

    private static string CardPackDisplayName(string packId)
    {
        try
        {
            var data = new DataConfig(packId, DataType.CardPack).data;
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }

            return data.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : packId;
        }
        catch
        {
            return packId;
        }
    }

    private static bool IsExistingCardPack(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId) || !IsValidPackForCurrentLobby(packId))
        {
            return false;
        }

        try
        {
            return new DataConfig(packId, DataType.CardPack).data != null;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> SortedDistinctCards(IEnumerable<string> cardIds)
    {
        return (cardIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    private static IEnumerable<string> CardIdsFromPack(string packId)
    {
        foreach (var pair in Singleton<GameConfigManager>.Instance.GetPackItems(packId))
        {
            if (pair.Key != DataType.Card)
            {
                continue;
            }

            foreach (var card in pair.Value)
            {
                if (card.TryGetValue("Id", out var id))
                {
                    yield return id;
                }
            }
        }
    }

    private static bool IsValidPackForCurrentLobby(string id)
    {
        return !string.IsNullOrWhiteSpace(id)
               && (!string.Equals(id, "cardpack_13", StringComparison.OrdinalIgnoreCase)
                   || GameConfigManager.ShouldEnableOnlineCardPack());
    }

    private static bool IsValidCard(string cardId)
    {
        try
        {
            return new DataConfig(cardId, DataType.Card).data != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetCatalogCard(string cardId, out StarterDeckCardCatalogEntry? card)
    {
        card = null;
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        return GetCardCatalogSnapshot("card-lookup").TryGetCard(cardId, out card);
    }

    public static string CardSortKey(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var rarity = data.TryGetValue("Rarity", out var r) ? r : "9";
            var cost = data.TryGetValue("Expend", out var c) ? c : "9";
            return rarity.PadLeft(2, '0') + "|" + cost.PadLeft(2, '0') + "|" + cardId;
        }
        catch
        {
            return "99|99|" + cardId;
        }
    }

    public static string CardDisplayName(string cardId)
    {
        if (TryGetCatalogCard(cardId, out var card) && card != null)
        {
            return string.IsNullOrWhiteSpace(card.DisplayName) ? cardId : card.DisplayName;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }

            return data.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : cardId;
        }
        catch
        {
            return cardId;
        }
    }

    public static string CardDisplayNameWithSpecialMarker(string cardId)
    {
        var name = CardDisplayName(cardId);
        return IsSpecialCardId(cardId) ? "\u3010*\u3011 " + name : name;
    }

    public static bool IsSpecialCardId(string cardId)
    {
        return !string.IsNullOrWhiteSpace(cardId)
               && (cardId.StartsWith("*", StringComparison.Ordinal)
                   || cardId.IndexOf("_*", StringComparison.Ordinal) >= 0);
    }

    public static string CardShortInfo(string cardId)
    {
        if (TryGetCatalogCard(cardId, out var card) && card != null)
        {
            var rarity = string.IsNullOrWhiteSpace(card.Rarity) ? "?" : "R" + card.Rarity;
            var cost = string.IsNullOrWhiteSpace(card.Cost) ? "?" : card.Cost;
            return rarity + " / Cost" + cost + " / " + cardId;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var rarity = data.TryGetValue("Rarity", out var r) ? "R" + r : "R?";
            var cost = data.TryGetValue("Expend", out var c) ? c : "?";
            return rarity + " / 费 " + cost + " / " + cardId;
        }
        catch
        {
            return cardId;
        }
    }

    public static string CardRarity(string cardId)
    {
        if (TryGetCatalogCard(cardId, out var card) && card != null)
        {
            return string.IsNullOrWhiteSpace(card.Rarity) ? "?" : "R" + card.Rarity;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return data.TryGetValue("Rarity", out var rarity) && !string.IsNullOrWhiteSpace(rarity) ? "R" + rarity : "?";
        }
        catch
        {
            return "?";
        }
    }

    public static string CardCost(string cardId)
    {
        if (TryGetCatalogCard(cardId, out var card) && card != null)
        {
            return string.IsNullOrWhiteSpace(card.Cost) ? "?" : card.Cost;
        }

        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            return data.TryGetValue("Expend", out var cost) && !string.IsNullOrWhiteSpace(cost) ? cost : "?";
        }
        catch
        {
            return "?";
        }
    }

    public static Sprite? TryLoadCardIcon(string cardId)
    {
        if (cardIconCache.TryGetValue(cardId, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            var iconPath = "";
            if (TryGetCatalogCard(cardId, out var card) && card != null)
            {
                iconPath = card.IconPath;
            }
            else
            {
                var data = new DataConfig(cardId, DataType.Card).data;
                if (data.TryGetValue("Icon", out var rawIconPath))
                {
                    iconPath = rawIconPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                sprite = AuraToolsResourceCache.Load<Sprite>(iconPath, true);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[StarterDeck] failed to load card icon for " + cardId + ": " + ex.Message);
        }

        cardIconCache[cardId] = sprite;
        return sprite;
    }
}

internal sealed class StarterDeckResolvedProfile
{
    public StarterDeckResolvedProfile(StarterDeckProfile profile, string reason)
    {
        Profile = profile;
        Reason = reason;
    }

    public StarterDeckProfile Profile { get; }

    public string Reason { get; }
}

public sealed class StarterDeckCardPackGroup
{
    public const string OtherGroupId = "__other__";

    public StarterDeckCardPackGroup(string packId, string displayName, List<string> cardIds)
    {
        PackId = packId ?? "";
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? PackId : displayName;
        CardIds = cardIds ?? new List<string>();
    }

    public string PackId { get; }

    public string DisplayName { get; }

    public List<string> CardIds { get; }

    public StarterDeckCardPackGroup WithCards(List<string> cardIds)
    {
        return new StarterDeckCardPackGroup(PackId, DisplayName, cardIds);
    }
}

internal sealed class StarterDeckCardCatalogSnapshot
{
    public static readonly StarterDeckCardCatalogSnapshot Empty = new(
        new List<StarterDeckCardCatalogEntry>(),
        new List<StarterDeckCardPackGroup>(),
        new List<string>(),
        new List<string>(),
        new List<string>(),
        new List<string>(),
        new List<string>());

    private readonly Dictionary<string, StarterDeckCardCatalogEntry> cardsById;
    private readonly HashSet<string> systemSkillCardIds;
    private readonly HashSet<string> starterDeckExcludedCardIds;

    public StarterDeckCardCatalogSnapshot(
        List<StarterDeckCardCatalogEntry> allCards,
        List<StarterDeckCardPackGroup> selectableGroups,
        List<string> selectableCardIds,
        List<string> hiddenCardIds,
        List<string> skillCardIds,
        List<string> systemSkillCardIds,
        List<string> excludedDerivedCardIds)
    {
        AllCards = allCards ?? new List<StarterDeckCardCatalogEntry>();
        SelectableGroups = selectableGroups ?? new List<StarterDeckCardPackGroup>();
        SelectableCardIds = selectableCardIds ?? new List<string>();
        HiddenCardIds = hiddenCardIds ?? new List<string>();
        SkillCardIds = skillCardIds ?? new List<string>();
        SystemSkillCardIds = systemSkillCardIds ?? new List<string>();
        ExcludedDerivedCardIds = excludedDerivedCardIds ?? new List<string>();
        cardsById = AllCards
            .GroupBy(card => card.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        this.systemSkillCardIds = new HashSet<string>(SystemSkillCardIds, StringComparer.OrdinalIgnoreCase);
        starterDeckExcludedCardIds = new HashSet<string>(SystemSkillCardIds, StringComparer.OrdinalIgnoreCase);
        starterDeckExcludedCardIds.UnionWith(ExcludedDerivedCardIds);
    }

    public List<StarterDeckCardCatalogEntry> AllCards { get; }

    public List<StarterDeckCardPackGroup> SelectableGroups { get; }

    public List<string> SelectableCardIds { get; }

    public List<string> HiddenCardIds { get; }

    public List<string> SkillCardIds { get; }

    public List<string> SystemSkillCardIds { get; }

    public List<string> ExcludedDerivedCardIds { get; }

    public bool TryGetCard(string cardId, out StarterDeckCardCatalogEntry? card)
    {
        card = null;
        return !string.IsNullOrWhiteSpace(cardId) && cardsById.TryGetValue(cardId, out card);
    }

    public bool IsSystemSkillCard(string cardId)
    {
        return !string.IsNullOrWhiteSpace(cardId) && systemSkillCardIds.Contains(cardId);
    }

    public bool IsStarterDeckExcluded(string cardId)
    {
        return !string.IsNullOrWhiteSpace(cardId) && starterDeckExcludedCardIds.Contains(cardId);
    }

    public List<StarterDeckCardPackGroup> CloneSelectableGroups()
    {
        return SelectableGroups
            .Select(group => new StarterDeckCardPackGroup(group.PackId, group.DisplayName, group.CardIds.ToList()))
            .ToList();
    }
}

internal sealed class StarterDeckCardCatalogEntry
{
    public StarterDeckCardCatalogEntry(
        string id,
        string packId,
        string displayName,
        string rarity,
        string cost,
        string iconPath,
        string action,
        bool isHidden,
        bool isSkillCard,
        bool isSystemSkillCard,
        bool isExcludedDerivedCard,
        bool isLocked)
    {
        Id = id ?? "";
        PackId = packId ?? "";
        DisplayName = displayName ?? Id;
        Rarity = rarity ?? "";
        Cost = cost ?? "";
        IconPath = iconPath ?? "";
        Action = action ?? "";
        IsHidden = isHidden;
        IsSkillCard = isSkillCard;
        IsSystemSkillCard = isSystemSkillCard;
        IsExcludedDerivedCard = isExcludedDerivedCard;
        IsLocked = isLocked;
    }

    public string Id { get; }

    public string PackId { get; }

    public string DisplayName { get; }

    public string Rarity { get; }

    public string Cost { get; }

    public string IconPath { get; }

    public string Action { get; }

    public bool IsHidden { get; }

    public bool IsSkillCard { get; }

    public bool IsSystemSkillCard { get; }

    public bool IsExcludedDerivedCard { get; }

    public bool IsLocked { get; }
}

internal sealed class StarterDeckRuntimeRole
{
    public StarterDeckRuntimeRole(
        string roleId,
        string roleTableRoleId,
        string selectedRoleId,
        string source)
    {
        RoleId = RoleCatalog.NormalizeRoleId(roleId);
        RoleTableRoleId = RoleCatalog.NormalizeRoleId(roleTableRoleId);
        SelectedRoleId = RoleCatalog.NormalizeRoleId(selectedRoleId);
        Source = source;
    }

    public string RoleId { get; }

    public string RoleTableRoleId { get; }

    public string SelectedRoleId { get; }

    public string Source { get; }

    public bool HasSelectedRoleConflict =>
        !string.IsNullOrWhiteSpace(SelectedRoleId)
        && !string.IsNullOrWhiteSpace(RoleTableRoleId)
        && !string.Equals(SelectedRoleId, RoleTableRoleId, StringComparison.OrdinalIgnoreCase);
}

public static class AuraToolsStarterDeckEditor
{
    public static void Show(Transform parent)
    {
        ShowGlobal(parent);
    }

    public static void ShowGlobal(Transform parent)
    {
        var profile = AuraToolsConfigService.MatchExperience.StarterDeck.GlobalProfile;
        profile.Normalize("", "全局自定义卡组");
        ShowLocalProfile(parent, profile, "", "【世界推演】全局开局卡组配置");
    }

    public static void ShowRole(Transform parent, string roleId, string displayName = "")
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var profile = AuraToolsStarterDeckRuntime.EnsureRoleProfileSettings(normalizedRole, displayName);
        ShowLocalProfile(parent, profile, normalizedRole, "【世界推演】角色开局卡组配置 - " + (string.IsNullOrWhiteSpace(displayName) ? normalizedRole : displayName));
    }

    public static void CopyRegisteredToRole(Transform parent, string roleId, string displayName, StarterDeckProfile source)
    {
        var profile = AuraToolsStarterDeckRuntime.EnsureRoleProfileSettings(roleId, displayName);
        profile.DeckSize = source.DeckSize;
        profile.CardIds = AuraToolsStarterDeckRuntime.BuildDeckFromProfile(source);
        profile.DerivedFromProfileId = source.QualifiedProfileId;
        profile.DisplayName = (string.IsNullOrWhiteSpace(displayName) ? RoleCatalog.NormalizeRoleId(roleId) : displayName) + " 自定义卡组";
        AuraToolsStarterDeckRuntime.SelectProfileForRole(roleId, AuraToolsStarterDeckRuntime.LocalRoleProfileId(roleId));
        AuraToolsConfigService.SaveMatchExperience();
        ShowRole(parent, roleId, displayName);
    }

    private static void ShowLocalProfile(Transform parent, StarterDeckLocalProfileSettings profile, string roleId, string title)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.StarterDeckEditor", parent, title);
        var session = new StarterDeckEditorSession(profile, roleId);
        session.Build(window.transform);
    }

    private sealed class StarterDeckEditorSession
    {
        private readonly List<string> editingDeck = new();
        private readonly List<string> autoFillCandidates = new();
        private readonly List<StarterDeckCardPackGroup> candidateGroups = new();
        private readonly Dictionary<string, CandidateGroupView> candidateGroupViews = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> expandedCandidateGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SelectedCardRowView> selectedRowViews = new();
        private readonly StarterDeckLocalProfileSettings profile;
        private readonly string editingRoleId;
        private Transform? candidateContent;
        private Transform? selectedContent;
        private Text? counterText;
        private Text? hintText;

        public StarterDeckEditorSession(StarterDeckLocalProfileSettings profile, string roleId)
        {
            this.profile = profile;
            editingRoleId = RoleCatalog.NormalizeRoleId(roleId);
            editingDeck.AddRange(profile.CardIds);
        }

        public void Build(Transform window)
        {
            candidateGroups.Clear();
            candidateGroups.AddRange(AuraToolsStarterDeckRuntime.BuildCandidateCardPackGroups());
            autoFillCandidates.Clear();
            autoFillCandidates.AddRange(candidateGroups
                .SelectMany(group => group.CardIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(AuraToolsStarterDeckRuntime.CardSortKey)
                .ToList());

            var body = Settings.AuraToolsUi.CreateLayout("Body", window);
            var bodyElement = body.AddComponent<LayoutElement>();
            bodyElement.flexibleHeight = 1f;
            bodyElement.minHeight = 420f;
            var bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
            bodyLayout.spacing = 12f;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = true;
            bodyLayout.childForceExpandHeight = true;

            var candidatePanel = CreateColumn(body.transform, "按卡包选择", out _);
            candidateContent = candidatePanel;
            BuildCandidateGroups();

            var selectedPanel = CreateColumn(body.transform, "当前预设", out counterText);
            selectedContent = selectedPanel;

            var footer = Settings.AuraToolsUi.CreateLayout("Footer", window);
            Settings.AuraToolsUi.SetFixedHeight(footer, Settings.AuraToolsUi.FooterHeight);
            var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f;
            footerLayout.childControlHeight = true;
            footerLayout.childControlWidth = true;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childForceExpandHeight = false;
            hintText = Settings.AuraToolsUi.AddText(footer.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddButton(footer.transform, "自动填充", () =>
            {
                editingDeck.Clear();
                editingDeck.AddRange(autoFillCandidates.Take(CurrentDeckSize()));
                RefreshSelected();
            });
            Settings.AuraToolsUi.AddButton(footer.transform, "清空", () =>
            {
                editingDeck.Clear();
                RefreshSelected();
            });
            Settings.AuraToolsUi.AddButton(footer.transform, "保存", Save);

            RefreshSelected();
        }

        private Transform CreateColumn(Transform parent, string title, out Text? counter)
        {
            var column = Settings.AuraToolsUi.CreateLayout("Column-" + title, parent);
            column.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var layout = column.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var header = Settings.AuraToolsUi.CreateLayout("Header", column.transform);
            Settings.AuraToolsUi.SetFixedHeight(header, Settings.AuraToolsUi.ColumnHeaderHeight);
            Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
            var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(10, 10, 2, 2);
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandHeight = false;
            Settings.AuraToolsUi.AddText(header.transform, title, Settings.AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
            counter = Settings.AuraToolsUi.AddText(header.transform, "", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleRight, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 0f, 110f);

            CreateCardInfoHeader(column.transform);
            return Settings.AuraToolsUi.CreateScroll(column.transform, title);
        }

        private void BuildCandidateGroups()
        {
            if (candidateContent == null || candidateGroupViews.Count > 0)
            {
                return;
            }

            foreach (var group in candidateGroups)
            {
                var view = CreateCandidateGroup(candidateContent, group);
                candidateGroupViews[group.PackId] = view;
            }
        }

        private CandidateGroupView CreateCandidateGroup(Transform parent, StarterDeckCardPackGroup group)
        {
            var expanded = expandedCandidateGroups.Contains(group.PackId);
            var root = Settings.AuraToolsUi.CreateLayout("PackGroup-" + group.PackId, parent);
            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.spacing = 8f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            var header = Settings.AuraToolsUi.CreateLayout("Pack-" + group.PackId, root.transform);
            Settings.AuraToolsUi.SetFixedHeight(header, 34f);
            var image = Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
            var button = header.AddComponent<Button>();
            AuraUiButtonFeedback.Apply(button, image, Settings.AuraToolsUi.Accent);
            button.onClick.AddListener(() => ToggleCandidateGroup(group.PackId));
            var layout = header.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 2, 2);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var titleText = Settings.AuraToolsUi.AddText(header.transform, CandidateGroupTitle(group, expanded), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddText(header.transform, group.CardIds.Count.ToString(), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 52f);

            var cardContent = Settings.AuraToolsUi.CreateLayout("PackCards-" + group.PackId, root.transform);
            var cardLayout = cardContent.AddComponent<VerticalLayoutGroup>();
            cardLayout.spacing = 8f;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;

            var view = new CandidateGroupView(root, cardContent, titleText, group);
            if (expanded)
            {
                EnsureCandidateRows(view);
            }

            Settings.AuraToolsUi.SetFoldoutExpanded(cardContent, expanded, root.transform);
            return view;
        }

        private void ToggleCandidateGroup(string packId)
        {
            if (!candidateGroupViews.TryGetValue(packId, out var view))
            {
                return;
            }

            var expanded = !expandedCandidateGroups.Contains(packId);
            if (expanded)
            {
                expandedCandidateGroups.Add(packId);
                EnsureCandidateRows(view);
            }
            else
            {
                expandedCandidateGroups.Remove(packId);
            }

            view.TitleText.text = CandidateGroupTitle(view.Group, expanded);
            Settings.AuraToolsUi.SetFoldoutExpanded(view.CardContent, expanded, view.Root.transform);
        }

        private static string CandidateGroupTitle(StarterDeckCardPackGroup group, bool expanded)
        {
            return (expanded ? "\u25be " : "\u25b8 ") + group.DisplayName;
        }

        private void EnsureCandidateRows(CandidateGroupView view)
        {
            if (view.RowsBuilt)
            {
                return;
            }

            foreach (var cardId in view.Group.CardIds)
            {
                CreateCandidateRow(view.CardContent.transform, cardId);
            }

            view.RowsBuilt = true;
        }

        private void CreateCandidateRow(Transform parent, string cardId)
        {
            var row = CreateRow(parent, "Candidate-" + cardId);
            CreateCardIconCell(row.transform, cardId, AuraToolsStarterDeckRuntime.CardCost(cardId));
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardDisplayNameWithSpecialMarker(cardId), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardRarity(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardCost(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
            Settings.AuraToolsUi.AddButton(row.transform, "添加", () =>
            {
                if (editingDeck.Count >= CurrentDeckSize())
                {
                    SetHint("预设已满，请先移除一张。");
                    return;
                }

                editingDeck.Add(cardId);
                RefreshSelected();
            }, 70f, 30f);
        }

        private void RefreshSelected()
        {
            if (selectedContent == null)
            {
                return;
            }

            while (selectedRowViews.Count < editingDeck.Count)
            {
                selectedRowViews.Add(CreateSelectedRow(selectedContent, selectedRowViews.Count));
            }

            for (var i = 0; i < selectedRowViews.Count; i++)
            {
                var view = selectedRowViews[i];
                var visible = i < editingDeck.Count;
                if (visible)
                {
                    BindSelectedRow(view, i, editingDeck[i]);
                }

                Settings.AuraToolsUi.SetActiveIfChanged(view.Root, visible);
            }

            var size = CurrentDeckSize();
            if (counterText != null)
            {
                counterText.text = editingDeck.Count + "/" + size;
                counterText.color = editingDeck.Count == size ? new Color(0.58f, 0.94f, 0.62f) : Settings.AuraToolsUi.Text;
            }

            SetHint(editingDeck.Count == size ? "预设完整，可以保存。" : "需要配置满 " + size + " 张牌。");
        }

        private SelectedCardRowView CreateSelectedRow(Transform parent, int slot)
        {
            var row = CreateRow(parent, "SelectedSlot-" + slot);
            var icon = CreateCardIconCellView(row.transform);
            var nameText = Settings.AuraToolsUi.AddText(row.transform, "", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
            var rarityText = Settings.AuraToolsUi.AddText(row.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
            var costText = Settings.AuraToolsUi.AddText(row.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
            var view = new SelectedCardRowView(row, icon, nameText, rarityText, costText);
            Settings.AuraToolsUi.AddButton(row.transform, "移除", () => RemoveSelectedRow(view), 70f, 30f);
            return view;
        }

        private static void BindSelectedRow(SelectedCardRowView view, int index, string cardId)
        {
            view.Index = index;
            view.Root.name = "Selected-" + index;
            BindCardIconCell(view.Icon, cardId, (index + 1).ToString());
            view.NameText.text = AuraToolsStarterDeckRuntime.CardDisplayNameWithSpecialMarker(cardId);
            view.RarityText.text = AuraToolsStarterDeckRuntime.CardRarity(cardId);
            view.CostText.text = AuraToolsStarterDeckRuntime.CardCost(cardId);
        }

        private void RemoveSelectedRow(SelectedCardRowView view)
        {
            var index = view.Index;
            if (index < 0 || index >= editingDeck.Count)
            {
                return;
            }

            editingDeck.RemoveAt(index);
            RefreshSelected();
        }

        private sealed class CandidateGroupView
        {
            public CandidateGroupView(GameObject root, GameObject cardContent, Text titleText, StarterDeckCardPackGroup group)
            {
                Root = root;
                CardContent = cardContent;
                TitleText = titleText;
                Group = group;
            }

            public GameObject Root { get; }
            public GameObject CardContent { get; }
            public Text TitleText { get; }
            public StarterDeckCardPackGroup Group { get; }
            public bool RowsBuilt { get; set; }
        }

        private sealed class SelectedCardRowView
        {
            public SelectedCardRowView(GameObject root, CardIconCellView icon, Text nameText, Text rarityText, Text costText)
            {
                Root = root;
                Icon = icon;
                NameText = nameText;
                RarityText = rarityText;
                CostText = costText;
            }

            public GameObject Root { get; }
            public CardIconCellView Icon { get; }
            public Text NameText { get; }
            public Text RarityText { get; }
            public Text CostText { get; }
            public int Index { get; set; } = -1;
        }

        private void Save()
        {
            if (editingDeck.Count != profile.DeckSize)
            {
                SetHint("保存失败：需要正好 " + profile.DeckSize + " 张牌。");
                return;
            }

            profile.CardIds = editingDeck.ToList();
            profile.Enabled = true;
            var fallbackDisplayName = string.IsNullOrWhiteSpace(editingRoleId)
                ? "全局自定义卡组"
                : RoleCatalog.GetDisplayName(editingRoleId) + " 自定义卡组";
            profile.Normalize(editingRoleId, fallbackDisplayName);
            if (!string.IsNullOrWhiteSpace(editingRoleId))
            {
                AuraToolsStarterDeckRuntime.SelectProfileForRole(editingRoleId, AuraToolsStarterDeckRuntime.LocalRoleProfileId(editingRoleId));
            }

            AuraToolsConfigService.SaveMatchExperience();
            SetHint(string.IsNullOrWhiteSpace(editingRoleId) ? "已保存全局开局卡组预设。" : "已保存本角色开局卡组预设。");
        }

        private int CurrentDeckSize()
        {
            return Math.Max(1, profile.DeckSize);
        }

        private void SetHint(string message)
        {
            if (hintText != null)
            {
                hintText.text = message;
            }
        }
    }

    private static void CreateCardInfoHeader(Transform parent)
    {
        var header = Settings.AuraToolsUi.CreateLayout("CardInfoHeader", parent);
        Settings.AuraToolsUi.SetFixedHeight(header, AuraToolsStarterDeckRuntime.CardInfoHeaderHeight);
        Settings.AuraToolsUi.AddImage(header, Settings.AuraToolsUi.Header);
        var layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 0, 0);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        Settings.AuraToolsUi.AddText(header.transform, "卡图", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardImageColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "卡牌名称", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddText(header.transform, "稀有度", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "费用", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
        Settings.AuraToolsUi.AddText(header.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardActionColumnWidth);
    }

    private static void CreateCardIconCell(Transform parent, string cardId, string fallbackText)
    {
        var view = CreateCardIconCellView(parent);
        BindCardIconCell(view, cardId, fallbackText);
    }

    private static CardIconCellView CreateCardIconCellView(Transform parent)
    {
        var cell = Settings.AuraToolsUi.CreateLayout("CardIcon", parent);
        var element = Settings.AuraToolsUi.EnsureLayoutElement(cell);
        element.minWidth = AuraToolsStarterDeckRuntime.CardImageColumnWidth;
        element.preferredWidth = AuraToolsStarterDeckRuntime.CardImageColumnWidth;
        element.minHeight = Settings.AuraToolsUi.TextMinHeight;
        element.preferredHeight = Settings.AuraToolsUi.TextMinHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;

        var background = Settings.AuraToolsUi.AddImage(cell, new Color(0.025f, 0.022f, 0.045f, 0.98f));
        var icon = Settings.AuraToolsUi.CreateRect("Image", cell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(AuraToolsStarterDeckRuntime.CardIconSize, AuraToolsStarterDeckRuntime.CardIconSize));
        var image = icon.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
        var fallback = Settings.AuraToolsUi.AddFillText(cell.transform, "", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent);
        return new CardIconCellView(background, image, fallback);
    }

    private static void BindCardIconCell(CardIconCellView view, string cardId, string fallbackText)
    {
        var sprite = AuraToolsStarterDeckRuntime.TryLoadCardIcon(cardId);
        var hasIcon = sprite != null;
        view.Background.enabled = !hasIcon;
        view.Image.sprite = sprite;
        view.Fallback.text = fallbackText;
        Settings.AuraToolsUi.SetActiveIfChanged(view.Image.gameObject, hasIcon);
        Settings.AuraToolsUi.SetActiveIfChanged(view.Fallback.gameObject, !hasIcon);
    }

    private sealed class CardIconCellView
    {
        public CardIconCellView(Image background, Image image, Text fallback)
        {
            Background = background;
            Image = image;
            Fallback = fallback;
        }

        public Image Background { get; }
        public Image Image { get; }
        public Text Fallback { get; }
    }

    private static GameObject CreateRow(Transform parent, string name)
    {
        var row = Settings.AuraToolsUi.CreateLayout(name, parent);
        Settings.AuraToolsUi.SetFixedHeight(row, Settings.AuraToolsUi.DataRowHeight);
        Settings.AuraToolsUi.AddImage(row, Settings.AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }
}

public static class AuraToolsStarterDeckRoleManager
{
    private static Text? hintText;

    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.StarterDeckRoleManager", parent, "【世界推演】角色开局卡组");
        var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        hintText = Settings.AuraToolsUi.AddText(toolbar.transform, "MOD 注册 Profile 为只读；复制后会生成 AuraTools 本地可编辑卡组。", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "刷新角色", () => Show(parent), 96f);

        var content = Settings.AuraToolsUi.CreateScroll(window.transform, "StarterDeckRoles");
        var roles = RoleCatalog.GetRoles(true);
        if (roles.Count == 0)
        {
            Settings.AuraToolsUi.AddText(content, "未扫描到可配置角色。", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
            return;
        }

        foreach (var role in roles)
        {
            CreateRoleRow(content, window.transform, role);
        }
    }

    private static void CreateRoleRow(Transform parent, Transform overlayParent, RoleInfo role)
    {
        var row = CreateRow(parent, "Role-" + role.Id, Settings.AuraToolsUi.RoleRowHeight);
        Settings.AuraToolsUi.AddText(row.transform, role.DisplayName, Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 0f, 220f);

        var resolved = AuraToolsStarterDeckRuntime.ResolveEffectiveProfileForPreview(role.Id);
        var status = resolved == null
            ? "生效：无完整卡组"
            : "生效：" + resolved.Profile.DisplayName;
        Settings.AuraToolsUi.AddText(row.transform, status, Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(row.transform, "候选", () => ShowProfilePicker(overlayParent, role), 82f, 34f);
        Settings.AuraToolsUi.AddButton(row.transform, "编辑本地", () => AuraToolsStarterDeckEditor.ShowRole(overlayParent, role.Id, role.DisplayName), 94f, 34f);
        if (AuraToolsConfigService.MatchExperience.StarterDeck.Roles.ContainsKey(role.Id))
        {
            Settings.AuraToolsUi.AddButton(row.transform, "删除本地", () =>
            {
                AuraToolsStarterDeckRuntime.DeleteRoleProfileSettings(role.Id);
                SetHint("已删除 " + role.DisplayName + " 的 AuraTools 本地卡组。");
            }, 94f, 34f);
        }
    }

    private static void ShowProfilePicker(Transform parent, RoleInfo role)
    {
        new StarterDeckProfilePickerSession(parent, role).Show();
    }

    private sealed class StarterDeckProfilePickerSession
    {
        private readonly Transform parent;
        private readonly RoleInfo role;
        private Transform? content;
        private Transform? overlayParent;
        private Text? localHintText;

        public StarterDeckProfilePickerSession(Transform parent, RoleInfo role)
        {
            this.parent = parent;
            this.role = role;
        }

        public void Show()
        {
            var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.StarterDeckProfilePicker", parent, "选择开局卡组 - " + role.DisplayName);
            overlayParent = window.transform;
            var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
            Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
            var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
            toolbarLayout.spacing = 8f;
            toolbarLayout.childControlWidth = true;
            toolbarLayout.childControlHeight = true;
            toolbarLayout.childForceExpandWidth = false;
            var isGlobalMode = AuraToolsStarterDeckRuntime.IsGlobalModeEnabled();
            Settings.AuraToolsUi.AddText(
                toolbar.transform,
                isGlobalMode ? "当前为全局模式：本页选择会保存，但只在切回按角色后生效。" : "当前为按角色模式：绿色项会用于该角色开局。",
                Settings.AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                isGlobalMode ? Settings.AuraToolsUi.WarningText : Settings.AuraToolsUi.SuccessText,
                Settings.AuraToolsUi.TextMinHeight,
                0f,
                360f);
            localHintText = Settings.AuraToolsUi.AddText(toolbar.transform, "同一角色可存在多套候选；默认优先角色所属 MOD 的只读注册 Profile。", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
            Settings.AuraToolsUi.AddButton(toolbar.transform, "恢复自动", () =>
            {
                AuraToolsStarterDeckRuntime.ClearSelectedProfileForRole(role.Id);
                RefreshProfiles();
                SetLocalHint("已恢复 " + role.DisplayName + " 的自动选择。", Settings.AuraToolsUi.SuccessText);
            }, 96f);

            content = Settings.AuraToolsUi.CreateScroll(window.transform, "StarterDeckProfiles");
            RefreshProfiles();
        }

        private void RefreshProfiles()
        {
            if (content == null)
            {
                return;
            }

            Settings.AuraToolsUi.ClearChildren(content);
            var profiles = AuraToolsStarterDeckRuntime.BuildCandidateProfilesForRole(role.Id);
            if (profiles.Count == 0)
            {
                Settings.AuraToolsUi.AddText(content, "暂无可用候选。可以先编辑本地角色卡组，或等待角色 MOD 注册 Profile。", Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 1f);
                return;
            }

            foreach (var profile in profiles)
            {
                CreateProfileRow(content, profile);
            }
        }

        private void CreateProfileRow(Transform parent, StarterDeckProfile profile)
        {
            var row = CreateRow(parent, "Profile-" + profile.ProfileId, Settings.AuraToolsUi.DataRowHeight);
            var isGlobalMode = AuraToolsStarterDeckRuntime.IsGlobalModeEnabled();
            var selectedProfileId = AuraToolsStarterDeckRuntime.ConfiguredSelectedProfileIdForRole(role.Id);
            var isConfiguredSelected = AuraToolsStarterDeckRuntime.ProfileMatchesId(profile, selectedProfileId);
            var effective = AuraToolsStarterDeckRuntime.ResolveEffectiveProfileForPreview(role.Id);
            var isEffective = effective != null && AuraToolsStarterDeckRuntime.ProfileMatchesId(profile, effective.Profile.QualifiedProfileId);
            var highlighted = isConfiguredSelected || isEffective;
            var status = ProfileSelectionStatus(isGlobalMode, isConfiguredSelected, isEffective);
            var rowImage = row.GetComponent<Image>();
            if (highlighted && rowImage != null)
            {
                rowImage.color = Settings.AuraToolsUi.ActiveRow;
            }

            var titleColor = highlighted ? Settings.AuraToolsUi.SuccessText : Settings.AuraToolsUi.Text;
            var detailColor = highlighted ? Settings.AuraToolsUi.SuccessText : Settings.AuraToolsUi.MutedText;
            Settings.AuraToolsUi.AddText(row.transform, profile.DisplayName + status, Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, titleColor, Settings.AuraToolsUi.TextMinHeight, 0f, 260f);
            Settings.AuraToolsUi.AddText(row.transform, DescribeSource(profile) + " / " + DeckStatus(profile) + "\n" + profile.QualifiedProfileId, Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, detailColor, Settings.AuraToolsUi.TextMinHeight, 1f);
            var enableButton = Settings.AuraToolsUi.AddButton(row.transform, isConfiguredSelected ? "已选择" : "启用此卡组", () =>
            {
                AuraToolsStarterDeckRuntime.SelectProfileForRole(role.Id, profile.QualifiedProfileId);
                RefreshProfiles();
                SetLocalHint(
                    isGlobalMode
                        ? "已为 " + role.DisplayName + " 保存选择：" + profile.DisplayName + "。当前是全局模式，切回按角色后生效。"
                        : "已启用 " + role.DisplayName + " 的卡组：" + profile.DisplayName,
                    Settings.AuraToolsUi.SuccessText);
            }, 92f, 34f);
            enableButton.interactable = !isConfiguredSelected;

            if (profile.SourceKind == StarterDeckProfileSourceKind.Registered)
            {
                Settings.AuraToolsUi.AddButton(row.transform, "复制为本角色", () =>
                {
                    if (overlayParent != null)
                    {
                        AuraToolsStarterDeckEditor.CopyRegisteredToRole(overlayParent, role.Id, role.DisplayName, profile);
                    }

                    RefreshProfiles();
                    SetLocalHint("已复制只读 Profile 到本地，并设为该角色选择。", Settings.AuraToolsUi.SuccessText);
                }, 104f, 34f);
                return;
            }

            if (string.Equals(profile.QualifiedProfileId, AuraToolsStarterDeckRuntime.LocalGlobalProfileId(), StringComparison.OrdinalIgnoreCase))
            {
                if (overlayParent != null)
                {
                    Settings.AuraToolsUi.AddButton(row.transform, "编辑全局", () => AuraToolsStarterDeckEditor.ShowGlobal(overlayParent), 82f, 34f);
                }

                return;
            }

            if (overlayParent != null)
            {
                Settings.AuraToolsUi.AddButton(row.transform, "编辑本角色", () => AuraToolsStarterDeckEditor.ShowRole(overlayParent, role.Id, role.DisplayName), 92f, 34f);
            }

            Settings.AuraToolsUi.AddButton(row.transform, "删除", () =>
            {
                AuraToolsStarterDeckRuntime.DeleteRoleProfileSettings(role.Id);
                RefreshProfiles();
                SetLocalHint("已删除 " + role.DisplayName + " 的 AuraTools 本地卡组。", Settings.AuraToolsUi.WarningText);
            }, 78f, 34f);
        }

        private void SetLocalHint(string message, Color? color = null)
        {
            if (localHintText != null)
            {
                localHintText.text = message;
                localHintText.color = color ?? Settings.AuraToolsUi.MutedText;
            }
        }
    }

    private static GameObject CreateRow(Transform parent, string name, float height)
    {
        var row = Settings.AuraToolsUi.CreateLayout(name, parent);
        Settings.AuraToolsUi.SetFixedHeight(row, height);
        Settings.AuraToolsUi.AddImage(row, Settings.AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 2, 2);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private static string DescribeSource(StarterDeckProfile profile)
    {
        if (profile.SourceKind == StarterDeckProfileSourceKind.Registered)
        {
            return "MOD只读/" + profile.OwnerModId;
        }

        return string.Equals(profile.QualifiedProfileId, AuraToolsStarterDeckRuntime.LocalGlobalProfileId(), StringComparison.OrdinalIgnoreCase)
            ? "AuraTools全局 fallback"
            : "AuraTools本角色";
    }

    private static string DescribeReason(string reason)
    {
        return reason switch
        {
            StarterDeckProfileResolutionReasons.Selected => "显式选择",
            StarterDeckProfileResolutionReasons.LocalRole => "本角色优先",
            StarterDeckProfileResolutionReasons.RoleOwnerRegistered => "角色MOD推荐",
            StarterDeckProfileResolutionReasons.LocalGlobal => "全局回退",
            _ => reason
        };
    }

    private static string DeckStatus(StarterDeckProfile profile)
    {
        var validation = StarterDeckArbiterRuntime.ValidateProfile(profile, null, AuraToolsStarterDeckRuntime.BuildDeckFromProfile);
        return validation.DeckCount + "/" + validation.DeckSize + (validation.Complete ? "" : " " + validation.Summary);
    }

    private static string ProfileSelectionStatus(bool isGlobalMode, bool isConfiguredSelected, bool isEffective)
    {
        if (isGlobalMode && isConfiguredSelected)
        {
            return "  [已选择，按角色模式生效]";
        }

        if (isConfiguredSelected)
        {
            return "  [当前启用]";
        }

        if (!isGlobalMode && isEffective)
        {
            return "  [当前自动生效]";
        }

        return "";
    }

    private static void SetHint(string message, Color? color = null)
    {
        if (hintText != null)
        {
            hintText.text = message;
            hintText.color = color ?? Settings.AuraToolsUi.MutedText;
        }
    }
}
