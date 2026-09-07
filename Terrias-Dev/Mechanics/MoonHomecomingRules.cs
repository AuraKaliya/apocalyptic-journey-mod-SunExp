using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

[Flags]
public enum MoonChronicles
{
    None = 0,
    First = 1,
    Second = 2,
    Third = 4
}

public readonly struct MoonHomecomingReward
{
    public MoonHomecomingReward(MoonChronicles chronicles)
    {
        Power = (chronicles & MoonChronicles.First) != 0 ? 2 : 0;
        Draw = (chronicles & MoonChronicles.First) != 0 ? 1 : 0;
        Ripples = (chronicles & MoonChronicles.Second) != 0 ? 5 : 0;
        ExtraUses = (chronicles & MoonChronicles.Third) != 0 ? 1 : 0;
    }

    public int Power { get; }
    public int Draw { get; }
    public int Ripples { get; }
    public int ExtraUses { get; }
}

public static class MoonHomecomingRules
{
    public static MoonChronicles Chronicle(string? cardId)
    {
        return cardId switch
        {
            MoonHomecomingIds.ChronicleI => MoonChronicles.First,
            MoonHomecomingIds.ChronicleII => MoonChronicles.Second,
            MoonHomecomingIds.ChronicleIII => MoonChronicles.Third,
            _ => MoonChronicles.None
        };
    }

    public static MoonChronicles ReadChronicles(IEnumerable<string>? cardIds)
    {
        var result = MoonChronicles.None;
        if (cardIds != null)
        {
            foreach (var id in cardIds) result |= Chronicle(id);
        }
        return result;
    }

    public static string RandomChronicleId(int index)
    {
        return index switch
        {
            0 => MoonHomecomingIds.ChronicleI,
            1 => MoonHomecomingIds.ChronicleII,
            2 => MoonHomecomingIds.ChronicleIII,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    public static bool IsMarrowReaction(ElementalReactionType reaction)
    {
        return reaction is ElementalReactionType.ElectroCharged
            or ElementalReactionType.Bloom or ElementalReactionType.Crystallize;
    }

    public static int MarrowGrowth(int maximumHp)
    {
        return maximumHp <= 0 ? 0 : 1 + maximumHp / 100;
    }

    public static int AddMaximumHp(int maximumHp, int growth)
    {
        return (int)Math.Min(int.MaxValue, Math.Max(1L, (long)maximumHp + Math.Max(0, growth)));
    }

    public static int Shield(int maximumHp, int percent)
    {
        return (int)Math.Min(int.MaxValue, (long)Math.Max(0, maximumHp) * Math.Max(0, percent) / 100L);
    }

    public static int OfferingRecovery(int maximumHp, int currentHp, int offeringCost)
    {
        var missing = Math.Max(0L, (long)maximumHp - Math.Max(0, currentHp));
        var amount = (long)Math.Max(0, maximumHp) * Math.Max(0, offeringCost) / 10L;
        return (int)Math.Min(missing, amount);
    }
}
