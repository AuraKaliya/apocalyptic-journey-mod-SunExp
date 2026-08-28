using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritArtifactPresetView
{
    public SpiritArtifactPreset Preset { get; set; } = new();

    public bool IsValid { get; set; }

    public string InvalidReason { get; set; } = "";

    public string MatchingSpiritUid { get; set; } = "";

    public List<string> OwnerSpiritUids { get; set; } = new();

    public int MissingArtifactCount { get; set; }

    public int TransferCountFor(string spiritUid)
        => OwnerSpiritUids.Count(value => !string.Equals(value, spiritUid ?? "", StringComparison.Ordinal));
}

public static class SpiritArtifactPresetService
{
    public static void NormalizeInventory(SpiritArtifactInventory inventory)
    {
        inventory.Presets ??= new List<SpiritArtifactPreset>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<SpiritArtifactPreset>();
        foreach (var source in inventory.Presets.Where(value => value != null)
                     .OrderBy(value => value.Order).ThenBy(value => value.CreatedAt, StringComparer.Ordinal))
        {
            var preset = source.Clone();
            preset.PresetUid = string.IsNullOrWhiteSpace(preset.PresetUid)
                ? Guid.NewGuid().ToString("N")
                : preset.PresetUid.Trim();
            while (!seen.Add(preset.PresetUid)) preset.PresetUid = Guid.NewGuid().ToString("N");
            preset.Name = UniqueName(
                NormalizeName(preset.Name, normalized.Count + 1),
                usedNames);
            preset.Revision = Math.Max(1, preset.Revision);
            foreach (var slot in SpiritArtifactSlots.All) preset.Set(slot, preset.Get(slot));
            normalized.Add(preset);
            if (normalized.Count >= SpiritSystemContract.ArtifactPresetCapacity) break;
        }
        for (var index = 0; index < normalized.Count; index++) normalized[index].Order = index;
        inventory.Presets = normalized;
    }

    public static HashSet<string> ProtectedArtifactUids(SpiritCollectionDocument? document)
        => ProtectedArtifactUids(document?.ArtifactInventory);

    public static HashSet<string> ProtectedArtifactUids(SpiritArtifactInventory? inventory)
    {
        return new HashSet<string>(
            (inventory?.Presets ?? new List<SpiritArtifactPreset>())
            .Where(value => value != null)
            .SelectMany(value => value.ArtifactUids()),
            StringComparer.Ordinal);
    }

    public static bool IsProtected(SpiritCollectionDocument? document, string artifactUid)
        => ProtectedArtifactUids(document).Contains((artifactUid ?? "").Trim());

    public static SpiritArtifactOperationResult Save(
        SpiritCollectionDocument document,
        SpiritArtifactPreset draft)
    {
        if (document?.ArtifactInventory == null || draft == null) return Failure("预设数据不存在。");
        NormalizeInventory(document.ArtifactInventory);
        var inventory = document.ArtifactInventory;
        var existing = inventory.Presets.FirstOrDefault(value => Same(value.PresetUid, draft.PresetUid));
        if (existing == null && inventory.Presets.Count >= SpiritSystemContract.ArtifactPresetCapacity)
            return Failure("圣遗物预设已经达到20套上限。");

        var name = NormalizeName(draft.Name, inventory.Presets.Count + 1);
        if (inventory.Presets.Any(value => !ReferenceEquals(value, existing)
                                           && SameName(value.Name, name)))
            return Failure("已经存在同名圣遗物预设。");
        if (!Validate(document, draft, out var reason)) return Failure(reason);
        if (existing == null && inventory.Presets.Any(value => SameArtifacts(value, draft)))
            return Failure("已经存在内容完全相同的圣遗物预设。");

        var now = DateTimeOffset.UtcNow.ToString("O");
        var saved = existing ?? new SpiritArtifactPreset
        {
            PresetUid = Guid.NewGuid().ToString("N"),
            Order = inventory.Presets.Count,
            Revision = 0,
            CreatedAt = now
        };
        saved.Name = name;
        foreach (var slot in SpiritArtifactSlots.All) saved.Set(slot, draft.Get(slot));
        saved.Revision = Math.Max(0, saved.Revision) + 1;
        saved.UpdatedAt = now;
        if (existing == null) inventory.Presets.Add(saved);
        NormalizeInventory(inventory);
        return new SpiritArtifactOperationResult { Success = true, Preset = saved.Clone() };
    }

    public static SpiritArtifactOperationResult SaveCurrent(
        SpiritCollectionDocument document,
        string spiritUid,
        string name)
    {
        if (document == null) return Failure("精灵收藏档案不存在。");
        var spirit = (document.Instances ?? new List<SpiritInstance>()).FirstOrDefault(value => Same(value.SpiritUid, spiritUid));
        if (spirit == null) return Failure("目标精灵不存在。");
        var preset = new SpiritArtifactPreset { Name = name };
        foreach (var slot in SpiritArtifactSlots.All) preset.Set(slot, spirit.ArtifactLoadout?.Get(slot));
        return Save(document, preset);
    }

    public static SpiritArtifactOperationResult Delete(SpiritCollectionDocument document, string presetUid)
    {
        var inventory = document?.ArtifactInventory;
        if (inventory == null) return Failure("圣遗物仓库不存在。");
        var removed = inventory.Presets?.RemoveAll(value => Same(value.PresetUid, presetUid)) ?? 0;
        if (removed == 0) return Failure("圣遗物预设已经不存在。");
        NormalizeInventory(inventory);
        return Success();
    }

    public static SpiritArtifactOperationResult Move(SpiritCollectionDocument document, string presetUid, int delta)
    {
        var inventory = document?.ArtifactInventory;
        if (inventory == null) return Failure("圣遗物仓库不存在。");
        NormalizeInventory(inventory);
        var index = inventory.Presets.FindIndex(value => Same(value.PresetUid, presetUid));
        if (index < 0) return Failure("圣遗物预设已经不存在。");
        var next = Math.Max(0, Math.Min(inventory.Presets.Count - 1, index + Math.Sign(delta)));
        if (next == index) return Success();
        var value = inventory.Presets[index];
        inventory.Presets.RemoveAt(index);
        inventory.Presets.Insert(next, value);
        for (var order = 0; order < inventory.Presets.Count; order++) inventory.Presets[order].Order = order;
        return new SpiritArtifactOperationResult { Success = true, Preset = value.Clone() };
    }

    public static SpiritArtifactOperationResult Apply(
        SpiritCollectionDocument document,
        string spiritUid,
        string presetUid)
    {
        if (document == null) return Failure("精灵收藏档案不存在。");
        var preset = document.ArtifactInventory?.Presets?.FirstOrDefault(value => Same(value.PresetUid, presetUid));
        if (preset == null) return Failure("圣遗物预设已经不存在。");
        if (!Validate(document, preset, out var reason)) return Failure(reason);
        var desired = SpiritArtifactSlots.All.ToDictionary(slot => slot, slot => preset.Get(slot), StringComparer.Ordinal);
        var result = SpiritArtifactLoadoutMutationService.ApplyExact(document, spiritUid, desired, requireComplete: true);
        if (result.Success) result.Preset = preset.Clone();
        return result;
    }

    public static bool Validate(
        SpiritCollectionDocument? document,
        SpiritArtifactPreset? preset,
        out string reason)
    {
        if (document?.ArtifactInventory == null || preset == null)
        {
            reason = "预设数据不存在。";
            return false;
        }
        var uids = preset.ArtifactUids();
        if (uids.Count != SpiritArtifactSlots.All.Count || uids.Distinct(StringComparer.Ordinal).Count() != uids.Count)
        {
            reason = "完整预设必须包含五个不同部件。";
            return false;
        }
        var known = document.ArtifactInventory.Artifacts.ToDictionary(value => value.ArtifactUid, StringComparer.Ordinal);
        foreach (var slot in SpiritArtifactSlots.All)
        {
            var uid = preset.Get(slot);
            if (!known.TryGetValue(uid, out var artifact))
            {
                reason = "预设中的圣遗物已经不存在。";
                return false;
            }
            if (!string.Equals(artifact.SlotId, slot, StringComparison.Ordinal))
            {
                reason = "预设中的圣遗物部件与槽位不匹配。";
                return false;
            }
        }
        reason = "";
        return true;
    }

    public static SpiritArtifactPresetView ResolveView(
        SpiritCollectionDocument document,
        SpiritArtifactPreset preset)
    {
        var known = (document.ArtifactInventory?.Artifacts ?? new List<SpiritArtifactInstance>())
            .ToDictionary(value => value.ArtifactUid, StringComparer.Ordinal);
        var missing = preset.ArtifactUids().Count(uid => !known.ContainsKey(uid));
        var valid = Validate(document, preset, out var reason);
        var owners = preset.ArtifactUids()
            .Select(uid => OwnerUid(document, uid))
            .Where(uid => uid.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var matching = (document.Instances ?? new List<SpiritInstance>()).FirstOrDefault(spirit =>
            SpiritArtifactSlots.All.All(slot => Same(spirit.ArtifactLoadout?.Get(slot), preset.Get(slot))))?.SpiritUid ?? "";
        return new SpiritArtifactPresetView
        {
            Preset = preset.Clone(),
            IsValid = valid,
            InvalidReason = reason,
            MatchingSpiritUid = matching,
            OwnerSpiritUids = owners,
            MissingArtifactCount = missing
        };
    }

    public static string SuggestName(SpiritCollectionDocument document, SpiritArtifactLoadout? loadout)
    {
        if (document == null) return "圣遗物预设";
        document.ArtifactInventory ??= new SpiritArtifactInventory();
        var inventory = document.ArtifactInventory;
        inventory.Presets ??= new List<SpiritArtifactPreset>();
        var known = (inventory.Artifacts ?? new List<SpiritArtifactInstance>())
            .ToDictionary(value => value.ArtifactUid, StringComparer.Ordinal);
        var set = SpiritArtifactSlots.All.Select(slot => loadout?.Get(slot) ?? "")
            .Where(known.ContainsKey)
            .Select(uid => known[uid].SetId)
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        var baseName = set == null ? "圣遗物预设" : SpiritArtifactRegistry.Name(SpiritArtifactRegistry.Set(set.Key));
        var used = new HashSet<string>(inventory.Presets.Select(value => value.Name), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName)) return baseName;
        for (var index = 2; index <= SpiritSystemContract.ArtifactPresetCapacity + 1; index++)
        {
            var candidate = baseName + " " + index;
            if (!used.Contains(candidate)) return candidate;
        }
        return "圣遗物预设 " + (inventory.Presets.Count + 1);
    }

    private static string OwnerUid(SpiritCollectionDocument document, string artifactUid)
        => (document.Instances ?? new List<SpiritInstance>()).FirstOrDefault(
            spirit => spirit.ArtifactLoadout?.ArtifactUids().Contains(artifactUid, StringComparer.Ordinal) == true)
            ?.SpiritUid ?? "";

    private static string NormalizeName(string? value, int fallbackIndex)
    {
        var result = (value ?? "").Trim();
        if (result.Length == 0) result = "预设 " + Math.Max(1, fallbackIndex);
        if (result.Length > SpiritSystemContract.ArtifactPresetNameMaximumLength)
            result = result.Substring(0, SpiritSystemContract.ArtifactPresetNameMaximumLength);
        return result;
    }

    private static string UniqueName(string baseName, ISet<string> usedNames)
    {
        if (usedNames.Add(baseName)) return baseName;
        for (var index = 2; index <= SpiritSystemContract.ArtifactPresetCapacity + 1; index++)
        {
            var suffix = " " + index;
            var maximumBaseLength = Math.Max(1, SpiritSystemContract.ArtifactPresetNameMaximumLength - suffix.Length);
            var prefix = baseName.Length > maximumBaseLength ? baseName.Substring(0, maximumBaseLength) : baseName;
            var candidate = prefix + suffix;
            if (usedNames.Add(candidate)) return candidate;
        }
        var fallback = "预设 " + (usedNames.Count + 1);
        usedNames.Add(fallback);
        return fallback;
    }

    private static bool SameArtifacts(SpiritArtifactPreset left, SpiritArtifactPreset right)
        => SpiritArtifactSlots.All.All(slot => Same(left.Get(slot), right.Get(slot)));

    private static bool Same(string? left, string? right)
        => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);

    private static bool SameName(string? left, string? right)
        => string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static SpiritArtifactOperationResult Success() => new() { Success = true };

    private static SpiritArtifactOperationResult Failure(string reason)
        => new() { Reason = reason ?? "圣遗物预设操作失败。" };
}
