using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritArtifactLoadoutMutationService
{
    public static SpiritArtifactOperationResult Equip(
        SpiritCollectionDocument document,
        string spiritUid,
        string artifactUid)
    {
        var target = FindSpirit(document, spiritUid);
        var artifact = SpiritArtifactInventoryService.Find(document, artifactUid);
        if (target == null || artifact == null) return Failure("精灵或圣遗物不存在。");

        var desired = Snapshot(target.ArtifactLoadout);
        desired[artifact.SlotId] = artifact.ArtifactUid;
        var result = ApplyExact(document, target.SpiritUid, desired, requireComplete: false);
        if (result.Success) result.Artifact = artifact.Clone();
        return result;
    }

    public static SpiritArtifactOperationResult Unequip(
        SpiritCollectionDocument document,
        string spiritUid,
        string slotId)
    {
        var target = FindSpirit(document, spiritUid);
        var normalizedSlot = SpiritArtifactSlots.Normalize(slotId);
        if (target == null || normalizedSlot.Length == 0) return Failure("精灵或圣遗物槽位不存在。");

        var desired = Snapshot(target.ArtifactLoadout);
        desired[normalizedSlot] = "";
        return ApplyExact(document, target.SpiritUid, desired, requireComplete: false);
    }

    public static SpiritArtifactOperationResult ApplyExact(
        SpiritCollectionDocument document,
        string spiritUid,
        IReadOnlyDictionary<string, string>? requested,
        bool requireComplete)
    {
        if (document == null) return Failure("精灵收藏档案不存在。");
        var target = FindSpirit(document, spiritUid);
        if (target == null) return Failure("目标精灵不存在。");

        var desired = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in SpiritArtifactSlots.All)
        {
            var value = requested != null && requested.TryGetValue(slot, out var uid)
                ? (uid ?? "").Trim()
                : "";
            desired[slot] = value;
        }

        var desiredUids = desired.Values.Where(value => value.Length > 0).ToArray();
        if (requireComplete && desiredUids.Length != SpiritArtifactSlots.All.Count)
            return Failure("完整预设必须包含五个圣遗物部件。");
        if (desiredUids.Distinct(StringComparer.Ordinal).Count() != desiredUids.Length)
            return Failure("配置中存在重复的圣遗物实例。");

        var artifacts = (document.ArtifactInventory?.Artifacts ?? new List<SpiritArtifactInstance>())
            .Where(value => value != null && !string.IsNullOrWhiteSpace(value.ArtifactUid))
            .ToDictionary(value => value.ArtifactUid, StringComparer.Ordinal);
        foreach (var pair in desired)
        {
            if (pair.Value.Length == 0) continue;
            if (!artifacts.TryGetValue(pair.Value, out var artifact))
                return Failure("配置中的圣遗物已经不存在。");
            if (!string.Equals(artifact.SlotId, pair.Key, StringComparison.Ordinal))
                return Failure("配置中的圣遗物部件与槽位不匹配。");
        }

        document.Instances ??= new List<SpiritInstance>();
        foreach (var spirit in document.Instances) spirit.ArtifactLoadout ??= new SpiritArtifactLoadout();
        var before = document.Instances.ToDictionary(
            spirit => spirit.SpiritUid,
            spirit => Signature(spirit.ArtifactLoadout),
            StringComparer.Ordinal);
        var ownersBefore = desiredUids.ToDictionary(
            uid => uid,
            uid => OwnerUid(document, uid),
            StringComparer.Ordinal);

        foreach (var slot in SpiritArtifactSlots.All) target.ArtifactLoadout.Set(slot, "");
        if (desiredUids.Length > 0)
        {
            var desiredSet = new HashSet<string>(desiredUids, StringComparer.Ordinal);
            foreach (var spirit in document.Instances)
            foreach (var slot in SpiritArtifactSlots.All)
            {
                if (desiredSet.Contains(spirit.ArtifactLoadout.Get(slot)))
                    spirit.ArtifactLoadout.Set(slot, "");
            }
        }
        foreach (var pair in desired) target.ArtifactLoadout.Set(pair.Key, pair.Value);

        var affected = new List<string>();
        foreach (var spirit in document.Instances)
        {
            if (before.TryGetValue(spirit.SpiritUid, out var previous)
                && string.Equals(previous, Signature(spirit.ArtifactLoadout), StringComparison.Ordinal)) continue;
            spirit.ArtifactLoadout.Revision = Math.Max(0, spirit.ArtifactLoadout.Revision) + 1;
            affected.Add(spirit.SpiritUid);
        }
        if (affected.Count > 0) SpiritArtifactInventoryService.RefreshLoadoutHashes(document);

        return new SpiritArtifactOperationResult
        {
            Success = true,
            AffectedSpiritUids = affected,
            TransferredArtifactCount = ownersBefore.Count(pair => pair.Value.Length > 0
                && !string.Equals(pair.Value, target.SpiritUid, StringComparison.Ordinal))
        };
    }

    public static Dictionary<string, string> Snapshot(SpiritArtifactLoadout? loadout)
    {
        loadout ??= new SpiritArtifactLoadout();
        return SpiritArtifactSlots.All.ToDictionary(
            slot => slot,
            slot => loadout.Get(slot),
            StringComparer.Ordinal);
    }

    private static SpiritInstance? FindSpirit(SpiritCollectionDocument document, string spiritUid)
        => (document.Instances ?? new List<SpiritInstance>()).FirstOrDefault(
            value => string.Equals(value.SpiritUid ?? "", spiritUid ?? "", StringComparison.Ordinal));

    private static string OwnerUid(SpiritCollectionDocument document, string artifactUid)
        => (document.Instances ?? new List<SpiritInstance>()).FirstOrDefault(
            spirit => spirit.ArtifactLoadout?.ArtifactUids().Contains(artifactUid, StringComparer.Ordinal) == true)
            ?.SpiritUid ?? "";

    private static string Signature(SpiritArtifactLoadout? loadout)
        => string.Join("|", SpiritArtifactSlots.All.Select(slot => loadout?.Get(slot) ?? ""));

    private static SpiritArtifactOperationResult Failure(string reason)
        => new() { Reason = reason ?? "圣遗物配置操作失败。" };
}
