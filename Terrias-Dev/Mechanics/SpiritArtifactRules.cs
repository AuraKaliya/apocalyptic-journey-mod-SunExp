using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Terrias.Dll.Mechanics;

public interface ISpiritArtifactRandom
{
    int Next(int exclusiveMaximum);
}

public sealed class SpiritArtifactCryptoRandom : ISpiritArtifactRandom, IDisposable
{
    private readonly RandomNumberGenerator generator = RandomNumberGenerator.Create();

    public int Next(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 1) return 0;
        var bound = (uint)exclusiveMaximum;
        var limit = uint.MaxValue - uint.MaxValue % bound;
        var bytes = new byte[4];
        uint value;
        do
        {
            generator.GetBytes(bytes);
            value = BitConverter.ToUInt32(bytes, 0);
        } while (value >= limit);
        return (int)(value % bound);
    }

    public void Dispose()
    {
        generator.Dispose();
    }
}

public sealed class SpiritArtifactDrawPlan
{
    public bool Success { get; set; }

    public string Reason { get; set; } = "";

    public string Token { get; set; } = "";

    public string PoolId { get; set; } = "";

    public string TargetSetId { get; set; } = "";

    public int TruthCost { get; set; }

    public int ResultingRarityPity { get; set; }

    public int ResultingTargetFate { get; set; }

    public List<SpiritArtifactInstance> Results { get; set; } = new();
}

public static class SpiritArtifactRoller
{
    public static SpiritArtifactDrawPlan PrepareTenDraw(
        SpiritArtifactInventory? inventory,
        string? requestedPoolId,
        string? requestedTargetSetId,
        ISpiritArtifactRandom random,
        string? token = null,
        string? acquiredAt = null)
    {
        inventory ??= new SpiritArtifactInventory();
        if (!SpiritArtifactRegistry.IsReady)
            return Failure("圣遗物注册表尚未就绪。");

        var rules = SpiritArtifactRegistry.DrawRules;
        var pool = SpiritArtifactRegistry.Pool(requestedPoolId);
        var targetSetId = (requestedTargetSetId ?? "").Trim();
        if (pool == null || !pool.SetIds.Contains(targetSetId, StringComparer.Ordinal))
            return Failure("请选择当前祈愿池中的目标套装。");
        if ((inventory.Artifacts?.Count ?? 0) + rules.Count > SpiritArtifactRegistry.InventoryCapacity)
            return Failure("圣遗物仓库空间不足。");

        var rarities = new List<int>(rules.Count);
        var rarityPity = Math.Max(0, inventory.RarityPity);
        for (var index = 0; index < rules.Count; index++)
        {
            var rarity = SpiritArtifactRollPolicy.ResolveRarity(
                rarityPity,
                random.Next(Math.Max(1, rules.RarityWeights.Values.Sum())),
                rules.RarityWeights,
                rules.ThreeStarHardPity);
            rarities.Add(rarity);
            rarityPity = SpiritArtifactRollPolicy.NextRarityPity(rarityPity, rarity);
        }
        SpiritArtifactRollPolicy.EnsureMinimumTwoStar(rarities, rules.MinimumTwoStarPerBatch);

        var drawToken = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token!.Trim();
        var timestamp = string.IsNullOrWhiteSpace(acquiredAt) ? DateTimeOffset.UtcNow.ToString("O") : acquiredAt!.Trim();
        var targetFate = Math.Max(0, inventory.TargetFate);
        var results = new List<SpiritArtifactInstance>(rules.Count);
        for (var index = 0; index < rarities.Count; index++)
        {
            var rarity = rarities[index];
            var forceTarget = SpiritArtifactRollPolicy.ForceTargetSet(
                rarity, targetFate, rules.GuaranteeTargetAfterOffTargetThreeStar);
            var setId = forceTarget
                ? targetSetId
                : RollSet(pool.SetIds, targetSetId, rules.TargetSetWeightPercent, random);
            if (rarity == 3)
            {
                targetFate = SpiritArtifactRollPolicy.NextTargetFate(
                    rarity,
                    string.Equals(setId, targetSetId, StringComparison.Ordinal),
                    rules.GuaranteeTargetAfterOffTargetThreeStar);
            }
            var slotId = SpiritArtifactSlots.All[random.Next(SpiritArtifactSlots.All.Count)];
            var piece = SpiritArtifactRegistry.PieceFor(setId, slotId);
            if (piece == null) return Failure("目标套装缺少有效部件：" + setId + "/" + slotId);
            var mainStatId = slotId == SpiritArtifactSlots.Flower
                ? SpiritArtifactStats.Life
                : SpiritArtifactStats.MainChoiceStats[random.Next(SpiritArtifactStats.MainChoiceStats.Count)];
            var mainRange = SpiritArtifactRegistry.Range(mainStatId, rarity, main: true);
            if (mainRange.Minimum <= 0) return Failure("主词条数值范围缺失：" + mainStatId);
            results.Add(new SpiritArtifactInstance
            {
                ArtifactUid = Guid.NewGuid().ToString("N"),
                SetId = setId,
                PieceId = piece.Id,
                SlotId = slotId,
                Rarity = rarity,
                Level = 1,
                MainStat = new SpiritArtifactStatRoll
                {
                    StatId = mainStatId,
                    Value = RollRange(mainRange, random)
                },
                AcquiredAt = timestamp,
                AcquisitionToken = drawToken + ":" + index
            });
        }

        return new SpiritArtifactDrawPlan
        {
            Success = true,
            Token = drawToken,
            PoolId = pool.Id,
            TargetSetId = targetSetId,
            TruthCost = rules.TruthCost,
            ResultingRarityPity = rarityPity,
            ResultingTargetFate = targetFate,
            Results = results
        };
    }

    public static SpiritArtifactStatRoll RollSubStat(int rarity, ISpiritArtifactRandom random)
    {
        var weights = SpiritArtifactRegistry.SubStatWeights();
        var total = weights.Sum(value => value.Weight);
        var roll = random.Next(Math.Max(1, total));
        var statId = weights[weights.Count - 1].StatId;
        foreach (var candidate in weights)
        {
            if (roll < candidate.Weight)
            {
                statId = candidate.StatId;
                break;
            }
            roll -= candidate.Weight;
        }
        var range = SpiritArtifactRegistry.Range(statId, rarity, main: false);
        return new SpiritArtifactStatRoll { StatId = statId, Value = RollRange(range, random) };
    }

    public static int DismantleValue(SpiritArtifactInstance? artifact)
    {
        if (artifact == null) return 0;
        var rules = SpiritArtifactRegistry.EnhancementRules;
        return Math.Max(0, SpiritArtifactRegistry.DismantleBaseEssence(artifact.Rarity))
               + Math.Max(0, artifact.InvestedEssence) * rules.InvestedEssenceRefundPercent / 100;
    }

    private static string RollSet(
        IReadOnlyList<string> setIds,
        string targetSetId,
        int targetWeightPercent,
        ISpiritArtifactRandom random)
    {
        var others = setIds.Where(value => !string.Equals(value, targetSetId, StringComparison.Ordinal)).ToArray();
        if (others.Length == 0 || random.Next(100) < targetWeightPercent) return targetSetId;
        return others[random.Next(others.Length)];
    }

    private static int RollRange(SpiritArtifactIntegerRange range, ISpiritArtifactRandom random)
    {
        var minimum = Math.Max(1, range?.Minimum ?? 1);
        var maximum = Math.Max(minimum, range?.Maximum ?? minimum);
        return minimum + random.Next(maximum - minimum + 1);
    }

    private static SpiritArtifactDrawPlan Failure(string reason)
    {
        return new SpiritArtifactDrawPlan { Reason = reason ?? "圣遗物抽取失败。" };
    }
}
