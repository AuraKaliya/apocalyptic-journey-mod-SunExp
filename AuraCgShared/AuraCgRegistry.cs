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
    public const int CurrentRegistrySchemaVersion = 1;

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
                entry.Normalize(manifest.OwnerModId);
                return entry;
            })
            .ToList();
        if (accepted.Count == 0)
        {
            return false;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = AuraSharedConfigStore.ReadShared(
                RegistryAuthorityId,
                AuraSharedSystems.Cg,
                RegistryFileName,
                new AuraCgRegistryDocument());
            var document = snapshot.Value ?? new AuraCgRegistryDocument();
            document.Normalize();
            document.ReplaceOwnerEntries(manifest.OwnerModId, accepted);
            var result = AuraSharedConfigStore.WriteShared(
                RegistryAuthorityId,
                AuraSharedSystems.Cg,
                RegistryFileName,
                document,
                snapshot.Found ? snapshot.Revision : 0,
                CurrentRegistrySchemaVersion);
            if (result.Success)
            {
                AuraCgActivationRuntime.ApplyManifestDefaults(manifest.OwnerModId, accepted);
                AuraCgLog.InfoOnce(
                    "cg-manifest-registered:" + manifest.OwnerModId,
                    "CG registry manifest registered. owner=" + manifest.OwnerModId + ", entries=" + accepted.Count);
                return true;
            }

            if (!result.Conflict)
            {
                AuraCgLog.WarnOnce("cg-registry-write-failed:" + manifest.OwnerModId, "CG registry write failed: " + result.Message);
                return false;
            }
        }

        AuraCgLog.WarnOnce("cg-registry-conflict:" + manifest.OwnerModId, "CG registry write conflicted repeatedly for " + manifest.OwnerModId + ".");
        return false;
    }

    public static IReadOnlyList<AuraCgRegistryEntry> GetRegisteredEntries(string ownerModId = "")
    {
        var snapshot = AuraSharedConfigStore.ReadShared(
            RegistryAuthorityId,
            AuraSharedSystems.Cg,
            RegistryFileName,
            new AuraCgRegistryDocument());
        var document = snapshot.Value ?? new AuraCgRegistryDocument();
        document.Normalize();
        return document.Entries
            .Where(entry => entry.Enabled)
            .Where(entry => string.IsNullOrWhiteSpace(ownerModId)
                            || string.Equals(entry.OwnerModId, ownerModId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Priority)
            .ThenBy(entry => entry.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.CgId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
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
        var owner = (ownerModId ?? "").Trim();
        Entries ??= new List<AuraCgRegistryEntry>();
        Entries.RemoveAll(entry => string.Equals(entry.OwnerModId, owner, StringComparison.OrdinalIgnoreCase));
        Entries.AddRange(entries ?? Array.Empty<AuraCgRegistryEntry>());
        Normalize();
    }
}

public sealed class AuraCgManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("protocol")]
    public AuraCgProtocolManifest Protocol { get; set; } = new();

    [JsonProperty("entries")]
    public List<AuraCgRegistryEntry> Entries { get; set; } = new();

    public void Normalize(string fallbackOwner)
    {
        SchemaVersion = Math.Max(1, SchemaVersion);
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? fallbackOwner : OwnerModId.Trim();
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

    [JsonProperty("version")]
    public string Version { get; set; } = "";

    [JsonProperty("hash")]
    public string Hash { get; set; } = "";

    public void Normalize()
    {
        Type = string.IsNullOrWhiteSpace(Type) ? "image" : Type.Trim();
        Resource = AuraSharedPaths.NormalizeRelativePath(Resource);
        FallbackImage = AuraSharedPaths.NormalizeRelativePath(FallbackImage);
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
