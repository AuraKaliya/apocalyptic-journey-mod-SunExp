using System;

namespace Terrias.Dll.Hooks.Ui;

internal static class SpiritArtifactRowRingPolicy
{
    public static int IncomingRowCount(int currentFirstRow, int nextFirstRow, int activeRowCount)
    {
        activeRowCount = Math.Max(0, activeRowCount);
        if (activeRowCount == 0 || currentFirstRow == nextFirstRow) return 0;
        if (currentFirstRow < 0) return activeRowCount;
        var delta = Math.Abs(nextFirstRow - currentFirstRow);
        return delta >= activeRowCount ? activeRowCount : delta;
    }

    public static bool RequiresFullRebind(int currentFirstRow, int nextFirstRow, int activeRowCount)
        => activeRowCount > 0
           && IncomingRowCount(currentFirstRow, nextFirstRow, activeRowCount) >= activeRowCount;
}
