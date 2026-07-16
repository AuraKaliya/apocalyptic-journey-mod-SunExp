using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Newtonsoft.Json;
using Witch.Mod;

namespace AuraCardUseFx.Shared;

public static class AuraCardUseFxRegistryRuntime
{
    public const string RegistryAuthorityId = "AuraCardUseFxShared";
    public const string RegistryFileName = "card-use-fx.registry.json";
    public const int CurrentSchemaVersion = 2;

    private static readonly object CacheGate = new();
    private static AuraCardUseFxRegistryDocument? cachedDocument;
    private static DateTime cachedUtc;
    private static long cachedRevision = -1;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);

    public static event Action<long>? Changed;

    public static bool RegisterManifest(
        ModConfig? modConfig,
        string ownerModId,
        string manifestRelativePath = "card-use-effect.registry.json")
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        var root = modConfig?.DirectoryName ?? AuraSharedPaths.PackageDirectory;
        var path = string.IsNullOrWhiteSpace(manifestRelativePath)
            ? ""
            : Path.Combine(root, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return RegisterManifestPath(ownerModId, path);
    }

    public static bool RegisterManifestPath(string ownerModId, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            AuraSharedLog.WarnOnce(RegistryAuthorityId, "manifest-missing:" + ownerModId,
                "Card-use FX manifest is missing for " + ownerModId + ": " + manifestPath);
            return false;
        }

        try
        {
            var manifest = AuraSharedJson.Deserialize<AuraCardUseFxManifest>(File.ReadAllText(manifestPath));
            return manifest != null && RegisterManifest(ownerModId, manifest);
        }
        catch (Exception ex)
        {
            AuraSharedLog.WarnOnce(RegistryAuthorityId, "manifest-load:" + ownerModId,
                "Card-use FX manifest failed to load for " + ownerModId + ": " + ex.Message);
            return false;
        }
    }

    public static bool RegisterManifest(string ownerModId, AuraCardUseFxManifest manifest)
    {
        manifest ??= new AuraCardUseFxManifest();
        manifest.Normalize(ownerModId);
        if (manifest.Protocol.MinVersion > CurrentSchemaVersion)
        {
            AuraSharedLog.WarnOnce(RegistryAuthorityId, "manifest-version:" + ownerModId,
                "Card-use FX manifest requires a newer schema. owner=" + ownerModId
                + ", min=" + manifest.Protocol.MinVersion
                + ", current=" + CurrentSchemaVersion);
            return false;
        }

        var accepted = manifest.Entries
            .Where(entry => entry != null && entry.Enabled && entry.EffectId.Length > 0 && entry.CardIds.Count > 0)
            .ToList();
        if (accepted.Count == 0)
        {
            return false;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = AuraSharedConfigStore.ReadShared(
                RegistryAuthorityId,
                AuraSharedSystems.CardUseFx,
                RegistryFileName,
                new AuraCardUseFxRegistryDocument());
            var document = snapshot.Value ?? new AuraCardUseFxRegistryDocument();
            document.Normalize();
            document.ReplaceOwnerEntries(manifest.OwnerModId, accepted);
            var result = AuraSharedConfigStore.WriteShared(
                RegistryAuthorityId,
                AuraSharedSystems.CardUseFx,
                RegistryFileName,
                document,
                snapshot.Found ? snapshot.Revision : 0,
                CurrentSchemaVersion);
            if (result.Success)
            {
                InvalidateCache();
                NotifyChanged(result.Revision);
                AuraSharedLog.InfoOnce(RegistryAuthorityId, "manifest-registered:" + manifest.OwnerModId,
                    "Card-use FX manifest registered. owner=" + manifest.OwnerModId + ", entries=" + accepted.Count);
                return true;
            }

            if (!result.Conflict)
            {
                AuraSharedLog.WarnOnce(RegistryAuthorityId, "registry-write:" + manifest.OwnerModId,
                    "Card-use FX registry write failed: " + result.Message);
                return false;
            }
        }

        AuraSharedLog.WarnOnce(RegistryAuthorityId, "registry-conflict:" + manifest.OwnerModId,
            "Card-use FX registry write conflicted repeatedly for " + manifest.OwnerModId + ".");
        return false;
    }

    public static IReadOnlyList<AuraCardUseFxRegistryEntry> Resolve(string cardId)
    {
        var normalizedCardId = AuraCardUseFxRegistryEntry.NormalizeCardId(cardId);
        if (normalizedCardId.Length == 0)
        {
            return Array.Empty<AuraCardUseFxRegistryEntry>();
        }

        var candidates = GetRegisteredEntries()
            .Where(entry => entry.CardIds.Any(id => string.Equals(
                AuraCardUseFxRegistryEntry.NormalizeCardId(id),
                normalizedCardId,
                StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var exclusiveWinners = candidates
            .Where(entry => entry.StackMode == AuraCardUseFxStackModes.Exclusive && entry.ExclusiveGroup.Length > 0)
            .GroupBy(entry => entry.ExclusiveGroup, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var nonExclusive = candidates
            .Where(entry => entry.StackMode != AuraCardUseFxStackModes.Exclusive || entry.ExclusiveGroup.Length == 0);
        return nonExclusive.Concat(exclusiveWinners)
            .Distinct(AuraCardUseFxEntryIdentityComparer.Instance)
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.QualifiedEffectId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<AuraCardUseFxRegistryEntry> GetRegisteredEntries(string ownerModId = "")
    {
        var owner = (ownerModId ?? "").Trim();
        return GetDocument().Entries
            .Where(entry => entry.Enabled)
            .Where(entry => owner.Length == 0 || string.Equals(entry.OwnerModId, owner, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.QualifiedEffectId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void InvalidateCache()
    {
        lock (CacheGate)
        {
            cachedDocument = null;
            cachedRevision = -1;
            cachedUtc = DateTime.MinValue;
        }
    }

    private static AuraCardUseFxRegistryDocument GetDocument()
    {
        lock (CacheGate)
        {
            if (cachedDocument != null && DateTime.UtcNow - cachedUtc <= CacheTtl)
            {
                return cachedDocument;
            }

            var snapshot = AuraSharedConfigStore.ReadShared(
                RegistryAuthorityId,
                AuraSharedSystems.CardUseFx,
                RegistryFileName,
                new AuraCardUseFxRegistryDocument());
            var document = snapshot.Value ?? new AuraCardUseFxRegistryDocument();
            document.Normalize();
            cachedDocument = document;
            cachedRevision = snapshot.Found ? snapshot.Revision : 0;
            cachedUtc = DateTime.UtcNow;
            return document;
        }
    }

    private static void NotifyChanged(long revision)
    {
        try
        {
            Changed?.Invoke(Math.Max(0, revision));
        }
        catch
        {
            // Registry observers cannot affect registration durability.
        }
    }
}

public static class AuraCardUseFxStackModes
{
    public const string Stack = "stack";
    public const string Exclusive = "exclusive";
}

public static class AuraCardUseFxPresentationScopes
{
    public const string OwnerLocal = "ownerLocal";
    public const string Observers = "observers";
    public const string All = "all";

    public static string Normalize(string value)
    {
        if (string.Equals(value, OwnerLocal, StringComparison.OrdinalIgnoreCase))
        {
            return OwnerLocal;
        }

        if (string.Equals(value, All, StringComparison.OrdinalIgnoreCase))
        {
            return All;
        }

        return Observers;
    }
}

public sealed class AuraCardUseFxManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraCardUseFxRegistryRuntime.CurrentSchemaVersion;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("protocol")]
    public AuraCardUseFxProtocolManifest Protocol { get; set; } = new();

    [JsonProperty("entries")]
    public List<AuraCardUseFxRegistryEntry> Entries { get; set; } = new();

    public void Normalize(string fallbackOwner)
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? (fallbackOwner ?? "").Trim() : OwnerModId.Trim();
        Protocol ??= new AuraCardUseFxProtocolManifest();
        Protocol.Normalize();
        Entries ??= new List<AuraCardUseFxRegistryEntry>();
        foreach (var entry in Entries)
        {
            entry?.Normalize(OwnerModId);
        }
    }
}

public sealed class AuraCardUseFxProtocolManifest
{
    [JsonProperty("minVersion")]
    public int MinVersion { get; set; } = 1;

    [JsonProperty("preferredVersion")]
    public int PreferredVersion { get; set; } = AuraCardUseFxRegistryRuntime.CurrentSchemaVersion;

    public void Normalize()
    {
        MinVersion = Math.Max(1, MinVersion);
        PreferredVersion = Math.Max(MinVersion, PreferredVersion);
    }
}

public sealed class AuraCardUseFxRegistryDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraCardUseFxRegistryRuntime.CurrentSchemaVersion;

    [JsonProperty("entries")]
    public List<AuraCardUseFxRegistryEntry> Entries { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = Math.Max(AuraCardUseFxRegistryRuntime.CurrentSchemaVersion, SchemaVersion);
        Entries ??= new List<AuraCardUseFxRegistryEntry>();
        var normalized = new Dictionary<string, AuraCardUseFxRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            if (entry == null)
            {
                continue;
            }

            entry.Normalize(entry.OwnerModId);
            if (entry.OwnerModId.Length > 0 && entry.EffectId.Length > 0)
            {
                normalized[entry.QualifiedEffectId] = entry;
            }
        }

        Entries = normalized.Values
            .OrderBy(entry => entry.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.EffectId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void ReplaceOwnerEntries(string ownerModId, IEnumerable<AuraCardUseFxRegistryEntry> entries)
    {
        var owner = (ownerModId ?? "").Trim();
        Entries.RemoveAll(entry => string.Equals(entry.OwnerModId, owner, StringComparison.OrdinalIgnoreCase));
        Entries.AddRange(entries ?? Array.Empty<AuraCardUseFxRegistryEntry>());
        Normalize();
    }
}

public sealed class AuraCardUseFxRegistryEntry
{
    [JsonProperty("effectId")]
    public string EffectId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    [JsonProperty("visualEffectId")]
    public string VisualEffectId { get; set; } = "";

    [JsonProperty("presentationScope")]
    public string PresentationScope { get; set; } = AuraCardUseFxPresentationScopes.Observers;

    [JsonProperty("stackMode")]
    public string StackMode { get; set; } = AuraCardUseFxStackModes.Stack;

    [JsonProperty("exclusiveGroup")]
    public string ExclusiveGroup { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; } = 10;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string QualifiedEffectId => OwnerModId + ":" + EffectId;

    public void Normalize(string fallbackOwner)
    {
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? (fallbackOwner ?? "").Trim() : OwnerModId.Trim();
        EffectId = (EffectId ?? "").Trim();
        DisplayName = (DisplayName ?? "").Trim();
        VisualEffectId = (VisualEffectId ?? "").Trim();
        PresentationScope = AuraCardUseFxPresentationScopes.Normalize(PresentationScope);
        ExclusiveGroup = (ExclusiveGroup ?? "").Trim();
        StackMode = string.Equals(StackMode, AuraCardUseFxStackModes.Exclusive, StringComparison.OrdinalIgnoreCase)
            ? AuraCardUseFxStackModes.Exclusive
            : AuraCardUseFxStackModes.Stack;
        CardIds = (CardIds ?? new List<string>())
            .Select(NormalizeCardId)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeCardId(string value)
    {
        return (value ?? "").Replace("*", "").Trim();
    }
}

internal sealed class AuraCardUseFxEntryIdentityComparer : IEqualityComparer<AuraCardUseFxRegistryEntry>
{
    public static readonly AuraCardUseFxEntryIdentityComparer Instance = new();

    public bool Equals(AuraCardUseFxRegistryEntry? x, AuraCardUseFxRegistryEntry? y)
    {
        return string.Equals(x?.QualifiedEffectId, y?.QualifiedEffectId, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(AuraCardUseFxRegistryEntry obj)
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(obj?.QualifiedEffectId ?? "");
    }
}
