using System;

namespace Terrias.Dll.Mechanics;

public static class WitchArchiveSelectionPolicy
{
    public static int Move(int currentIndex, int count, int delta)
    {
        if (count <= 0)
        {
            return -1;
        }

        var normalized = Math.Max(0, Math.Min(count - 1, currentIndex));
        var next = (normalized + delta) % count;
        return next < 0 ? next + count : next;
    }
}
