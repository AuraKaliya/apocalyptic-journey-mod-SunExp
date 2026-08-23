using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioArbiter.Shared;
using AuraShared.Core;
using Newtonsoft.Json;

namespace AuraAudio.Shared;

public static class AuraAudioRegistryRuntime
{
    public const string RegistryAuthorityId = "AuraAudioShared";
    public const string RegistryFileName = "audio.registry.json";
    public const int CurrentRegistrySchemaVersion = 1;

    public static event Action<long>? Changed;

    public static AuraAudioRegistrySnapshot GetSnapshot()
    {
        var snapshot = AuraSharedConfigStore.ReadShared(
            RegistryAuthorityId,
            AuraSharedSystems.Audio,
            RegistryFileName,
            new AuraAudioRegistryDocument());
        var document = snapshot.Value ?? new AuraAudioRegistryDocument();
        document.Normalize();
        return new AuraAudioRegistrySnapshot(
            snapshot.Found ? snapshot.Revision : 0,
            document.Contributions.ToList());
    }

    public static AuraAudioRegistryWriteResult RegisterManifestPath(
        string ownerModId,
        string contributionId,
        string sourceModProjectId,
        string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return AuraAudioRegistryWriteResult.Fail("Audio registry manifest is missing: " + manifestPath);
        }
        try
        {
            var manifest = AuraSharedJson.Deserialize<AudioRegistryManifest>(File.ReadAllText(manifestPath));
            return manifest == null
                ? AuraAudioRegistryWriteResult.Fail("Audio registry manifest JSON is invalid: " + manifestPath)
                : RegisterContribution(ownerModId, contributionId, sourceModProjectId, manifest);
        }
        catch (Exception ex)
        {
            return AuraAudioRegistryWriteResult.Fail(ex.Message);
        }
    }

    public static AuraAudioRegistryWriteResult RegisterContribution(
        string ownerModId,
        string contributionId,
        string sourceModProjectId,
        AudioRegistryManifest manifest)
    {
        var owner = (ownerModId ?? "").Trim();
        var contribution = (contributionId ?? "").Trim();
        if (owner.Length == 0 || contribution.Length == 0 || manifest == null)
        {
            return AuraAudioRegistryWriteResult.Fail("Audio registry owner, contribution id, or manifest is empty.");
        }
        if (manifest.schemaVersion <= 0
            || manifest.schemaVersion > AudioArbiterRuntime.SupportedManifestSchemaVersion)
        {
            return AuraAudioRegistryWriteResult.Fail(
                "Unsupported audio registry schemaVersion=" + manifest.schemaVersion + ".");
        }
        if (manifest.audioProtocol != null
            && manifest.audioProtocol.minVersion > AudioArbiterRuntime.CurrentProtocolVersion)
        {
            return AuraAudioRegistryWriteResult.Fail(
                "Audio registry requires protocol=" + manifest.audioProtocol.minVersion + ".");
        }
        if (!string.IsNullOrWhiteSpace(manifest.ownerModId)
            && !string.Equals(manifest.ownerModId.Trim(), owner, StringComparison.OrdinalIgnoreCase))
        {
            return AuraAudioRegistryWriteResult.Fail("Audio registry owner does not match discovered owner.");
        }

        var normalized = CloneManifest(manifest);
        normalized.ownerModId = owner;
        normalized.providers ??= Array.Empty<AudioProviderManifest>();
        foreach (var provider in normalized.providers.Where(item => item != null))
        {
            provider.providerId = (provider.providerId ?? "").Trim();
            provider.ownerModId = string.IsNullOrWhiteSpace(provider.ownerModId)
                ? owner
                : provider.ownerModId.Trim();
            provider.displayName = (provider.displayName ?? "").Trim();
        }
        var duplicate = normalized.providers
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.providerId))
            .GroupBy(item => item.providerId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (!string.IsNullOrWhiteSpace(duplicate))
        {
            return AuraAudioRegistryWriteResult.Fail("Duplicate audio provider id in contribution: " + duplicate);
        }
        if (normalized.providers.Any(item => item == null || string.IsNullOrWhiteSpace(item.providerId)))
        {
            return AuraAudioRegistryWriteResult.Fail("Audio registry contains an empty provider id.");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var snapshot = AuraSharedConfigStore.ReadShared(
                RegistryAuthorityId,
                AuraSharedSystems.Audio,
                RegistryFileName,
                new AuraAudioRegistryDocument());
            var document = snapshot.Value ?? new AuraAudioRegistryDocument();
            document.Normalize();
            var changed = document.ReplaceContribution(new AuraAudioRegistryContribution
            {
                OwnerModId = owner,
                ContributionId = contribution,
                SourceModProjectId = (sourceModProjectId ?? "").Trim(),
                Manifest = normalized
            });
            if (!changed)
            {
                return AuraAudioRegistryWriteResult.Ok(snapshot.Found ? snapshot.Revision : 0, false);
            }
            var write = AuraSharedConfigStore.WriteShared(
                RegistryAuthorityId,
                AuraSharedSystems.Audio,
                RegistryFileName,
                document,
                snapshot.Found ? snapshot.Revision : 0,
                CurrentRegistrySchemaVersion);
            if (write.Success)
            {
                NotifyChanged(write.Revision);
                return AuraAudioRegistryWriteResult.Ok(write.Revision, write.Changed);
            }
            if (!write.Conflict)
            {
                return AuraAudioRegistryWriteResult.Fail(write.Message);
            }
        }
        return AuraAudioRegistryWriteResult.Fail("Audio registry write conflicted repeatedly.");
    }

    public static AuraAudioRegistryWriteResult RemoveContribution(
        string ownerModId,
        string contributionId,
        string sourceModProjectId)
    {
        return RegisterContribution(ownerModId, contributionId, sourceModProjectId, new AudioRegistryManifest
        {
            schemaVersion = AudioArbiterRuntime.SupportedManifestSchemaVersion,
            ownerModId = ownerModId,
            audioProtocol = new AudioProtocolManifest
            {
                minVersion = AudioArbiterRuntime.MinimumSupportedProtocolVersion,
                preferredVersion = AudioArbiterRuntime.CurrentProtocolVersion
            },
            providers = Array.Empty<AudioProviderManifest>()
        });
    }

    private static AudioRegistryManifest CloneManifest(AudioRegistryManifest manifest)
    {
        return AuraSharedJson.Deserialize<AudioRegistryManifest>(AuraSharedJson.Serialize(manifest))
               ?? new AudioRegistryManifest();
    }

    private static void NotifyChanged(long revision)
    {
        try
        {
            Changed?.Invoke(Math.Max(0, revision));
        }
        catch
        {
        }
    }
}

public sealed class AuraAudioRegistryDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraAudioRegistryRuntime.CurrentRegistrySchemaVersion;

    [JsonProperty("contributions")]
    public List<AuraAudioRegistryContribution> Contributions { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = Math.Max(AuraAudioRegistryRuntime.CurrentRegistrySchemaVersion, SchemaVersion);
        Contributions ??= new List<AuraAudioRegistryContribution>();
        Contributions.ForEach(item => item?.Normalize());
        Contributions = Contributions
            .Where(item => item != null
                           && !string.IsNullOrWhiteSpace(item.OwnerModId)
                           && !string.IsNullOrWhiteSpace(item.ContributionId)
                           && item.Manifest != null)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ContributionId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool ReplaceContribution(AuraAudioRegistryContribution contribution)
    {
        contribution.Normalize();
        var existing = Contributions.FirstOrDefault(item => string.Equals(
            item.Key,
            contribution.Key,
            StringComparison.OrdinalIgnoreCase));
        var incomingHasProviders = (contribution.Manifest.providers ?? Array.Empty<AudioProviderManifest>()).Length > 0;
        if (!incomingHasProviders && existing == null)
        {
            return false;
        }
        if (existing != null
            && incomingHasProviders
            && string.Equals(
                AuraSharedJson.Serialize(existing),
                AuraSharedJson.Serialize(contribution),
                StringComparison.Ordinal))
        {
            return false;
        }
        Contributions.RemoveAll(item => string.Equals(item.Key, contribution.Key, StringComparison.OrdinalIgnoreCase));
        if (incomingHasProviders)
        {
            Contributions.Add(contribution);
        }
        Normalize();
        return true;
    }
}

public sealed class AuraAudioRegistryContribution
{
    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("contributionId")]
    public string ContributionId { get; set; } = "";

    [JsonProperty("sourceModProjectId")]
    public string SourceModProjectId { get; set; } = "";

    [JsonProperty("manifest")]
    public AudioRegistryManifest Manifest { get; set; } = new();

    [JsonIgnore]
    public string Key => OwnerModId + ":" + ContributionId;

    public void Normalize()
    {
        OwnerModId = (OwnerModId ?? "").Trim();
        ContributionId = (ContributionId ?? "").Trim();
        SourceModProjectId = (SourceModProjectId ?? "").Trim();
        Manifest ??= new AudioRegistryManifest();
        Manifest.ownerModId = OwnerModId;
        Manifest.defaults ??= new AudioRegistryDefaults();
        Manifest.providers ??= Array.Empty<AudioProviderManifest>();
    }
}

public sealed class AuraAudioRegistrySnapshot
{
    public AuraAudioRegistrySnapshot(long revision, IReadOnlyList<AuraAudioRegistryContribution> contributions)
    {
        Revision = Math.Max(0, revision);
        Contributions = contributions ?? Array.Empty<AuraAudioRegistryContribution>();
    }

    public long Revision { get; }

    public IReadOnlyList<AuraAudioRegistryContribution> Contributions { get; }
}

public sealed class AuraAudioRegistryWriteResult
{
    public bool Success { get; set; }

    public bool Changed { get; set; }

    public long Revision { get; set; }

    public string Message { get; set; } = "";

    public static AuraAudioRegistryWriteResult Ok(long revision, bool changed)
    {
        return new AuraAudioRegistryWriteResult { Success = true, Revision = revision, Changed = changed };
    }

    public static AuraAudioRegistryWriteResult Fail(string message)
    {
        return new AuraAudioRegistryWriteResult { Message = message ?? "" };
    }
}
