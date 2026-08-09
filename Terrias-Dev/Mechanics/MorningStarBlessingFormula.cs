using System;

namespace Terrias.Dll.Mechanics;

public static class MorningStarBlessingFormula
{
    public static int MissingHealthRecovery(int maxHp, int currentHp)
    {
        var missing = Math.Max(0, Math.Max(0, maxHp) - Math.Max(0, currentHp));
        return missing <= 0 ? 0 : Math.Max(1, missing / 100);
    }
}
