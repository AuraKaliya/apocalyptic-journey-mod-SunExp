using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace AudioArbiter.Shared;

internal sealed class AudioManifestLoadResult
{
    private AudioManifestLoadResult(
        bool success,
        string manifestPath,
        string manifestOwner,
        AudioRegistryManifest? manifest,
        string error)
    {
        Success = success;
        ManifestPath = manifestPath ?? "";
        ManifestOwner = manifestOwner ?? "";
        Manifest = manifest;
        Error = error ?? "";
    }

    public bool Success { get; }

    public string ManifestPath { get; }

    public string ManifestOwner { get; }

    public AudioRegistryManifest? Manifest { get; }

    public AudioRegistryDefaults Defaults => Manifest?.defaults ?? new AudioRegistryDefaults();

    public AudioProviderManifest[] Providers => Manifest?.providers ?? Array.Empty<AudioProviderManifest>();

    public string Error { get; }

    public static AudioManifestLoadResult Accepted(
        string manifestPath,
        string manifestOwner,
        AudioRegistryManifest manifest)
    {
        return new AudioManifestLoadResult(true, manifestPath, manifestOwner, manifest, "");
    }

    public static AudioManifestLoadResult Rejected(string manifestPath, string error)
    {
        return new AudioManifestLoadResult(false, manifestPath, "", null, error);
    }
}

internal sealed class AudioManifestProviderPlan
{
    public string ProviderId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string AudioPath { get; set; } = "";

    public string[] AudioVariantPaths { get; set; } = Array.Empty<string>();

    public int Priority { get; set; }

    public string Bus { get; set; } = "";

    public string Policy { get; set; } = "";

    public bool HardClaim { get; set; }

    public float CooldownSeconds { get; set; }

    public bool Sync { get; set; }

    public float GainDb { get; set; }

    public float VolumeMultiplier { get; set; }

    public string Kind { get; set; } = "";

    public float? LowHealthCrossDownThreshold { get; set; }

    public string[] SuppressVocalStates { get; set; } = Array.Empty<string>();

    public int[] SuppressNarrationIds { get; set; } = Array.Empty<int>();
}

internal static class AudioManifestLoader
{
    public static AudioManifestLoadResult Load(
        string modRoot,
        string owner,
        string manifestRelativePath,
        int supportedSchemaVersion,
        int currentProtocolVersion)
    {
        return Load(
            modRoot,
            owner,
            manifestRelativePath,
            supportedSchemaVersion,
            currentProtocolVersion,
            File.Exists,
            File.ReadAllText,
            DeserializeManifest);
    }

    internal static AudioManifestLoadResult Load(
        string modRoot,
        string owner,
        string manifestRelativePath,
        int supportedSchemaVersion,
        int currentProtocolVersion,
        Func<string, bool> fileExists,
        Func<string, string> readAllText,
        Func<string, AudioRegistryManifest?> deserialize)
    {
        var manifestPath = ResolveManifestFilePath(modRoot, manifestRelativePath);
        if (!fileExists(manifestPath))
        {
            return AudioManifestLoadResult.Rejected(
                manifestPath,
                "Manifest registration skipped: file missing. owner=" + owner + ", path=" + manifestPath);
        }

        var manifest = deserialize(readAllText(manifestPath));
        if (manifest == null)
        {
            return AudioManifestLoadResult.Rejected(
                manifestPath,
                "Manifest registration skipped: JSON is empty or invalid. owner=" + owner + ", path=" + manifestPath);
        }

        if (manifest.schemaVersion <= 0)
        {
            manifest.schemaVersion = 1;
        }

        if (manifest.schemaVersion > supportedSchemaVersion)
        {
            return AudioManifestLoadResult.Rejected(
                manifestPath,
                "Manifest registration skipped: unsupported schemaVersion=" + manifest.schemaVersion
                + ", supported=" + supportedSchemaVersion
                + ", owner=" + owner);
        }

        if (manifest.audioProtocol != null && manifest.audioProtocol.minVersion > currentProtocolVersion)
        {
            return AudioManifestLoadResult.Rejected(
                manifestPath,
                "Manifest registration skipped: protocol minVersion=" + manifest.audioProtocol.minVersion
                + ", runtime=" + currentProtocolVersion
                + ", owner=" + owner);
        }

        var manifestOwner = string.IsNullOrWhiteSpace(manifest.ownerModId) ? owner : manifest.ownerModId.Trim();
        return AudioManifestLoadResult.Accepted(manifestPath, manifestOwner, manifest);
    }

    public static AudioManifestProviderPlan CreateProviderPlan(
        AudioProviderManifest provider,
        AudioRegistryDefaults defaults,
        string manifestOwner,
        string modRoot,
        Func<string, string> resolveSharedPath)
    {
        return new AudioManifestProviderPlan
        {
            ProviderId = provider.providerId?.Trim() ?? "",
            OwnerModId = string.IsNullOrWhiteSpace(provider.ownerModId) ? manifestOwner : provider.ownerModId.Trim(),
            AudioPath = ResolveProviderPath(modRoot, provider.path, resolveSharedPath),
            AudioVariantPaths = ResolveProviderVariantPaths(
                modRoot,
                provider.path,
                provider.variantPaths,
                resolveSharedPath),
            Priority = provider.priority,
            Bus = Coalesce(provider.bus, defaults.bus, SoundBuses.Effect),
            Policy = Coalesce(provider.policy, defaults.policy, SoundPolicies.Additive),
            HardClaim = provider.hardClaim ?? defaults.hardClaim ?? false,
            CooldownSeconds = provider.cooldownSeconds ?? defaults.cooldownSeconds ?? 0f,
            Sync = provider.sync ?? defaults.sync ?? true,
            GainDb = provider.gainDb ?? defaults.gainDb ?? 0f,
            VolumeMultiplier = provider.volumeMultiplier ?? defaults.volumeMultiplier ?? 1f,
            Kind = provider.kind,
            LowHealthCrossDownThreshold = provider.match?.hpRatioCrossDown,
            SuppressVocalStates = provider.suppressOriginal?.vocalStates ?? Array.Empty<string>(),
            SuppressNarrationIds = provider.suppressOriginal?.narrationIds ?? Array.Empty<int>()
        };
    }

    public static string ResolveManifestFilePath(string modRoot, string manifestRelativePath)
    {
        return Path.Combine(
            modRoot,
            string.IsNullOrWhiteSpace(manifestRelativePath) ? "audio.registry.json" : manifestRelativePath);
    }

    public static string ResolveProviderPath(
        string modRoot,
        string relativeOrAbsolutePath,
        Func<string, string> resolveSharedPath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            return "";
        }

        const string sharedPrefix = "Shared:";
        if (relativeOrAbsolutePath.StartsWith(sharedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return resolveSharedPath(relativeOrAbsolutePath.Substring(sharedPrefix.Length));
        }

        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(modRoot, relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string[] ResolveProviderVariantPaths(
        string modRoot,
        string primaryPath,
        string[]? variantPaths,
        Func<string, string> resolveSharedPath)
    {
        var primary = ResolveProviderPath(modRoot, primaryPath, resolveSharedPath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(primary))
        {
            seen.Add(primary);
        }

        var resolved = new List<string>();
        foreach (var variantPath in variantPaths ?? Array.Empty<string>())
        {
            var candidate = ResolveProviderPath(modRoot, variantPath, resolveSharedPath);
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                resolved.Add(candidate);
            }
        }

        return resolved.ToArray();
    }

    public static AudioRegistryManifest? DeserializeManifest(string json)
    {
        try
        {
            var jsonConvert = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json")
                ?? Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var method = jsonConvert?.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
            if (method != null)
            {
                return method.Invoke(null, new object[] { json, typeof(AudioRegistryManifest) }) as AudioRegistryManifest;
            }
        }
        catch
        {
        }

        try
        {
            var jsonUtility = Type.GetType("UnityEngine.JsonUtility, UnityEngine.JSONSerializeModule")
                ?? Assembly.Load("UnityEngine.JSONSerializeModule").GetType("UnityEngine.JsonUtility");
            var method = jsonUtility?.GetMethod("FromJson", new[] { typeof(string), typeof(Type) });
            if (method != null)
            {
                return method.Invoke(null, new object[] { json, typeof(AudioRegistryManifest) }) as AudioRegistryManifest;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string Coalesce(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}
