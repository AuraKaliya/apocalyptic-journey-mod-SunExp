using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.SafeBox;

internal static class AuraToolsSafeBoxDataCompatibility
{
    public const string DefaultExpend = "0";
    public const string DefaultIcon = "Icon/Card/\u5361\u9762\u5360\u4f4d";
    public const string DefaultRarity = "1";

    public static bool TryCreateSafeCardData(
        IDictionary<string, string>? data,
        IDictionary<string, string>? vars,
        out Dictionary<string, string> safeData,
        out string id,
        out bool changed)
    {
        safeData = data != null
            ? new Dictionary<string, string>(data, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        id = ReadFirstNonEmpty(safeData, vars, "Id");
        changed = false;

        if (!string.IsNullOrWhiteSpace(id) && PutIfMissingOrBlank(safeData, "Id", id))
        {
            changed = true;
        }

        if (PutIfMissingOrBlank(safeData, "Expend", DefaultExpend))
        {
            changed = true;
        }

        if (PutIfMissing(safeData, "Tag", ""))
        {
            changed = true;
        }

        if (PutIfMissingOrBlank(safeData, "Icon", DefaultIcon))
        {
            changed = true;
        }

        if (PutIfMissingOrBlank(safeData, "Rarity", DefaultRarity))
        {
            changed = true;
        }

        if (PutIfMissingOrBlank(safeData, "Name", string.IsNullOrWhiteSpace(id) ? "Unknown Card" : id))
        {
            changed = true;
        }

        if (PutIfMissing(safeData, "Description", ""))
        {
            changed = true;
        }

        return changed;
    }

    private static string ReadFirstNonEmpty(
        IDictionary<string, string> data,
        IDictionary<string, string>? vars,
        string key)
    {
        if (data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (vars != null && vars.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return "";
    }

    private static bool PutIfMissing(IDictionary<string, string> data, string key, string value)
    {
        if (data.ContainsKey(key))
        {
            return false;
        }

        data[key] = value;
        return true;
    }

    private static bool PutIfMissingOrBlank(IDictionary<string, string> data, string key, string value)
    {
        if (data.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing))
        {
            return false;
        }

        data[key] = value;
        return true;
    }
}
