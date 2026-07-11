using System;
using System.Collections.Generic;
using AuraShared.Core;
using Data.Save;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace AuraJourney.Shared;

/// <summary>
/// Repairs only the client-side MapTree projection when native map synchronisation
/// has already supplied an unambiguous current-node identity.  It never selects a
/// route, mutates map sync arrays, or sends a network command.
/// </summary>
public static class AuraJourneyCurrentNodeProjectionRuntime
{
    private const string OwnerId = "AuraJourneyShared";
    private const string RepairKeyPrefix = "CurrentNodeProjection.Repair.";
    private const int MaximumDeferredAttempts = 2;
    private static readonly object SyncRoot = new();
    private static bool initialized;
    private static long generation;
    private static ProjectionSnapshot snapshot = new();

    public static void Initialize(ModConfig modConfig, string callerId)
    {
        lock (SyncRoot)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
        }

        RegisterBefore(modConfig, "MapManager.TargetUpdateMap", context => Capture(context, "TargetUpdateMap.before"));
        RegisterAfter(modConfig, "MapManager.TargetUpdateMap", context => VerifyAfterNative("TargetUpdateMap.after"));
        RegisterBefore(modConfig, "MapManager.RpcUpdateMap", context => Capture(context, "RpcUpdateMap.before"));
        RegisterAfter(modConfig, "MapManager.RpcUpdateMap", context => VerifyAfterNative("RpcUpdateMap.after"));
        RegisterBefore(modConfig, "MapManager.RpcNextMap", context => RepairBeforeNextMap("RpcNextMap.before"));
        RegisterAfter(modConfig, "MapManager.RpcNextMap", context => VerifyAfterNative("RpcNextMap.after"));
        RegisterBefore(modConfig, "MapSelectUI.ReadyToSelect", context => VerifyBeforeMapUi("MapSelectUI.ReadyToSelect"));
        AuraSharedDiagnostics.Info(AuraJourneyConstants.SystemName, OwnerId, "CurrentNodeProjection", "Initialized by " + (callerId ?? "") + ".");
    }

    public static bool TryRepairCurrentNode(string source)
    {
        if (!IsClientOnly() || HasCurrentNode())
        {
            return false;
        }

        Capture(null, (source ?? "CurrentNodeProjection") + ":capture");
        if (TryRepair(source ?? "CurrentNodeProjection", out var reason))
        {
            return true;
        }

        ScheduleDeferredRepair(source ?? "CurrentNodeProjection", reason, 1);
        return false;
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, message => AuraSharedDiagnostics.Info(AuraJourneyConstants.SystemName, OwnerId, target, message), message => AuraSharedDiagnostics.Warn(AuraJourneyConstants.SystemName, OwnerId, target, message), safeInvoke: true);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, message => AuraSharedDiagnostics.Info(AuraJourneyConstants.SystemName, OwnerId, target, message), message => AuraSharedDiagnostics.Warn(AuraJourneyConstants.SystemName, OwnerId, target, message), safeInvoke: true);
    }

    private static void Capture(ModHookContext? context, string source)
    {
        if (!IsClientOnly())
        {
            return;
        }

        var manager = MapManager.Instance;
        if (manager == null)
        {
            return;
        }

        var arrays = ExtractArrays(context?.Arguments) ?? new ProjectionArrays(manager.mapList, manager.mapData);
        var current = IdentityFromNode(manager.MapTree?.currentNode) ?? IdentityFromNode(SafeSaveNode());
        lock (SyncRoot)
        {
            generation++;
            snapshot = new ProjectionSnapshot
            {
                Generation = generation,
                Maps = Clone(arrays.Maps),
                MapData = Clone(arrays.MapData),
                VerifiedIdentity = current,
                Source = source
            };
        }
    }

    private static void RepairBeforeNextMap(string source)
    {
        if (!IsClientOnly())
        {
            return;
        }

        TryRepairCurrentNode(source);
    }

    private static void VerifyBeforeMapUi(string source)
    {
        if (!IsClientOnly())
        {
            return;
        }

        if (!HasCurrentNode())
        {
            TryRepairCurrentNode(source);
        }
    }

    private static void VerifyAfterNative(string source)
    {
        if (!IsClientOnly())
        {
            return;
        }

        if (HasCurrentNode())
        {
            return;
        }

        TryRepairCurrentNode(source);
    }

    private static void ScheduleDeferredRepair(string source, string reason, int attempt)
    {
        if (attempt > MaximumDeferredAttempts)
        {
            AuraSharedDiagnostics.Warn(AuraJourneyConstants.SystemName, OwnerId, "CurrentNodeProjection", "repair exhausted; source=" + source + "; reason=" + reason + ".", false);
            return;
        }

        long scheduledGeneration;
        lock (SyncRoot)
        {
            scheduledGeneration = snapshot.Generation;
        }

        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = OwnerId,
            Key = RepairKeyPrefix + scheduledGeneration + "." + attempt,
            Source = OwnerId + "." + source,
            DelayFrames = attempt,
            Phase = AuraSharedFramePhase.CriticalLifecycle,
            Priority = 900,
            Action = () =>
            {
                lock (SyncRoot)
                {
                    if (snapshot.Generation != scheduledGeneration)
                    {
                        return;
                    }
                }

                if (!IsClientOnly() || HasCurrentNode())
                {
                    return;
                }

                if (!TryRepair(source + ":deferred" + attempt, out var deferredReason))
                {
                    ScheduleDeferredRepair(source, deferredReason, attempt + 1);
                }
            }
        });
    }

    private static bool TryRepair(string source, out string reason)
    {
        reason = "missing-identity";
        var manager = MapManager.Instance;
        var tree = manager?.MapTree;
        if (tree == null)
        {
            reason = "missing-map-tree";
            return false;
        }

        if (tree.currentNode != null)
        {
            AuraJourneyGameBridge.EnsureNodeDice(tree.currentNode, tree);
            return true;
        }

        ProjectionSnapshot local;
        lock (SyncRoot)
        {
            local = snapshot.Clone();
        }

        var identity = IdentityFromNode(SafeSaveNode()) ?? local.VerifiedIdentity;
        if (identity == null || !identity.IsValid)
        {
            reason = "missing-verified-identity";
            return false;
        }

        if (!TryFindIdentity(local.Maps, local.MapData, identity, out var mapId, out var nodeId))
        {
            reason = "identity-not-in-sync-projection";
            return false;
        }

        var restored = AuraJourneyGameBridge.CreateMapNode(tree, new AuraJourneyMapNodeSpec
        {
            MapId = mapId,
            NodeId = nodeId,
            DicePolicy = AuraJourneyDicePolicies.TreeDice
        });
        if (restored.data == null)
        {
            reason = "unable-to-build-node";
            return false;
        }

        tree.currentNode = restored;
        AuraJourneyGameBridge.EnsureNodeDice(restored, tree);
        GameSaveManager.UpdateNode(restored);
        AuraSharedDiagnostics.Warn(AuraJourneyConstants.SystemName, OwnerId, "CurrentNodeProjection", "restored client current node; source=" + source + "; generation=" + local.Generation + "; map=" + mapId + "; node=" + nodeId + ".", false);
        return true;
    }

    private static bool HasCurrentNode()
    {
        return MapManager.Instance?.MapTree?.currentNode != null;
    }

    private static bool TryFindIdentity(string[] maps, string[] mapData, NodeIdentity identity, out string mapId, out string nodeId)
    {
        mapId = "";
        nodeId = "";
        var count = Math.Min(maps.Length, mapData.Length);
        var matches = 0;
        for (var i = 0; i < count; i++)
        {
            if (!string.Equals(maps[i], identity.MapId, StringComparison.Ordinal)
                || !string.Equals(mapData[i], identity.NodeId, StringComparison.Ordinal))
            {
                continue;
            }

            mapId = maps[i];
            nodeId = mapData[i];
            matches++;
        }

        return matches == 1;
    }

    private static ProjectionArrays? ExtractArrays(object[]? args)
    {
        if (args == null)
        {
            return null;
        }

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is string[] maps && args[i + 1] is string[] mapData)
            {
                return new ProjectionArrays(maps, mapData);
            }
        }

        return null;
    }

    private static NodeIdentity? IdentityFromNode(MapTree.Node? node)
    {
        var data = node?.data;
        if (data == null)
        {
            return null;
        }

        var mapId = Field(data, "Id");
        var nodeId = Field(data, "NodeId");
        return string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(nodeId)
            ? null
            : new NodeIdentity(mapId, nodeId);
    }

    private static MapTree.Node? SafeSaveNode()
    {
        try
        {
            return GameSaveManager.GetNode();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsClientOnly()
    {
        try
        {
            return PlayerManager.Instance != null && !PlayerManager.Instance.isServer;
        }
        catch
        {
            return false;
        }
    }

    private static string[] Clone(string[]? values)
    {
        return values == null ? Array.Empty<string>() : (string[])values.Clone();
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private sealed class ProjectionArrays
    {
        public ProjectionArrays(string[]? maps, string[]? mapData)
        {
            Maps = maps ?? Array.Empty<string>();
            MapData = mapData ?? Array.Empty<string>();
        }

        public string[] Maps { get; }
        public string[] MapData { get; }
    }

    private sealed class ProjectionSnapshot
    {
        public long Generation { get; set; }
        public string[] Maps { get; set; } = Array.Empty<string>();
        public string[] MapData { get; set; } = Array.Empty<string>();
        public NodeIdentity? VerifiedIdentity { get; set; }
        public string Source { get; set; } = "";

        public ProjectionSnapshot Clone()
        {
            return new ProjectionSnapshot
            {
                Generation = Generation,
                Maps = (string[])Maps.Clone(),
                MapData = (string[])MapData.Clone(),
                VerifiedIdentity = VerifiedIdentity,
                Source = Source
            };
        }
    }

    private sealed class NodeIdentity
    {
        public NodeIdentity(string mapId, string nodeId)
        {
            MapId = mapId;
            NodeId = nodeId;
        }

        public string MapId { get; }
        public string NodeId { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(MapId) && !string.IsNullOrWhiteSpace(NodeId);
    }
}
