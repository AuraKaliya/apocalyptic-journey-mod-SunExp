using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Network;

namespace SunExp.Dll.Mechanics;

public static class CompanionAuthorityService
{
    public const int ProjectionProtocolVersion = 4;

    private static int battleEpoch;

    public static bool IsAuthoritative()
    {
        return !SunExpNetworkRuntime.IsMultiplayerSession() || SunExpNetworkRuntime.IsServer();
    }

    public static int BattleEpoch => battleEpoch;

    public static void BeginBattleEpoch()
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

    public static string ResolveOwnerPlayerId(string ownerStatusId, string preferredPlayerId = "")
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

        var local = SunExpNetworkRuntime.LocalPlayerId();
        return string.IsNullOrWhiteSpace(local) ? ownerStatusId ?? "" : local;
    }

    public static CompanionEntityIdentity Create(
        string statusId,
        string ownerPlayerId,
        string ownerStatusId,
        string roleId,
        int slotIndex)
    {
        return new CompanionEntityIdentity
        {
            StatusId = statusId ?? "",
            OwnerPlayerId = ownerPlayerId ?? "",
            OwnerStatusId = ownerStatusId ?? "",
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

        // Projection attachments deliberately stay outside RoleStatusMap so
        // native friendly targeting and formal player slots never see them.
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
        CompanionEntityIdentity? identity;
        lock (SyncRoot)
        {
            Identities.TryGetValue(statusId ?? "", out identity);
            Identities.Remove(statusId ?? "");
        }

        var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
        if (map == null)
        {
            return;
        }

        if (identity != null
            && !string.IsNullOrWhiteSpace(identity.OwnerPlayerId)
            && map.TryGetValue(identity.OwnerPlayerId, out var statuses))
        {
            statuses?.Remove(statusId);
            return;
        }

        foreach (var entry in map.Values)
        {
            entry?.Remove(statusId);
        }
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

        foreach (var statuses in map.Values)
        {
            if (statuses == null)
            {
                continue;
            }

            foreach (var statusId in statusIds)
            {
                statuses.Remove(statusId);
            }
        }
    }
}
