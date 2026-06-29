using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class MapNodeSafetyService
{
    public static bool EnsureNodeDice(MapTree? tree, MapTree.Node? node, string source, bool preferDefaultDice = false)
    {
        if (node == null || node.NodeDice != null)
        {
            return false;
        }

        node.NodeDice = preferDefaultDice ? Dice.Default : tree?.treedice ?? Dice.Default;
        SunExpLog.Debug("[MapNodeSafety] repaired missing NodeDice; source="
            + source
            + "; id="
            + NodeField(node, "Id")
            + "; nodeId="
            + NodeField(node, "NodeId")
            + ".");
        return true;
    }

    public static bool IsExclusiveNode(MapTree.Node? node)
    {
        if (node?.data == null)
        {
            return false;
        }

        return SunExpIds.IsSolarMemoryExclusiveMapId(NodeField(node, "Id"))
            || SunExpIds.IsSolarMemoryExclusiveEventId(NodeField(node, "NodeId"));
    }

    public static bool IsBreakNode(MapTree.Node? node)
    {
        if (node?.data == null)
        {
            return false;
        }

        return NodeField(node, "NodeId").Contains("Breaks")
            || NodeField(node, "Id").Contains("Breaks");
    }

    public static bool RestoreCurrentNodeIfMissingOrExclusive(int level, string source, bool clientOnly)
    {
        try
        {
            if (clientOnly && !IsClientOnlyPlayer())
            {
                return false;
            }

            var manager = MapManager.Instance;
            var tree = manager?.MapTree;
            if (tree == null)
            {
                return false;
            }

            var currentNode = tree.currentNode;
            if (IsUsableRegularNode(currentNode))
            {
                EnsureNodeDice(tree, currentNode, source);
                return false;
            }

            var reason = currentNode == null ? "null-current" : "exclusive-current";
            if (TryBuildCurrentNodeFromSyncArrays(tree, manager?.mapList, manager?.mapData, out var syncedNode))
            {
                AssignCurrentNode(tree, syncedNode, source, reason, "sync-arrays");
                return true;
            }

            var fallback = FindDeterministicFallbackNode(tree, level);
            if (fallback == null)
            {
                SunExpLog.Warn("[MapNodeSafety] unable to restore current node; source="
                    + source
                    + "; reason="
                    + reason
                    + ".");
                return false;
            }

            AssignCurrentNode(tree, fallback, source, reason, "map-tree");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[MapNodeSafety] current node repair failed; source="
                + source
                + ": "
                + ex.Message);
            return false;
        }
    }

    public static string NodeField(MapTree.Node? node, string key)
    {
        return node?.data != null && node.data.TryGetValue(key, out var value) ? value : "";
    }

    private static void AssignCurrentNode(MapTree tree, MapTree.Node node, string source, string reason, string restoreSource)
    {
        EnsureNodeDice(tree, node, source);
        tree.currentNode = node;
        GameSaveManager.UpdateNode(node);
        SunExpLog.Warn("[MapNodeSafety] restored current node; source="
            + source
            + "; reason="
            + reason
            + "; restoreSource="
            + restoreSource
            + "; id="
            + NodeField(node, "Id")
            + "; nodeId="
            + NodeField(node, "NodeId")
            + ".");
    }

    private static bool TryBuildCurrentNodeFromSyncArrays(
        MapTree tree,
        string[]? maps,
        string[]? mapData,
        out MapTree.Node node)
    {
        node = null!;
        if (maps == null || mapData == null)
        {
            return false;
        }

        var count = Math.Min(maps.Length, mapData.Length);
        MapTree.Node? first = null;
        MapTree.Node? previous = null;
        for (var i = 0; i < count; i++)
        {
            var candidate = CreateNodeFromMapData(tree, maps[i], mapData[i]);
            if (!IsUsableRegularNode(candidate))
            {
                continue;
            }

            if (first == null)
            {
                first = candidate;
            }
            else
            {
                previous?.SetChild(0, candidate);
            }

            previous = candidate;
        }

        if (first == null)
        {
            return false;
        }

        node = first;
        return true;
    }

    private static MapTree.Node CreateNodeFromMapData(MapTree tree, string? mapId, string? nodeId)
    {
        var data = CreateNodeData(mapId, nodeId);
        var type = DictionaryUtil.Get(data, "Note");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = DictionaryUtil.Get(data, "Type", "Map");
        }

        var node = new MapTree.Node(type)
        {
            type = type,
            data = data,
            NodeDice = tree.treedice ?? Dice.Default
        };
        return node;
    }

    private static Dictionary<string, string> CreateNodeData(string? mapId, string? nodeId)
    {
        var data = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(mapId))
        {
            var row = FindMapById(mapId!);
            if (row != null)
            {
                data = new Dictionary<string, string>(row);
            }

            data["Id"] = mapId!;
        }

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            data["NodeId"] = nodeId!;
        }
        else if (!data.ContainsKey("NodeId") && !string.IsNullOrWhiteSpace(mapId))
        {
            data["NodeId"] = mapId!;
        }

        if (!data.ContainsKey("Type") || string.IsNullOrWhiteSpace(data["Type"]))
        {
            data["Type"] = "Fight";
        }

        if (!data.ContainsKey("Note") || string.IsNullOrWhiteSpace(data["Note"]))
        {
            data["Note"] = data["Type"];
        }

        if (!data.ContainsKey("Level") || string.IsNullOrWhiteSpace(data["Level"]))
        {
            data["Level"] = "-1";
        }

        return data;
    }

    private static MapTree.Node? FindDeterministicFallbackNode(MapTree tree, int level)
    {
        return EnumerateNodes(tree.SelectNode)
            .Concat(EnumerateNodes(tree.DefaultNode))
            .Where(IsUsableRegularNode)
            .OrderBy(node => Math.Abs(DictionaryUtil.ParseInt(NodeField(node, "Level"), level) - level))
            .ThenBy(node => NodeField(node, "Id"), StringComparer.Ordinal)
            .ThenBy(node => NodeField(node, "NodeId"), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IEnumerable<MapTree.Node> EnumerateNodes(IList<MapTree.Node>? nodes)
    {
        if (nodes == null)
        {
            yield break;
        }

        foreach (var node in nodes)
        {
            if (node != null)
            {
                yield return node;
            }
        }
    }

    private static bool IsUsableRegularNode(MapTree.Node? node)
    {
        return node != null
            && node.data != null
            && !IsExclusiveNode(node)
            && !IsBreakNode(node);
    }

    private static Dictionary<string, string>? FindMapById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return SunExpConfigIndex.Row(DataType.Map, id);
    }

    private static bool IsClientOnlyPlayer()
    {
        try
        {
            var playerManager = PlayerManager.Instance;
            return playerManager != null && !playerManager.isServer;
        }
        catch
        {
            return false;
        }
    }
}
