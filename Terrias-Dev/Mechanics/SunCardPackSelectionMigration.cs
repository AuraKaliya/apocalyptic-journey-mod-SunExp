using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SunCardPackSelectionMigration
{
    private const string CanonicalLocalId = "cardpack_solar_ember_crown_canopy";

    private static readonly HashSet<string> LegacyLocalIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "cardpack_radiant_spark",
        "cardpack_ember_crown",
        "cardpack_solar_canopy"
    };

    public static bool Apply(ICollection<string>? selectedPackIds)
    {
        if (selectedPackIds == null)
        {
            return false;
        }

        var legacyEntries = selectedPackIds
            .Where(id => LegacyLocalIds.Contains(TerriasContentIdCompatibility.LocalId(id)))
            .ToArray();
        var canonicalEntries = selectedPackIds
            .Where(id => string.Equals(
                TerriasContentIdCompatibility.LocalId(id),
                CanonicalLocalId,
                StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    id,
                    TerriasIds.SolarEmberCrownCanopyCardPackId,
                    StringComparison.Ordinal))
            .ToArray();
        if (legacyEntries.Length == 0 && canonicalEntries.Length == 0)
        {
            return false;
        }

        var changed = false;
        foreach (var id in legacyEntries.Concat(canonicalEntries))
        {
            changed |= selectedPackIds.Remove(id);
        }

        if (!selectedPackIds.Contains(TerriasIds.SolarEmberCrownCanopyCardPackId))
        {
            selectedPackIds.Add(TerriasIds.SolarEmberCrownCanopyCardPackId);
            changed = true;
        }

        return changed;
    }
}
