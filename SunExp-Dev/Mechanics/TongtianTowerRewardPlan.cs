using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public sealed class TongtianTowerRewardSpec
{
    public int CardChoices { get; set; }

    public int BlessingChoices { get; set; }

    public IReadOnlyList<int[]> RelicRarityGroups { get; set; } = Array.Empty<int[]>();
}

public static class TongtianTowerRewardPlan
{
    public static bool IsEndless(int floor)
    {
        return Math.Max(1, floor) >= 7;
    }

    public static TongtianTowerRewardSpec ForCurrentNode(int floor, bool boss)
    {
        floor = Math.Max(1, floor);
        if (IsEndless(floor))
        {
            return boss
                ? new TongtianTowerRewardSpec
                {
                    CardChoices = 5,
                    BlessingChoices = 1,
                    RelicRarityGroups = new[] { new[] { 3, 4 } }
                }
                : new TongtianTowerRewardSpec
                {
                    CardChoices = 5,
                    RelicRarityGroups = new[] { new[] { 1, 2, 3, 4 }, new[] { 2, 3, 4 } }
                };
        }

        if (floor <= 2)
        {
            return new TongtianTowerRewardSpec
            {
                CardChoices = 2,
                BlessingChoices = 1,
                RelicRarityGroups = new[] { new[] { 1 } }
            };
        }

        if (floor <= 4)
        {
            return new TongtianTowerRewardSpec
            {
                CardChoices = 2,
                BlessingChoices = 1,
                RelicRarityGroups = new[] { new[] { 2 } }
            };
        }

        return boss
            ? new TongtianTowerRewardSpec
            {
                CardChoices = 5,
                BlessingChoices = 1,
                RelicRarityGroups = new[] { new[] { 3, 4 } }
            }
            : new TongtianTowerRewardSpec
            {
                CardChoices = 3,
                BlessingChoices = 1,
                RelicRarityGroups = new[] { new[] { 1, 2, 3 }, new[] { 1, 2, 3 } }
            };
    }

    public static bool IsCurrentNodeBoss()
    {
        return IsBossKind(CurrentNodeKind());
    }

    public static TongtianTowerNodeKind CurrentNodeKind()
    {
        try
        {
            var data = MapManager.Instance?.MapTree?.currentNode?.data;
            if (data == null)
            {
                return TongtianTowerNodeKind.Monster;
            }

            var note = DictionaryUtil.Get(data, "Note");
            var kind = DictionaryUtil.Get(data, SunExpIds.TongtianTowerNodeKindKey);
            if (Enum.TryParse<TongtianTowerNodeKind>(kind, out var parsed))
            {
                return parsed;
            }

            return note.Contains("\u9996\u9886")
                ? TongtianTowerNodeKind.Boss
                : TongtianTowerNodeKind.Monster;
        }
        catch
        {
            return TongtianTowerNodeKind.Monster;
        }
    }

    public static bool IsBossKind(TongtianTowerNodeKind kind)
    {
        return kind == TongtianTowerNodeKind.Boss || kind == TongtianTowerNodeKind.EndlessBoss;
    }
}
