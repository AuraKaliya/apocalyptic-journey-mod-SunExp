using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public static class FriendlyRoleSeatPolicy
{
    public const int Capacity = 4;

    public static int FindOpenSeat(
        int realPlayerCount,
        IEnumerable<int>? projectionSlots,
        IEnumerable<int>? reservationSlots)
    {
        var occupied = new HashSet<int>();
        var normalizedPlayers = System.Math.Max(0, System.Math.Min(Capacity, realPlayerCount));
        for (var index = 0; index < normalizedPlayers; index++)
        {
            occupied.Add(index);
        }

        AddValidSlots(occupied, projectionSlots);
        AddValidSlots(occupied, reservationSlots);
        for (var index = 0; index < Capacity; index++)
        {
            if (!occupied.Contains(index))
            {
                return index;
            }
        }

        return -1;
    }

    private static void AddValidSlots(ISet<int> occupied, IEnumerable<int>? slots)
    {
        if (slots == null)
        {
            return;
        }

        foreach (var slot in slots)
        {
            if (slot >= 0 && slot < Capacity)
            {
                occupied.Add(slot);
            }
        }
    }
}
