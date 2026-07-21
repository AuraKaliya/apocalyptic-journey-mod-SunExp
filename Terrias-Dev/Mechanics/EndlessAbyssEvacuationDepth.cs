using System;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class EndlessAbyssEvacuationDepth
{
    public static int Calculate(int floor, int level)
    {
        var normalizedFloor = Math.Max(1, floor);
        var normalizedLevel = Math.Max(0, level);
        var depth = (long)(normalizedFloor - 1) * SunExpIds.EndlessSeaLayerNodeCount + normalizedLevel;
        return (int)Math.Min(int.MaxValue, depth);
    }
}
