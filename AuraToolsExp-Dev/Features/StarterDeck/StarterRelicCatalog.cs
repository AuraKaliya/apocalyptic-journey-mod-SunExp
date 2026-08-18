using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal sealed class StarterRelicCatalogEntry
{
    internal string Id { get; set; } = "";

    internal string PackId { get; set; } = "";

    internal string DisplayName { get; set; } = "";

    internal string Rarity { get; set; } = "";

    internal string IconPath { get; set; } = "";
}

internal sealed class StarterRelicPackGroup
{
    internal string PackId { get; set; } = "";

    internal string DisplayName { get; set; } = "";

    internal List<string> RelicIds { get; set; } = new();
}

internal static class StarterRelicCatalog
{
    private const string OtherGroupId = "__other__";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Sprite?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static List<StarterRelicCatalogEntry> entries = new();
    private static List<StarterRelicPackGroup> groups = new();
    private static Dictionary<string, StarterRelicCatalogEntry> byId = new(StringComparer.OrdinalIgnoreCase);
    private static long epoch = -1;
    private static bool initialized;

    internal static void Initialize()
    {
        lock (Gate)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            AuraGameDataCatalogRuntime.SnapshotChanged += _ => Invalidate();
        }
    }

    internal static IReadOnlyList<StarterRelicPackGroup> BuildGroups()
    {
        EnsureBuilt();
        lock (Gate)
        {
            return groups.Select(group => new StarterRelicPackGroup
            {
                PackId = group.PackId,
                DisplayName = group.DisplayName,
                RelicIds = group.RelicIds.ToList()
            }).ToList();
        }
    }

    internal static bool IsValidRelic(string relicId)
    {
        if (string.IsNullOrWhiteSpace(relicId))
        {
            return false;
        }

        try
        {
            return AuraGameDataHostApi.Resolve(DataType.Relic, relicId) != null;
        }
        catch
        {
            return false;
        }
    }

    internal static string ResolveRelicId(string relicId, string ownerModId = "")
    {
        EnsureBuilt();
        List<string> ids;
        lock (Gate)
        {
            ids = entries.Select(entry => entry.Id).ToList();
        }

        var resolution = AuraSharedContentId.Resolve(relicId, ids, ownerModId);
        return resolution.Success ? resolution.ResolvedId : (relicId ?? "").Trim();
    }

    internal static string DisplayName(string relicId)
    {
        return TryGet(relicId, out var entry) && entry != null ? entry.DisplayName : relicId;
    }

    internal static string Rarity(string relicId)
    {
        return TryGet(relicId, out var entry) && entry != null && !string.IsNullOrWhiteSpace(entry.Rarity)
            ? "R" + entry.Rarity
            : "?";
    }

    internal static string SortKey(string relicId)
    {
        return TryGet(relicId, out var entry) && entry != null
            ? entry.Rarity.PadLeft(2, '0') + "|" + entry.DisplayName + "|" + entry.Id
            : "99|" + relicId;
    }

    internal static Sprite? TryLoadIcon(string relicId)
    {
        if (IconCache.TryGetValue(relicId, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        if (TryGet(relicId, out var entry) && entry != null && !string.IsNullOrWhiteSpace(entry.IconPath))
        {
            try
            {
                sprite = AuraToolsResourceCache.Load<Sprite>(entry.IconPath, true);
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[CustomStart] failed to load relic icon for " + relicId + ": " + ex.Message);
            }
        }

        IconCache[relicId] = sprite;
        return sprite;
    }

    private static bool TryGet(string relicId, out StarterRelicCatalogEntry? entry)
    {
        EnsureBuilt();
        lock (Gate)
        {
            return byId.TryGetValue(relicId ?? "", out entry);
        }
    }

    private static void EnsureBuilt()
    {
        var snapshot = AuraGameDataHostApi.AcquireSnapshot();
        lock (Gate)
        {
            if (epoch == snapshot.Version.Epoch)
            {
                return;
            }

            if (!snapshot.Version.NativeReady)
            {
                return;
            }

            Build(snapshot.Version.Epoch);
        }
    }

    private static void Build(long currentEpoch)
    {
        var packNames = AuraGameDataHostApi.CopyTableForHostInterop(DataType.CardPack)
            .Where(row => row.TryGetValue("Id", out var id) && !string.IsNullOrWhiteSpace(id))
            .GroupBy(row => row["Id"], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => LocalizedName(group.First(), group.Key), StringComparer.OrdinalIgnoreCase);
        var built = new List<StarterRelicCatalogEntry>();
        foreach (var row in AuraGameDataHostApi.CopyTableForHostInterop(DataType.Relic))
        {
            if (!row.TryGetValue("Id", out var id) || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            row.TryGetValue("PackBelong", out var packId);
            row.TryGetValue("Rarity", out var rarity);
            row.TryGetValue("Icon", out var icon);
            built.Add(new StarterRelicCatalogEntry
            {
                Id = id.Trim(),
                PackId = (packId ?? "").Trim(),
                DisplayName = LocalizedName(row, id),
                Rarity = rarity ?? "",
                IconPath = icon ?? ""
            });
        }

        entries = built
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Rarity)
            .ThenBy(entry => entry.DisplayName)
            .ThenBy(entry => entry.Id)
            .ToList();
        byId = entries.ToDictionary(entry => entry.Id, entry => entry, StringComparer.OrdinalIgnoreCase);
        groups = entries
            .GroupBy(entry => string.IsNullOrWhiteSpace(entry.PackId) ? OtherGroupId : entry.PackId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StarterRelicPackGroup
            {
                PackId = group.Key,
                DisplayName = group.Key == OtherGroupId
                    ? "其它"
                    : packNames.TryGetValue(group.Key, out var name) ? name : group.Key,
                RelicIds = group
                    .OrderBy(entry => entry.Rarity)
                    .ThenBy(entry => entry.DisplayName)
                    .ThenBy(entry => entry.Id)
                    .Select(entry => entry.Id)
                    .ToList()
            })
            .OrderBy(group => group.DisplayName)
            .ThenBy(group => group.PackId)
            .ToList();
        epoch = currentEpoch;
        IconCache.Clear();
        AuraToolsLog.Info("[CustomStart] built relic catalog: relics=" + entries.Count + ", groups=" + groups.Count + ".");
    }

    private static string LocalizedName(Dictionary<string, string> row, string fallback)
    {
        try
        {
            var localized = row.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }
        }
        catch
        {
        }

        return row.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : fallback;
    }

    private static void Invalidate()
    {
        lock (Gate)
        {
            epoch = -1;
            entries.Clear();
            groups.Clear();
            byId.Clear();
            IconCache.Clear();
        }
    }
}
