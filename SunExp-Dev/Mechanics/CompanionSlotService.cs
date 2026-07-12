using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.Mechanics;

public static class CompanionSlotService
{
    public const int MaxFriendlySlots = 4;

    private const float CenterX = -3.5f;
    private const float MultiplayerSpacing = 2.5f;
    private const float SinglePlayerSpacing = 3.5f;

    public static int? FindOpenPlayerSlot()
    {
        var occupied = new HashSet<int>();
        var nativePlayers = CurrentFriendlyStatuses().ToArray();
        for (var i = 0; i < nativePlayers.Length && i < MaxFriendlySlots; i++)
        {
            occupied.Add(i);
        }

        foreach (var state in ProjectionStateStore.Active())
        {
            if (state.SlotIndex >= 0)
            {
                occupied.Add(state.SlotIndex);
            }
        }

        foreach (var slotIndex in HeartChangeControlService.ActiveSlotIndexes())
        {
            if (slotIndex >= 0)
            {
                occupied.Add(slotIndex);
            }
        }

        for (var i = 0; i < MaxFriendlySlots; i++)
        {
            if (!occupied.Contains(i))
            {
                return i;
            }
        }

        return null;
    }

    public static void PositionInPlayerSlot(ProjectionOtherObj projection, int slotIndex)
    {
        PositionStatusInPlayerSlot(projection.Status, slotIndex);
    }

    public static void PositionStatusInPlayerSlot(IStatusManager? status, int slotIndex)
    {
        ReflowFriendlyLineup("PositionStatusInPlayerSlot", status, slotIndex);
    }

    public static void ReflowFriendlyLineup(string source)
    {
        ReflowFriendlyLineup(source, null, -1);
    }

    public static float SlotX(int slotIndex, int friendlyCount = MaxFriendlySlots)
    {
        var count = Math.Max(1, Math.Min(MaxFriendlySlots, friendlyCount));
        var index = Math.Max(0, Math.Min(count - 1, slotIndex));
        var spacing = count > 1 ? MultiplayerSpacing : SinglePlayerSpacing;
        return CenterX + ((count - 1 - index) - (count - 1) / 2f) * spacing;
    }

    private static void ReflowFriendlyLineup(string source, IStatusManager? pendingStatus, int pendingSlot)
    {
        try
        {
            var lineup = BuildLineup(pendingStatus, pendingSlot);
            var count = Math.Min(MaxFriendlySlots, lineup.Count);
            for (var i = 0; i < count; i++)
            {
                PositionAt(lineup[i].Status, i, count);
            }

            SunExpPerformanceCounters.Record("CompanionSlot.Reflow");
            SunExpLog.Debug("[CompanionSlot] reflowed from " + source
                + ": count=" + count
                + ", lineup=" + string.Join(",", lineup.Take(count).Select(entry => entry.StatusId + "@" + entry.LogicalSlot)));
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[CompanionSlot] reflow failed from " + source + ": " + ex.Message);
        }
    }

    private static List<FriendlyEntry> BuildLineup(IStatusManager? pendingStatus, int pendingSlot)
    {
        var result = new List<FriendlyEntry>();
        var statusIds = new HashSet<string>(StringComparer.Ordinal);
        var playerSlot = 0;
        foreach (var status in CurrentFriendlyStatuses())
        {
            Add(result, statusIds, status, playerSlot++, isNativePlayer: true);
        }

        foreach (var state in ProjectionStateStore.Active().OrderBy(state => state.SlotIndex).ThenBy(state => state.StatusId, StringComparer.Ordinal))
        {
            Add(result, statusIds, state.Projection?.Status, state.SlotIndex, isNativePlayer: false);
        }

        foreach (var entry in HeartChangeControlService.ActiveSlotStatuses().OrderBy(entry => entry.Key).ThenBy(entry => entry.Value?.InstanceId, StringComparer.Ordinal))
        {
            Add(result, statusIds, entry.Value, entry.Key, isNativePlayer: false);
        }

        Add(result, statusIds, pendingStatus, pendingSlot, isNativePlayer: false);
        return result
            .OrderBy(entry => entry.IsNativePlayer ? 0 : 1)
            .ThenBy(entry => entry.LogicalSlot)
            .ThenBy(entry => entry.StatusId, StringComparer.Ordinal)
            .ToList();
    }

    private static void Add(
        ICollection<FriendlyEntry> result,
        ISet<string> statusIds,
        IStatusManager? status,
        int logicalSlot,
        bool isNativePlayer)
    {
        if (status?.transform == null)
        {
            return;
        }

        var id = status.InstanceId ?? "";
        var dedupeId = id.Length > 0 ? id : "ref:" + status.GetHashCode();
        if (!statusIds.Add(dedupeId))
        {
            return;
        }

        result.Add(new FriendlyEntry(status, id, logicalSlot, isNativePlayer));
    }

    private static void PositionAt(IStatusManager status, int visualIndex, int friendlyCount)
    {
        if (status.transform == null)
        {
            return;
        }

        var groundY = CurrentGroundY(status.transform.position.y);
        var bottom = status.transform.Find("bottom");
        var bottomOffset = bottom == null ? 0f : bottom.localPosition.y;
        status.SetPosition(new Vector3(SlotX(visualIndex, friendlyCount), groundY - bottomOffset, 0f));
    }

    private static IEnumerable<IStatusManager> CurrentFriendlyStatuses()
    {
        var result = new List<IStatusManager>();
        var roleIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var manager = FightManager.Instance;
            if (manager?.roleQueue != null)
            {
                foreach (var role in manager.roleQueue)
                {
                    var instanceId = role?.InstanceId ?? "";
                    if (!string.IsNullOrWhiteSpace(instanceId)
                        && roleIds.Add(instanceId)
                        && manager.statuses?.TryGetValue(instanceId, out var status) == true)
                    {
                        result.Add(status);
                    }
                }
            }
        }
        catch
        {
            // Fall through to the singleton player.
        }

        var self = FightPlayer.Instance?.Status;
        var selfId = self?.InstanceId ?? "";
        if (self != null && !string.IsNullOrWhiteSpace(selfId) && roleIds.Add(selfId))
        {
            result.Add(self);
        }

        return result;
    }

    private static float CurrentGroundY(float fallback)
    {
        try
        {
            return GameApp.Instance.NowBackground.transform.Find("com").GetComponent<SceneInfo>().ground_y;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed class FriendlyEntry
    {
        public FriendlyEntry(IStatusManager status, string statusId, int logicalSlot, bool isNativePlayer)
        {
            Status = status;
            StatusId = statusId;
            LogicalSlot = logicalSlot;
            IsNativePlayer = isNativePlayer;
        }

        public IStatusManager Status { get; }

        public string StatusId { get; }

        public int LogicalSlot { get; }

        public bool IsNativePlayer { get; }
    }
}
