using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Witch;
using Witch.Core;

namespace AuraJourney.Shared;

public static class AuraJourneyGameBridge
{
    public static MapTree.Node CreateMapNode(MapTree tree, AuraJourneyMapNodeSpec spec)
    {
        var projection = AuraJourneyMapNodeDataBuilder.Build(spec, ResolveMapRow);
        if (!projection.Valid)
        {
            return new MapTree.Node("null")
            {
                type = "null",
                data = null,
                NodeDice = ResolveDice(tree, AuraJourneyDicePolicies.Default)
            };
        }

        var node = new MapTree.Node(projection.Note)
        {
            type = projection.Note,
            data = new Dictionary<string, string>(projection.Data, StringComparer.Ordinal),
            NodeDice = ResolveDice(tree, projection.DicePolicy)
        };
        return node;
    }

    public static bool EnsureNodeDice(MapTree.Node? node, MapTree tree, string dicePolicy = AuraJourneyDicePolicies.TreeDice)
    {
        if (node == null || node.NodeDice != null)
        {
            return false;
        }

        node.NodeDice = ResolveDice(tree, dicePolicy);
        return node.NodeDice != null;
    }

    public static AuraJourneySyncProjectionResult RepairSyncArrays(
        string[]? maps,
        string[]? mapData,
        IEnumerable<AuraJourneySlotRule>? rules)
    {
        return AuraJourneySyncProjection.Repair(maps, mapData, rules, ResolveMapRow);
    }

    public static MapTree.Node? BuildSyncedNodeChain(MapTree tree, string[]? maps, string[]? mapData)
    {
        if (tree == null || maps == null || mapData == null)
        {
            return null;
        }

        var count = Math.Min(maps.Length, mapData.Length);
        MapTree.Node? first = null;
        MapTree.Node? previous = null;
        for (var i = 0; i < count; i++)
        {
            if (string.IsNullOrWhiteSpace(maps[i]))
            {
                continue;
            }

            var node = CreateMapNode(tree, new AuraJourneyMapNodeSpec
            {
                MapId = maps[i],
                NodeId = mapData[i],
                DicePolicy = AuraJourneyDicePolicies.TreeDice
            });

            if (first == null)
            {
                first = node;
            }
            else
            {
                previous?.SetChild(0, node);
            }

            previous = node;
        }

        return first;
    }

    public static bool RestoreCurrentNodeFromSyncArrays(MapTree tree, string[]? maps, string[]? mapData, bool updateSaveNode)
    {
        var first = BuildSyncedNodeChain(tree, maps, mapData);
        if (first == null)
        {
            return false;
        }

        tree.currentNode = first;
        if (updateSaveNode)
        {
            GameSaveManager.UpdateNode(first);
        }

        return true;
    }

    public static Dictionary<string, string>? ResolveMapRow(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return null;
        }

        try
        {
            var manager = Singleton<GameConfigManager>.Instance;
            var candidates = AuraJourneyMapIdAliasRegistry.Expand(mapId);
            foreach (var candidate in candidates)
            {
                var direct = manager.GetOne(DataType.Map, candidate);
                if (direct != null)
                {
                    return new Dictionary<string, string>(direct, StringComparer.Ordinal);
                }
            }

            var candidateSet = new HashSet<string>(candidates, StringComparer.Ordinal);
            return manager.GetTable(DataType.Map).Getlines()
                .FirstOrDefault(row => candidateSet.Contains(Field(row, "Id"))) is { } row
                ? new Dictionary<string, string>(row, StringComparer.Ordinal)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static Dice? ResolveDice(MapTree tree, string dicePolicy)
    {
        if (string.Equals(dicePolicy, AuraJourneyDicePolicies.None, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(dicePolicy, AuraJourneyDicePolicies.Default, StringComparison.OrdinalIgnoreCase))
        {
            return Dice.Default;
        }

        return tree?.treedice ?? Dice.Default;
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}
