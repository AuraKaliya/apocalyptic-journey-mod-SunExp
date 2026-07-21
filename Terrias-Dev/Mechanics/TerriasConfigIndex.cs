using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class TerriasConfigIndex
{
    private static readonly Dictionary<string, FilterCache> FilterCaches = new(StringComparer.Ordinal);

    public static List<Dictionary<string, string>> Rows(DataType type)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            return AuraGameDataHostApi.CopyTableForHostInterop(type);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ConfigIndex] shared catalog failed to read " + type + " rows: " + ex.Message);
            return new List<Dictionary<string, string>>();
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("ConfigIndex.Rows." + type, start);
        }
    }

    public static Dictionary<string, string>? Row(DataType type, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            var resolved = AuraGameDataHostApi.Resolve(
                type,
                TerriasContentIdCompatibility.LookupCandidates(id, "terrias"));
            return resolved?.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("ConfigIndex.Row." + type, start);
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

        var snapshot = AuraGameDataHostApi.AcquireSnapshot();
        var cacheKey = type + "\u001f" + key;
        if (FilterCaches.TryGetValue(cacheKey, out var cached) && cached.Revision == snapshot.Version.Epoch)
        {
            return cached.Rows.Select(Clone).ToList();
        }

        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            var filtered = snapshot.GetTable(type.ToString())
                .Select(item => item.Fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                .Where(predicate)
                .ToList();
            FilterCaches[cacheKey] = new FilterCache(snapshot.Version.Epoch, filtered);
            return filtered.Select(Clone).ToList();
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("ConfigIndex.FilteredRows." + type + "." + key, start);
        }
    }

    public static void Reset()
    {
        FilterCaches.Clear();
        AuraGameDataHostApi.InvalidateNativeCatalog();
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
