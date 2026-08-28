using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritArtifactRollPolicy
{
    public static int ResolveRarity(
        int currentPity,
        int weightedRoll,
        IReadOnlyDictionary<string, int> rarityWeights,
        int threeStarHardPity)
    {
        if (Math.Max(0, currentPity) + 1 >= Math.Max(1, threeStarHardPity)) return 3;
        var total = Math.Max(1, rarityWeights?.Values.Sum() ?? 0);
        var value = Math.Max(0, Math.Min(total - 1, weightedRoll));
        foreach (var rarity in new[] { 1, 2, 3 })
        {
            var weight = rarityWeights != null && rarityWeights.TryGetValue(rarity.ToString(), out var found) ? found : 0;
            if (value < weight) return rarity;
            value -= weight;
        }
        return 1;
    }

    public static int NextRarityPity(int currentPity, int rarity)
        => rarity >= 3 ? 0 : Math.Max(0, currentPity) + 1;

    public static void EnsureMinimumTwoStar(IList<int> rarities, int minimumCount)
    {
        if (rarities == null || rarities.Count == 0 || minimumCount <= 0) return;
        var missing = Math.Max(0, minimumCount - rarities.Count(value => value >= 2));
        for (var index = rarities.Count - 1; index >= 0 && missing > 0; index--)
        {
            if (rarities[index] >= 2) continue;
            rarities[index] = 2;
            missing--;
        }
    }

    public static bool ForceTargetSet(int rarity, int targetFate, bool guaranteeEnabled)
        => rarity >= 3 && targetFate > 0 && guaranteeEnabled;

    public static int NextTargetFate(int rarity, bool hitTarget, bool guaranteeEnabled)
        => rarity < 3 || !guaranteeEnabled ? 0 : hitTarget ? 0 : 1;
}
