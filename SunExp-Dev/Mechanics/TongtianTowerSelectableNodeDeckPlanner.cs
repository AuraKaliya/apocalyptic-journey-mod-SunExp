using System;
using System.Collections.Generic;
using Witch;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerSelectableNodeDeckPlanner
{
    public static IReadOnlyList<TongtianTowerNodeKind> CreateKinds(MapTree tree, int floor, int count)
    {
        var normalizedCount = Math.Max(0, count);
        var kinds = new List<TongtianTowerNodeKind>(normalizedCount);
        if (normalizedCount <= 0)
        {
            return kinds;
        }

        kinds.Add(TongtianTowerNodeKind.Rest);
        if (kinds.Count < normalizedCount)
        {
            kinds.Add(TongtianTowerNodeKind.Building);
        }

        var eliteCount = TongtianTowerRewardPlan.IsEndless(floor)
            ? 2
            : Math.Max(1, floor) >= 3 ? 1 : 0;
        while (eliteCount > 0 && kinds.Count < normalizedCount)
        {
            kinds.Add(TongtianTowerNodeKind.Elite);
            eliteCount--;
        }

        while (kinds.Count < normalizedCount)
        {
            kinds.Add(TongtianTowerNodeKind.Monster);
        }

        Shuffle(tree, floor, kinds);
        return kinds;
    }

    private static void Shuffle(MapTree tree, int floor, IList<TongtianTowerNodeKind> kinds)
    {
        if (kinds.Count <= 1)
        {
            return;
        }

        var dice = tree?.treedice ?? Dice.Default;
        for (var i = kinds.Count - 1; i > 0; i--)
        {
            var raw = Math.Abs((long)dice.Roll().Value + Math.Max(1, floor) * 31L + i * 17L);
            var swap = (int)(raw % (i + 1));
            (kinds[i], kinds[swap]) = (kinds[swap], kinds[i]);
        }
    }
}
