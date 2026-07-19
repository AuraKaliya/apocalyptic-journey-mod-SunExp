using System;

namespace SunExp.Dll.Mechanics;

public static class LunarReactionRules
{
    public static int ElectroChargedDamage(int personalTriggerCount)
    {
        return Math.Max(0, personalTriggerCount) * 2;
    }

    public static int AddCrystallizeCounts(int current, int added, out int triggerTimes)
    {
        var total = Math.Max(0, current) + Math.Max(0, added);
        triggerTimes = total / 3;
        return total % 3;
    }

    public static bool Crossed(int before, int after, int threshold)
    {
        return before < threshold && after >= threshold;
    }
}
