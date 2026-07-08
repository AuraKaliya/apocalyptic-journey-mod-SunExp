using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
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
    private static readonly TimeSpan PreparationContextTtl = TimeSpan.FromMinutes(2);
    public const float CardInfoHeaderHeight = 40f;
    public const float CardImageColumnWidth = 44f;
    public const float CardIconSize = 34f;
    public const float CardRarityColumnWidth = 70f;
    public const float CardCostColumnWidth = 56f;
    public const float CardActionColumnWidth = 84f;
    private static readonly Dictionary<string, Sprite?> cardIconCache = new(StringComparer.OrdinalIgnoreCase);
    private const string SunExpSolarMemoryModeKey = "SunExp_SolarMemoryMode";
    private static StarterDeckPreparationContext? preparationContext;
    private static int lastMultiplayerSkipLogFrame = -100000;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "GameEntryUI.ChangeRole", CaptureRoleSelectionContext);
        RegisterBefore(modConfig, "GameEntryUI.StartGame", CapturePreparationContext);
        RegisterAfter(modConfig, "NormalMapManager.InitRoleTable", ApplyStarterDeckAfterRoleInit);
    }

    public static List<string> BuildAllCandidateCardIds()
    {
        return BuildCandidateCardIds(BuildSelectablePacks());
    }

    public static List<string> BuildCandidateCardIds(IEnumerable<string> packIds)
    {
        return (packIds ?? Array.Empty<string>())
            .Where(IsValidPackForCurrentLobby)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(CardIdsFromPack)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardSortKey)
            .ToList();
    }

    private static void ApplyStarterDeckAfterRoleInit(ModHookContext context)
    {
        try
        {
            var roleTable = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as RoleTable
                : RoleTable.Instance;
            ApplyStarterDeck(roleTable, context, "NormalMapManager.InitRoleTable", allowNormalMapHookFallback: true);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[StarterDeck] failed to apply preset", ex);
        }
    }

    private static void CaptureRoleSelectionContext(ModHookContext context)
    {
        CaptureSelectedRoleContext(context, "GameEntryUI.ChangeRole");
    }

    private static void CapturePreparationContext(ModHookContext context)
    {
        try
        {
            CaptureSelectedRoleContext(context, "GameEntryUI.StartGame");
            ApplyStarterDeck(RoleTable.Instance, context, "GameEntryUI.StartGame", allowNormalMapHookFallback: false);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[StarterDeck] failed to reconcile preset before start", ex);
        }
    }

    private static void ApplyStarterDeck(
        RoleTable? roleTable,
        ModHookContext context,
        string source,
        bool allowNormalMapHookFallback)
    {
        if (!AuraToolsConfigService.Root.MatchExperience.Enabled
            || !AuraToolsConfigService.MatchExperience.StarterDeck.Enabled
            || roleTable == null)
        {
            return;
        }

        if (!IsWorldSimulationRun(context, allowNormalMapHookFallback))
        {
            AuraToolsLog.Info("[StarterDeck] skipped: not a confirmed world-simulation run. source=" + source + ".");
            return;
        }

        if (IsMultiplayerSession())
        {
            LogMultiplayerStarterDeckSkipped(source);
            return;
        }

        if (ShouldSkipForExternalOwner(roleTable))
        {
            return;
        }

        var role = ResolveRuntimeRole(roleTable);
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
        if (ReferenceEquals(preparationContext, role.PreparationContext))
        {
            preparationContext = null;
        }

        AuraToolsLog.Info("[StarterDeck] applied world-simulation profile; role="
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

    private static bool IsWorldSimulationRun(ModHookContext context, bool allowNormalMapHookFallback)
    {
        var modeType = ReadLobbyModeType();
        if (!string.IsNullOrWhiteSpace(modeType))
        {
            return string.Equals(modeType, "Normal", StringComparison.OrdinalIgnoreCase);
        }

        if (IsNormalMapManager(context.Target))
        {
            return true;
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

        return allowNormalMapHookFallback;
    }

    private static bool IsMultiplayerSession()
    {
        try
        {
            return PlayerManager.Instance != null;
        }
        catch
        {
            return false;
        }
    }

    private static void LogMultiplayerStarterDeckSkipped(string source)
    {
        var frame = SafeFrameCount();
        if (frame - lastMultiplayerSkipLogFrame < 300)
        {
            return;
        }

        lastMultiplayerSkipLogFrame = frame;
        AuraToolsLog.Info("[StarterDeck] skipped: multiplayer world-simulation keeps native per-player decks; source="
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

    private static void CaptureSelectedRoleContext(ModHookContext context, string source)
    {
        var selectedRole = RoleCatalog.NormalizeRoleId(ReadDataId(GameEntryUI.career));
        if (string.IsNullOrWhiteSpace(selectedRole))
        {
            return;
        }

        var modeType = ReadLobbyModeType();
        var capturedUtc = DateTime.UtcNow;
        var previous = preparationContext;
        preparationContext = new StarterDeckPreparationContext
        {
            RoleId = selectedRole,
            ModeType = modeType,
            Source = source,
            CapturedUtc = capturedUtc
        };

        if (previous != null
            && capturedUtc - previous.CapturedUtc < TimeSpan.FromSeconds(3)
            && string.Equals(previous.RoleId, selectedRole, StringComparison.OrdinalIgnoreCase)
            && string.Equals(previous.ModeType, modeType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(previous.Source, source, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AuraToolsLog.Info("[StarterDeck] role context captured; role="
                          + selectedRole
                          + ", mode=" + modeType
                          + ", source=" + source + ".");
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
            return GameSaveManager.GetValue<string>(SunExpSolarMemoryModeKey) == "1";
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
        AuraSharedHooks.RegisterBefore(config, target, action, warn: AuraToolsLog.Warn);
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
            .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal))
            .Where(IsValidCard)
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
        if (string.IsNullOrWhiteSpace(roleTableRole))
        {
            roleTableRole = RoleCatalog.NormalizeRoleId(ReflectionUtil.ReadString(roleTable, "Id", "id"));
        }

        var selectedRole = RoleCatalog.NormalizeRoleId(ReadDataId(GameEntryUI.career));
        var prepared = GetFreshPreparationContext();
        if (prepared != null)
        {
            return new StarterDeckRuntimeRole(
                prepared.RoleId,
                roleTableRole,
                selectedRole,
                "preparation-context:" + prepared.Source,
                prepared);
        }

        if (!string.IsNullOrWhiteSpace(selectedRole))
        {
            return new StarterDeckRuntimeRole(
                selectedRole,
                roleTableRole,
                selectedRole,
                "GameEntryUI.career",
                null);
        }

        return new StarterDeckRuntimeRole(
            roleTableRole,
            roleTableRole,
            selectedRole,
            string.IsNullOrWhiteSpace(roleTableRole) ? "unknown" : "RoleTable.Career",
            null);
    }

    private static StarterDeckPreparationContext? GetFreshPreparationContext()
    {
        var context = preparationContext;
        if (context == null)
        {
            return null;
        }

        if (DateTime.UtcNow - context.CapturedUtc > PreparationContextTtl)
        {
            preparationContext = null;
            return null;
        }

        if (!string.IsNullOrWhiteSpace(context.ModeType)
            && !string.Equals(context.ModeType, "Normal", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(context.RoleId) ? null : context;
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
            Owner = registered ? profile.OwnerModId + ".StarterDeckProfile" : Owner,
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

    private static string CardSortKey(string cardId)
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

    public static string CardShortInfo(string cardId)
    {
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
            var data = new DataConfig(cardId, DataType.Card).data;
            if (data.TryGetValue("Icon", out var iconPath) && !string.IsNullOrWhiteSpace(iconPath))
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

internal sealed class StarterDeckPreparationContext
{
    public string RoleId { get; set; } = "";

    public string ModeType { get; set; } = "";

    public string Source { get; set; } = "";

    public DateTime CapturedUtc { get; set; }
}

internal sealed class StarterDeckRuntimeRole
{
    public StarterDeckRuntimeRole(
        string roleId,
        string roleTableRoleId,
        string selectedRoleId,
        string source,
        StarterDeckPreparationContext? preparationContext)
    {
        RoleId = RoleCatalog.NormalizeRoleId(roleId);
        RoleTableRoleId = RoleCatalog.NormalizeRoleId(roleTableRoleId);
        SelectedRoleId = RoleCatalog.NormalizeRoleId(selectedRoleId);
        Source = source;
        PreparationContext = preparationContext;
    }

    public string RoleId { get; }

    public string RoleTableRoleId { get; }

    public string SelectedRoleId { get; }

    public string Source { get; }

    public StarterDeckPreparationContext? PreparationContext { get; }

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
        private readonly StarterDeckLocalProfileSettings profile;
        private readonly string editingRoleId;
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
            var candidates = AuraToolsStarterDeckRuntime.BuildAllCandidateCardIds();

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

            var candidatePanel = CreateColumn(body.transform, "全部可选卡牌", out _);
            foreach (var cardId in candidates)
            {
                CreateCandidateRow(candidatePanel, cardId);
            }

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
                editingDeck.AddRange(candidates.Take(CurrentDeckSize()));
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

        private void CreateCandidateRow(Transform parent, string cardId)
        {
            var row = CreateRow(parent, "Candidate-" + cardId);
            CreateCardIconCell(row.transform, cardId, AuraToolsStarterDeckRuntime.CardCost(cardId));
            Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardDisplayName(cardId), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
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

            Settings.AuraToolsUi.ClearChildren(selectedContent);
            for (var i = 0; i < editingDeck.Count; i++)
            {
                var index = i;
                var cardId = editingDeck[i];
                var row = CreateRow(selectedContent, "Selected-" + index);
                CreateCardIconCell(row.transform, cardId, (index + 1).ToString());
                Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardDisplayName(cardId), Settings.AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Text, Settings.AuraToolsUi.TextMinHeight, 1f);
                Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardRarity(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardRarityColumnWidth);
                Settings.AuraToolsUi.AddText(row.transform, AuraToolsStarterDeckRuntime.CardCost(cardId), Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, AuraToolsStarterDeckRuntime.CardCostColumnWidth);
                Settings.AuraToolsUi.AddButton(row.transform, "移除", () =>
                {
                    if (index >= 0 && index < editingDeck.Count)
                    {
                        editingDeck.RemoveAt(index);
                        RefreshSelected();
                    }
                }, 70f, 30f);
            }

            var size = CurrentDeckSize();
            if (counterText != null)
            {
                counterText.text = editingDeck.Count + "/" + size;
                counterText.color = editingDeck.Count == size ? new Color(0.58f, 0.94f, 0.62f) : Settings.AuraToolsUi.Text;
            }

            SetHint(editingDeck.Count == size ? "预设完整，可以保存。" : "需要配置满 " + size + " 张牌。");
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
        var sprite = AuraToolsStarterDeckRuntime.TryLoadCardIcon(cardId);
        var cell = Settings.AuraToolsUi.CreateLayout("CardIcon", parent);
        var element = Settings.AuraToolsUi.EnsureLayoutElement(cell);
        element.minWidth = AuraToolsStarterDeckRuntime.CardImageColumnWidth;
        element.preferredWidth = AuraToolsStarterDeckRuntime.CardImageColumnWidth;
        element.minHeight = Settings.AuraToolsUi.TextMinHeight;
        element.preferredHeight = Settings.AuraToolsUi.TextMinHeight;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;

        if (sprite == null)
        {
            Settings.AuraToolsUi.AddImage(cell, new Color(0.025f, 0.022f, 0.045f, 0.98f));
            Settings.AuraToolsUi.AddFillText(cell.transform, fallbackText, Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.Accent);
            return;
        }

        var icon = Settings.AuraToolsUi.CreateRect("Image", cell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(AuraToolsStarterDeckRuntime.CardIconSize, AuraToolsStarterDeckRuntime.CardIconSize));
        var image = icon.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
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
