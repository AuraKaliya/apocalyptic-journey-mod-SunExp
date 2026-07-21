using System;
using System.Collections.Generic;

namespace Terrias.Dll.Infrastructure;

public static class TerriasContentIdCompatibility
{
    public const string CurrentModId = "Terrias";
    public const string CurrentMainPrefix = "Terrias_terrias_";
    public static readonly string LegacyModId = BuildText(83, 117, 110, 69, 120, 112);
    private static readonly string LegacyMainTableId = BuildText(115, 117, 110, 101, 120, 112);
    public static readonly string LegacyMainPrefix = LegacyPrefix(LegacyMainTableId);

    private static readonly PrefixMapping[] Mappings =
    {
        new("cursecard", "Terrias_cursecard_", LegacyPrefix("cursecard")),
        new("terrias", CurrentMainPrefix, LegacyMainPrefix, LegacyMainTableId),
        new("loneer", "Terrias_loneer_", LegacyPrefix("loneer")),
        new("wuna", "Terrias_wuna_", LegacyPrefix("wuna")),
        new("columbina", "Terrias_columbina_", LegacyPrefix("columbina")),
        new("solar_memory", "Terrias_solar_memory_", LegacyPrefix("solar_memory"))
    };

    public static string Canonicalize(string? id)
    {
        var value = (id ?? "").Trim();
        var mapping = FindByPrefix(value);
        return mapping == null || value.StartsWith(mapping.CurrentPrefix, StringComparison.OrdinalIgnoreCase)
            ? value
            : mapping.CurrentPrefix + value.Substring(mapping.LegacyPrefix.Length);
    }

    public static string LocalId(string? id)
    {
        var value = (id ?? "").Trim();
        var mapping = FindByPrefix(value);
        if (mapping == null)
        {
            return value;
        }

        var prefix = value.StartsWith(mapping.CurrentPrefix, StringComparison.OrdinalIgnoreCase)
            ? mapping.CurrentPrefix
            : mapping.LegacyPrefix;
        return value.Substring(prefix.Length);
    }

    public static bool HasKnownPrefix(string? id)
    {
        return FindByPrefix((id ?? "").Trim()) != null;
    }

    public static bool IsLegacyId(string? id)
    {
        var value = (id ?? "").Trim();
        var mapping = FindByPrefix(value);
        return mapping != null && value.StartsWith(mapping.LegacyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Equivalent(string? left, string? right)
    {
        var leftValue = (left ?? "").Trim();
        var rightValue = (right ?? "").Trim();
        if (string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftMapping = FindByPrefix(leftValue);
        var rightMapping = FindByPrefix(rightValue);
        if (leftMapping != null && rightMapping != null)
        {
            return string.Equals(Canonicalize(leftValue), Canonicalize(rightValue), StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(LocalId(leftValue), LocalId(rightValue), StringComparison.OrdinalIgnoreCase);
    }

    public static string[] LookupCandidates(string? id, params string[] tableIds)
    {
        var value = (id ?? "").Trim();
        if (value.Length == 0)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        Add(values, value);
        var mapping = FindByPrefix(value);
        if (mapping != null)
        {
            var localId = LocalId(value);
            Add(values, mapping.CurrentPrefix + localId);
            Add(values, mapping.LegacyPrefix + localId);
            Add(values, localId);
            return values.ToArray();
        }

        var requestedTables = tableIds == null || tableIds.Length == 0
            ? new[] { "terrias" }
            : tableIds;
        foreach (var tableId in requestedTables)
        {
            var tableMapping = FindByTable(tableId);
            if (tableMapping == null)
            {
                continue;
            }

            Add(values, tableMapping.CurrentPrefix + value);
            Add(values, tableMapping.LegacyPrefix + value);
        }

        return values.ToArray();
    }

    public static string CurrentPrefixFor(string tableId)
    {
        return FindByTable(tableId)?.CurrentPrefix ?? "";
    }

    public static string LegacyPrefixFor(string tableId)
    {
        return FindByTable(tableId)?.LegacyPrefix ?? "";
    }

    private static PrefixMapping? FindByPrefix(string value)
    {
        foreach (var mapping in Mappings)
        {
            if (value.StartsWith(mapping.CurrentPrefix, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(mapping.LegacyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return mapping;
            }
        }

        return null;
    }

    private static PrefixMapping? FindByTable(string? tableId)
    {
        var value = (tableId ?? "").Trim();
        foreach (var mapping in Mappings)
        {
            if (mapping.MatchesTable(value))
            {
                return mapping;
            }
        }

        return null;
    }

    private static void Add(List<string> values, string value)
    {
        if (value.Length > 0 && !values.Exists(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            values.Add(value);
        }
    }

    private static string LegacyPrefix(string tableId)
    {
        return LegacyModId + "_" + tableId + "_";
    }

    private static string BuildText(params int[] codePoints)
    {
        var characters = new char[codePoints.Length];
        for (var index = 0; index < codePoints.Length; index++)
        {
            characters[index] = (char)codePoints[index];
        }

        return new string(characters);
    }

    private sealed class PrefixMapping
    {
        private readonly string aliasTableId;

        public PrefixMapping(string tableId, string currentPrefix, string legacyPrefix, string aliasTableId = "")
        {
            TableId = tableId;
            CurrentPrefix = currentPrefix;
            LegacyPrefix = legacyPrefix;
            this.aliasTableId = aliasTableId;
        }

        public string TableId { get; }

        public string CurrentPrefix { get; }

        public string LegacyPrefix { get; }

        public bool MatchesTable(string value)
        {
            return string.Equals(value, TableId, StringComparison.OrdinalIgnoreCase)
                   || aliasTableId.Length > 0
                   && string.Equals(value, aliasTableId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
