using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class EndlessAbyssEvacuationDepth
{
    public static int Calculate(int floor, int level)
    {
        var normalizedFloor = Math.Max(1, floor);
        var normalizedLevel = Math.Max(0, level);
        var depth = (long)(normalizedFloor - 1) * TerriasIds.EndlessSeaLayerNodeCount + normalizedLevel;
        return (int)Math.Min(int.MaxValue, depth);
    }
}
