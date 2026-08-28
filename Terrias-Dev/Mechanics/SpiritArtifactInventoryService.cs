using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritArtifactInventoryService
{
    public static void NormalizeDocument(SpiritCollectionDocument document)
    {
        document.ArtifactInventory ??= new SpiritArtifactInventory();
        var inventory = document.ArtifactInventory;
        inventory.Version = SpiritSystemContract.ArtifactInventoryVersion;
        inventory.Essence = Math.Max(0, inventory.Essence);
        inventory.RarityPity = Math.Max(0, Math.Min(29, inventory.RarityPity));
        inventory.TargetFate = Math.Max(0, Math.Min(1, inventory.TargetFate));
        inventory.Artifacts ??= new List<SpiritArtifactInstance>();
        inventory.Presets ??= new List<SpiritArtifactPreset>();
        inventory.PendingReveals ??= new List<SpiritArtifactDrawReceipt>();
        inventory.ProcessedDrawTokens ??= new List<string>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        inventory.Artifacts = inventory.Artifacts
            .Where(value => value != null)
            .Select(value => NormalizeArtifact(value))
            .Where(value => value != null && seen.Add(value.ArtifactUid))
            .Cast<SpiritArtifactInstance>()
            .Take(SpiritArtifactRegistry.InventoryCapacity)
            .ToList();
        SpiritArtifactPresetService.NormalizeInventory(inventory);

        var firstPool = SpiritArtifactRegistry.Pools().FirstOrDefault();
        var pool = SpiritArtifactRegistry.Pool(inventory.SelectedPoolId) ?? firstPool;
        inventory.SelectedPoolId = pool?.Id ?? "";
        if (pool == null || !pool.SetIds.Contains(inventory.TargetSetId, StringComparer.Ordinal))
            inventory.TargetSetId = pool?.SetIds.FirstOrDefault() ?? "";

        inventory.PendingReveals = inventory.PendingReveals
            .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Token))
            .TakeLastCompat(SpiritSystemContract.ArtifactPendingRevealLimit)
            .Select(value => value.Clone())
            .ToList();
        inventory.ProcessedDrawTokens = inventory.ProcessedDrawTokens
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .TakeLastCompat(SpiritSystemContract.ArtifactProcessedDrawTokenLimit)
            .ToList();

        var known = inventory.Artifacts.ToDictionary(value => value.ArtifactUid, StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spirit in document.Instances ?? new List<SpiritInstance>())
        {
            spirit.ArtifactLoadout ??= new SpiritArtifactLoadout();
            foreach (var slot in SpiritArtifactSlots.All)
            {
                var uid = spirit.ArtifactLoadout.Get(slot);
                if (uid.Length == 0) continue;
                if (!known.TryGetValue(uid, out var artifact) || artifact.SlotId != slot || !claimed.Add(uid))
                    spirit.ArtifactLoadout.Set(slot, "");
            }
        }
        RefreshLoadoutHashes(document);
    }

    public static SpiritArtifactOperationResult SetTarget(
        SpiritCollectionDocument document,
        string poolId,
        string setId)
    {
        var pool = SpiritArtifactRegistry.Pool(poolId);
        if (pool == null || !pool.SetIds.Contains(setId ?? "", StringComparer.Ordinal))
            return Failure("目标套装不属于所选祈愿池。");
        var normalizedSetId = (setId ?? "").Trim();
        var changed = !string.Equals(document.ArtifactInventory.SelectedPoolId, pool.Id, StringComparison.Ordinal)
                      || !string.Equals(document.ArtifactInventory.TargetSetId, normalizedSetId, StringComparison.Ordinal);
        document.ArtifactInventory.SelectedPoolId = pool.Id;
        document.ArtifactInventory.TargetSetId = normalizedSetId;
        if (changed) document.ArtifactInventory.TargetFate = 0;
        return Success();
    }

    public static SpiritArtifactOperationResult PrepareDraw(
        SpiritCollectionDocument document,
        ISpiritArtifactRandom random,
        string token)
    {
        var inventory = document.ArtifactInventory;
        if (inventory.PreparedDraw != null) return Failure("已有一笔圣遗物抽取正在结算。");
        if (inventory.ProcessedDrawTokens.Contains(token, StringComparer.Ordinal))
            return Failure("该抽取操作已经结算。");
        var plan = SpiritArtifactRoller.PrepareTenDraw(
            inventory,
            inventory.SelectedPoolId,
            inventory.TargetSetId,
            random,
            token);
        if (!plan.Success) return Failure(plan.Reason);
        inventory.PreparedDraw = new SpiritArtifactPreparedDraw
        {
            Token = plan.Token,
            TruthCost = plan.TruthCost,
            PoolId = plan.PoolId,
            TargetSetId = plan.TargetSetId,
            Results = plan.Results.Select(value => value.Clone()).ToList(),
            ResultingRarityPity = plan.ResultingRarityPity,
            ResultingTargetFate = plan.ResultingTargetFate,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        return new SpiritArtifactOperationResult
        {
            Success = true,
            Token = plan.Token,
            Artifacts = plan.Results.Select(value => value.Clone()).ToList()
        };
    }

    public static SpiritArtifactOperationResult CommitPreparedDraw(SpiritCollectionDocument document, string token)
    {
        var inventory = document.ArtifactInventory;
        var prepared = inventory.PreparedDraw;
        if (prepared == null || !string.Equals(prepared.Token, token, StringComparison.Ordinal))
            return Failure("找不到待结算的圣遗物抽取。");
        if (inventory.Artifacts.Count + prepared.Results.Count > SpiritArtifactRegistry.InventoryCapacity)
            return Failure("圣遗物仓库空间不足，无法结算抽取。");
        if (inventory.ProcessedDrawTokens.Contains(token, StringComparer.Ordinal))
        {
            inventory.PreparedDraw = null;
            return Success();
        }
        var existing = new HashSet<string>(inventory.Artifacts.Select(value => value.ArtifactUid), StringComparer.Ordinal);
        if (prepared.Results.Any(value => string.IsNullOrWhiteSpace(value.ArtifactUid) || !existing.Add(value.ArtifactUid)))
            return Failure("抽取结果包含重复的圣遗物实例标识。");
        foreach (var artifact in prepared.Results)
        {
            if (!SpiritArtifactLoadoutResolver.ValidateItem(SpiritArtifactLoadoutResolver.ToBattleItem(artifact), out var reason))
                return Failure("抽取结果校验失败：" + reason);
        }

        var committed = prepared.Results.Select(value => value.Clone()).ToList();
        inventory.Artifacts.AddRange(committed);
        inventory.RarityPity = prepared.ResultingRarityPity;
        inventory.TargetFate = prepared.ResultingTargetFate;
        inventory.SelectedPoolId = prepared.PoolId;
        inventory.TargetSetId = prepared.TargetSetId;
        inventory.ProcessedDrawTokens.Add(token);
        inventory.ProcessedDrawTokens = inventory.ProcessedDrawTokens
            .TakeLastCompat(SpiritSystemContract.ArtifactProcessedDrawTokenLimit).ToList();
        inventory.PendingReveals.Add(new SpiritArtifactDrawReceipt
        {
            Token = token,
            ArtifactUids = committed.Select(value => value.ArtifactUid).ToList(),
            CreatedAt = prepared.CreatedAt
        });
        inventory.PendingReveals = inventory.PendingReveals
            .TakeLastCompat(SpiritSystemContract.ArtifactPendingRevealLimit).ToList();
        inventory.PreparedDraw = null;
        return new SpiritArtifactOperationResult { Success = true, Token = token, Artifacts = committed };
    }

    public static SpiritArtifactOperationResult CancelPreparedDraw(SpiritCollectionDocument document, string token)
    {
        if (document.ArtifactInventory.PreparedDraw == null) return Success();
        if (!string.Equals(document.ArtifactInventory.PreparedDraw.Token, token ?? "", StringComparison.Ordinal))
            return Failure("待取消的抽取令牌不匹配。");
        document.ArtifactInventory.PreparedDraw = null;
        return Success();
    }

    public static SpiritArtifactOperationResult Upgrade(
        SpiritCollectionDocument document,
        string artifactUid,
        ISpiritArtifactRandom random)
    {
        var artifact = Find(document, artifactUid);
        if (artifact == null) return Failure("未找到圣遗物。");
        if (artifact.Level >= 5) return Failure("圣遗物已经达到最高等级。");
        var cost = SpiritArtifactRegistry.UpgradeCost(artifact.Level);
        if (cost <= 0 || document.ArtifactInventory.Essence < cost) return Failure("精粹不足。");
        var roll = SpiritArtifactRoller.RollSubStat(artifact.Rarity, random);
        document.ArtifactInventory.Essence -= cost;
        artifact.InvestedEssence += cost;
        artifact.Level++;
        artifact.SubStatRolls.Add(roll);
        RefreshLoadoutHashes(document);
        return new SpiritArtifactOperationResult { Success = true, EssenceDelta = -cost, Artifact = artifact.Clone() };
    }

    public static SpiritArtifactOperationResult ToggleLock(SpiritCollectionDocument document, string artifactUid)
    {
        var artifact = Find(document, artifactUid);
        if (artifact == null) return Failure("未找到圣遗物。");
        artifact.Locked = !artifact.Locked;
        return new SpiritArtifactOperationResult { Success = true, Artifact = artifact.Clone() };
    }

    public static SpiritArtifactOperationResult SetLock(
        SpiritCollectionDocument document,
        IReadOnlyCollection<string> artifactUids,
        bool locked)
    {
        var requested = new HashSet<string>(artifactUids ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (requested.Count == 0) return Failure("请选择需要操作的圣遗物。");
        var artifacts = document.ArtifactInventory.Artifacts
            .Where(value => requested.Contains(value.ArtifactUid)).ToList();
        if (artifacts.Count != requested.Count) return Failure("部分圣遗物已经不存在。");
        foreach (var artifact in artifacts) artifact.Locked = locked;
        return new SpiritArtifactOperationResult
        {
            Success = true,
            Artifacts = artifacts.Select(value => value.Clone()).ToList()
        };
    }

    public static SpiritArtifactOperationResult Dismantle(
        SpiritCollectionDocument document,
        IReadOnlyCollection<string> artifactUids)
    {
        var requested = new HashSet<string>(artifactUids ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (requested.Count == 0) return Failure("请选择需要分解的圣遗物。");
        var artifacts = document.ArtifactInventory.Artifacts.Where(value => requested.Contains(value.ArtifactUid)).ToList();
        if (artifacts.Count != requested.Count) return Failure("部分圣遗物已经不存在。");
        var presetProtected = SpiritArtifactPresetService.ProtectedArtifactUids(document);
        if (artifacts.Any(value => presetProtected.Contains(value.ArtifactUid)))
            return Failure("预设中的圣遗物无法分解。");
        if (artifacts.Any(value => value.Locked)) return Failure("锁定的圣遗物无法分解。");
        var equipped = EquippedUids(document);
        if (artifacts.Any(value => equipped.Contains(value.ArtifactUid))) return Failure("已装备的圣遗物无法分解。");
        var essence = artifacts.Sum(SpiritArtifactRoller.DismantleValue);
        document.ArtifactInventory.Artifacts.RemoveAll(value => requested.Contains(value.ArtifactUid));
        document.ArtifactInventory.Essence += essence;
        foreach (var receipt in document.ArtifactInventory.PendingReveals)
            receipt.ArtifactUids.RemoveAll(requested.Contains);
        document.ArtifactInventory.PendingReveals.RemoveAll(value => value.ArtifactUids.Count == 0);
        return new SpiritArtifactOperationResult
        {
            Success = true,
            EssenceDelta = essence,
            Artifacts = artifacts.Select(value => value.Clone()).ToList()
        };
    }

    public static SpiritArtifactOperationResult Equip(
        SpiritCollectionDocument document,
        string spiritUid,
        string artifactUid)
        => SpiritArtifactLoadoutMutationService.Equip(document, spiritUid, artifactUid);

    public static SpiritArtifactOperationResult Unequip(
        SpiritCollectionDocument document,
        string spiritUid,
        string slotId)
        => SpiritArtifactLoadoutMutationService.Unequip(document, spiritUid, slotId);

    public static SpiritArtifactOperationResult AcknowledgeReveal(SpiritCollectionDocument document, string token)
    {
        var removed = document.ArtifactInventory.PendingReveals.RemoveAll(value => Same(value.Token, token));
        return removed > 0 ? Success() : Failure("抽取结果已经确认或不存在。");
    }

    public static SpiritArtifactInstance? Find(SpiritCollectionDocument document, string artifactUid)
        => document.ArtifactInventory.Artifacts.FirstOrDefault(value => Same(value.ArtifactUid, artifactUid));

    public static string EquippedSpiritUid(SpiritCollectionDocument document, string artifactUid)
    {
        return document.Instances.FirstOrDefault(spirit => spirit.ArtifactLoadout?.ArtifactUids()
            .Contains(artifactUid ?? "", StringComparer.Ordinal) == true)?.SpiritUid ?? "";
    }

    public static void RefreshLoadoutHashes(SpiritCollectionDocument document)
    {
        foreach (var spirit in document.Instances ?? new List<SpiritInstance>())
        {
            spirit.ArtifactLoadout ??= new SpiritArtifactLoadout();
            spirit.ArtifactLoadout.LoadoutHash = SpiritArtifactLoadoutResolver.Resolve(document, spirit).Battle.LoadoutHash;
        }
    }

    private static SpiritArtifactInstance? NormalizeArtifact(SpiritArtifactInstance artifact)
    {
        artifact.ArtifactUid = string.IsNullOrWhiteSpace(artifact.ArtifactUid) ? Guid.NewGuid().ToString("N") : artifact.ArtifactUid.Trim();
        artifact.SetId = (artifact.SetId ?? "").Trim();
        artifact.PieceId = (artifact.PieceId ?? "").Trim();
        artifact.SlotId = SpiritArtifactSlots.Normalize(artifact.SlotId);
        artifact.Rarity = Math.Max(1, Math.Min(3, artifact.Rarity));
        artifact.Level = Math.Max(1, Math.Min(5, artifact.Level));
        artifact.InvestedEssence = Math.Max(0, artifact.InvestedEssence);
        artifact.MainStat ??= new SpiritArtifactStatRoll();
        artifact.SubStatRolls ??= new List<SpiritArtifactStatRoll>();
        artifact.SubStatRolls = artifact.SubStatRolls.Take(artifact.Level - 1).Select(value => value.Clone()).ToList();
        return SpiritArtifactLoadoutResolver.ValidateItem(SpiritArtifactLoadoutResolver.ToBattleItem(artifact), out _)
            ? artifact
            : null;
    }

    private static HashSet<string> EquippedUids(SpiritCollectionDocument document)
        => new(document.Instances.SelectMany(value => value.ArtifactLoadout?.ArtifactUids() ?? Array.Empty<string>()), StringComparer.Ordinal);

    private static SpiritArtifactOperationResult Success() => new() { Success = true };
    private static SpiritArtifactOperationResult Failure(string reason) => new() { Reason = reason ?? "圣遗物操作失败。" };
    private static bool Same(string? left, string? right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
}

internal static class SpiritArtifactEnumerableExtensions
{
    public static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> source, int count)
    {
        var values = (source ?? Array.Empty<T>()).ToList();
        return values.Skip(Math.Max(0, values.Count - Math.Max(0, count)));
    }
}
