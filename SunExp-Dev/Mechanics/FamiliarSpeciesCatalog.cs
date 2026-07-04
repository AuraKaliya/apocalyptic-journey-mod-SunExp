using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class FamiliarSpeciesCatalog
{
    public static IReadOnlyList<FamiliarSpeciesSpec> AllSpecies()
    {
        try
        {
            return SunExpConfigIndex.Rows(DataType.Partner)
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
            SunExpLog.Warn("[FamiliarGrowth] failed to read partner species: " + ex.Message);
            return Array.Empty<FamiliarSpeciesSpec>();
        }
    }

    public static FamiliarSpeciesSpec? Find(string speciesId)
    {
        var normalized = FamiliarId.NormalizeSpeciesId(speciesId);
        if (normalized.Length == 0)
        {
            return null;
        }

        return AllSpecies().FirstOrDefault(spec => string.Equals(spec.SpeciesId, normalized, StringComparison.Ordinal));
    }

    private static FamiliarSpeciesSpec ToSpec(Dictionary<string, string> row)
    {
        var id = DictionaryUtil.Get(row, "Id");
        var speciesId = FamiliarId.NormalizeSpeciesId(id);
        if (speciesId.Length == 0 || string.Equals(speciesId, "id", StringComparison.OrdinalIgnoreCase))
        {
            return EmptySpec();
        }

        return new FamiliarSpeciesSpec(
            speciesId,
            FullPartnerId(id),
            DisplayName(row, speciesId),
            Description(row),
            FirstNonEmpty(
                DictionaryUtil.Get(row, "ChoiceIcon"),
                DictionaryUtil.Get(row, "CareerImage"),
                DictionaryUtil.Get(row, "Model")),
            DictionaryUtil.Get(row, "Model"),
            DictionaryUtil.Get(row, "Animation"),
            DictionaryUtil.Get(row, "Bless"));
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

    private static string FullPartnerId(string id)
    {
        var value = (id ?? "").Trim();
        return value.StartsWith("SunExp_sunexp_", StringComparison.Ordinal)
            ? value
            : "SunExp_sunexp_" + FamiliarId.NormalizeSpeciesId(value);
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
