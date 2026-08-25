using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.Mechanics;

public static class CompanionAuthorityService
{
    public const int ProjectionProtocolVersion = 20;

    private static int battleEpoch;

    public static bool IsAuthoritative()
    {
        return !TerriasNetworkRuntime.IsMultiplayerSession() || TerriasNetworkRuntime.IsServer();
    }

    public static int BattleEpoch => battleEpoch;

    public static void BeginBattleEpoch()
    {
        AdvanceBattleEpoch();
    }

    public static void InvalidateBattleEpoch()
    {
        AdvanceBattleEpoch();
    }

    private static void AdvanceBattleEpoch()
    {
        battleEpoch++;
        if (battleEpoch <= 0)
        {
            battleEpoch = 1;
        }
    }
}

public static class CompanionOwnershipService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, CompanionEntityIdentity> Identities = new(StringComparer.Ordinal);

    public static string ResolveSemanticOwnerPlayerId(string ownerStatusId, string preferredPlayerId = "")
    {
        if (!string.IsNullOrWhiteSpace(preferredPlayerId))
        {
            return preferredPlayerId.Trim();
        }

        var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
        if (map != null)
        {
            foreach (var entry in map)
            {
                if (entry.Value != null && entry.Value.Contains(ownerStatusId))
                {
                    return entry.Key;
                }
            }
        }

        var local = TerriasNetworkRuntime.LocalPlayerId();
        return string.IsNullOrWhiteSpace(local) ? ownerStatusId ?? "" : local;
    }

    public static CompanionEntityIdentity Create(
        string statusId,
        string ownerPlayerId,
        string ownerStatusId,
        string roleId,
        int slotIndex,
        string executionRoutePlayerId = "")
    {
        return new CompanionEntityIdentity
        {
            StatusId = statusId ?? "",
            SemanticOwnerPlayerId = ownerPlayerId ?? "",
            SemanticOwnerStatusId = ownerStatusId ?? "",
            ExecutionRoutePlayerId = string.IsNullOrWhiteSpace(executionRoutePlayerId)
                ? ownerPlayerId ?? ""
                : executionRoutePlayerId.Trim(),
            RoleId = roleId ?? "",
            Faction = "Friendly",
            EntityKind = "Companion",
            SlotIndex = slotIndex
        };
    }

    public static void Register(CompanionEntityIdentity identity)
    {
        if (identity == null || string.IsNullOrWhiteSpace(identity.StatusId))
        {
            return;
        }

        lock (SyncRoot)
        {
            Identities[identity.StatusId] = identity;
        }

        EnsureNativeStatusRoute(identity, "CompanionOwnershipService.Register");
    }

    public static bool EnsureNativeStatusRoute(string statusId, string source)
    {
        var identity = Find(statusId);
        return identity != null && EnsureNativeStatusRoute(identity, source);
    }

    private static bool EnsureNativeStatusRoute(
        CompanionEntityIdentity identity,
        string source)
    {
        try
        {
            var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            if (!CompanionNativeStatusRouting.Register(
                    map,
                    identity.ExecutionRoutePlayerId,
                    identity.StatusId))
            {
                TerriasLog.Warn("[CompanionRouting] native status route was not registered: route="
                                + identity.ExecutionRoutePlayerId
                                + ", semanticOwner="
                                + identity.SemanticOwnerPlayerId
                                + ", status="
                                + identity.StatusId
                                + ", source="
                                + source);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CompanionRouting] native status route registration failed: source="
                            + source
                            + ", error="
                            + ex.Message);
            return false;
        }
    }

    public static CompanionEntityIdentity? Find(string statusId)
    {
        lock (SyncRoot)
        {
            return !string.IsNullOrWhiteSpace(statusId) && Identities.TryGetValue(statusId, out var identity)
                ? identity
                : null;
        }
    }

    public static bool IsFriendlyCompanion(IStatusManager? status)
    {
        var identity = status == null ? null : Find(status.InstanceId);
        return identity != null
            && string.Equals(identity.Faction, "Friendly", StringComparison.Ordinal)
            && string.Equals(identity.EntityKind, "Companion", StringComparison.Ordinal);
    }

    public static void Remove(string statusId)
    {
        lock (SyncRoot)
        {
            Identities.Remove(statusId ?? "");
        }

        var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
        if (map == null)
        {
            return;
        }

        CompanionNativeStatusRouting.Remove(map, statusId);
    }

    public static void Clear()
    {
        string[] statusIds;
        lock (SyncRoot)
        {
            statusIds = new string[Identities.Count];
            Identities.Keys.CopyTo(statusIds, 0);
            Identities.Clear();
        }

        var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
        if (map == null)
        {
            return;
        }

        foreach (var statusId in statusIds)
        {
            CompanionNativeStatusRouting.Remove(map, statusId);
        }
    }
}
