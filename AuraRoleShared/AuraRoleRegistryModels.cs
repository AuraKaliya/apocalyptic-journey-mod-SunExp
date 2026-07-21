using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraRole.Shared;

public sealed class AuraRoleRegistryEntry
{
    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("aliases")]
    public List<string> Aliases { get; set; } = new();

    [JsonProperty("packBelong")]
    public string PackBelong { get; set; } = "";

    [JsonProperty("icon")]
    public string Icon { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    public void Normalize(string fallbackOwnerModId = "")
    {
        RoleId = AuraShared.Core.AuraSharedIdentity.NormalizeRoleId(RoleId);
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? (fallbackOwnerModId ?? "").Trim() : OwnerModId.Trim();
        DisplayName = (DisplayName ?? "").Trim();
        PackBelong = (PackBelong ?? "").Trim();
        Icon = (Icon ?? "").Trim();
        Aliases = Clean(Aliases.Concat(new[] { RoleId }));
        Tags = Clean(Tags);
    }

    public AuraRoleRegistryEntry Clone()
    {
        return new AuraRoleRegistryEntry
        {
            RoleId = RoleId,
            OwnerModId = OwnerModId,
            DisplayName = DisplayName,
            Aliases = Aliases.ToList(),
            PackBelong = PackBelong,
            Icon = Icon,
            Priority = Priority,
            Tags = Tags.ToList(),
            Enabled = Enabled
        };
    }

    private static List<string> Clean(IEnumerable<string>? values)
    {
        return values?
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }
}

public sealed class AuraRoleRegistryContribution
{
    [JsonProperty("contributorModId")]
    public string ContributorModId { get; set; } = "";

    [JsonProperty("contributionId")]
    public string ContributionId { get; set; } = "";

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonProperty("persistent")]
    public bool Persistent { get; set; }

    [JsonProperty("entries")]
    public List<AuraRoleRegistryEntry> Entries { get; set; } = new();

    [JsonIgnore]
    public string QualifiedContributionId => ContributorModId + ":" + ContributionId;

    public void Normalize()
    {
        ContributorModId = (ContributorModId ?? "").Trim();
        ContributionId = string.IsNullOrWhiteSpace(ContributionId) ? "default" : ContributionId.Trim();
        SessionId = (SessionId ?? "").Trim();
        Entries ??= new List<AuraRoleRegistryEntry>();
        Entries = Entries
            .Where(entry => entry != null)
            .Select(entry =>
            {
                entry.Normalize();
                return entry;
            })
            .Where(entry => entry.Enabled && !string.IsNullOrWhiteSpace(entry.RoleId))
            .OrderBy(entry => entry.RoleId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(entry => entry.Priority)
            .ToList();
    }

    public bool SemanticallyEquals(AuraRoleRegistryContribution other)
    {
        if (other == null
            || !string.Equals(ContributorModId, other.ContributorModId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ContributionId, other.ContributionId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
            || Persistent != other.Persistent
            || Entries.Count != other.Entries.Count)
        {
            return false;
        }

        for (var i = 0; i < Entries.Count; i++)
        {
            var left = Entries[i];
            var right = other.Entries[i];
            if (!string.Equals(left.RoleId, right.RoleId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(left.OwnerModId, right.OwnerModId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
                || !string.Equals(left.PackBelong, right.PackBelong, StringComparison.Ordinal)
                || !string.Equals(left.Icon, right.Icon, StringComparison.Ordinal)
                || left.Priority != right.Priority
                || left.Enabled != right.Enabled
                || !left.Aliases.SequenceEqual(right.Aliases, StringComparer.OrdinalIgnoreCase)
                || !left.Tags.SequenceEqual(right.Tags, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class AuraRoleRegistryDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("contributions")]
    public List<AuraRoleRegistryContribution> Contributions { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        Contributions ??= new List<AuraRoleRegistryContribution>();
        var normalized = new Dictionary<string, AuraRoleRegistryContribution>(StringComparer.OrdinalIgnoreCase);
        foreach (var contribution in Contributions)
        {
            contribution?.Normalize();
            if (contribution != null
                && !string.IsNullOrWhiteSpace(contribution.ContributorModId)
                && !string.IsNullOrWhiteSpace(contribution.ContributionId))
            {
                normalized[contribution.QualifiedContributionId] = contribution;
            }
        }

        Contributions = normalized.Values
            .OrderBy(value => value.ContributorModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.ContributionId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool ReplaceContribution(AuraRoleRegistryContribution contribution)
    {
        contribution.Normalize();
        Normalize();
        var existing = Contributions.FirstOrDefault(value =>
            string.Equals(value.QualifiedContributionId, contribution.QualifiedContributionId, StringComparison.OrdinalIgnoreCase));
        if (existing != null && existing.SemanticallyEquals(contribution))
        {
            return false;
        }

        Contributions.RemoveAll(value =>
            string.Equals(value.QualifiedContributionId, contribution.QualifiedContributionId, StringComparison.OrdinalIgnoreCase));
        Contributions.Add(contribution);
        Normalize();
        return true;
    }

    public IReadOnlyList<AuraRoleRegistryEntry> BuildActiveEntries(string currentSessionId)
    {
        Normalize();
        var active = Contributions
            .Where(contribution => string.Equals(contribution.SessionId, currentSessionId, StringComparison.Ordinal))
            .SelectMany(contribution => contribution.Entries)
            .Where(entry => entry.Enabled)
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.RoleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var merged = new Dictionary<string, AuraRoleRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in active)
        {
            if (!merged.TryGetValue(entry.RoleId, out var current))
            {
                merged[entry.RoleId] = entry.Clone();
                continue;
            }

            current.Aliases = current.Aliases.Concat(entry.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            current.Tags = current.Tags.Concat(entry.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(current.OwnerModId)) current.OwnerModId = entry.OwnerModId;
            if (string.IsNullOrWhiteSpace(current.DisplayName)) current.DisplayName = entry.DisplayName;
            if (string.IsNullOrWhiteSpace(current.PackBelong)) current.PackBelong = entry.PackBelong;
            if (string.IsNullOrWhiteSpace(current.Icon)) current.Icon = entry.Icon;
        }

        return merged.Values
            .OrderBy(entry => entry.PackBelong, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.RoleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class AuraRoleManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("contributionId")]
    public string ContributionId { get; set; } = "manifest";

    [JsonProperty("entries")]
    public List<AuraRoleRegistryEntry> Entries { get; set; } = new();

    public void Normalize(string fallbackOwnerModId)
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? (fallbackOwnerModId ?? "").Trim() : OwnerModId.Trim();
        ContributionId = string.IsNullOrWhiteSpace(ContributionId) ? "manifest" : ContributionId.Trim();
        Entries ??= new List<AuraRoleRegistryEntry>();
        foreach (var entry in Entries)
        {
            entry?.Normalize(OwnerModId);
        }
    }
}

public sealed class AuraRoleRegistrySnapshot
{
    public AuraRoleRegistrySnapshot(long revision, IReadOnlyList<AuraRoleRegistryEntry> entries)
    {
        Revision = Math.Max(0, revision);
        Entries = entries ?? Array.Empty<AuraRoleRegistryEntry>();
    }

    public long Revision { get; }

    public IReadOnlyList<AuraRoleRegistryEntry> Entries { get; }
}

public sealed class AuraEffectiveRoleSnapshot
{
    public AuraEffectiveRoleSnapshot(
        long registryRevision,
        long catalogEpoch,
        bool nativeReady,
        IReadOnlyList<AuraRoleRegistryEntry> entries)
    {
        RegistryRevision = Math.Max(0, registryRevision);
        CatalogEpoch = Math.Max(0, catalogEpoch);
        NativeReady = nativeReady;
        Entries = entries ?? Array.Empty<AuraRoleRegistryEntry>();
    }

    public long RegistryRevision { get; }

    public long CatalogEpoch { get; }

    public bool NativeReady { get; }

    public IReadOnlyList<AuraRoleRegistryEntry> Entries { get; }
}

public static class AuraEffectiveRoleCatalog
{
    public static IReadOnlyList<AuraRoleRegistryEntry> Merge(
        IEnumerable<AuraRoleRegistryEntry>? runtimeEntries,
        IEnumerable<AuraRoleRegistryEntry>? metadataEntries)
    {
        var metadata = NormalizeEntries(metadataEntries)
            .OrderByDescending(entry => entry.Priority)
            .ToList();
        var result = new List<AuraRoleRegistryEntry>();

        foreach (var runtimeEntry in NormalizeEntries(runtimeEntries))
        {
            var merged = runtimeEntry.Clone();
            var ownerDeclared = false;
            var displayNameDeclared = false;
            var packBelongDeclared = false;
            var iconDeclared = false;
            foreach (var declaration in metadata.Where(candidate => IdentitiesOverlap(merged, candidate)))
            {
                merged.Aliases = merged.Aliases
                    .Concat(declaration.Aliases)
                    .Concat(new[] { declaration.RoleId })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                merged.Tags = merged.Tags
                    .Concat(declaration.Tags)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                merged.Priority = Math.Max(merged.Priority, declaration.Priority);

                if (!ownerDeclared && CanUseDeclaredOwner(merged.OwnerModId, declaration.OwnerModId))
                {
                    merged.OwnerModId = declaration.OwnerModId;
                    ownerDeclared = true;
                }

                if (!displayNameDeclared && !string.IsNullOrWhiteSpace(declaration.DisplayName))
                {
                    merged.DisplayName = declaration.DisplayName;
                    displayNameDeclared = true;
                }

                if (!packBelongDeclared && !string.IsNullOrWhiteSpace(declaration.PackBelong))
                {
                    merged.PackBelong = declaration.PackBelong;
                    packBelongDeclared = true;
                }

                if (!iconDeclared && !string.IsNullOrWhiteSpace(declaration.Icon))
                {
                    merged.Icon = declaration.Icon;
                    iconDeclared = true;
                }
            }

            merged.Enabled = true;
            merged.Normalize();
            if (!result.Any(entry => string.Equals(
                    AuraShared.Core.AuraSharedIdentity.NormalizeRoleId(entry.RoleId),
                    AuraShared.Core.AuraSharedIdentity.NormalizeRoleId(merged.RoleId),
                    StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(merged);
            }
        }

        return result
            .OrderBy(entry => entry.PackBelong, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.RoleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<AuraRoleRegistryEntry> NormalizeEntries(IEnumerable<AuraRoleRegistryEntry>? entries)
    {
        return (entries ?? Array.Empty<AuraRoleRegistryEntry>())
            .Where(entry => entry != null && entry.Enabled)
            .Select(entry =>
            {
                var clone = entry.Clone();
                clone.Normalize();
                return clone;
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.RoleId))
            .ToList();
    }

    private static bool IdentitiesOverlap(AuraRoleRegistryEntry left, AuraRoleRegistryEntry right)
    {
        var leftIds = left.Aliases.Concat(new[] { left.RoleId })
            .Select(AuraShared.Core.AuraSharedIdentity.NormalizeRoleId)
            .Where(value => value.Length > 0);
        var rightIds = right.Aliases.Concat(new[] { right.RoleId })
            .Select(AuraShared.Core.AuraSharedIdentity.NormalizeRoleId)
            .Where(value => value.Length > 0);
        return leftIds.Intersect(rightIds, StringComparer.OrdinalIgnoreCase).Any();
    }

    private static bool CanUseDeclaredOwner(string runtimeOwner, string declaredOwner)
    {
        if (string.IsNullOrWhiteSpace(declaredOwner))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(runtimeOwner)
            || string.Equals(runtimeOwner, "BaseGame", StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtimeOwner, declaredOwner, StringComparison.OrdinalIgnoreCase);
    }
}
