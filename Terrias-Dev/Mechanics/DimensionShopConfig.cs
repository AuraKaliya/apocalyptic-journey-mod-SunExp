using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public sealed class DimensionShopConfigDocument
{
    public int SchemaVersion { get; set; } = 2;

    public int CardPrice { get; set; } = 8;

    public int RelicPrice { get; set; } = 8;

    public int RefreshPrice { get; set; } = 1;

    public string[] CardPackIds { get; set; } = { TerriasIds.MoreDimensionsCardPackId };

    public string[] IncludeCardIds { get; set; } = Array.Empty<string>();

    public string[] ExcludeCardIds { get; set; } = Array.Empty<string>();

    public string[] RelicIds { get; set; } = { TerriasIds.BrokenDialRelicId };

    public string ShopkeeperPortraitResourcePath { get; set; } = "";

    public string ShopkeeperPortraitNodePath { get; set; } = "";
}

public static class DimensionShopConfigStore
{
    private static readonly object SyncRoot = new();
    private static DimensionShopConfigDocument current = Normalize(new DimensionShopConfigDocument());

    public static DimensionShopConfigDocument Current
    {
        get
        {
            lock (SyncRoot)
            {
                return current;
            }
        }
    }

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var fallback = Normalize(new DimensionShopConfigDocument());
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.DimensionShopConfigFile);
            if (!File.Exists(path))
            {
                current = fallback;
                TerriasLog.Warn("[DimensionShop] missing config; using built-in defaults.");
                return;
            }

            try
            {
                current = Normalize(
                    JsonConvert.DeserializeObject<DimensionShopConfigDocument>(File.ReadAllText(path))
                    ?? new DimensionShopConfigDocument());
                TerriasLog.Info("[DimensionShop] loaded config from " + path);
            }
            catch (Exception ex)
            {
                current = fallback;
                TerriasLog.Warn("[DimensionShop] failed to load config; using built-in defaults: " + ex.Message);
            }
        }
    }

    private static DimensionShopConfigDocument Normalize(DimensionShopConfigDocument document)
    {
        document ??= new DimensionShopConfigDocument();
        document.SchemaVersion = Math.Max(1, document.SchemaVersion);
        document.CardPrice = Math.Max(0, document.CardPrice);
        document.RelicPrice = Math.Max(0, document.RelicPrice);
        document.RefreshPrice = Math.Max(0, document.RefreshPrice);
        document.CardPackIds = NormalizeIds(document.CardPackIds, TerriasIds.MoreDimensionsCardPackId);
        document.IncludeCardIds = NormalizeIds(document.IncludeCardIds);
        document.ExcludeCardIds = NormalizeIds(document.ExcludeCardIds);
        document.RelicIds = NormalizeIds(document.RelicIds, TerriasIds.BrokenDialRelicId);
        document.ShopkeeperPortraitResourcePath = (document.ShopkeeperPortraitResourcePath ?? "").Trim();
        document.ShopkeeperPortraitNodePath = (document.ShopkeeperPortraitNodePath ?? "").Trim();
        return document;
    }

    private static string[] NormalizeIds(string[]? values, params string[] fallback)
    {
        var result = (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return result.Length > 0 ? result : fallback;
    }
}
