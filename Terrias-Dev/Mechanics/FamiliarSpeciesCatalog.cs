using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class FamiliarSpeciesCatalog
{
    public static IReadOnlyList<FamiliarSpeciesSpec> AllSpecies()
    {
        try
        {
            return TerriasConfigIndex.Rows(DataType.Partner)
                .Select(ToSpec)
                .Where(spec => !string.IsNullOrWhiteSpace(spec.SpeciesId))
                .GroupBy(spec => spec.SpeciesId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(spec => spec.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(spec => spec.SpeciesId, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[FamiliarGrowth] failed to read partner species: " + ex.Message);
            return Array.Empty<FamiliarSpeciesSpec>();
        }
    }

    public static FamiliarSpeciesSpec? Find(string speciesId)
    {
        var value = (speciesId ?? "").Trim();
        if (value.Length == 0)
        {
            return null;
        }

        return AllSpecies().FirstOrDefault(spec => FamiliarId.Matches(value, spec));
    }

    private static FamiliarSpeciesSpec ToSpec(Dictionary<string, string> row)
    {
        var id = DictionaryUtil.Get(row, "Id");
        var nativeBlessingId = DictionaryUtil.Get(row, "Bless");
        var fullSpeciesId = FullPartnerId(id, nativeBlessingId);
        var speciesId = LocalPartnerId(id, fullSpeciesId);
        if (speciesId.Length == 0 || string.Equals(speciesId, "id", StringComparison.OrdinalIgnoreCase))
        {
            return EmptySpec();
        }

        return new FamiliarSpeciesSpec(
            speciesId,
            fullSpeciesId,
            DisplayName(row, speciesId),
            Description(row),
            FirstNonEmpty(
                DictionaryUtil.Get(row, "ChoiceIcon"),
                DictionaryUtil.Get(row, "CareerImage"),
                DictionaryUtil.Get(row, "Model")),
            DictionaryUtil.Get(row, "Model"),
            DictionaryUtil.Get(row, "Animation"),
            nativeBlessingId);
    }

    private static string DisplayName(Dictionary<string, string> data, string fallback)
    {
        try
        {
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }
        }
        catch
        {
            // Fall through to raw fields.
        }

        return FirstNonEmpty(DictionaryUtil.Get(data, "Name"), fallback);
    }

    private static string Description(Dictionary<string, string> data)
    {
        try
        {
            var description = data.Localize("Description");
            return string.IsNullOrWhiteSpace(description) ? "" : description;
        }
        catch
        {
            return DictionaryUtil.Get(data, "Description");
        }
    }

    private static string FullPartnerId(string id, string nativeBlessingId)
    {
        var value = (id ?? "").Trim();
        if (value.Length == 0)
        {
            return "";
        }

        var blessing = (nativeBlessingId ?? "").Trim();
        var marker = "_" + value;
        var markerIndex = blessing.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0)
        {
            var prefix = blessing.Substring(0, markerIndex + 1);
            if (prefix.TrimEnd('_').Count(ch => ch == '_') >= 1)
            {
                return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? value : prefix + value;
            }
        }

        return value;
    }

    private static string LocalPartnerId(string id, string fullSpeciesId)
    {
        var value = (id ?? "").Trim();
        var full = (fullSpeciesId ?? "").Trim();
        if (full.Length > value.Length && full.EndsWith("_" + value, StringComparison.OrdinalIgnoreCase))
        {
            return FamiliarId.Sanitize(value).ToLowerInvariant();
        }

        if (value.StartsWith("Terrias_terrias_", StringComparison.OrdinalIgnoreCase))
        {
            return FamiliarId.NormalizeSpeciesId(value);
        }

        return FamiliarId.Sanitize(value).ToLowerInvariant();
    }

    private static FamiliarSpeciesSpec EmptySpec()
    {
        return new FamiliarSpeciesSpec("", "", "", "", "", "", "", "");
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}
