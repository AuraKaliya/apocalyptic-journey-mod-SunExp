using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;

namespace AuraCg.Shared;

public static class AuraCgCatalogQueryService
{
    public static AuraCgCatalogSnapshot QueryRegisteredResources(
        string callerId,
        string featureId,
        string scopeType = "",
        string scopeId = "")
    {
        var snapshot = AuraSharedResourceProtocol.QueryCatalog(callerId, new AuraSharedCatalogQueryV4
        {
            ModuleId = AuraSharedSystems.Cg,
            FeatureId = featureId,
            ScopeType = scopeType,
            ScopeId = scopeId
        });
        var entries = snapshot.Entries
            .Where(entry => entry.Active && entry.Available)
            .Select(Project)
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new AuraCgCatalogSnapshot(snapshot.Revision, entries);
    }

    public static HashSet<string> GetActiveResourceKeys(
        string callerId,
        string featureId,
        string scopeType = "",
        string scopeId = "")
    {
        var snapshot = AuraSharedResourceProtocol.QueryCatalog(callerId, new AuraSharedCatalogQueryV4
        {
            ModuleId = AuraSharedSystems.Cg,
            FeatureId = featureId,
            ScopeType = scopeType,
            ScopeId = scopeId
        });
        return snapshot.Entries
            .Where(entry => entry.Active && entry.Available)
            .SelectMany(entry => new[]
            {
                ResourceKey(entry.OwnerModId, entry.Resource.ResourceId),
                PathKey(entry.OwnerModId, entry.CanonicalPath)
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsActive(ISet<string>? activeRegistrationKeys, AuraCgRegistryEntry? entry)
    {
        if (activeRegistrationKeys == null || entry == null)
        {
            return false;
        }

        return activeRegistrationKeys.Contains(ResourceKey(entry.OwnerModId, entry.CgId))
               || activeRegistrationKeys.Contains(PathKey(entry.OwnerModId, entry.Media?.Resource ?? ""))
               || activeRegistrationKeys.Contains(PathKey(entry.OwnerModId, entry.Media?.FallbackImage ?? ""));
    }

    private static string ResourceKey(string ownerModId, string resourceId)
    {
        return (ownerModId ?? "").Trim() + ":" + (resourceId ?? "").Trim();
    }

    private static string PathKey(string ownerModId, string relativePath)
    {
        return (ownerModId ?? "").Trim()
               + "|path:"
               + AuraSharedPaths.NormalizeRelativePath(relativePath ?? "");
    }

    private static AuraCgCatalogResource Project(AuraSharedCatalogEntryV4 entry)
    {
        var metadata = entry.Resource.Metadata
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new AuraCgCatalogResource
        {
            OwnerModId = entry.OwnerModId,
            ParticipantKind = entry.ParticipantKind,
            PackageId = entry.PackageId,
            PackageVersion = entry.PackageVersion,
            OriginKind = entry.Resource.OriginKind,
            FeatureId = entry.Resource.FeatureId,
            ScopeType = entry.Resource.ScopeType,
            ScopeId = entry.Resource.ScopeId,
            ScopeOwnerModId = entry.Resource.ScopeOwnerModId,
            ScopeAliases = (entry.Resource.ScopeAliases ?? new List<string>()).ToArray(),
            ResourceId = entry.Resource.ResourceId,
            QualifiedResourceId = entry.QualifiedResourceId,
            DisplayName = Metadata(metadata, "displayName", entry.Resource.ResourceId),
            MediaType = Metadata(metadata, "mediaType", "image"),
            CanonicalResource = AuraSharedPaths.NormalizeRelativePath(entry.CanonicalPath),
            Priority = entry.Resource.Priority,
            Tags = (entry.Resource.Tags ?? new List<string>()).ToArray(),
            EnabledByDefault = MetadataBool(metadata, "enabled", true),
            Presentation = new AuraCgPresentationSpec
            {
                Mode = Metadata(metadata, "presentationMode", SkillCgPresentationModes.FullscreenFade),
                Fit = Metadata(metadata, "fit", SkillCgFitModes.Cover),
                FadeIn = MetadataFloat(metadata, "fadeIn", 0.35f),
                Hold = MetadataFloat(metadata, "hold", 1.5f),
                FadeOut = MetadataFloat(metadata, "fadeOut", 0.5f),
                FocusX = MetadataFloat(metadata, "focusX", 0.5f),
                FocusY = MetadataFloat(metadata, "focusY", 0.5f),
                SafeScale = MetadataFloat(metadata, "safeScale", 1f)
            }
        };
    }

    private static string Metadata(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string fallback)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private static bool MetadataBool(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        bool fallback)
    {
        return metadata.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static float MetadataFloat(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        float fallback)
    {
        return metadata.TryGetValue(key, out var value)
               && float.TryParse(
                   value,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var parsed)
            ? parsed
            : fallback;
    }
}

public sealed class AuraCgCatalogSnapshot
{
    public AuraCgCatalogSnapshot(long revision, IReadOnlyList<AuraCgCatalogResource> entries)
    {
        Revision = Math.Max(0, revision);
        Entries = entries ?? Array.Empty<AuraCgCatalogResource>();
    }

    public long Revision { get; }

    public IReadOnlyList<AuraCgCatalogResource> Entries { get; }
}

public sealed class AuraCgCatalogResource
{
    public string OwnerModId { get; set; } = "";
    public string ParticipantKind { get; set; } = "";
    public string PackageId { get; set; } = "";
    public long PackageVersion { get; set; }
    public string OriginKind { get; set; } = "";
    public string FeatureId { get; set; } = "";
    public string ScopeType { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string ScopeOwnerModId { get; set; } = "";
    public IReadOnlyList<string> ScopeAliases { get; set; } = Array.Empty<string>();
    public string ResourceId { get; set; } = "";
    public string QualifiedResourceId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string MediaType { get; set; } = "image";
    public string CanonicalResource { get; set; } = "";
    public int Priority { get; set; }
    public bool EnabledByDefault { get; set; } = true;
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    public AuraCgPresentationSpec Presentation { get; set; } = new();
}
