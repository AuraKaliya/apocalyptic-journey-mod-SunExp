using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Newtonsoft.Json;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace StarterDeckArbiter.Shared;

public sealed class StarterDeckClaim
{
    public string Owner { get; set; } = "";
    public string Scope { get; set; } = "";
    public string ModeId { get; set; } = "";
    public string Source { get; set; } = "";
    public string State { get; set; } = StarterDeckArbiterRuntime.StatePending;
    public string AppliedKey { get; set; } = "";
    public string AppliedModeKey { get; set; } = "";
    public string AppliedMode { get; set; } = "";
    public string LegacyMode { get; set; } = "";
    public int DeckSize { get; set; } = 11;
    public string SourceName { get; set; } = "StarterDeck";
    public bool MarkLegacyCardPackApplied { get; set; } = true;
}

public static class StarterDeckProfileSourceKind
{
    public const string Registered = "Registered";
    public const string Local = "Local";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Local, StringComparison.OrdinalIgnoreCase) ? Local : Registered;
    }
}

public sealed class StarterDeckProfileManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("profiles")]
    public List<StarterDeckProfile> Profiles { get; set; } = new();

    public void Normalize(string fallbackOwner)
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        OwnerModId = Clean(OwnerModId, fallbackOwner);
        Profiles ??= new List<StarterDeckProfile>();
        foreach (var profile in Profiles)
        {
            profile?.Normalize(OwnerModId);
        }
    }

    private static string Clean(string? value, string fallback)
    {
        var result = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(result) ? (fallback ?? "").Trim() : result;
    }
}

public sealed class StarterDeckProfile
{
    [JsonProperty("profileId")]
    public string ProfileId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("modeIds")]
    public List<string> ModeIds { get; set; } = new();

    [JsonProperty("targetRoleIds")]
    public List<string> TargetRoleIds { get; set; } = new();

    [JsonProperty("deckSize")]
    public int DeckSize { get; set; } = 11;

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    [JsonProperty("candidatePackIds")]
    public List<string> CandidatePackIds { get; set; } = new();

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("sourceKind")]
    public string SourceKind { get; set; } = StarterDeckProfileSourceKind.Registered;

    [JsonProperty("editable")]
    public bool Editable { get; set; }

    [JsonProperty("deletable")]
    public bool Deletable { get; set; }

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("derivedFromProfileId")]
    public string DerivedFromProfileId { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonIgnore]
    public string QualifiedProfileId => QualifyProfileId(OwnerModId, ProfileId);

    public StarterDeckProfile Clone()
    {
        return new StarterDeckProfile
        {
            ProfileId = ProfileId,
            OwnerModId = OwnerModId,
            DisplayName = DisplayName,
            ModeIds = ModeIds.ToList(),
            TargetRoleIds = TargetRoleIds.ToList(),
            DeckSize = DeckSize,
            CardIds = CardIds.ToList(),
            CandidatePackIds = CandidatePackIds.ToList(),
            Priority = Priority,
            SourceKind = SourceKind,
            Editable = Editable,
            Deletable = Deletable,
            Enabled = Enabled,
            DerivedFromProfileId = DerivedFromProfileId,
            Description = Description
        };
    }

    public void Normalize(string fallbackOwner)
    {
        OwnerModId = Clean(OwnerModId, fallbackOwner);
        ProfileId = Clean(ProfileId, "default");
        DisplayName = Clean(DisplayName, ProfileId);
        Description = (Description ?? "").Trim();
        DerivedFromProfileId = (DerivedFromProfileId ?? "").Trim();
        ModeIds = CleanList(ModeIds, preserveDuplicates: false);
        TargetRoleIds = CleanList(TargetRoleIds, preserveDuplicates: false);
        CandidatePackIds = CleanList(CandidatePackIds, preserveDuplicates: false);
        CardIds = CleanList(CardIds, preserveDuplicates: true);
        DeckSize = Math.Max(1, DeckSize);
        SourceKind = StarterDeckProfileSourceKind.Normalize(SourceKind);
        if (SourceKind == StarterDeckProfileSourceKind.Registered)
        {
            Editable = false;
            Deletable = false;
        }
    }

    public static string QualifyProfileId(string ownerModId, string profileId)
    {
        var owner = Clean(ownerModId, "UnknownOwner");
        var id = Clean(profileId, "default");
        return owner + ":" + id;
    }

    private static string Clean(string? value, string fallback)
    {
        var result = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(result) ? (fallback ?? "").Trim() : result;
    }

    private static List<string> CleanList(IEnumerable<string>? values, bool preserveDuplicates)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values ?? Array.Empty<string>())
        {
            var value = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!preserveDuplicates && !seen.Add(value))
            {
                continue;
            }

            result.Add(value);
        }

        return result;
    }
}

public sealed class StarterDeckProfileContext
{
    public string ModeId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string RoleOwnerModId { get; set; } = "";

    public string SelectedProfileId { get; set; } = "";
}

public sealed class StarterDeckProfileResolutionPolicy
{
    public bool PreferRoleModProfile { get; set; } = true;

    public bool UseRoleSpecificLocalProfiles { get; set; }

    public bool AllowGlobalLocalProfileFallback { get; set; } = true;

    public bool IncludeNonOwnerRegisteredFallback { get; set; }

    public bool RequireCompleteProfile { get; set; } = true;
}

public static class StarterDeckProfileResolutionReasons
{
    public const string None = "none";
    public const string Selected = "selected";
    public const string RoleOwnerRegistered = "role-owner-registered";
    public const string LocalRole = "local-role";
    public const string LocalGlobal = "local-global";
    public const string RegisteredFallback = "registered-fallback";
}

public static class StarterDeckProfileValidationIssues
{
    public const string Disabled = "disabled";
    public const string ModeMismatch = "mode-mismatch";
    public const string RoleMismatch = "role-mismatch";
    public const string EmptyDeck = "empty-deck";
    public const string DeckSizeMismatch = "deck-size-mismatch";
    public const string CandidatePacksNotResolved = "candidate-packs-not-resolved";
}

public sealed class StarterDeckProfileValidationResult
{
    public string ProfileId { get; set; } = "";

    public string QualifiedProfileId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public int DeckSize { get; set; }

    public int DeckCount { get; set; }

    public bool Eligible { get; set; }

    public bool Complete { get; set; }

    public List<string> Issues { get; set; } = new();

    public string Summary => Complete
        ? "complete"
        : string.Join("|", Issues.Distinct(StringComparer.OrdinalIgnoreCase));
}

public sealed class StarterDeckProfileResolutionResult
{
    public StarterDeckProfile? Profile { get; set; }

    public string Reason { get; set; } = StarterDeckProfileResolutionReasons.None;

    public List<StarterDeckProfile> Candidates { get; set; } = new();

    public bool Found => Profile != null;
}

public static class StarterDeckArbiterRuntime
{
    public const string ProfileSystem = "StarterDeck";
    public const string ProfileKind = "StarterDeckProfile";
    public const string ProfileJsonMetadataKey = "profileJson";
    public const string OwnerKey = "StarterDeck.Owner";
    public const string ScopeKey = "StarterDeck.Scope";
    public const string StateKey = "StarterDeck.State";
    public const string SourceKey = "StarterDeck.Source";
    public const string ModeKey = "StarterDeck.Mode";
    public const string CardsKey = "StarterDeck.Cards";
    public const string LegacyCardPackAppliedKey = "CardPackExp.StarterDeckApplied";
    public const string StatePending = "pending";
    public const string StateApplied = "applied";
    public const string StateOfficial = "official";

    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags PublicOrPrivateStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PublicOrPrivateInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly HashSet<string> PersistentProfilesLoadedFor = new(StringComparer.OrdinalIgnoreCase);

    public static bool RegisterProfileManifest(ModConfig? modConfig, string ownerModId, string manifestRelativePath = "starterdeck.registry.json")
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        var modRoot = modConfig?.DirectoryName ?? AuraSharedPaths.PackageDirectory;
        var manifestPath = string.IsNullOrWhiteSpace(manifestRelativePath)
            ? ""
            : Path.Combine(modRoot, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return RegisterProfileManifestPath(ownerModId, manifestPath, modRoot);
    }

    public static bool RegisterProfileManifestPath(string ownerModId, string manifestPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            return RegisterProfileManifestJson(ownerModId, File.ReadAllText(manifestPath), baseDirectory, manifestPath);
        }
        catch (Exception ex)
        {
            Warn("Profile manifest failed: " + manifestPath + " -> " + RootMessage(ex));
            return false;
        }
    }

    public static bool RegisterProfileManifestJson(string ownerModId, string manifestJson, string baseDirectory, string sourcePath = "")
    {
        try
        {
            var manifest = JsonConvert.DeserializeObject<StarterDeckProfileManifest>(manifestJson);
            if (manifest == null)
            {
                return false;
            }

            manifest.Normalize(ownerModId);
            var count = 0;
            foreach (var profile in manifest.Profiles)
            {
                if (profile == null || !profile.Enabled || string.IsNullOrWhiteSpace(profile.ProfileId))
                {
                    continue;
                }

                profile.Normalize(manifest.OwnerModId);
                if (profile.CardIds.Count == 0 && profile.CandidatePackIds.Count == 0)
                {
                    continue;
                }

                if (RegisterProfileResource(profile, sourcePath, baseDirectory))
                {
                    count++;
                }
            }

            if (count > 0)
            {
                Log("Registered starter deck profiles. owner=" + manifest.OwnerModId + ", count=" + count);
            }

            return count > 0;
        }
        catch (Exception ex)
        {
            Warn("Profile manifest json failed for " + ownerModId + ": " + RootMessage(ex));
            return false;
        }
    }

    public static IReadOnlyList<StarterDeckProfile> GetRegisteredProfiles(string callerModId, bool includePersistent = true)
    {
        if (includePersistent)
        {
            LoadPersistentProfilesOnce(callerModId);
        }

        var profiles = new List<StarterDeckProfile>();
        foreach (var record in AuraSharedRegistry.GetResources(callerModId, ProfileSystem))
        {
            if (!record.Enabled || !string.Equals(record.Kind, ProfileKind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var profile = ProfileFromRecord(record);
            if (profile == null || !profile.Enabled)
            {
                continue;
            }

            profile.SourceKind = StarterDeckProfileSourceKind.Registered;
            profile.Editable = false;
            profile.Deletable = false;
            profile.Normalize(record.OwnerModId);
            profiles.Add(profile);
        }

        return profiles
            .OrderByDescending(profile => profile.Priority)
            .ThenBy(profile => profile.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static StarterDeckProfile? ResolveRegisteredProfile(IEnumerable<StarterDeckProfile> profiles, StarterDeckProfileContext context)
    {
        var normalizedContext = NormalizeContext(context);
        return profiles
            .Where(profile => IsProfileEligible(profile, normalizedContext))
            .OrderByDescending(profile => IsSelectedProfile(profile, normalizedContext))
            .ThenByDescending(profile => OwnerMatchesRole(profile.OwnerModId, normalizedContext.RoleId, normalizedContext.RoleOwnerModId))
            .ThenByDescending(profile => RoleMatchScore(profile, normalizedContext.RoleId))
            .ThenByDescending(profile => profile.Priority)
            .ThenBy(profile => profile.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static StarterDeckProfileResolutionResult ResolveEffectiveProfile(
        IEnumerable<StarterDeckProfile> profiles,
        StarterDeckProfileContext context,
        StarterDeckProfileResolutionPolicy? policy = null,
        Func<StarterDeckProfile, bool>? isProfileComplete = null)
    {
        var normalizedContext = NormalizeContext(context);
        var resolvedPolicy = policy ?? new StarterDeckProfileResolutionPolicy();
        var candidates = SortCandidateProfiles(profiles, normalizedContext, resolvedPolicy).ToList();
        var complete = BuildCompletenessPredicate(resolvedPolicy, isProfileComplete);

        var selected = candidates.FirstOrDefault(profile => IsSelectedProfile(profile, normalizedContext) && complete(profile));
        if (selected != null)
        {
            return Resolution(selected, StarterDeckProfileResolutionReasons.Selected, candidates);
        }

        if (resolvedPolicy.PreferRoleModProfile)
        {
            var ownerProfile = candidates.FirstOrDefault(profile =>
                IsRegisteredProfile(profile)
                && IsRoleOwnerProfile(profile, normalizedContext.RoleId, normalizedContext.RoleOwnerModId)
                && complete(profile));
            if (ownerProfile != null)
            {
                return Resolution(ownerProfile, StarterDeckProfileResolutionReasons.RoleOwnerRegistered, candidates);
            }
        }

        if (resolvedPolicy.UseRoleSpecificLocalProfiles)
        {
            var localRole = candidates.FirstOrDefault(profile => IsLocalRoleProfile(profile, normalizedContext.RoleId) && complete(profile));
            if (localRole != null)
            {
                return Resolution(localRole, StarterDeckProfileResolutionReasons.LocalRole, candidates);
            }
        }

        if (resolvedPolicy.AllowGlobalLocalProfileFallback)
        {
            var localGlobal = candidates.FirstOrDefault(profile => IsLocalGlobalProfile(profile) && complete(profile));
            if (localGlobal != null)
            {
                return Resolution(localGlobal, StarterDeckProfileResolutionReasons.LocalGlobal, candidates);
            }
        }

        if (resolvedPolicy.IncludeNonOwnerRegisteredFallback)
        {
            var registered = candidates.FirstOrDefault(profile => IsRegisteredProfile(profile) && complete(profile));
            if (registered != null)
            {
                return Resolution(registered, StarterDeckProfileResolutionReasons.RegisteredFallback, candidates);
            }
        }

        return new StarterDeckProfileResolutionResult
        {
            Candidates = candidates
        };
    }

    public static IReadOnlyList<StarterDeckProfile> SortCandidateProfiles(
        IEnumerable<StarterDeckProfile> profiles,
        StarterDeckProfileContext context,
        StarterDeckProfileResolutionPolicy? policy = null)
    {
        var normalizedContext = NormalizeContext(context);
        return (profiles ?? Array.Empty<StarterDeckProfile>())
            .Where(profile => profile != null && IsProfileEligible(profile, normalizedContext))
            .OrderByDescending(profile => IsSelectedProfile(profile, normalizedContext))
            .ThenByDescending(profile => IsRoleOwnerProfile(profile, normalizedContext.RoleId, normalizedContext.RoleOwnerModId))
            .ThenByDescending(profile => IsLocalRoleProfile(profile, normalizedContext.RoleId))
            .ThenByDescending(IsLocalGlobalProfile)
            .ThenByDescending(profile => RoleMatchScore(profile, normalizedContext.RoleId))
            .ThenByDescending(profile => profile.Priority)
            .ThenBy(profile => IsRegisteredProfile(profile) ? 0 : 1)
            .ThenBy(profile => profile.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static StarterDeckProfileValidationResult ValidateProfile(
        StarterDeckProfile profile,
        StarterDeckProfileContext? context = null,
        Func<StarterDeckProfile, IEnumerable<string>>? resolveDeck = null)
    {
        profile.Normalize(profile.OwnerModId);
        var normalizedContext = NormalizeContext(context);
        var result = new StarterDeckProfileValidationResult
        {
            ProfileId = profile.ProfileId,
            QualifiedProfileId = profile.QualifiedProfileId,
            OwnerModId = profile.OwnerModId,
            DeckSize = profile.DeckSize
        };

        if (!profile.Enabled)
        {
            result.Issues.Add(StarterDeckProfileValidationIssues.Disabled);
        }

        if (!string.IsNullOrWhiteSpace(normalizedContext.ModeId) && !ModeMatches(profile, normalizedContext.ModeId))
        {
            result.Issues.Add(StarterDeckProfileValidationIssues.ModeMismatch);
        }

        if (!string.IsNullOrWhiteSpace(normalizedContext.RoleId) && !RoleMatches(profile, normalizedContext.RoleId))
        {
            result.Issues.Add(StarterDeckProfileValidationIssues.RoleMismatch);
        }

        if (profile.CandidatePackIds.Count > 0 && resolveDeck == null)
        {
            result.Issues.Add(StarterDeckProfileValidationIssues.CandidatePacksNotResolved);
        }

        var deck = resolveDeck == null
            ? profile.CardIds.Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal)).Take(profile.DeckSize).ToList()
            : resolveDeck(profile).Where(id => !string.IsNullOrWhiteSpace(id)).Take(profile.DeckSize).ToList();
        result.DeckCount = deck.Count;

        if (result.DeckCount == 0)
        {
            result.Issues.Add(StarterDeckProfileValidationIssues.EmptyDeck);
        }

        if (result.DeckCount != profile.DeckSize)
        {
            result.Issues.Add(StarterDeckProfileValidationIssues.DeckSizeMismatch);
        }

        result.Eligible = !result.Issues.Contains(StarterDeckProfileValidationIssues.Disabled)
                          && !result.Issues.Contains(StarterDeckProfileValidationIssues.ModeMismatch)
                          && !result.Issues.Contains(StarterDeckProfileValidationIssues.RoleMismatch);
        result.Complete = result.Eligible && result.DeckCount == profile.DeckSize;
        return result;
    }

    public static bool IsProfileEligible(StarterDeckProfile profile, StarterDeckProfileContext context)
    {
        if (profile == null || !profile.Enabled)
        {
            return false;
        }

        profile.Normalize(profile.OwnerModId);
        var normalizedContext = NormalizeContext(context);
        return ModeMatches(profile, normalizedContext.ModeId)
               && RoleMatches(profile, normalizedContext.RoleId)
               && (profile.CardIds.Count > 0 || profile.CandidatePackIds.Count > 0);
    }

    public static bool OwnerMatchesRole(string ownerModId, string roleId, string roleOwnerModId = "")
    {
        var owner = (ownerModId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(owner))
        {
            return false;
        }

        var explicitOwner = (roleOwnerModId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(explicitOwner)
            && string.Equals(owner, explicitOwner, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var role = NormalizeRoleId(roleId);
        return role.StartsWith(owner + "_", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRoleOwnerProfile(StarterDeckProfile profile, string roleId, string roleOwnerModId = "")
    {
        if (OwnerMatchesRole(profile.OwnerModId, roleId, roleOwnerModId))
        {
            return true;
        }

        return profile.TargetRoleIds.Any(targetRoleId => OwnerMatchesRole(profile.OwnerModId, targetRoleId));
    }

    public static bool IsLocalRoleProfile(StarterDeckProfile profile, string roleId)
    {
        return IsLocalProfile(profile)
               && profile.TargetRoleIds.Count > 0
               && RoleMatches(profile, roleId);
    }

    public static bool IsLocalGlobalProfile(StarterDeckProfile profile)
    {
        return IsLocalProfile(profile) && profile.TargetRoleIds.Count == 0;
    }

    public static bool IsLocalProfile(StarterDeckProfile profile)
    {
        return string.Equals(profile.SourceKind, StarterDeckProfileSourceKind.Local, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRegisteredProfile(StarterDeckProfile profile)
    {
        return string.Equals(profile.SourceKind, StarterDeckProfileSourceKind.Registered, StringComparison.OrdinalIgnoreCase);
    }

    public static string InferOwnerModId(string roleId, IEnumerable<string>? knownOwners = null)
    {
        var role = NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(role)
            || role.StartsWith(AuraSharedIdentity.OfficialCareerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        foreach (var owner in knownOwners ?? Array.Empty<string>())
        {
            var cleanOwner = (owner ?? "").Trim();
            if (cleanOwner.Length > 0
                && (string.Equals(role, cleanOwner, StringComparison.OrdinalIgnoreCase)
                    || role.StartsWith(cleanOwner + "_", StringComparison.OrdinalIgnoreCase)))
            {
                return cleanOwner;
            }
        }

        var index = role.IndexOf('_');
        return index > 0 ? role.Substring(0, index) : "";
    }

    public static bool RoleMatches(StarterDeckProfile profile, string roleId)
    {
        return RoleMatchScore(profile, roleId) > 0;
    }

    public static int RoleMatchScore(StarterDeckProfile profile, string roleId)
    {
        var role = NormalizeRoleId(roleId);
        if (profile.TargetRoleIds == null || profile.TargetRoleIds.Count == 0)
        {
            return 1;
        }

        var targets = profile.TargetRoleIds
            .Select(NormalizeRoleId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var target in targets)
        {
            if (string.Equals(role, target, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (role.EndsWith("_" + target, StringComparison.OrdinalIgnoreCase)
                || target.EndsWith("_" + role, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }
        }

        return targets.Count(target => AuraSharedContentId.Resolve(
                   target,
                   new[] { role },
                   profile.OwnerModId,
                   AuraSharedIdentity.OfficialCareerPrefix).Success) == 1
            ? 2
            : 0;
    }

    private static bool RegisterProfileResource(StarterDeckProfile profile, string sourcePath, string baseDirectory)
    {
        var resource = new AuraSharedResourceRecord
        {
            System = ProfileSystem,
            ResourceId = StarterDeckProfile.QualifyProfileId(profile.OwnerModId, profile.ProfileId),
            OwnerModId = profile.OwnerModId,
            Kind = ProfileKind,
            Path = string.IsNullOrWhiteSpace(sourcePath) ? "" : Path.GetFileName(sourcePath),
            AbsolutePath = sourcePath,
            SourceRoot = baseDirectory,
            TargetRoleIds = profile.TargetRoleIds.ToArray(),
            Tags = new[] { "starter-deck-profile", "readonly" },
            Priority = profile.Priority,
            Enabled = profile.Enabled,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ProfileJsonMetadataKey] = AuraSharedJson.Serialize(profile)
            }
        };

        return AuraSharedRegistry.RegisterResource(profile.OwnerModId, resource);
    }

    private static void LoadPersistentProfilesOnce(string callerModId)
    {
        var caller = string.IsNullOrWhiteSpace(callerModId) ? "UnknownOwner" : callerModId.Trim();
        lock (PersistentProfilesLoadedFor)
        {
            if (!PersistentProfilesLoadedFor.Add(caller))
            {
                return;
            }
        }

        try
        {
            AuraSharedRegistry.LoadPersistentManifests(caller, ProfileSystem);
        }
        catch (Exception ex)
        {
            Warn("Persistent profile manifest load failed for " + caller + ": " + RootMessage(ex));
        }
    }

    private static StarterDeckProfile? ProfileFromRecord(AuraSharedResourceRecord record)
    {
        try
        {
            if (record.Metadata != null
                && record.Metadata.TryGetValue(ProfileJsonMetadataKey, out var json)
                && !string.IsNullOrWhiteSpace(json))
            {
                var profile = JsonConvert.DeserializeObject<StarterDeckProfile>(json);
                if (profile != null)
                {
                    if (string.IsNullOrWhiteSpace(profile.OwnerModId))
                    {
                        profile.OwnerModId = record.OwnerModId;
                    }

                    if (string.IsNullOrWhiteSpace(profile.ProfileId))
                    {
                        profile.ProfileId = record.ResourceId;
                    }

                    return profile;
                }
            }
        }
        catch (Exception ex)
        {
            Warn("Profile resource json failed: " + record.ResourceId + " -> " + RootMessage(ex));
        }

        var fallback = new StarterDeckProfile
        {
            ProfileId = record.ResourceId,
            OwnerModId = record.OwnerModId,
            DisplayName = record.ResourceId,
            TargetRoleIds = record.TargetRoleIds?.ToList() ?? new List<string>(),
            Priority = record.Priority,
            Enabled = record.Enabled
        };
        fallback.Normalize(record.OwnerModId);
        return fallback.CardIds.Count > 0 || fallback.CandidatePackIds.Count > 0 ? fallback : null;
    }

    private static StarterDeckProfileResolutionResult Resolution(
        StarterDeckProfile profile,
        string reason,
        List<StarterDeckProfile> candidates)
    {
        return new StarterDeckProfileResolutionResult
        {
            Profile = profile,
            Reason = reason,
            Candidates = candidates
        };
    }

    private static Func<StarterDeckProfile, bool> BuildCompletenessPredicate(
        StarterDeckProfileResolutionPolicy policy,
        Func<StarterDeckProfile, bool>? isProfileComplete)
    {
        if (!policy.RequireCompleteProfile)
        {
            return _ => true;
        }

        return isProfileComplete ?? (profile =>
            profile.CardIds.Count(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal)) == profile.DeckSize);
    }

    private static StarterDeckProfileContext NormalizeContext(StarterDeckProfileContext? context)
    {
        return new StarterDeckProfileContext
        {
            ModeId = (context?.ModeId ?? "").Trim(),
            RoleId = NormalizeRoleId(context?.RoleId),
            RoleOwnerModId = (context?.RoleOwnerModId ?? "").Trim(),
            SelectedProfileId = (context?.SelectedProfileId ?? "").Trim()
        };
    }

    private static bool IsSelectedProfile(StarterDeckProfile profile, StarterDeckProfileContext context)
    {
        return !string.IsNullOrWhiteSpace(context.SelectedProfileId)
               && (string.Equals(context.SelectedProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(context.SelectedProfileId, profile.QualifiedProfileId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ModeMatches(StarterDeckProfile profile, string modeId)
    {
        var mode = (modeId ?? "").Trim();
        return profile.ModeIds == null
               || profile.ModeIds.Count == 0
               || profile.ModeIds.Any(candidate => string.Equals(candidate, mode, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRoleId(string? roleId)
    {
        return AuraSharedIdentity.NormalizeRoleId(roleId);
    }

    public static bool ApplyDeck(
        RoleTable? roleTable,
        IEnumerable<string> cardIds,
        StarterDeckClaim claim,
        Func<string, bool>? rejectCard = null,
        bool sync = true)
    {
        if (roleTable == null)
        {
            Warn("ApplyDeck skipped: role table is null. owner=" + claim.Owner);
            return false;
        }

        if (roleTable.cardList == null)
        {
            Warn("ApplyDeck skipped: role card list is null. owner=" + claim.Owner);
            return false;
        }

        var cards = NormalizeDeck(cardIds, rejectCard);
        if (claim.DeckSize > 0 && cards.Count != claim.DeckSize)
        {
            Warn("ApplyDeck skipped: deck size mismatch. owner="
                + claim.Owner
                + ", expected="
                + claim.DeckSize
                + ", actual="
                + cards.Count);
            return false;
        }

        roleTable.cardList.Clear();
        foreach (var cardId in cards)
        {
            roleTable.cardList.Add(new DataConfig(cardId, DataType.Card));
        }

        NormalizeRoleCounts(roleTable);
        WriteClaim(roleTable, claim, StateApplied, string.Join("|", cards));
        if (sync)
        {
            SyncRoleTable(roleTable, claim.SourceName + ".ApplyDeck");
        }

        Log("Applied deck. owner=" + claim.Owner + ", scope=" + claim.Scope + ", cards=" + cards.Count);
        return true;
    }

    public static void ClaimOwnership(RoleTable? roleTable, StarterDeckClaim claim, string state, bool sync)
    {
        if (roleTable == null)
        {
            return;
        }

        WriteClaim(roleTable, claim, string.IsNullOrWhiteSpace(state) ? StatePending : state, null);
        if (sync)
        {
            SyncRoleTable(roleTable, claim.SourceName + ".ClaimOwnership");
        }
    }

    public static void KeepOfficialDeck(RoleTable? roleTable, StarterDeckClaim claim, bool sync = true)
    {
        if (roleTable == null)
        {
            return;
        }

        NormalizeRoleCounts(roleTable);
        WriteClaim(roleTable, claim, StateOfficial, null);
        if (sync)
        {
            SyncRoleTable(roleTable, claim.SourceName + ".KeepOfficialDeck");
        }

        Log("Kept official deck. owner=" + claim.Owner + ", scope=" + claim.Scope);
    }

    public static bool HasApplied(RoleTable? roleTable, string appliedKey, string owner)
    {
        if (roleTable?.SpecialVarMap == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(appliedKey)
            && roleTable.SpecialVarMap.TryGetValue(appliedKey, out var applied)
            && applied == "1")
        {
            return true;
        }

        return roleTable.SpecialVarMap.TryGetValue(OwnerKey, out var currentOwner)
            && string.Equals(currentOwner, owner, StringComparison.OrdinalIgnoreCase)
            && roleTable.SpecialVarMap.TryGetValue(StateKey, out var state)
            && IsFinishedState(state);
    }

    public static bool IsOwnedByOther(RoleTable? roleTable, string owner, out string otherOwner)
    {
        otherOwner = "";
        if (roleTable?.SpecialVarMap == null)
        {
            return false;
        }

        if (!roleTable.SpecialVarMap.TryGetValue(OwnerKey, out var currentOwner)
            || string.IsNullOrWhiteSpace(currentOwner)
            || string.Equals(currentOwner, owner, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        otherOwner = currentOwner;
        return true;
    }

    public static void SyncRoleTable(RoleTable? roleTable, string source)
    {
        if (roleTable == null)
        {
            return;
        }

        TryUpdateSaveRole(roleTable, source);
        TryCmdSyncRoleTable(roleTable, source);
    }

    private static List<string> NormalizeDeck(IEnumerable<string> cardIds, Func<string, bool>? rejectCard)
    {
        var cards = new List<string>();
        foreach (var cardId in cardIds)
        {
            if (string.IsNullOrWhiteSpace(cardId))
            {
                continue;
            }

            var id = cardId.Trim();
            if (rejectCard != null && rejectCard(id))
            {
                continue;
            }

            cards.Add(id);
        }

        return cards;
    }

    private static void NormalizeRoleCounts(RoleTable roleTable)
    {
        if (roleTable.cardList == null)
        {
            return;
        }

        roleTable.CardTopCount = Math.Max(roleTable.CardTopCount, roleTable.cardList.Count);
        roleTable.CardBottomCount = Math.Min(roleTable.CardBottomCount, roleTable.cardList.Count);
    }

    private static void WriteClaim(RoleTable roleTable, StarterDeckClaim claim, string state, string? deckCards)
    {
        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        WriteIfNotEmpty(roleTable.SpecialVarMap, OwnerKey, claim.Owner);
        WriteIfNotEmpty(roleTable.SpecialVarMap, ScopeKey, claim.Scope);
        WriteIfNotEmpty(roleTable.SpecialVarMap, StateKey, state);
        WriteIfNotEmpty(roleTable.SpecialVarMap, SourceKey, claim.Source);
        WriteIfNotEmpty(roleTable.SpecialVarMap, ModeKey, claim.ModeId);

        if (!string.IsNullOrWhiteSpace(deckCards))
        {
            roleTable.SpecialVarMap[CardsKey] = deckCards;
        }

        if (IsFinishedState(state))
        {
            WriteIfNotEmpty(roleTable.SpecialVarMap, claim.AppliedKey, "1");
            WriteIfNotEmpty(roleTable.SpecialVarMap, claim.AppliedModeKey, claim.AppliedMode);
        }

        if (!claim.MarkLegacyCardPackApplied)
        {
            return;
        }

        roleTable.SpecialVarMap[LegacyCardPackAppliedKey] = "1";
        if (!string.IsNullOrWhiteSpace(claim.LegacyMode))
        {
            roleTable.SpecialVarMap[LegacyCardPackAppliedKey + ".Mode"] = claim.LegacyMode;
        }
    }

    private static void WriteIfNotEmpty(IDictionary<string, string> map, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            map[key] = value ?? "";
        }
    }

    private static bool IsFinishedState(string state)
    {
        return string.Equals(state, StateApplied, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, StateOfficial, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryUpdateSaveRole(RoleTable roleTable, string source)
    {
        try
        {
            var type = FindType("Data.Save.GameSaveManager") ?? FindType("GameSaveManager");
            if (IsClientOnlySession())
            {
                Log("Skipped GameSaveManager.UpdateRoles on client-only session. source=" + source);
                return;
            }

            if (!HasWritableSaveRoleTable(type))
            {
                Log("Skipped GameSaveManager.UpdateRoles before writable save role table. source=" + source);
                return;
            }

            var method = type?.GetMethod("UpdateRoles", PublicStatic, null, new[] { typeof(RoleTable) }, null);
            method?.Invoke(null, new object[] { roleTable });
        }
        catch (Exception ex)
        {
            Warn("GameSaveManager.UpdateRoles failed from " + source + ": " + RootMessage(ex));
        }
    }

    private static void TryCmdSyncRoleTable(RoleTable roleTable, string source)
    {
        try
        {
            if (!IsNetworkClientReady())
            {
                Log("Skipped CmdSyncRoleTable before client ready. source=" + source);
                return;
            }

            var playerManager = StaticMemberValue(FindType("PlayerManager"), "Instance");
            var method = playerManager?.GetType().GetMethod("CmdSyncRoleTable", PublicOrPrivateInstance, null, new[] { typeof(RoleTable) }, null);
            method?.Invoke(playerManager, new object[] { roleTable });
        }
        catch (Exception ex)
        {
            Warn("PlayerManager.CmdSyncRoleTable failed from " + source + ": " + RootMessage(ex));
        }
    }

    private static bool IsClientOnlySession()
    {
        try
        {
            var playerManager = StaticMemberValue(FindType("PlayerManager"), "Instance");
            if (playerManager == null)
            {
                return false;
            }

            if (InstanceMemberValue(playerManager, "isClientOnly") is bool isClientOnly && isClientOnly)
            {
                return true;
            }

            if (InstanceMemberValue(playerManager, "isServer") is bool isServer)
            {
                return !isServer;
            }
        }
        catch
        {
            // Fall back to the legacy local-save path when multiplayer state cannot be inspected.
        }

        return false;
    }

    private static bool HasWritableSaveRoleTable(Type? gameSaveManagerType)
    {
        if (gameSaveManagerType == null)
        {
            return false;
        }

        try
        {
            var getNowSave = gameSaveManagerType.GetMethod("GetNowSave", PublicStatic, null, Type.EmptyTypes, null);
            if (getNowSave == null)
            {
                return true;
            }

            var save = getNowSave.Invoke(null, Array.Empty<object>());
            return InstanceMemberValue(save, "roleTable") != null;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsNetworkClientReady()
    {
        try
        {
            var networkClient = FindType("Mirror.NetworkClient");
            var value = StaticMemberValue(networkClient, "ready");
            return value is bool ready && ready;
        }
        catch
        {
            return false;
        }
    }

    private static object? StaticMemberValue(Type? type, string memberName)
    {
        if (type == null)
        {
            return null;
        }

        return type.GetProperty(memberName, PublicOrPrivateStatic)?.GetValue(null)
            ?? type.GetField(memberName, PublicOrPrivateStatic)?.GetValue(null);
    }

    private static object? InstanceMemberValue(object? source, string memberName)
    {
        if (source == null)
        {
            return null;
        }

        var type = source.GetType();
        return type.GetProperty(memberName, PublicOrPrivateInstance)?.GetValue(source)
            ?? type.GetField(memberName, PublicOrPrivateInstance)?.GetValue(source);
    }

    private static Type? FindType(string fullNameOrName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = null;
            try
            {
                type = assembly.GetType(fullNameOrName, false);
            }
            catch
            {
                // ignored
            }

            if (type != null)
            {
                return type;
            }

            foreach (var candidate in SafeTypes(assembly))
            {
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.FullName, fullNameOrName, StringComparison.Ordinal)
                    || string.Equals(candidate.Name, fullNameOrName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<Type?> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static string RootMessage(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: { } inner }
            ? inner.Message
            : ex.Message;
    }

    private static void Log(string message)
    {
        Debug.Log("[StarterDeckArbiter] " + message);
    }

    private static void Warn(string message)
    {
        Debug.LogWarning("[StarterDeckArbiter] " + message);
    }
}
