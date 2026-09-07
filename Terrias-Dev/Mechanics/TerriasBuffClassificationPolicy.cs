using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public static class TerriasBuffClassificationPolicy
{
    private static readonly HashSet<string> PositiveExcludeIds = new(StringComparer.Ordinal)
    {
        "scorching_canopy",
        "ember_cloak",
        "solar_crown",
        "solar_crown_tier",
        "origin_core_radiance",
        "cycle_gathered_flame",
        "afterglow_omen",
        TerriasIds.SolarCrown,
        TerriasIds.SolarCrownTier,
        TerriasIds.StarStonePouch,
        TerriasIds.MiracleClock,
        TerriasIds.Starlight,
        TerriasIds.StarBlessing,
        TerriasIds.StarScore,
        TerriasIds.Resonance,
        TerriasIds.StarClayBody,
        TerriasIds.StarClayDollTrait,
        TerriasIds.SandroneCatTrait,
        TerriasIds.PolymorphTraitBuffId
    };

    public static bool IsExcludedFromPositiveEffects(string? buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        if (FieldApi.IsFieldBuffId(buffId))
        {
            return true;
        }

        var id = buffId ?? "";
        var normalized = TerriasContentIdCompatibility.LocalId(id);
        return PositiveExcludeIds.Contains(id) || PositiveExcludeIds.Contains(normalized);
    }

}
