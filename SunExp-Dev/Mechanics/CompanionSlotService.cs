using System;
using System.Collections.Generic;
using UnityEngine;

namespace SunExp.Dll.Mechanics;

public static class CompanionSlotService
{
    public const int MaxFriendlySlots = 4;

    private const float CenterX = -3.5f;
    private const float SlotSpacing = 2.5f;

    public static int? FindOpenPlayerSlot()
    {
        var occupied = new HashSet<int>();
        foreach (var status in CurrentFriendlyStatuses())
        {
            var slot = NearestSlot(status?.transform?.position.x ?? CenterX);
            if (slot >= 0)
            {
                occupied.Add(slot);
            }
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
        if (status?.transform == null)
        {
            return;
        }

        var groundY = CurrentGroundY(status.transform.position.y);
        var bottom = status.transform.Find("bottom");
        var bottomOffset = bottom == null ? 0f : bottom.localPosition.y;
        status.SetPosition(new Vector3(SlotX(slotIndex), groundY - bottomOffset, 0f));
    }

    public static float SlotX(int slotIndex)
    {
        var index = Math.Max(0, Math.Min(MaxFriendlySlots - 1, slotIndex));
        return CenterX + ((MaxFriendlySlots - 1 - index) - (MaxFriendlySlots - 1) / 2f) * SlotSpacing;
    }

    private static int NearestSlot(float x)
    {
        var bestSlot = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < MaxFriendlySlots; i++)
        {
            var distance = Math.Abs(x - SlotX(i));
            if (distance < bestDistance)
            {
                bestSlot = i;
                bestDistance = distance;
            }
        }

        return bestSlot;
    }

    private static IEnumerable<IStatusManager?> CurrentFriendlyStatuses()
    {
        var result = new List<IStatusManager?>();
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
        if (!string.IsNullOrWhiteSpace(selfId) && roleIds.Add(selfId))
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
}
