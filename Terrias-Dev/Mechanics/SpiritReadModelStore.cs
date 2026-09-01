using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritCodexEntryView
{
    public string ProfileId { get; internal set; } = "";
    public string SpeciesId { get; internal set; } = "";
    public string FormKey { get; internal set; } = "";
    public string FormLabel { get; internal set; } = "";
    public string Tier { get; internal set; } = "";
    public int OwnedCount { get; internal set; }
    public int BestLevel { get; internal set; }
    public int BestAptitude { get; internal set; }
    public string RepresentativeSpiritUid { get; internal set; } = "";
    public string DisplayName { get; internal set; } = "";
    public string ElementId { get; internal set; } = "";
}

internal sealed class SpiritDetailReadModel
{
    public SpiritInstance Instance { get; set; } = new();
    public SpiritArtifactBattleSnapshot Artifacts { get; set; } = new();
    public SpiritGrowthViewSnapshot Growth { get; set; } = new();
    public SpiritTrainingViewSnapshot Training { get; set; } = new();
}

internal sealed class SpiritReadModelSnapshot
{
    private readonly object detailGate = new();
    private readonly Dictionary<string, SpiritDetailReadModel> details = new(StringComparer.Ordinal);

    public int Version { get; set; } = SpiritSystemContract.ReadModelVersion;
    public long StateGeneration { get; set; }
    public long CollectionRevision { get; set; }
    public string RegistryKey { get; set; } = "";
    public SpiritCollectionDocument Collection { get; set; } = new();
    public IReadOnlyDictionary<string, SpiritInstance> InstancesByUid { get; set; }
        = new Dictionary<string, SpiritInstance>(StringComparer.Ordinal);
    public IReadOnlyList<SpiritCodexEntryView> Codex { get; set; } = Array.Empty<SpiritCodexEntryView>();

    public SpiritInstance? Find(string uid)
        => InstancesByUid.TryGetValue((uid ?? "").Trim(), out var value) ? value : null;

    public SpiritDetailReadModel? Detail(string uid)
    {
        var normalized = (uid ?? "").Trim();
        if (normalized.Length == 0 || !InstancesByUid.TryGetValue(normalized, out var instance)) return null;
        lock (detailGate)
        {
            if (details.TryGetValue(normalized, out var cached))
            {
                TerriasPerformanceCounters.Record("Spirit.ReadModel.DetailCacheHit");
                return cached;
            }
            var artifacts = SpiritArtifactLoadoutResolver.Resolve(Collection, instance).Battle;
            var result = new SpiritDetailReadModel
            {
                Instance = instance,
                Artifacts = artifacts,
                Growth = SpiritGrowthQueryService.Build(instance, artifacts),
                Training = SpiritTrainingService.BuildView(instance)
            };
            details[normalized] = result;
            TerriasPerformanceCounters.Record("Spirit.ReadModel.DetailBuilt");
            return result;
        }
    }
}

internal static class SpiritReadModelStore
{
    private static readonly object SyncRoot = new();
    private static SpiritReadModelSnapshot cached = new();

    public static SpiritReadModelSnapshot Current()
    {
        lock (SyncRoot)
        {
            var generation = SpiritCollectionService.StateGeneration;
            var registryKey = CurrentRegistryKey();
            if (cached.StateGeneration == generation
                && cached.Version == SpiritSystemContract.ReadModelVersion
                && string.Equals(cached.RegistryKey, registryKey, StringComparison.Ordinal))
            {
                TerriasPerformanceCounters.Record("Spirit.ReadModel.CacheHit");
                return cached;
            }

            var collection = SpiritCollectionService.Snapshot();
            var byUid = collection.Instances
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.SpiritUid))
                .ToDictionary(value => value.SpiritUid, StringComparer.Ordinal);
            cached = new SpiritReadModelSnapshot
            {
                StateGeneration = generation,
                CollectionRevision = collection.Revision,
                RegistryKey = registryKey,
                Collection = collection,
                InstancesByUid = byUid,
                Codex = BuildCodex(collection)
            };
            TerriasPerformanceCounters.Record("Spirit.ReadModel.Rebuilt");
            return cached;
        }
    }

    private static IReadOnlyList<SpiritCodexEntryView> BuildCodex(SpiritCollectionDocument collection)
    {
        var ownedByProfile = collection.Instances
            .GroupBy(value => value.ProfileId ?? "", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return SpiritGrowthRegistry.RegisteredProfiles()
            .OrderBy(profile => profile.SpeciesId, StringComparer.Ordinal)
            .ThenBy(profile => profile.FormOrder)
            .ThenBy(profile => profile.ProfileId, StringComparer.Ordinal)
            .Select(profile =>
            {
                ownedByProfile.TryGetValue(profile.ProfileId ?? "", out var owned);
                owned ??= Array.Empty<SpiritInstance>();
                var representative = owned
                    .OrderByDescending(value => value.Level)
                    .ThenByDescending(value => value.Aptitude)
                    .FirstOrDefault();
                return new SpiritCodexEntryView
                {
                    ProfileId = profile.ProfileId ?? "",
                    SpeciesId = profile.SpeciesId ?? "",
                    FormKey = profile.FormKey ?? "",
                    FormLabel = SpiritGrowthRegistry.FormLabel(profile),
                    Tier = profile.Tier ?? "",
                    OwnedCount = owned.Length,
                    BestLevel = owned.Length == 0 ? 0 : owned.Max(value => value.Level),
                    BestAptitude = owned.Length == 0 ? 0 : owned.Max(value => value.Aptitude),
                    RepresentativeSpiritUid = representative?.SpiritUid ?? "",
                    DisplayName = representative == null
                        ? First(SpiritGrowthRegistry.FormLabel(profile), profile.ProfileId)
                        : SpiritPresentationResolver.Name(representative),
                    ElementId = representative?.ElementId ?? profile.CaptureElement
                };
            })
            .ToArray();
    }

    private static string CurrentRegistryKey()
        => SpiritGrowthRegistry.RegistryHash + "|"
           + SpiritIntentRegistry.RegistryHash + "|"
           + SpiritTrainingRegistry.RegistryHash + "|"
           + SpiritArtifactRegistry.RegistryHash + "|"
           + Terrias.Dll.GameApi.TerriasLanguageApi.CurrentLocale;

    private static string First(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first! : second ?? "";
}
