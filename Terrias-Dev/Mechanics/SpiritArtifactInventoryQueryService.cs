using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritArtifactInventoryFilter
{
    public string SlotId { get; set; } = "";

    public int RarityMask { get; set; }

    public int LevelBand { get; set; }

    public string SetId { get; set; } = "";

    public string MainStatId { get; set; } = "";

    public bool CleanableOnly { get; set; }

    public SpiritArtifactInventoryFilter Clone()
    {
        return new SpiritArtifactInventoryFilter
        {
            SlotId = SlotId,
            RarityMask = RarityMask,
            LevelBand = LevelBand,
            SetId = SetId,
            MainStatId = MainStatId,
            CleanableOnly = CleanableOnly
        };
    }

    public void Reset()
    {
        RarityMask = 0;
        LevelBand = 0;
        SetId = "";
        MainStatId = "";
        CleanableOnly = false;
    }
}

public sealed class SpiritArtifactBatchSummary
{
    public int SelectedCount { get; set; }

    public int CleanableCount { get; set; }

    public int PresetProtectedCount { get; set; }

    public int EquippedCount { get; set; }

    public int LockedCount { get; set; }

    public int EstimatedEssence { get; set; }
}

public static class SpiritArtifactInventoryQueryService
{
    public static IReadOnlyList<SpiritArtifactInstance> Filter(
        SpiritCollectionDocument document,
        SpiritArtifactInventoryFilter? filter)
    {
        document ??= new SpiritCollectionDocument();
        filter ??= new SpiritArtifactInventoryFilter();
        var source = document.ArtifactInventory?.Artifacts ?? new List<SpiritArtifactInstance>();
        var protectedUids = SpiritArtifactPresetService.ProtectedArtifactUids(document);
        var equippedUids = EquippedUids(document);
        var values = source.Where(value => Matches(document, value, filter, protectedUids, equippedUids));
        return values.OrderByDescending(value => value.Rarity)
            .ThenByDescending(value => value.Level)
            .ThenByDescending(value => value.AcquiredAt, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool Matches(
        SpiritCollectionDocument document,
        SpiritArtifactInstance artifact,
        SpiritArtifactInventoryFilter filter)
        => Matches(
            document,
            artifact,
            filter,
            SpiritArtifactPresetService.ProtectedArtifactUids(document),
            EquippedUids(document));

    private static bool Matches(
        SpiritCollectionDocument document,
        SpiritArtifactInstance artifact,
        SpiritArtifactInventoryFilter filter,
        ISet<string> protectedUids,
        ISet<string> equippedUids)
    {
        if (!string.IsNullOrWhiteSpace(filter.SlotId)
            && !string.Equals(artifact.SlotId, SpiritArtifactSlots.Normalize(filter.SlotId), StringComparison.Ordinal)) return false;
        if (filter.RarityMask != 0 && (filter.RarityMask & (1 << artifact.Rarity)) == 0) return false;
        if (filter.LevelBand == 1 && artifact.Level != 1) return false;
        if (filter.LevelBand == 2 && (artifact.Level < 2 || artifact.Level > 4)) return false;
        if (filter.LevelBand == 3 && artifact.Level != 5) return false;
        if (!string.IsNullOrWhiteSpace(filter.SetId)
            && !string.Equals(artifact.SetId, filter.SetId, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(filter.MainStatId)
            && !string.Equals(artifact.MainStat?.StatId, filter.MainStatId, StringComparison.Ordinal)) return false;
        return !filter.CleanableOnly || IsCleanable(artifact, protectedUids, equippedUids);
    }

    public static bool IsCleanable(SpiritCollectionDocument document, SpiritArtifactInstance artifact)
        => IsCleanable(
            artifact,
            SpiritArtifactPresetService.ProtectedArtifactUids(document),
            EquippedUids(document));

    public static HashSet<string> SelectAllCleanable(
        SpiritCollectionDocument document,
        IEnumerable<SpiritArtifactInstance>? values)
    {
        var protectedUids = SpiritArtifactPresetService.ProtectedArtifactUids(document);
        var equippedUids = EquippedUids(document);
        return new HashSet<string>(
            (values ?? Array.Empty<SpiritArtifactInstance>())
            .Where(value => IsCleanable(value, protectedUids, equippedUids))
            .Select(value => value.ArtifactUid),
            StringComparer.Ordinal);
    }

    public static SpiritArtifactBatchSummary Summarize(
        SpiritCollectionDocument document,
        IEnumerable<string>? artifactUids)
    {
        document ??= new SpiritCollectionDocument();
        var requested = new HashSet<string>(artifactUids ?? Array.Empty<string>(), StringComparer.Ordinal);
        var artifacts = (document.ArtifactInventory?.Artifacts ?? new List<SpiritArtifactInstance>())
            .Where(value => requested.Contains(value.ArtifactUid)).ToArray();
        var protectedUids = SpiritArtifactPresetService.ProtectedArtifactUids(document);
        var equippedUids = EquippedUids(document);
        var result = new SpiritArtifactBatchSummary { SelectedCount = artifacts.Length };
        foreach (var artifact in artifacts)
        {
            var presetProtected = protectedUids.Contains(artifact.ArtifactUid);
            var equipped = equippedUids.Contains(artifact.ArtifactUid);
            if (presetProtected) result.PresetProtectedCount++;
            if (equipped) result.EquippedCount++;
            if (artifact.Locked) result.LockedCount++;
            if (presetProtected || equipped || artifact.Locked) continue;
            result.CleanableCount++;
            result.EstimatedEssence += SpiritArtifactRoller.DismantleValue(artifact);
        }
        return result;
    }

    private static bool IsCleanable(
        SpiritArtifactInstance artifact,
        ISet<string> protectedUids,
        ISet<string> equippedUids)
        => artifact != null
           && !artifact.Locked
           && !protectedUids.Contains(artifact.ArtifactUid)
           && !equippedUids.Contains(artifact.ArtifactUid);

    private static HashSet<string> EquippedUids(SpiritCollectionDocument? document)
        => new(
            (document?.Instances ?? new List<SpiritInstance>())
            .SelectMany(value => value.ArtifactLoadout?.ArtifactUids() ?? Array.Empty<string>()),
            StringComparer.Ordinal);
}
