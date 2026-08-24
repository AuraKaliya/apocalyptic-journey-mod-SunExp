using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

internal static class ProjectionWrappedCardPolicy
{
    private static readonly HashSet<string> SafeTerriasCards = new(
        new[]
        {
            "spark",
            "scorching_canopy_card",
            "radiant_flame_slash",
            "ember_cloak_card",
            "draw_flame",
            "solar_prayer",
            "burning_star_hex",
            "crown_radiance",
            "canopy_return",
            "solar_coronation",
            "blazing_crown_collapse",
            "solar_ignition",
            "scorching_flow_reclaim",
            "impurity_purge",
            "eclipse_hex",
            "solar_scorching_light",
            "burning_calamity",
            "burning_crown_oath",
            "morning_light_bulwark",
            "gathered_flame_shield",
            "gathered_flame_cycle",
            "solar_eclipse",
            "smoke_erosion",
            "afterglow_omen_card",
            "solar_phase_tuning",
            "radiant_oath",
            "solar_return",
            "solar_origin_core",
            "ember_tower",
            TerriasIds.ProjectionBasicActionShortId
        },
        StringComparer.OrdinalIgnoreCase);

    public static bool IsHeadlessSafe(string cardId, string script)
    {
        const string safePrefix = "CS.Terrias.Dll.Scripting.CardScripts.";
        if (string.IsNullOrWhiteSpace(script)
            || script.IndexOf(
                safePrefix,
                StringComparison.Ordinal) < 0)
        {
            return false;
        }

        for (var index = script.IndexOf("CS.", StringComparison.Ordinal);
             index >= 0;
             index = script.IndexOf("CS.", index + 3, StringComparison.Ordinal))
        {
            if (script.IndexOf(safePrefix, index, StringComparison.Ordinal) != index)
            {
                return false;
            }
        }

        var localId = TerriasContentIdCompatibility.LocalId(cardId);
        return SafeTerriasCards.Contains(localId);
    }

    public static bool IsLifecycleSafe(string cardId, string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return true;
        }
        return ProjectionCardExecutionPolicy.Resolve(null, cardId, script).LifecycleSafe;
    }

    public static bool IsProjectionStateCard(string cardId)
    {
        return string.Equals(cardId, "solar_phase_tuning", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "radiant_oath", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "solar_return", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "solar_origin_core", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "ember_tower", StringComparison.OrdinalIgnoreCase);
    }
}
