using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public sealed class EndlessSeaRewardSpec
{
    public int CardChoices { get; set; }

    public int BlessingChoices { get; set; }

    public IReadOnlyList<int[]> RelicRarityGroups { get; set; } = Array.Empty<int[]>();
}

public static class EndlessSeaRewardPlan
{
    public static bool IsEndless(int floor)
    {
        return Math.Max(1, floor) >= 7;
    }

    public static EndlessSeaRewardSpec ForCurrentNode(int floor, bool boss)
    {
        floor = Math.Max(1, floor);
        if (IsEndless(floor))
        {
            return boss
                ? new EndlessSeaRewardSpec
                {
                    CardChoices = 5,
                    BlessingChoices = 1,
                    RelicRarityGroups = new[] { new[] { 3, 4 } }
                }
                : new EndlessSeaRewardSpec
                {
                    CardChoices = 5,
                    RelicRarityGroups = new[] { new[] { 1, 2, 3, 4 }, new[] { 2, 3, 4 } }
                };
        }

        if (floor <= 2)
        {
            return new EndlessSeaRewardSpec
            {
                CardChoices = 2,
                BlessingChoices = 1,
                RelicRarityGroups = new[] { new[] { 1 } }
            };
        }

        if (floor <= 4)
        {
            return new EndlessSeaRewardSpec
            {
                CardChoices = 2,
                BlessingChoices = 1,
                RelicRarityGroups = new[] { new[] { 2 } }
            };
        }

        return boss
            ? new EndlessSeaRewardSpec
            {
                CardChoices = 5,
                BlessingChoices = 1,
                RelicRarityGroups = new[] { new[] { 3, 4 } }
            }
            : new EndlessSeaRewardSpec
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

    public static EndlessSeaNodeKind CurrentNodeKind()
    {
        try
        {
            var data = MapManager.Instance?.MapTree?.currentNode?.data;
            if (data == null)
            {
                return EndlessSeaNodeKind.Monster;
            }

            var note = DictionaryUtil.Get(data, "Note");
            var kind = DictionaryUtil.Get(data, SunExpIds.EndlessSeaNodeKindKey);
            if (Enum.TryParse<EndlessSeaNodeKind>(kind, out var parsed))
            {
                return parsed;
            }

            return note.Contains("\u9996\u9886")
                ? EndlessSeaNodeKind.Boss
                : EndlessSeaNodeKind.Monster;
        }
        catch
        {
            return EndlessSeaNodeKind.Monster;
        }
    }

    public static bool IsBossKind(EndlessSeaNodeKind kind)
    {
        return kind == EndlessSeaNodeKind.Boss || kind == EndlessSeaNodeKind.EndlessBoss;
    }
}
