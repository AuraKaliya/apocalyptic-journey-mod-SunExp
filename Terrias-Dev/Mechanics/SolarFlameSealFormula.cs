using System;

namespace Terrias.Dll.Mechanics;

public static class SolarFlameSealFormula
{
    public static int GatheredFlameGain(int actualPaidCost)
    {
        return (int)Math.Min(int.MaxValue, (long)Math.Max(0, actualPaidCost) + 1L);
    }
}
