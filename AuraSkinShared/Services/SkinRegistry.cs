using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Newtonsoft.Json;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Models;

namespace AuraSkin.Shared.Services;

public static class SkinRegistry
{
    private const string SkinManifestFileName = "skin.json";
    private static readonly Dictionary<string, SkinDefinition> ByQualifiedId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<SkinDefinition>> BySemanticKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<SkinDefinition>> ByCareer = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> EnabledQualifiedIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AmbiguityWarnings = new(StringComparer.OrdinalIgnoreCase);
    private static bool candidateSelectionConfigured;

    public static IEnumerable<string> CareerIds => ByCareer.Keys;

    public static void Reload()
    {
        ByQualifiedId.Clear();
        BySemanticKey.Clear();
        ByCareer.Clear();
        AmbiguityWarnings.Clear();

        var root = SkinPaths.SkinRootDirectory;
        var activeResources = SkinPackageInstaller.GetActiveResources();
        if (activeResources.Count == 0)
        {
            SkinLog.Info("Shared skin scan found no active package leases; residual files under " + root + " remain inactive");
            return;
        }

        foreach (var resource in activeResources)
        {
            var resolvedDirectory = AuraSharedResourceProtocol.ResolvePath(
                "AuraSkinShared",
                resource.CanonicalRelativePath);
            var manifestPath = Path.Combine(resolvedDirectory, SkinManifestFileName);
            if (!File.Exists(manifestPath))
            {
                SkinLog.Warn("Active shared skin resource is unavailable: " + resource.CanonicalRelativePath);
                continue;
            }
            TryLoadSkin(manifestPath, resource);
        }

        foreach (var list in ByCareer.Values)
        {
            list.Sort(CompareCandidates);
        }

        foreach (var list in BySemanticKey.Values)
        {
            list.Sort(CompareCandidates);
        }

        SkinLog.Info("Discovered " + ByQualifiedId.Count + " shared skin candidate(s) in "
                     + BySemanticKey.Count + " semantic group(s) for " + ByCareer.Count + " career(s)");
    }

    public static IReadOnlyList<SkinDefinition> GetForCareer(string careerId)
    {
        var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
        return !string.IsNullOrWhiteSpace(normalizedCareerId) && ByCareer.TryGetValue(normalizedCareerId, out var list)
            ? list.Where(IsEffectivelyEnabled).ToArray()
            : Array.Empty<SkinDefinition>();
    }

    public static IReadOnlyList<SkinDefinition> GetAllForCareer(string careerId)
    {
        var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
        return !string.IsNullOrWhiteSpace(normalizedCareerId) && ByCareer.TryGetValue(normalizedCareerId, out var list)
            ? list
            : Array.Empty<SkinDefinition>();
    }

    public static IReadOnlyList<SkinDefinition> GetAll()
    {
        return ByQualifiedId.Values.OrderBy(value => value.TargetCareerId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, Comparer<SkinDefinition>.Create(CompareCandidates))
            .ToArray();
    }

    public static IReadOnlyList<SkinDefinition> GetCandidates(string careerId, string skinId)
    {
        return BySemanticKey.TryGetValue(ResourceKey(careerId, skinId), out var candidates)
            ? candidates
            : Array.Empty<SkinDefinition>();
    }

    public static void ConfigureCandidateEnablement(bool configured, IEnumerable<string>? enabledQualifiedSkinIds)
    {
        candidateSelectionConfigured = configured;
        EnabledQualifiedIds.Clear();
        foreach (var value in enabledQualifiedSkinIds ?? Array.Empty<string>())
        {
            var normalized = (value ?? "").Trim();
            if (normalized.Length > 0)
            {
                EnabledQualifiedIds.Add(normalized);
            }
        }
    }

    public static bool IsEffectivelyEnabled(SkinDefinition? skin)
    {
        return skin != null
               && (!candidateSelectionConfigured || EnabledQualifiedIds.Contains(skin.QualifiedSkinId));
    }

    public static SkinDefinition? Find(string careerId, string skinId)
    {
        if (string.IsNullOrWhiteSpace(careerId) || string.IsNullOrWhiteSpace(skinId))
        {
            return null;
        }

        return ResolveReference(careerId, skinId);
    }

    public static SkinDefinition? FindByKey(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey) || !BySemanticKey.TryGetValue(resourceKey, out var candidates))
        {
            return null;
        }

        return candidates.FirstOrDefault(IsEffectivelyEnabled);
    }

    public static SkinDefinition? FindQualified(string qualifiedSkinId, bool effectiveOnly = true)
    {
        if (string.IsNullOrWhiteSpace(qualifiedSkinId)
            || !ByQualifiedId.TryGetValue(qualifiedSkinId.Trim(), out var skin))
        {
            return null;
        }

        return !effectiveOnly || IsEffectivelyEnabled(skin) ? skin : null;
    }

    public static SkinDefinition? ResolveReference(
        string careerId,
        string skinReference,
        string ownerModId = "",
        string contentHash = "",
        bool effectiveOnly = true)
    {
        var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
        var reference = (skinReference ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedCareerId) || string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var exactReference = string.IsNullOrWhiteSpace(ownerModId)
            ? reference
            : SkinDefinition.Qualify(ownerModId, normalizedCareerId, reference);
        var exact = FindQualified(exactReference, effectiveOnly);
        if (exact != null && string.Equals(exact.TargetCareerId, normalizedCareerId, StringComparison.OrdinalIgnoreCase))
        {
            return exact;
        }

        var legacyOwner = "";
        var semanticReference = reference;
        var separator = reference.IndexOf(':');
        if (separator > 0 && reference.IndexOf(':', separator + 1) < 0)
        {
            legacyOwner = reference.Substring(0, separator);
            semanticReference = reference.Substring(separator + 1);
        }

        if (!BySemanticKey.TryGetValue(ResourceKey(normalizedCareerId, semanticReference), out var semanticCandidates))
        {
            return null;
        }

        var candidates = semanticCandidates
            .Where(candidate => !effectiveOnly || IsEffectivelyEnabled(candidate))
            .Where(candidate => string.IsNullOrWhiteSpace(legacyOwner)
                                || string.Equals(candidate.OwnerModId, legacyOwner, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (!string.IsNullOrWhiteSpace(contentHash))
        {
            var hashMatches = candidates
                .Where(candidate => string.Equals(candidate.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (hashMatches.Length > 0)
            {
                candidates = hashMatches;
            }
        }

        if (candidates.Length > 1 && AmbiguityWarnings.Add(ResourceKey(normalizedCareerId, semanticReference)))
        {
            SkinLog.Warn("Ambiguous legacy skin reference " + reference + " for " + normalizedCareerId
                         + "; selected " + candidates[0].QualifiedSkinId
                         + ". Persist a qualified skin id to remove this fallback.");
        }

        return candidates.FirstOrDefault();
    }

    public static string ContentHash(string careerId, string skinId)
    {
        return Find(careerId, skinId)?.ContentHash ?? "";
    }

    public static string ResourceKey(string careerId, string skinId)
    {
        return CareerConfigApi.NormalizeId(careerId).Trim().ToLowerInvariant()
               + "::"
               + (skinId ?? "").Trim().ToLowerInvariant();
    }

    private static void TryLoadSkin(string path, SkinPackageInstaller.RegisteredSkinResource registered)
    {
        try
        {
            var manifest = JsonConvert.DeserializeObject<SkinManifest>(File.ReadAllText(path));
            if (manifest == null || !manifest.Enabled)
            {
                return;
            }

            manifest.SkinId = manifest.SkinId?.Trim() ?? "";
            manifest.TargetCareerId = CareerConfigApi.NormalizeId(manifest.TargetCareerId);
            if (string.IsNullOrWhiteSpace(manifest.TargetCareerId))
            {
                manifest.TargetCareerId = registered.TargetCareerId;
            }
            else if (!string.Equals(manifest.TargetCareerId, registered.TargetCareerId, StringComparison.OrdinalIgnoreCase))
            {
                SkinLog.Warn("Ignored skin whose targetCareerId differs from its shared character folder: " + path);
                return;
            }

            if (manifest.SchemaVersion != 2
                || string.IsNullOrWhiteSpace(manifest.SkinId)
                || string.IsNullOrWhiteSpace(manifest.TargetCareerId))
            {
                SkinLog.Warn("Ignored invalid shared skin manifest: " + path);
                return;
            }

            if (!string.Equals(registered.SkinId, manifest.SkinId, StringComparison.OrdinalIgnoreCase))
            {
                SkinLog.Warn("Ignored non-canonical shared skin directory: " + path);
                return;
            }

            var definition = new SkinDefinition
            {
                OwnerModId = registered.OwnerModId,
                SkinId = manifest.SkinId,
                TargetCareerId = manifest.TargetCareerId,
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.SkinId : manifest.Name.Trim(),
                Author = manifest.Author?.Trim() ?? "",
                ManifestPath = path,
                PreviewPath = SkinPaths.ResolveManifestAsset(path, manifest.Preview, false),
                Assets = ResolveAssets(path, manifest.Assets ?? new SkinAssets()),
                PackageId = registered.PackageId,
                PackageVersion = registered.PackageVersion,
                Priority = registered.Priority
            };
            ApplyInstalledMetadata(definition, registered.OwnerModId);

            if (string.IsNullOrWhiteSpace(definition.Assets.CareerImage)
                && string.IsNullOrWhiteSpace(definition.Assets.Avatar)
                && string.IsNullOrWhiteSpace(definition.Assets.Character)
                && string.IsNullOrWhiteSpace(definition.Assets.DollIcon)
                && string.IsNullOrWhiteSpace(definition.Assets.ChoiceIcon)
                && string.IsNullOrWhiteSpace(definition.Assets.Animation))
            {
                SkinLog.Warn("Ignored shared skin with no valid assets: " + path);
                return;
            }

            if (ByQualifiedId.ContainsKey(definition.QualifiedSkinId))
            {
                SkinLog.Warn("Ignored conflicting qualified skin identity " + definition.QualifiedSkinId + " from " + path);
                return;
            }

            ByQualifiedId.Add(definition.QualifiedSkinId, definition);
            var semanticKey = ResourceKey(definition.TargetCareerId, definition.SkinId);
            if (!BySemanticKey.TryGetValue(semanticKey, out var candidates))
            {
                candidates = new List<SkinDefinition>();
                BySemanticKey.Add(semanticKey, candidates);
            }
            candidates.Add(definition);
            if (!ByCareer.TryGetValue(definition.TargetCareerId, out var skins))
            {
                skins = new List<SkinDefinition>();
                ByCareer.Add(definition.TargetCareerId, skins);
            }

            skins.Add(definition);
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to load shared skin manifest " + path + ": " + ex.Message);
        }
    }

    private static int CompareCandidates(SkinDefinition left, SkinDefinition right)
    {
        var priority = right.Priority.CompareTo(left.Priority);
        if (priority != 0)
        {
            return priority;
        }

        var name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        return name != 0
            ? name
            : string.Compare(left.QualifiedSkinId, right.QualifiedSkinId, StringComparison.OrdinalIgnoreCase);
    }

    private static SkinAssets ResolveAssets(string manifestPath, SkinAssets assets)
    {
        return new SkinAssets
        {
            CareerImage = SkinPaths.ResolveManifestAsset(manifestPath, assets.CareerImage, false),
            Avatar = SkinPaths.ResolveManifestAsset(manifestPath, assets.Avatar, false),
            Character = SkinPaths.ResolveManifestAsset(manifestPath, assets.Character, false),
            DollIcon = SkinPaths.ResolveManifestAsset(manifestPath, assets.DollIcon, false),
            ChoiceIcon = SkinPaths.ResolveManifestAsset(manifestPath, assets.ChoiceIcon, false),
            Animation = SkinPaths.ResolveManifestAsset(manifestPath, assets.Animation, true)
        };
    }

    private static void ApplyInstalledMetadata(SkinDefinition definition, string ownerModId)
    {
        try
        {
            var logicalId = "Skin:Skin:Role:"
                            + definition.TargetCareerId
                            + ":"
                            + ownerModId
                            + ":"
                            + definition.SkinId;
            var resource = AuraSharedPackageEngine.GetResources("AuraSkin", AuraSharedSystems.Skin)
                .FirstOrDefault(item => string.Equals(item.LogicalId, logicalId, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                return;
            }

            definition.ContentHash = resource.ContentHash ?? "";
            var source = resource.Sources?
                .Where(item => string.Equals(item.OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.PackageVersion)
                .ThenBy(item => item.OwnerModId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (source == null)
            {
                return;
            }

            definition.PackageId = source.PackageId ?? "";
            definition.PackageVersion = source.PackageVersion;
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to read installed skin metadata for " + definition.SkinId + ": " + ex.Message);
        }
    }
}
