using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class SunExpConfigIndex
{
    private const string SunExpPrefix = "SunExp_sunexp_";
    private static readonly Dictionary<DataType, TableCache> TableCaches = new();
    private static readonly Dictionary<string, FilterCache> FilterCaches = new(StringComparer.Ordinal);

    public static List<Dictionary<string, string>> Rows(DataType type)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var rows = Singleton<GameConfigManager>.Instance.GetTable(type).Getlines();
            if (TableCaches.TryGetValue(type, out var cache) && cache.SourceCount == rows.Count)
            {
                return cache.Rows;
            }

            cache = new TableCache(rows);
            TableCaches[type] = cache;
            return cache.Rows;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[ConfigIndex] failed to read " + type + " rows: " + ex.Message);
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
            var direct = TryGetOne(type, normalized)
                ?? TryGetOne(type, AlternateSunExpId(normalized));
            if (direct != null)
            {
                return direct;
            }

            var cache = EnsureTableCache(type);
            return cache.ById.TryGetValue(normalized, out var row)
                ? row
                : null;
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

        var rows = Rows(type);
        var cacheKey = type + "\u001f" + key;
        if (FilterCaches.TryGetValue(cacheKey, out var cached) && cached.SourceCount == rows.Count)
        {
            return cached.Rows;
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var filtered = rows.Where(predicate).ToList();
            FilterCaches[cacheKey] = new FilterCache(rows.Count, filtered);
            return filtered;
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("ConfigIndex.FilteredRows." + type + "." + key, start);
        }
    }

    public static void Reset()
    {
        TableCaches.Clear();
        FilterCaches.Clear();
    }

    private static TableCache EnsureTableCache(DataType type)
    {
        Rows(type);
        return TableCaches.TryGetValue(type, out var cache)
            ? cache
            : new TableCache(new List<Dictionary<string, string>>());
    }

    private static Dictionary<string, string>? TryGetOne(DataType type, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return Singleton<GameConfigManager>.Instance.GetOne(type, id);
        }
        catch
        {
            return null;
        }
    }

    private static string AlternateSunExpId(string id)
    {
        return id.StartsWith(SunExpPrefix, StringComparison.Ordinal)
            ? id.Substring(SunExpPrefix.Length)
            : SunExpPrefix + id;
    }

    private sealed class TableCache
    {
        public TableCache(List<Dictionary<string, string>> rows)
        {
            Rows = rows;
            SourceCount = rows.Count;
            ById = BuildIndex(rows);
        }

        public List<Dictionary<string, string>> Rows { get; }

        public int SourceCount { get; }

        public Dictionary<string, Dictionary<string, string>> ById { get; }

        private static Dictionary<string, Dictionary<string, string>> BuildIndex(IEnumerable<Dictionary<string, string>> rows)
        {
            var index = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var id = DictionaryUtil.Get(row, "Id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                Add(index, id, row);
                Add(index, AlternateSunExpId(id), row);
            }

            return index;
        }

        private static void Add(
            IDictionary<string, Dictionary<string, string>> index,
            string key,
            Dictionary<string, string> row)
        {
            if (!string.IsNullOrWhiteSpace(key) && !index.ContainsKey(key))
            {
                index[key] = row;
            }
        }
    }

    private sealed class FilterCache
    {
        public FilterCache(int sourceCount, List<Dictionary<string, string>> rows)
        {
            SourceCount = sourceCount;
            Rows = rows;
        }

        public int SourceCount { get; }

        public List<Dictionary<string, string>> Rows { get; }
    }
}
