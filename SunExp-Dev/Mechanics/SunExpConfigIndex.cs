using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class SunExpConfigIndex
{
    private const string SunExpPrefix = "SunExp_sunexp_";
    private static readonly Dictionary<string, FilterCache> FilterCaches = new(StringComparer.Ordinal);

    public static List<Dictionary<string, string>> Rows(DataType type)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            return AuraGameDataHostApi.Rows(type);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[ConfigIndex] shared catalog failed to read " + type + " rows: " + ex.Message);
            return new List<Dictionary<string, string>>();
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("ConfigIndex.Rows." + type, start);
        }
    }

    public static Dictionary<string, string>? Row(DataType type, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var normalized = id.Trim();
            return AuraGameDataHostApi.Row(type, normalized, AlternateSunExpId(normalized));
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("ConfigIndex.Row." + type, start);
        }
    }

    public static List<Dictionary<string, string>> FilteredRows(
        DataType type,
        string key,
        Func<Dictionary<string, string>, bool> predicate)
    {
        if (predicate == null)
        {
            return new List<Dictionary<string, string>>();
        }

        var snapshot = AuraGameDataHostApi.Query(type);
        var cacheKey = type + "\u001f" + key;
        if (FilterCaches.TryGetValue(cacheKey, out var cached) && cached.Revision == snapshot.Revision)
        {
            return cached.Rows.Select(Clone).ToList();
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var filtered = snapshot.Items
                .Select(item => item.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                .Where(predicate)
                .ToList();
            FilterCaches[cacheKey] = new FilterCache(snapshot.Revision, filtered);
            return filtered.Select(Clone).ToList();
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("ConfigIndex.FilteredRows." + type + "." + key, start);
        }
    }

    public static void Reset()
    {
        FilterCaches.Clear();
        AuraGameDataHostApi.InvalidateNativeCatalog();
    }

    private static string AlternateSunExpId(string id)
    {
        return id.StartsWith(SunExpPrefix, StringComparison.Ordinal)
            ? id.Substring(SunExpPrefix.Length)
            : SunExpPrefix + id;
    }

    private static Dictionary<string, string> Clone(Dictionary<string, string> row)
    {
        return new Dictionary<string, string>(row, StringComparer.Ordinal);
    }

    private sealed class FilterCache
    {
        public FilterCache(long revision, List<Dictionary<string, string>> rows)
        {
            Revision = revision;
            Rows = rows.Select(Clone).ToList();
        }

        public long Revision { get; }

        public List<Dictionary<string, string>> Rows { get; }
    }
}
