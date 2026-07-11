using System;
using System.Collections.Generic;
using AuraShared.Core;
using Data.Save;
using Network.Command;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace AuraJourney.Shared;

/// <summary>
/// Repairs only the client-side MapTree projection from native map arrays and a
/// host-published, read-only current-node identity. It never selects a route or
/// mutates the native map synchronisation arrays.
/// </summary>
public static class AuraJourneyCurrentNodeProjectionRuntime
{
    private const string OwnerId = "AuraJourneyShared";
    private const string RepairKeyPrefix = "CurrentNodeProjection.Repair.";
    private const int MaximumDeferredAttempts = 8;
    private const int MaximumRecentProjections = 6;
    private static readonly object SyncRoot = new();
    private static bool initialized;
    private static long generation;
    private static ProjectionSnapshot snapshot = new();
    private static readonly List<ProjectionArraysSnapshot> recentProjections = new();
    private static NodeIdentity? authoritativeIdentity;
    private static string authoritativeSessionId = "";
    private static long authoritativeVersion;
    private static readonly string hostSessionId = "journey-" + Guid.NewGuid().ToString("N");
    private static long hostVersion;

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
        RegisterBefore(modConfig, "PlayerInfo.EventTryChangeMap", context => VerifyBeforeTransition("PlayerInfo.EventTryChangeMap"));
        RegisterBefore(modConfig, "Fight_Start.Init", context => VerifyBeforeTransition("Fight_Start.Init"));
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
        var current = IdentityFromNode(manager.MapTree?.currentNode);
        var saved = IdentityFromNode(SafeSaveNode());
        lock (SyncRoot)
        {
            generation++;
            RememberProjectionNoLock(arrays, source);
            var verified = current ?? saved ?? authoritativeIdentity ?? snapshot.VerifiedIdentity;
            var arraysContainVerifiedIdentity = verified != null
                && verified.IsValid
                && TryFindIdentity(arrays.Maps, arrays.MapData, verified, out _, out _);
            var maps = arraysContainVerifiedIdentity ? Clone(arrays.Maps) : Clone(snapshot.Maps);
            var mapData = arraysContainVerifiedIdentity ? Clone(arrays.MapData) : Clone(snapshot.MapData);
            snapshot = new ProjectionSnapshot
            {
                Generation = generation,
                Maps = maps,
                MapData = mapData,
                // A missing projection is the condition being repaired. Never let it
                // erase the last identity or pair it with unrelated sync arrays.
                VerifiedIdentity = verified,
                Source = source
            };
        }

        LogProjectionCapture(source, arrays, current, saved);
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

    private static void VerifyBeforeTransition(string source)
    {
        if (IsServer())
        {
            PublishHostProjection(source);
            return;
        }

        if (!IsClientOnly() || HasCurrentNode())
        {
            return;
        }

        AuraSharedDiagnostics.Warn(
            AuraJourneyConstants.SystemName,
            OwnerId,
            "CurrentNodeProjection",
            "missing client current node at transition preflight; source=" + source + ".",
            false);
        TryRepairCurrentNode(source);
    }

    private static void VerifyAfterNative(string source)
    {
        if (IsServer())
        {
            PublishHostProjection(source);
            return;
        }

        if (!IsClientOnly())
        {
            return;
        }

        Capture(null, source + ":capture");
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
            DelayFrames = 1,
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
        ProjectionArraysSnapshot[] projections;
        NodeIdentity? authoritative;
        lock (SyncRoot)
        {
            local = snapshot.Clone();
            projections = recentProjections.ToArray();
            authoritative = authoritativeIdentity;
        }

        var identity = authoritative ?? IdentityFromNode(SafeSaveNode()) ?? local.VerifiedIdentity;
        if (identity == null || !identity.IsValid)
        {
            reason = "missing-verified-identity";
            return false;
        }

        if (!TryFindInProjections(local, projections, identity, out var mapId, out var nodeId, out var projectionSource))
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
        AuraSharedDiagnostics.Warn(AuraJourneyConstants.SystemName, OwnerId, "CurrentNodeProjection", "restored client current node; source=" + source + "; projectionSource=" + projectionSource + "; generation=" + local.Generation + "; map=" + mapId + "; node=" + nodeId + ".", false);
        return true;
    }

    public static void ApplyHostProjection(AuraJourneyNodeProjectionSnapshot? projection)
    {
        if (!IsClientOnly() || projection == null || !projection.IsValid())
        {
            return;
        }

        lock (SyncRoot)
        {
            if (string.Equals(authoritativeSessionId, projection.SessionId, StringComparison.Ordinal)
                && projection.Version <= authoritativeVersion)
            {
                return;
            }

            authoritativeSessionId = projection.SessionId;
            authoritativeVersion = projection.Version;
            authoritativeIdentity = new NodeIdentity(projection.MapId, projection.NodeId);
        }

        AuraSharedLog.Info(OwnerId, "[CurrentNodeProjection] accepted host projection; session="
            + projection.SessionId + "; version=" + projection.Version + "; map=" + projection.MapId
            + "; node=" + projection.NodeId + "; hash=" + projection.ProjectionHash + ".");
        Capture(null, "HostProjection.accepted");
        if (!HasCurrentNode())
        {
            TryRepairCurrentNode("HostProjection.accepted");
        }
    }

    private static void PublishHostProjection(string source)
    {
        var manager = MapManager.Instance;
        var identity = IdentityFromNode(manager?.MapTree?.currentNode) ?? IdentityFromNode(SafeSaveNode());
        if (manager == null || identity == null || !identity.IsValid)
        {
            return;
        }

        var projection = new AuraJourneyNodeProjectionSnapshot
        {
            SessionId = hostSessionId,
            Version = ++hostVersion,
            MapId = identity.MapId,
            NodeId = identity.NodeId,
            ProjectionHash = ProjectionHash(manager.mapList, manager.mapData),
            CreatedAtUtcTicks = DateTime.UtcNow.Ticks
        };

        try
        {
            PlayerManager.Instance?.SendRpcCommand(new RpcAuraJourneyNodeProjection(projection));
            AuraSharedLog.Info(OwnerId, "[CurrentNodeProjection] host projection published; source=" + source
                + "; version=" + projection.Version + "; map=" + projection.MapId + "; node=" + projection.NodeId
                + "; hash=" + projection.ProjectionHash + ".");
        }
        catch (Exception ex)
        {
            AuraSharedDiagnostics.Warn(AuraJourneyConstants.SystemName, OwnerId, "CurrentNodeProjection", "host projection publish failed: " + ex.Message, true);
        }
    }

    private static bool TryFindInProjections(
        ProjectionSnapshot local,
        ProjectionArraysSnapshot[] projections,
        NodeIdentity identity,
        out string mapId,
        out string nodeId,
        out string source)
    {
        if (TryFindIdentity(local.Maps, local.MapData, identity, out mapId, out nodeId))
        {
            source = local.Source;
            return true;
        }

        for (var i = projections.Length - 1; i >= 0; i--)
        {
            if (TryFindIdentity(projections[i].Maps, projections[i].MapData, identity, out mapId, out nodeId))
            {
                source = projections[i].Source;
                return true;
            }
        }

        mapId = "";
        nodeId = "";
        source = "";
        return false;
    }

    private static void RememberProjectionNoLock(ProjectionArrays arrays, string source)
    {
        if (arrays.Maps.Length == 0 || arrays.MapData.Length == 0)
        {
            return;
        }

        var hash = ProjectionHash(arrays.Maps, arrays.MapData);
        if (recentProjections.Count > 0
            && string.Equals(recentProjections[recentProjections.Count - 1].Hash, hash, StringComparison.Ordinal))
        {
            return;
        }

        recentProjections.Add(new ProjectionArraysSnapshot(Clone(arrays.Maps), Clone(arrays.MapData), hash, source));
        while (recentProjections.Count > MaximumRecentProjections)
        {
            recentProjections.RemoveAt(0);
        }
    }

    private static void LogProjectionCapture(string source, ProjectionArrays arrays, NodeIdentity? current, NodeIdentity? saved)
    {
        NodeIdentity? authority;
        lock (SyncRoot)
        {
            authority = authoritativeIdentity;
        }

        AuraSharedLog.Info(OwnerId, "[CurrentNodeProjection] capture; source=" + source
            + "; pairs=" + Math.Min(arrays.Maps.Length, arrays.MapData.Length)
            + "; hash=" + ProjectionHash(arrays.Maps, arrays.MapData)
            + "; current=" + DisplayIdentity(current)
            + "; save=" + DisplayIdentity(saved)
            + "; authority=" + DisplayIdentity(authority) + ".");
    }

    private static string DisplayIdentity(NodeIdentity? identity)
    {
        return identity == null ? "<none>" : identity.MapId + "/" + identity.NodeId;
    }

    private static string ProjectionHash(string[]? maps, string[]? mapData)
    {
        unchecked
        {
            var hash = 1469598103934665603UL;
            var count = Math.Min(maps?.Length ?? 0, mapData?.Length ?? 0);
            for (var i = 0; i < count; i++)
            {
                var value = (maps![i] ?? "") + "\u001f" + (mapData![i] ?? "");
                for (var j = 0; j < value.Length; j++)
                {
                    hash ^= value[j];
                    hash *= 1099511628211UL;
                }
            }

            return hash.ToString("x16");
        }
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

    private static bool IsServer()
    {
        try
        {
            return PlayerManager.Instance != null && PlayerManager.Instance.isServer;
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

    private sealed class ProjectionArraysSnapshot
    {
        public ProjectionArraysSnapshot(string[] maps, string[] mapData, string hash, string source)
        {
            Maps = maps;
            MapData = mapData;
            Hash = hash;
            Source = source ?? "";
        }

        public string[] Maps { get; }
        public string[] MapData { get; }
        public string Hash { get; }
        public string Source { get; }
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

[Serializable]
public sealed class AuraJourneyNodeProjectionSnapshot
{
    public string SessionId { get; set; } = "";
    public long Version { get; set; }
    public string MapId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string ProjectionHash { get; set; } = "";
    public long CreatedAtUtcTicks { get; set; }

    public bool IsValid()
    {
        return SessionId.Length > 0 && SessionId.Length <= 96
            && Version > 0
            && MapId.Length > 0 && MapId.Length <= 160
            && NodeId.Length > 0 && NodeId.Length <= 160
            && ProjectionHash.Length <= 64;
    }
}

[Serializable]
public sealed class RpcAuraJourneyNodeProjection : RpcCommandBase
{
    public RpcAuraJourneyNodeProjection()
    {
        Projection = new AuraJourneyNodeProjectionSnapshot();
    }

    public RpcAuraJourneyNodeProjection(AuraJourneyNodeProjectionSnapshot projection)
    {
        Projection = projection ?? new AuraJourneyNodeProjectionSnapshot();
    }

    public AuraJourneyNodeProjectionSnapshot Projection { get; set; }

    public override void RpcExecute()
    {
        AuraJourneyCurrentNodeProjectionRuntime.ApplyHostProjection(Projection);
    }
}
