using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraToolsExp.Dll.Features.AdventureArchive;

internal sealed class AdventureArchiveContentEntry
{
    [JsonProperty("id")] public string Id { get; set; } = "";
    [JsonProperty("ownerModId")] public string OwnerModId { get; set; } = "";
    [JsonProperty("displayName")] public string DisplayName { get; set; } = "";
    [JsonProperty("zone")] public string Zone { get; set; } = "";
    [JsonProperty("count")] public int Count { get; set; } = 1;

    internal void Normalize(string defaultZone)
    {
        Id = (Id ?? "").Trim();
        OwnerModId = (OwnerModId ?? "").Trim();
        DisplayName = (DisplayName ?? "").Trim();
        Zone = string.IsNullOrWhiteSpace(Zone) ? defaultZone : Zone.Trim();
        Count = Math.Max(1, Count);
    }

    internal AdventureArchiveContentEntry CloneWithCount(int count)
    {
        return new AdventureArchiveContentEntry
        {
            Id = Id,
            OwnerModId = OwnerModId,
            DisplayName = DisplayName,
            Zone = Zone,
            Count = Math.Max(1, count)
        };
    }
}

internal sealed class AdventureArchiveContentDelta
{
    internal AdventureArchiveContentEntry Entry { get; set; } = new();
    internal int Delta { get; set; }
}

internal sealed class AdventureArchiveSnapshotDiff
{
    internal List<AdventureArchiveContentDelta> Cards { get; } = new();
    internal List<AdventureArchiveContentDelta> Relics { get; } = new();
    internal List<AdventureArchiveContentDelta> Blessings { get; } = new();
    internal int MoneyDelta { get; set; }
    internal bool HasChanges => Cards.Count > 0 || Relics.Count > 0 || Blessings.Count > 0 || MoneyDelta != 0;
}

internal static class AdventureArchiveProjection
{
    internal static string SerializeEntries(IEnumerable<AdventureArchiveContentEntry> values)
    {
        return JsonConvert.SerializeObject(
            NormalizeEntries(values, ""),
            Formatting.None);
    }

    internal static List<AdventureArchiveContentEntry> ReadEntries(string json, string defaultZone)
    {
        var result = new List<AdventureArchiveContentEntry>();
        JArray values;
        try
        {
            values = JArray.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        }
        catch
        {
            return result;
        }

        foreach (var token in values)
        {
            AdventureArchiveContentEntry? entry;
            if (token.Type == JTokenType.String)
            {
                entry = new AdventureArchiveContentEntry
                {
                    Id = token.Value<string>() ?? "",
                    DisplayName = "",
                    Zone = defaultZone,
                    Count = 1
                };
            }
            else
            {
                try { entry = token.ToObject<AdventureArchiveContentEntry>(); }
                catch { entry = null; }
            }
            if (entry == null) continue;
            entry.Normalize(defaultZone);
            if (entry.Id.Length > 0) result.Add(entry);
        }
        return NormalizeEntries(result, defaultZone);
    }

    internal static string MigrateLegacyArray(string json, string defaultZone)
    {
        return SerializeEntries(ReadEntries(json, defaultZone));
    }

    internal static AdventureArchiveSnapshotDiff Diff(
        AdventureArchiveSnapshot? previous,
        AdventureArchiveSnapshot current)
    {
        var diff = new AdventureArchiveSnapshotDiff();
        AddDeltas(diff.Cards,
            previous == null ? Array.Empty<AdventureArchiveContentEntry>() : ReadEntries(previous.CardsJson, "牌组"),
            ReadEntries(current.CardsJson, "牌组"));
        AddDeltas(diff.Relics,
            previous == null ? Array.Empty<AdventureArchiveContentEntry>() : ReadEntries(previous.RelicsJson, "遗物"),
            ReadEntries(current.RelicsJson, "遗物"));
        AddDeltas(diff.Blessings,
            previous == null ? Array.Empty<AdventureArchiveContentEntry>() : ReadEntries(previous.BlessingsJson, "祝福"),
            ReadEntries(current.BlessingsJson, "祝福"));
        diff.MoneyDelta = ReadInt(current.StateJson, "money")
                          - (previous == null ? ReadInt(current.StateJson, "money") : ReadInt(previous.StateJson, "money"));
        return diff;
    }

    internal static string Signature(AdventureArchiveSnapshot snapshot)
    {
        return snapshot.Stage + "|" + snapshot.RoleId + "|" + snapshot.CardsJson + "|"
               + snapshot.RelicsJson + "|" + snapshot.BlessingsJson + "|" + snapshot.StateJson;
    }

    internal static int ReadInt(string json, string property)
    {
        try { return JObject.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).Value<int?>(property) ?? 0; }
        catch { return 0; }
    }

    private static List<AdventureArchiveContentEntry> NormalizeEntries(
        IEnumerable<AdventureArchiveContentEntry> values,
        string defaultZone)
    {
        return (values ?? Array.Empty<AdventureArchiveContentEntry>())
            .Where(value => value != null)
            .Select(value =>
            {
                value.Normalize(defaultZone);
                return value;
            })
            .Where(value => value.Id.Length > 0)
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var display = group.Select(item => item.DisplayName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
                var result = first.CloneWithCount(group.Sum(item => item.Count));
                result.DisplayName = display;
                return result;
            })
            .OrderBy(value => value.Zone, StringComparer.Ordinal)
            .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddDeltas(
        ICollection<AdventureArchiveContentDelta> target,
        IEnumerable<AdventureArchiveContentEntry> before,
        IEnumerable<AdventureArchiveContentEntry> after)
    {
        var left = before.ToDictionary(Key, value => value, StringComparer.OrdinalIgnoreCase);
        var right = after.ToDictionary(Key, value => value, StringComparer.OrdinalIgnoreCase);
        foreach (var key in left.Keys.Concat(right.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            left.TryGetValue(key, out var oldValue);
            right.TryGetValue(key, out var newValue);
            var delta = (newValue?.Count ?? 0) - (oldValue?.Count ?? 0);
            if (delta == 0) continue;
            target.Add(new AdventureArchiveContentDelta
            {
                Entry = (newValue ?? oldValue)!.CloneWithCount(Math.Abs(delta)),
                Delta = delta
            });
        }
    }

    private static string Key(AdventureArchiveContentEntry value)
    {
        return (value.OwnerModId ?? "") + "|" + (value.Id ?? "") + "|" + (value.Zone ?? "");
    }
}
