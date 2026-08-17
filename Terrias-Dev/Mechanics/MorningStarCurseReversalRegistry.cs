using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class MorningStarCurseReward
{
    public int Resilient { get; set; }

    public int Starlight { get; set; }

    public int KeenEdge { get; set; }

    public int Impregnable { get; set; }

    public int Evergreen { get; set; }

    public int Power { get; set; }

    public int VowPower { get; set; }

    public int Extraordinary { get; set; }

    public int Rebirth { get; set; }

    public void Add(MorningStarCurseReward? other)
    {
        if (other == null)
        {
            return;
        }

        Resilient = MorningStarCurseFormula.SaturatingAdd(Resilient, other.Resilient);
        Starlight = MorningStarCurseFormula.SaturatingAdd(Starlight, other.Starlight);
        KeenEdge = MorningStarCurseFormula.SaturatingAdd(KeenEdge, other.KeenEdge);
        Impregnable = MorningStarCurseFormula.SaturatingAdd(Impregnable, other.Impregnable);
        Evergreen = MorningStarCurseFormula.SaturatingAdd(Evergreen, other.Evergreen);
        Power = MorningStarCurseFormula.SaturatingAdd(Power, other.Power);
        VowPower = MorningStarCurseFormula.SaturatingAdd(VowPower, other.VowPower);
        Extraordinary = MorningStarCurseFormula.SaturatingAdd(Extraordinary, other.Extraordinary);
        Rebirth = MorningStarCurseFormula.SaturatingAdd(Rebirth, other.Rebirth);
    }
}

public static class MorningStarCurseReversalRegistry
{
    private static readonly Dictionary<string, Func<int, MorningStarCurseReward>> Recipes =
        new(StringComparer.Ordinal)
        {
            ["cursecard_1"] = tier => new MorningStarCurseReward { Resilient = Twice(tier) },
            ["cursecard_2"] = tier => new MorningStarCurseReward { Starlight = Twice(tier) },
            ["cursecard_3"] = tier => new MorningStarCurseReward { KeenEdge = Twice(tier) },
            ["cursecard_4"] = _ => new MorningStarCurseReward { Impregnable = 1 },
            ["cursecard_5"] = tier => new MorningStarCurseReward { Resilient = Twice(tier) },
            ["cursecard_6"] = tier => new MorningStarCurseReward { Starlight = Twice(tier) },
            ["cursecard_7"] = tier => new MorningStarCurseReward { Evergreen = Twice(tier) },
            ["cursecard_8"] = tier => new MorningStarCurseReward { KeenEdge = Twice(tier) },
            ["cursecard_9"] = _ => new MorningStarCurseReward { Power = 1 },
            ["cursecard_10"] = tier => new MorningStarCurseReward { Evergreen = Twice(tier) },
            ["cursecard_11"] = _ => new MorningStarCurseReward { VowPower = 2, Starlight = 2 },
            ["cursecard_12"] = tier => new MorningStarCurseReward { Evergreen = Twice(tier) },
            ["cursecard_13"] = tier => new MorningStarCurseReward { Extraordinary = TenTimes(tier) },
            ["cursecard_14"] = _ => new MorningStarCurseReward { Rebirth = 30 },
            ["cursecard_15"] = tier => new MorningStarCurseReward { Extraordinary = TenTimes(tier) },
            ["abyss_life_theft"] = tier => new MorningStarCurseReward { Evergreen = Twice(tier) },
            ["abyss_deficit"] = _ => new MorningStarCurseReward { Power = 1 }
        };

    public static MorningStarCurseReward Resolve(string? cardId, int rarity)
    {
        var tier = MorningStarCurseFormula.NormalizeTier(rarity);
        var normalized = NormalizeCardId(cardId);
        return Recipes.TryGetValue(normalized, out var recipe)
            ? recipe(tier)
            : new MorningStarCurseReward { VowPower = tier, Starlight = tier };
    }

    public static bool IsKnown(string? cardId)
    {
        return Recipes.ContainsKey(NormalizeCardId(cardId));
    }

    public static string NormalizeCardId(string? cardId)
    {
        var value = (cardId ?? "").Trim().TrimStart('*');
        value = TerriasContentIdCompatibility.LocalId(value).TrimStart('*');
        if (value.StartsWith("Terrias_terrias_", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("Terrias_terrias_".Length);
        }

        if (value.StartsWith("Terrias_cursecard_", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("Terrias_cursecard_".Length);
        }

        return value.ToLowerInvariant();
    }

    private static int Twice(int tier)
    {
        return MorningStarCurseFormula.SaturatingMultiply(2, tier);
    }

    private static int TenTimes(int tier)
    {
        return MorningStarCurseFormula.SaturatingMultiply(10, tier);
    }
}
