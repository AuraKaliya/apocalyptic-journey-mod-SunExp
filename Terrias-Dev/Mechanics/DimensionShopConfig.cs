using System;
using System.IO;
using System.Linq;
using AuraShared.Core;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public sealed class DimensionShopConfigDocument
{
    public int SchemaVersion { get; set; } = 3;

    public int CardPrice { get; set; } = 8;

    public int RelicPrice { get; set; } = 8;

    public int RefreshPrice { get; set; } = 1;

    public string[] CardPackIds { get; set; } = { TerriasIds.MoreDimensionsCardPackId };

    public string[] IncludeCardIds { get; set; } = Array.Empty<string>();

    public string[] ExcludeCardIds { get; set; } = Array.Empty<string>();

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
            var bundled = LoadBundledDefaults(modConfig);
            var snapshot = AuraSharedConfigStore.ReadOwner(
                TerriasIds.ModId,
                TerriasIds.DimensionShopConfigSystem,
                TerriasIds.DimensionShopConfigFile,
                bundled);
            current = Normalize(snapshot.Value);
            if (snapshot.Found)
            {
                TerriasLog.Info("[DimensionShop] loaded owner config from "
                                + AuraSharedConfigStore.ConfigPath(
                                    TerriasIds.ModId,
                                    TerriasIds.DimensionShopConfigSystem,
                                    TerriasIds.DimensionShopConfigFile)
                                + ".");
            }
            else
            {
                var write = AuraSharedConfigStore.WriteOwner(
                    TerriasIds.ModId,
                    TerriasIds.DimensionShopConfigSystem,
                    TerriasIds.DimensionShopConfigFile,
                    current,
                    expectedRevision: 0,
                    schemaVersion: current.SchemaVersion);
                if (!write.Success)
                {
                    TerriasLog.Warn("[DimensionShop] could not seed owner config; using bundled defaults: "
                                   + write.Message);
                }
            }
        }
    }

    private static DimensionShopConfigDocument LoadBundledDefaults(ModConfig modConfig)
    {
        var fallback = Normalize(new DimensionShopConfigDocument());
        var relative = TerriasIds.DimensionShopBundledConfigRelativePath
            .Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(modConfig.DirectoryName, relative);
        if (!File.Exists(path))
        {
            TerriasLog.Warn("[DimensionShop] missing bundled defaults; using built-in defaults.");
            return fallback;
        }

        try
        {
            var document = JsonConvert.DeserializeObject<DimensionShopConfigDocument>(
                File.ReadAllText(path));
            TerriasLog.Info("[DimensionShop] loaded bundled defaults from " + path + ".");
            return Normalize(document ?? new DimensionShopConfigDocument());
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[DimensionShop] failed to load bundled defaults; using built-in defaults: "
                           + ex.Message);
            return fallback;
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
