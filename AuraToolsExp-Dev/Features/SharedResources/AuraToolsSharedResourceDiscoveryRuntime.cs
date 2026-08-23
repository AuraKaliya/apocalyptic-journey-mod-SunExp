using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AudioArbiter.Shared;
using AuraAudio.Shared;
using AuraCg.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.SharedResources;

public sealed class AuraToolsSharedResourceDiscoveryResult
{
    public bool Success { get; set; }

    public int LoadedMods { get; set; }

    public int Participants { get; set; }

    public int Registered { get; set; }

    public int Deduplicated { get; set; }

    public int Conflicts { get; set; }

    public int Removed { get; set; }

    public List<string> Errors { get; set; } = new();
}

public static class AuraToolsSharedResourceDiscoveryRuntime
{
    private const string Owner = "SharedResourceDiscovery";
    private const string StateSystem = "SharedResourceDiscovery";
    private const string StateFile = "sources.json";
    private static ModConfig? currentConfig;
    private static bool initialized;
    private static long generation;
    private static readonly HashSet<string> ActiveSourceProjectIds = new(StringComparer.OrdinalIgnoreCase);

    public static event Action<AuraToolsSharedResourceDiscoveryResult>? Changed;

    public static AuraToolsSharedResourceDiscoveryResult LastResult { get; private set; } = new();

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }
        currentConfig = modConfig;
        AuraSharedRuntime.Initialize(modConfig, AuraToolsIds.ModId);
        AuraToolsHookRegistry.After(
            modConfig,
            "GameEntryUI.Init",
            _ => ScheduleRefresh("GameEntryUI.Init", 2),
            Owner);
        AuraCgRegistryRuntime.SetActiveDiscoverySources(Array.Empty<string>());
        AuraAudioRegistryRuntime.Changed += _ => AuraToolsAudioRuntime.RegisterProviders();
        initialized = true;
        ScheduleRefresh("startup", 4);
    }

    public static bool IsSourceActive(string sourceModProjectId)
    {
        var source = (sourceModProjectId ?? "").Trim();
        return source.Length == 0 || ActiveSourceProjectIds.Contains(source);
    }

    public static void Refresh(string reason = "manual")
    {
        if (!initialized || currentConfig == null)
        {
            return;
        }
        var loaded = AuraToolsLoadedModCatalog.Capture();
        if (!loaded.LoadedStateAvailable)
        {
            ActiveSourceProjectIds.Clear();
            AuraCgRegistryRuntime.SetActiveDiscoverySources(Array.Empty<string>());
            Publish(new AuraToolsSharedResourceDiscoveryResult
            {
                Success = false,
                Errors = new List<string> { loaded.Diagnostic }
            });
            return;
        }

        var refreshGeneration = Interlocked.Increment(ref generation);
        var mods = loaded.Mods.ToList();
        var byRoot = mods.ToDictionary(
            mod => Normalize(mod.DirectoryName),
            mod => mod,
            StringComparer.OrdinalIgnoreCase);
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<DiscoveryBatch>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "shared-resource-discovery",
                Source = "AuraTools.SharedResourceDiscovery." + reason,
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = cancellation => PrepareBatch(byRoot.Keys, cancellation),
                IsStillCurrent = () => refreshGeneration == Interlocked.Read(ref generation),
                ApplyOnMainThread = batch => ApplyBatch(refreshGeneration, batch, byRoot),
                OnFailedOnMainThread = error => Publish(new AuraToolsSharedResourceDiscoveryResult
                {
                    Success = false,
                    LoadedMods = mods.Count,
                    Errors = new List<string> { error.Message }
                })
            });
        if (!queued)
        {
            Publish(new AuraToolsSharedResourceDiscoveryResult
            {
                Success = false,
                LoadedMods = mods.Count,
                Errors = new List<string> { "Shared resource discovery work could not be queued." }
            });
        }
    }

    private static void ScheduleRefresh(string source, int frames)
    {
        if (!AuraSharedFrameScheduler.RunAfterFrames(
                "AuraTools.SharedResourceDiscovery." + source,
                Math.Max(1, frames),
                () => Refresh(source)))
        {
            AuraToolsLog.Warn("[Resources] shared resource discovery could not be scheduled: " + source);
        }
    }

    private static DiscoveryBatch PrepareBatch(IEnumerable<string> roots, CancellationToken cancellation)
    {
        var batch = new DiscoveryBatch();
        foreach (var root in roots.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            batch.Items.Add(new DiscoveryCandidate
            {
                ModRoot = root,
                Load = AuraSharedDiscoveryLoader.Load(root, forceRefresh: true)
            });
        }
        return batch;
    }

    private static void ApplyBatch(
        long refreshGeneration,
        DiscoveryBatch batch,
        IReadOnlyDictionary<string, ModConfig> byRoot)
    {
        if (refreshGeneration != Interlocked.Read(ref generation))
        {
            return;
        }
        var result = new AuraToolsSharedResourceDiscoveryResult
        {
            LoadedMods = byRoot.Count
        };
        foreach (var failed in batch.Items.Where(item => item.Load.Found && !item.Load.Success))
        {
            result.Errors.Add(Path.GetFileName(failed.ModRoot) + ": " + failed.Load.Message);
        }

        var participants = batch.Items
            .Where(item => item.Load.Found && item.Load.Success && item.Load.Source != null)
            .ToList();
        result.Participants = participants.Count;
        var selected = new List<DiscoveryCandidate>();
        foreach (var group in participants.GroupBy(
                     item => item.Load.Source!.ModProjectId,
                     StringComparer.OrdinalIgnoreCase))
        {
            var variants = group.OrderBy(item => item.ModRoot, StringComparer.OrdinalIgnoreCase).ToList();
            var fingerprints = variants.Select(item => item.Load.Source!.Fingerprint)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (fingerprints.Count > 1)
            {
                result.Conflicts++;
                result.Errors.Add("MOD project id " + group.Key + " has different loaded SharedResources variants.");
                continue;
            }
            selected.Add(variants[0]);
            result.Deduplicated += variants.Count - 1;
        }

        var previousSnapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            StateSystem,
            StateFile,
            new DiscoveryStateDocument());
        var previous = previousSnapshot.Value ?? new DiscoveryStateDocument();
        previous.Normalize();
        var currentStates = new List<DiscoverySourceState>();
        foreach (var candidate in selected)
        {
            var source = candidate.Load.Source!;
            if (!byRoot.TryGetValue(Normalize(candidate.ModRoot), out var modConfig))
            {
                result.Errors.Add("Loaded Mod config disappeared during discovery: " + candidate.ModRoot);
                continue;
            }
            if (!TryPrepareSource(source, out var prepared, out var prepareError))
            {
                result.Errors.Add(Path.GetFileName(candidate.ModRoot) + ": " + prepareError);
                continue;
            }
            if (!CommitSource(modConfig, prepared, out var state, out var commitError))
            {
                DeactivateSource(prepared.ToState(), null);
                result.Errors.Add(Path.GetFileName(candidate.ModRoot) + ": " + commitError);
                continue;
            }
            currentStates.Add(state);
            result.Registered++;
        }

        foreach (var oldSource in previous.Sources)
        {
            var current = currentStates.FirstOrDefault(item => string.Equals(
                item.ModProjectId,
                oldSource.ModProjectId,
                StringComparison.OrdinalIgnoreCase));
            if (current == null)
            {
                DeactivateSource(oldSource, result);
                continue;
            }
            ReconcileRemovedContributions(oldSource, current, result);
        }

        PersistState(previousSnapshot.Revision, new DiscoveryStateDocument { Sources = currentStates }, result);
        ActiveSourceProjectIds.Clear();
        foreach (var sourceId in currentStates.Select(item => item.ModProjectId))
        {
            ActiveSourceProjectIds.Add(sourceId);
        }
        AuraCgRegistryRuntime.SetActiveDiscoverySources(ActiveSourceProjectIds);
        AuraToolsAudioRuntime.RegisterProviders();
        result.Success = result.Errors.Count == 0 && result.Conflicts == 0;
        Publish(result);
    }

    private static bool TryPrepareSource(
        AuraSharedDiscoverySource source,
        out PreparedDiscoverySource prepared,
        out string error)
    {
        prepared = new PreparedDiscoverySource { Source = source };
        error = "";
        try
        {
            foreach (var contribution in source.Contributions)
            {
                if (contribution.Kind == AuraSharedDiscoveryContributionKinds.Resources)
                {
                    var manifest = AuraSharedJson.Deserialize<AuraSharedRegistrationManifestV4>(
                        File.ReadAllText(contribution.AbsolutePath));
                    if (manifest == null
                        || manifest.SchemaVersion != AuraSharedResourceSchemaVersions.Current
                        || !string.Equals(manifest.OwnerModId, source.OwnerModId, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(
                            AuraSharedParticipantKinds.Normalize(manifest.ParticipantKind),
                            source.ParticipantKind,
                            StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(manifest.PackageId))
                    {
                        error = "Invalid or foreign-owned resource manifest: " + contribution.RelativePath;
                        return false;
                    }
                    prepared.Resources.Add(new PreparedResourceContribution
                    {
                        DiscoveryId = contribution.Id,
                        RelativePathFromModRoot = Relative(source.ModRoot, contribution.AbsolutePath),
                        Manifest = manifest
                    });
                    continue;
                }
                if (contribution.Kind == AuraSharedDiscoveryContributionKinds.Audio)
                {
                    var manifest = AuraSharedJson.Deserialize<AudioRegistryManifest>(
                        File.ReadAllText(contribution.AbsolutePath));
                    if (manifest == null
                        || manifest.schemaVersion <= 0
                        || manifest.schemaVersion > AudioArbiterRuntime.SupportedManifestSchemaVersion
                        || !string.Equals(manifest.ownerModId, source.OwnerModId, StringComparison.OrdinalIgnoreCase)
                        || manifest.audioProtocol?.minVersion > AudioArbiterRuntime.CurrentProtocolVersion)
                    {
                        error = "Invalid or foreign-owned audio registry: " + contribution.RelativePath;
                        return false;
                    }
                    prepared.Audio.Add(new PreparedAudioContribution
                    {
                        DiscoveryId = contribution.Id,
                        Manifest = manifest
                    });
                    continue;
                }
                if (contribution.Kind == AuraSharedDiscoveryContributionKinds.Cg)
                {
                    var manifest = AuraSharedJson.Deserialize<AuraCgManifest>(
                        File.ReadAllText(contribution.AbsolutePath));
                    if (manifest == null
                        || !string.Equals(manifest.OwnerModId, source.OwnerModId, StringComparison.OrdinalIgnoreCase)
                        || manifest.Protocol.MinVersion > AuraCgRegistryRuntime.CurrentRegistrySchemaVersion)
                    {
                        error = "Invalid or foreign-owned CG registry: " + contribution.RelativePath;
                        return false;
                    }
                    prepared.Cg.Add(new PreparedCgContribution
                    {
                        DiscoveryId = contribution.Id,
                        RegistryContributionId = string.IsNullOrWhiteSpace(manifest.ContributionId)
                            ? "manifest"
                            : manifest.ContributionId.Trim(),
                        Manifest = manifest
                    });
                    continue;
                }
                error = "Unsupported shared discovery contribution kind: " + contribution.Kind;
                return false;
            }
            if (prepared.Resources.GroupBy(item => item.Manifest.PackageId, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1))
            {
                error = "Shared discovery declares the same resource package id more than once.";
                return false;
            }
            if (prepared.Cg.GroupBy(item => item.RegistryContributionId, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1))
            {
                error = "Shared discovery declares the same CG contribution id more than once.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool CommitSource(
        ModConfig modConfig,
        PreparedDiscoverySource prepared,
        out DiscoverySourceState state,
        out string error)
    {
        state = prepared.ToState();
        error = "";
        foreach (var resource in prepared.Resources)
        {
            var bootstrap = AuraSharedResourceBootstrapper.Bootstrap(
                modConfig,
                prepared.Source.OwnerModId,
                resource.RelativePathFromModRoot);
            if (!bootstrap.Success)
            {
                error = "Resource contribution " + resource.DiscoveryId + " failed: "
                        + string.Join("; ", bootstrap.Responses.Where(item => !item.Success).Select(item => item.Message));
                return false;
            }
        }
        foreach (var audio in prepared.Audio)
        {
            var contributionId = ContributionId(prepared.Source.ModProjectId, audio.DiscoveryId);
            var registered = AuraAudioRegistryRuntime.RegisterContribution(
                prepared.Source.OwnerModId,
                contributionId,
                prepared.Source.ModProjectId,
                audio.Manifest);
            if (!registered.Success)
            {
                error = "Audio contribution " + audio.DiscoveryId + " failed: " + registered.Message;
                return false;
            }
        }
        foreach (var cg in prepared.Cg)
        {
            cg.Manifest.OwnerModId = prepared.Source.OwnerModId;
            cg.Manifest.ContributionId = cg.RegistryContributionId;
            cg.Manifest.SourceModProjectId = prepared.Source.ModProjectId;
            if (!AuraCgRegistryRuntime.RegisterManifest(prepared.Source.OwnerModId, cg.Manifest))
            {
                error = "CG contribution " + cg.DiscoveryId + " failed.";
                return false;
            }
        }
        return true;
    }

    private static void ReconcileRemovedContributions(
        DiscoverySourceState previous,
        DiscoverySourceState current,
        AuraToolsSharedResourceDiscoveryResult result)
    {
        if (!string.Equals(previous.OwnerModId, current.OwnerModId, StringComparison.OrdinalIgnoreCase))
        {
            DeactivateSource(previous, result);
            return;
        }
        foreach (var audio in previous.AudioContributionIds.Except(current.AudioContributionIds, StringComparer.OrdinalIgnoreCase))
        {
            AuraAudioRegistryRuntime.RemoveContribution(previous.OwnerModId, audio, previous.ModProjectId);
            result.Removed++;
        }
        foreach (var cg in previous.CgContributionIds.Except(current.CgContributionIds, StringComparer.OrdinalIgnoreCase))
        {
            AuraCgRegistryRuntime.RegisterContribution(previous.OwnerModId, cg, Array.Empty<AuraCgRegistryEntry>());
            result.Removed++;
        }
        foreach (var package in previous.ResourcePackages.Where(old => current.ResourcePackages.All(now =>
                     !string.Equals(now.PackageId, old.PackageId, StringComparison.OrdinalIgnoreCase))))
        {
            DeactivatePackage(previous.OwnerModId, previous.ParticipantKind, package);
            result.Removed++;
        }
    }

    private static void DeactivateSource(
        DiscoverySourceState source,
        AuraToolsSharedResourceDiscoveryResult? result)
    {
        foreach (var audio in source.AudioContributionIds)
        {
            AuraAudioRegistryRuntime.RemoveContribution(source.OwnerModId, audio, source.ModProjectId);
            if (result != null) result.Removed++;
        }
        foreach (var cg in source.CgContributionIds)
        {
            AuraCgRegistryRuntime.RegisterContribution(source.OwnerModId, cg, Array.Empty<AuraCgRegistryEntry>());
            if (result != null) result.Removed++;
        }
        foreach (var package in source.ResourcePackages)
        {
            DeactivatePackage(source.OwnerModId, source.ParticipantKind, package);
            if (result != null) result.Removed++;
        }
    }

    private static void DeactivatePackage(
        string ownerModId,
        string participantKind,
        DiscoveryResourcePackageState package)
    {
        AuraSharedResourceProtocol.Register(
            ownerModId,
            new AuraSharedRegistrationManifestV4
            {
                OwnerModId = ownerModId,
                ParticipantKind = participantKind,
                PackageId = package.PackageId,
                PackageVersion = Math.Max(1, package.PackageVersion + 1),
                Resources = new List<AuraSharedResourceDeclarationV4>(),
                Defaults = new List<AuraSharedDefaultProfileV4>()
            },
            AuraSharedPaths.RootDirectory);
    }

    private static void PersistState(
        long expectedRevision,
        DiscoveryStateDocument state,
        AuraToolsSharedResourceDiscoveryResult result)
    {
        state.Normalize();
        var revision = expectedRevision;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var write = AuraSharedConfigStore.WriteOwner(
                AuraToolsIds.ModId,
                StateSystem,
                StateFile,
                state,
                revision,
                DiscoveryStateDocument.CurrentSchemaVersion);
            if (write.Success)
            {
                return;
            }
            if (!write.Conflict)
            {
                result.Errors.Add("Shared discovery state could not be persisted: " + write.Message);
                return;
            }
            revision = AuraSharedConfigStore.ReadOwner(
                AuraToolsIds.ModId,
                StateSystem,
                StateFile,
                new DiscoveryStateDocument()).Revision;
        }
        result.Errors.Add("Shared discovery state conflicted repeatedly.");
    }

    private static string ContributionId(string projectId, string localId)
    {
        return "discovery." + projectId + "." + localId;
    }

    private static string Relative(string root, string path)
    {
        var prefix = Normalize(root) + Path.DirectorySeparatorChar;
        var full = Normalize(path);
        return (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(prefix.Length)
                : full)
            .Replace('\\', '/');
    }

    private static string Normalize(string path)
    {
        return Path.GetFullPath(path ?? "")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void Publish(AuraToolsSharedResourceDiscoveryResult result)
    {
        LastResult = result;
        if (result.Success)
        {
            AuraToolsLog.Info("[Resources] discovered=" + result.Participants
                              + ", registered=" + result.Registered
                              + ", deduplicated=" + result.Deduplicated
                              + ", removed=" + result.Removed + ".");
        }
        else
        {
            AuraToolsLog.Warn("[Resources] discovery completed with issues: "
                              + string.Join(" | ", result.Errors));
        }
        try
        {
            Changed?.Invoke(result);
        }
        catch
        {
        }
    }

    private sealed class DiscoveryBatch
    {
        internal List<DiscoveryCandidate> Items { get; } = new();
    }

    private sealed class DiscoveryCandidate
    {
        internal string ModRoot { get; set; } = "";
        internal AuraSharedDiscoveryLoadResult Load { get; set; } = new();
    }

    private sealed class PreparedDiscoverySource
    {
        internal AuraSharedDiscoverySource Source { get; set; } = new();
        internal List<PreparedResourceContribution> Resources { get; } = new();
        internal List<PreparedAudioContribution> Audio { get; } = new();
        internal List<PreparedCgContribution> Cg { get; } = new();

        internal DiscoverySourceState ToState()
        {
            return new DiscoverySourceState
            {
                ModProjectId = Source.ModProjectId,
                OwnerModId = Source.OwnerModId,
                ParticipantKind = Source.ParticipantKind,
                ModRoot = Source.ModRoot,
                Fingerprint = Source.Fingerprint,
                ResourcePackages = Resources.Select(item => new DiscoveryResourcePackageState
                {
                    PackageId = item.Manifest.PackageId,
                    PackageVersion = item.Manifest.PackageVersion
                }).ToList(),
                AudioContributionIds = Audio.Select(item => ContributionId(Source.ModProjectId, item.DiscoveryId)).ToList(),
                CgContributionIds = Cg.Select(item => item.RegistryContributionId).ToList()
            };
        }
    }

    private sealed class PreparedResourceContribution
    {
        internal string DiscoveryId { get; set; } = "";
        internal string RelativePathFromModRoot { get; set; } = "";
        internal AuraSharedRegistrationManifestV4 Manifest { get; set; } = new();
    }

    private sealed class PreparedAudioContribution
    {
        internal string DiscoveryId { get; set; } = "";
        internal AudioRegistryManifest Manifest { get; set; } = new();
    }

    private sealed class PreparedCgContribution
    {
        internal string DiscoveryId { get; set; } = "";
        internal string RegistryContributionId { get; set; } = "manifest";
        internal AuraCgManifest Manifest { get; set; } = new();
    }
}

internal sealed class DiscoveryStateDocument
{
    internal const int CurrentSchemaVersion = 1;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("sources")]
    public List<DiscoverySourceState> Sources { get; set; } = new();

    internal void Normalize()
    {
        SchemaVersion = Math.Max(CurrentSchemaVersion, SchemaVersion);
        Sources ??= new List<DiscoverySourceState>();
        Sources.ForEach(source => source?.Normalize());
        Sources = Sources
            .Where(source => source != null
                             && !string.IsNullOrWhiteSpace(source.ModProjectId)
                             && !string.IsNullOrWhiteSpace(source.OwnerModId))
            .GroupBy(source => source.ModProjectId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
    }
}

internal sealed class DiscoverySourceState
{
    [JsonProperty("modProjectId")]
    public string ModProjectId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = AuraSharedParticipantKinds.Content;

    [JsonProperty("modRoot")]
    public string ModRoot { get; set; } = "";

    [JsonProperty("fingerprint")]
    public string Fingerprint { get; set; } = "";

    [JsonProperty("resourcePackages")]
    public List<DiscoveryResourcePackageState> ResourcePackages { get; set; } = new();

    [JsonProperty("audioContributionIds")]
    public List<string> AudioContributionIds { get; set; } = new();

    [JsonProperty("cgContributionIds")]
    public List<string> CgContributionIds { get; set; } = new();

    public void Normalize()
    {
        ModProjectId = (ModProjectId ?? "").Trim();
        OwnerModId = (OwnerModId ?? "").Trim();
        ParticipantKind = AuraSharedParticipantKinds.Normalize(ParticipantKind);
        ModRoot = (ModRoot ?? "").Trim();
        Fingerprint = (Fingerprint ?? "").Trim();
        ResourcePackages ??= new List<DiscoveryResourcePackageState>();
        AudioContributionIds = Clean(AudioContributionIds);
        CgContributionIds = Clean(CgContributionIds);
    }

    private static List<string> Clean(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

internal sealed class DiscoveryResourcePackageState
{
    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; }
}
