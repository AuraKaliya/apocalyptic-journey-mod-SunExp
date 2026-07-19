using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Newtonsoft.Json;
using Witch.Mod;

namespace AuraCg.Shared;

public static class AuraCgRegistryRuntime
{
    public const string RegistryAuthorityId = "AuraCgShared";
    public const string RegistryFileName = "cg.registry.json";
    public const int CurrentRegistrySchemaVersion = 2;
    private static readonly object CacheGate = new();
    private static AuraCgRegistryDocument? cachedDocument;
    private static DateTime cachedDocumentUtc;
    private static long cachedRevision = -1;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);

    // Same-process consumers use this notification to invalidate local derived
    // state. It is deliberately revision-based: no consumer depends on being
    // loaded before or after another mod.
    public static event Action<long>? Changed;

    public static AuraCgRegistrySnapshot GetSnapshot(string ownerModId = "")
    {
        var document = GetCachedDocument();
        var owner = (ownerModId ?? "").Trim();
        var entries = document.Entries
            .Where(entry => entry.Enabled
                            && (string.IsNullOrWhiteSpace(owner)
                                || string.Equals(entry.OwnerModId, owner, StringComparison.OrdinalIgnoreCase)))
            .ToList()
            .AsReadOnly();
        return new AuraCgRegistrySnapshot(Math.Max(0, cachedRevision), entries);
    }

    public static bool RegisterManifest(ModConfig? modConfig, string ownerModId, string manifestRelativePath = "SharedResources/cg.registry.json")
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        var modRoot = modConfig?.DirectoryName ?? AuraSharedPaths.PackageDirectory;
        var manifestPath = string.IsNullOrWhiteSpace(manifestRelativePath)
            ? ""
            : Path.Combine(modRoot, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return RegisterManifestPath(ownerModId, manifestPath);
    }

    public static bool RegisterManifestPath(string ownerModId, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            AuraCgLog.WarnOnce("cg-manifest-missing:" + ownerModId, "CG registry manifest is missing for " + ownerModId + ": " + manifestPath);
            return false;
        }

        try
        {
            var manifest = AuraSharedJson.Deserialize<AuraCgManifest>(File.ReadAllText(manifestPath));
            return manifest != null && RegisterManifest(ownerModId, manifest);
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("cg-manifest-load-failed:" + ownerModId, "CG registry manifest failed to load for " + ownerModId + ": " + ex.Message);
            return false;
        }
    }

    public static bool RegisterManifest(string ownerModId, AuraCgManifest manifest)
    {
        manifest ??= new AuraCgManifest();
        manifest.Normalize(ownerModId);
        if (manifest.Protocol.MinVersion > CurrentRegistrySchemaVersion)
        {
            AuraCgLog.WarnOnce(
                "cg-manifest-protocol:" + ownerModId,
                "CG registry manifest requires newer schema. owner=" + ownerModId
                + ", min=" + manifest.Protocol.MinVersion
                + ", current=" + CurrentRegistrySchemaVersion);
            return false;
        }

        var accepted = manifest.Entries
            .Where(entry => entry != null && entry.Enabled && !string.IsNullOrWhiteSpace(entry.CgId))
            .Select(entry =>
            {
                entry.OwnerModId = manifest.OwnerModId;
                entry.RegistrationSourceId = manifest.ContributionId;
                entry.Normalize(manifest.OwnerModId);
                return entry;
            })
            .ToList();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = AuraSharedConfigStore.ReadShared(
                RegistryAuthorityId,
                AuraSharedSystems.Cg,
                RegistryFileName,
                new AuraCgRegistryDocument());
            var document = snapshot.Value ?? new AuraCgRegistryDocument();
            document.Normalize();
            if (!document.ReplaceContributionEntries(manifest.OwnerModId, manifest.ContributionId, accepted))
            {
                lock (CacheGate)
                {
                    cachedDocument = document;
                    cachedRevision = snapshot.Found ? snapshot.Revision : 0;
                    cachedDocumentUtc = DateTime.UtcNow;
                }

                return true;
            }
            var result = AuraSharedConfigStore.WriteShared(
                RegistryAuthorityId,
                AuraSharedSystems.Cg,
                RegistryFileName,
                document,
                snapshot.Found ? snapshot.Revision : 0,
                CurrentRegistrySchemaVersion);
            if (result.Success)
            {
                InvalidateCache();
                if (accepted.Count > 0)
                {
                    AuraCgActivationRuntime.ApplyManifestDefaults(manifest.OwnerModId, accepted);
                }
                NotifyChanged(result.Revision);
                AuraCgLog.InfoOnce(
                    "cg-manifest-registered:" + manifest.OwnerModId + ":" + manifest.ContributionId,
                    "CG registry manifest registered. owner=" + manifest.OwnerModId
                    + ", contribution=" + manifest.ContributionId
                    + ", entries=" + accepted.Count);
                return true;
            }

            if (!result.Conflict)
            {
                AuraCgLog.WarnOnce("cg-registry-write-failed:" + manifest.OwnerModId + ":" + manifest.ContributionId, "CG registry write failed: " + result.Message);
                return false;
            }
        }

        AuraCgLog.WarnOnce("cg-registry-conflict:" + manifest.OwnerModId + ":" + manifest.ContributionId, "CG registry write conflicted repeatedly for " + manifest.OwnerModId + ".");
        return false;
    }

    public static bool RegisterContribution(
        string ownerModId,
        string contributionId,
        IEnumerable<AuraCgRegistryEntry> entries)
    {
        return RegisterManifest(ownerModId, new AuraCgManifest
        {
            SchemaVersion = CurrentRegistrySchemaVersion,
            OwnerModId = ownerModId,
            ContributionId = contributionId,
            Entries = (entries ?? Array.Empty<AuraCgRegistryEntry>()).ToList()
        });
    }

    public static IReadOnlyList<AuraCgRegistryEntry> GetRegisteredEntries(string ownerModId = "")
    {
        var document = GetCachedDocument();
        return document.Entries
            .Where(entry => entry.Enabled)
            .Where(entry => string.IsNullOrWhiteSpace(ownerModId)
                            || string.Equals(entry.OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void InvalidateCache()
    {
        lock (CacheGate)
        {
            cachedDocument = null;
            cachedRevision = -1;
            cachedDocumentUtc = DateTime.MinValue;
        }
    }

    private static AuraCgRegistryDocument GetCachedDocument()
    {
        lock (CacheGate)
        {
            if (cachedDocument != null && DateTime.UtcNow - cachedDocumentUtc <= CacheTtl)
            {
                return cachedDocument;
            }

            var snapshot = AuraSharedConfigStore.ReadShared(
                RegistryAuthorityId,
                AuraSharedSystems.Cg,
                RegistryFileName,
                new AuraCgRegistryDocument());
            if (cachedDocument != null
                && snapshot.Found
                && snapshot.Revision == cachedRevision
                && DateTime.UtcNow - cachedDocumentUtc <= CacheTtl)
            {
                return cachedDocument;
            }

            var document = snapshot.Value ?? new AuraCgRegistryDocument();
            document.Normalize();
            cachedDocument = document;
            cachedRevision = snapshot.Found ? snapshot.Revision : 0;
            cachedDocumentUtc = DateTime.UtcNow;
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
            // Registry observers are consumers, never part of registration
            // durability or compatibility.
        }
    }
}

public sealed class AuraCgRegistrySnapshot
{
    public AuraCgRegistrySnapshot(long revision, IReadOnlyList<AuraCgRegistryEntry> entries)
    {
        Revision = Math.Max(0, revision);
        Entries = entries ?? Array.Empty<AuraCgRegistryEntry>();
    }

    public long Revision { get; }

    public IReadOnlyList<AuraCgRegistryEntry> Entries { get; }
}

public sealed class AuraCgRegistryDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraCgRegistryRuntime.CurrentRegistrySchemaVersion;

    [JsonProperty("entries")]
    public List<AuraCgRegistryEntry> Entries { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = Math.Max(AuraCgRegistryRuntime.CurrentRegistrySchemaVersion, SchemaVersion);
        Entries ??= new List<AuraCgRegistryEntry>();
        var normalized = new Dictionary<string, AuraCgRegistryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            if (entry == null)
            {
                continue;
            }

            entry.Normalize(entry.OwnerModId);
            if (string.IsNullOrWhiteSpace(entry.OwnerModId) || string.IsNullOrWhiteSpace(entry.CgId))
            {
                continue;
            }

            normalized[entry.QualifiedCgId] = entry;
        }

        Entries = normalized.Values
            .OrderBy(entry => entry.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void ReplaceOwnerEntries(string ownerModId, IEnumerable<AuraCgRegistryEntry> entries)
    {
        ReplaceContributionEntries(ownerModId, "manifest", entries);
    }

    public bool ReplaceContributionEntries(
        string ownerModId,
        string contributionId,
        IEnumerable<AuraCgRegistryEntry> entries)
    {
        var owner = (ownerModId ?? "").Trim();
        var source = string.IsNullOrWhiteSpace(contributionId) ? "manifest" : contributionId.Trim();
        Entries ??= new List<AuraCgRegistryEntry>();
        var incoming = (entries ?? Array.Empty<AuraCgRegistryEntry>())
            .Where(entry => entry != null)
            .Select(entry =>
            {
                entry.OwnerModId = owner;
                entry.RegistrationSourceId = source;
                entry.Normalize(owner);
                return entry;
            })
            .OrderBy(entry => entry.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existing = Entries
            .Where(entry => string.Equals(entry.OwnerModId, owner, StringComparison.OrdinalIgnoreCase)
                            && (string.Equals(entry.RegistrationSourceId, source, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(source, "manifest", StringComparison.OrdinalIgnoreCase)
                                   && string.IsNullOrWhiteSpace(entry.RegistrationSourceId)))
            .OrderBy(entry => entry.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.Equals(
                AuraSharedJson.Serialize(existing),
                AuraSharedJson.Serialize(incoming),
                StringComparison.Ordinal))
        {
            return false;
        }

        Entries.RemoveAll(entry =>
            string.Equals(entry.OwnerModId, owner, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(entry.RegistrationSourceId, source, StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "manifest", StringComparison.OrdinalIgnoreCase)
                   && string.IsNullOrWhiteSpace(entry.RegistrationSourceId)));
        foreach (var entry in incoming)
        {
            Entries.Add(entry);
        }

        Normalize();
        return true;
    }
}

public sealed class AuraCgManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("contributionId")]
    public string ContributionId { get; set; } = "manifest";

    [JsonProperty("protocol")]
    public AuraCgProtocolManifest Protocol { get; set; } = new();

    [JsonProperty("entries")]
    public List<AuraCgRegistryEntry> Entries { get; set; } = new();

    public void Normalize(string fallbackOwner)
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? fallbackOwner : OwnerModId.Trim();
        ContributionId = string.IsNullOrWhiteSpace(ContributionId) ? "manifest" : ContributionId.Trim();
        Protocol ??= new AuraCgProtocolManifest();
        Protocol.Normalize();
        Entries ??= new List<AuraCgRegistryEntry>();
        foreach (var entry in Entries)
        {
            entry?.Normalize(OwnerModId);
        }
    }
}

public sealed class AuraCgProtocolManifest
{
    [JsonProperty("minVersion")]
    public int MinVersion { get; set; } = 1;

    [JsonProperty("preferredVersion")]
    public int PreferredVersion { get; set; } = AuraCgRegistryRuntime.CurrentRegistrySchemaVersion;

    public void Normalize()
    {
        MinVersion = Math.Max(1, MinVersion);
        PreferredVersion = Math.Max(MinVersion, PreferredVersion);
    }
}

public sealed class AuraCgRegistryEntry
{
    [JsonProperty("cgId")]
    public string CgId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("registrationSourceId")]
    public string RegistrationSourceId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = "skill";

    [JsonProperty("targetRoleIds")]
    public List<string> TargetRoleIds { get; set; } = new();

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    [JsonProperty("media")]
    public AuraCgMediaSpec Media { get; set; } = new();

    [JsonProperty("defaultPresentation")]
    public AuraCgPresentationSpec DefaultPresentation { get; set; } = new();

    [JsonProperty("defaultActivation")]
    public AuraCgDefaultActivationSpec DefaultActivation { get; set; } = new();

    [JsonProperty("priority")]
    public int Priority { get; set; } = 10;

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string QualifiedCgId => string.IsNullOrWhiteSpace(OwnerModId) ? CgId : OwnerModId + ":" + CgId;

    public static string Qualify(string ownerModId, string cgId)
    {
        var owner = (ownerModId ?? "").Trim();
        var id = (cgId ?? "").Trim();
        return string.IsNullOrWhiteSpace(owner) ? id : owner + ":" + id;
    }

    public void Normalize(string fallbackOwner)
    {
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? fallbackOwner : OwnerModId.Trim();
        RegistrationSourceId = (RegistrationSourceId ?? "").Trim();
        CgId = (CgId ?? "").Trim();
        DisplayName = (DisplayName ?? "").Trim();
        Kind = string.IsNullOrWhiteSpace(Kind) ? "skill" : Kind.Trim();
        TargetRoleIds = CleanList(TargetRoleIds);
        CardIds = CleanList(CardIds);
        Media ??= new AuraCgMediaSpec();
        Media.Normalize();
        DefaultPresentation ??= new AuraCgPresentationSpec();
        DefaultPresentation.Normalize();
        DefaultActivation ??= new AuraCgDefaultActivationSpec();
        DefaultActivation.Normalize();
        Tags = CleanList(Tags);
    }

    private static List<string> CleanList(IEnumerable<string>? values)
    {
        return values?
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }
}

public sealed class AuraCgMediaSpec
{
    [JsonProperty("type")]
    public string Type { get; set; } = "image";

    [JsonProperty("resource")]
    public string Resource { get; set; } = "";

    [JsonProperty("fallbackImage")]
    public string FallbackImage { get; set; } = "";

    [JsonProperty("bundlePath")]
    public string BundlePath { get; set; } = "";

    [JsonProperty("bundleAssetPrefix")]
    public string BundleAssetPrefix { get; set; } = "";

    [JsonProperty("frameSeconds")]
    public float FrameSeconds { get; set; } = 0.08f;

    [JsonProperty("alphaMode")]
    public string AlphaMode { get; set; } = SkillCgAlphaModes.None;

    [JsonProperty("keyThreshold")]
    public float KeyThreshold { get; set; } = 0.03f;

    [JsonProperty("keySoftness")]
    public float KeySoftness { get; set; } = 0.08f;

    [JsonProperty("flashAtSeconds")]
    public float FlashAtSeconds { get; set; } = -1f;

    [JsonProperty("flashDuration")]
    public float FlashDuration { get; set; } = 0.18f;

    [JsonProperty("flashMode")]
    public string FlashMode { get; set; } = SkillCgFlashModes.Screen;

    [JsonProperty("flashStartFrame")]
    public int FlashStartFrame { get; set; }

    [JsonProperty("flashEndFrame")]
    public int FlashEndFrame { get; set; }

    [JsonProperty("flashPulseEveryFrames")]
    public int FlashPulseEveryFrames { get; set; } = 1;

    [JsonProperty("flashStrength")]
    public float FlashStrength { get; set; } = 0.82f;

    [JsonProperty("version")]
    public string Version { get; set; } = "";

    [JsonProperty("hash")]
    public string Hash { get; set; } = "";

    public void Normalize()
    {
        Type = SkillCgMediaTypes.Normalize(Type);
        Resource = AuraSharedPaths.NormalizeRelativePath(Resource);
        FallbackImage = AuraSharedPaths.NormalizeRelativePath(FallbackImage);
        BundlePath = AuraSharedPaths.NormalizeRelativePath(BundlePath);
        BundleAssetPrefix = AuraSharedPaths.NormalizeRelativePath(BundleAssetPrefix);
        FrameSeconds = Math.Max(0.01f, FrameSeconds);
        AlphaMode = SkillCgAlphaModes.Normalize(AlphaMode);
        KeyThreshold = Math.Max(0f, Math.Min(1f, KeyThreshold));
        KeySoftness = Math.Max(0.001f, Math.Min(1f, KeySoftness));
        FlashAtSeconds = FlashAtSeconds < 0f ? -1f : FlashAtSeconds;
        FlashDuration = Math.Max(0.03f, Math.Min(1f, FlashDuration));
        FlashMode = SkillCgFlashModes.Normalize(FlashMode);
        FlashStartFrame = Math.Max(0, FlashStartFrame);
        FlashEndFrame = Math.Max(0, FlashEndFrame);
        if (FlashStartFrame > 0 && FlashEndFrame > 0 && FlashEndFrame < FlashStartFrame)
        {
            FlashEndFrame = FlashStartFrame;
        }

        FlashPulseEveryFrames = Math.Max(1, FlashPulseEveryFrames);
        FlashStrength = Math.Max(0f, Math.Min(1f, FlashStrength <= 0f ? 0.82f : FlashStrength));
        Version = (Version ?? "").Trim();
        Hash = (Hash ?? "").Trim();
    }
}

public sealed class AuraCgPresentationSpec
{
    [JsonProperty("mode")]
    public string Mode { get; set; } = SkillCgPresentationModes.Slide;

    [JsonProperty("fit")]
    public string Fit { get; set; } = SkillCgFitModes.Contain;

    [JsonProperty("fadeIn")]
    public float FadeIn { get; set; } = 0.35f;

    [JsonProperty("hold")]
    public float Hold { get; set; } = 1f;

    [JsonProperty("fadeOut")]
    public float FadeOut { get; set; } = 0.45f;

    [JsonProperty("focusX")]
    public float FocusX { get; set; } = 0.5f;

    [JsonProperty("focusY")]
    public float FocusY { get; set; } = 0.5f;

    [JsonProperty("safeScale")]
    public float SafeScale { get; set; } = 1f;

    public void Normalize()
    {
        Mode = SkillCgPresentationModes.Normalize(Mode);
        Fit = SkillCgFitModes.Normalize(Fit);
        FadeIn = Math.Max(0f, FadeIn);
        Hold = Math.Max(0f, Hold);
        FadeOut = Math.Max(0f, FadeOut);
        FocusX = Clamp01(FocusX);
        FocusY = Clamp01(FocusY);
        SafeScale = Math.Max(1f, Math.Min(3f, SafeScale <= 0f ? 1f : SafeScale));
    }

    private static float Clamp01(float value)
    {
        return Math.Max(0f, Math.Min(1f, value));
    }
}
