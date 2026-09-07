using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public readonly struct OlimyaDamageReward
{
    public OlimyaDamageReward(int hpLoss, int shieldLoss)
    {
        HpLoss = hpLoss;
        Gold = (int)Math.Min(int.MaxValue, (long)hpLoss + shieldLoss);
    }
    public int HpLoss { get; }
    public int Gold { get; }
}

public readonly struct OlimyaCoinReward
{
    public OlimyaCoinReward(int manufactured, int remainder)
    {
        Manufactured = manufactured;
        Remainder = remainder;
    }
    public int Manufactured { get; }
    public int Remainder { get; }
}

public static class OlimyaRules
{
    public static bool IsOlimya(string? roleId)
    {
        return string.Equals(roleId, "olimya", StringComparison.OrdinalIgnoreCase)
            || string.Equals(roleId, OlimyaIds.Career, StringComparison.OrdinalIgnoreCase);
    }

    public static OlimyaCoinReward Coins(long actualChange, int remainder)
    {
        var magnitude = Math.Min(int.MaxValue, Math.Abs(Math.Max(-int.MaxValue, actualChange)));
        var total = magnitude + (remainder == 1 ? 1 : 0);
        return new OlimyaCoinReward((int)(total / 2), (int)(total % 2));
    }

    public static OlimyaDamageReward Damage(int hpBefore, int shieldBefore, int resolvedDamage, bool ignoresShield)
    {
        if (hpBefore <= 0 || resolvedDamage <= 0) return new OlimyaDamageReward(0, 0);
        var shieldLoss = ignoresShield ? 0 : Math.Min(Math.Max(0, shieldBefore), resolvedDamage);
        return new OlimyaDamageReward(Math.Min(hpBefore, resolvedDamage - shieldLoss), shieldLoss);
    }
}
