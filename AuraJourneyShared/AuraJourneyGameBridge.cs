using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using Data.Save;
using Witch;
using Witch.Core;

namespace AuraJourney.Shared;

public static class AuraJourneyGameBridge
{
    public static MapTree.Node CreateMapNode(MapTree tree, AuraJourneyMapNodeSpec spec)
    {
        return CreateMapNode(tree, spec, CreateMapResolver());
    }

    private static MapTree.Node CreateMapNode(
        MapTree tree,
        AuraJourneyMapNodeSpec spec,
        Func<string, Dictionary<string, string>?> resolveMapRow)
    {
        var projection = AuraJourneyMapNodeDataBuilder.Build(spec, resolveMapRow);
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
        var started = AuraGameDataDiagnostics.Timestamp();
        try
        {
            return AuraJourneySyncProjection.Repair(maps, mapData, rules, CreateMapResolver());
        }
        finally
        {
            AuraGameDataDiagnostics.RecordOperation("MapSwitch.RepairSyncArrays", started);
        }
    }

    public static MapTree.Node? BuildSyncedNodeChain(MapTree tree, string[]? maps, string[]? mapData)
    {
        var started = AuraGameDataDiagnostics.Timestamp();
        try
        {
            if (tree == null || maps == null || mapData == null)
            {
                return null;
            }

            var count = Math.Min(maps.Length, mapData.Length);
            var resolveMapRow = CreateMapResolver();
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
                }, resolveMapRow);

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
        finally
        {
            AuraGameDataDiagnostics.RecordOperation("MapSwitch.BuildNodeChain", started);
        }
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

        return CreateMapResolver()(mapId);
    }

    private static Func<string, Dictionary<string, string>?> CreateMapResolver()
    {
        var snapshot = AuraGameDataHostApi.AcquireSnapshot();
        return mapId =>
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                return null;
            }

            var resolved = snapshot.Resolve(
                DataType.Map.ToString(),
                AuraJourneyMapIdAliasRegistry.Expand(mapId));
            return resolved?.Fields.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
        };
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
}
