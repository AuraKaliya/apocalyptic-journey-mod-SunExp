using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public readonly struct PolymorphRoleCrop
{
    public PolymorphRoleCrop(int offsetX, int offsetY, int size)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        Size = Math.Max(1, size);
    }

    public int OffsetX { get; }

    public int OffsetY { get; }

    public int Size { get; }
}

public static class PolymorphRoleCropRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PolymorphRoleCrop> Crops = new(StringComparer.Ordinal);
    private static int defaultCropSize = 512;

    public static int DefaultCropSize
    {
        get
        {
            lock (SyncRoot)
            {
                return defaultCropSize;
            }
        }
    }

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            Crops.Clear();
            defaultCropSize = 512;

            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.PolymorphCropConfigFile);
            if (!File.Exists(path))
            {
                TerriasLog.Warn("[PolymorphRoleCrop] config missing; using centered 512 crops.");
                return;
            }

            try
            {
                var document = JsonConvert.DeserializeObject<PolymorphRoleCropDocument>(File.ReadAllText(path))
                    ?? new PolymorphRoleCropDocument();
                defaultCropSize = Math.Max(1, document.DefaultCropSize <= 0 ? 512 : document.DefaultCropSize);
                foreach (var pair in document.Roles ?? new Dictionary<string, PolymorphRoleCropEntry>())
                {
                    var roleId = (pair.Key ?? "").Trim();
                    if (roleId.Length == 0)
                    {
                        continue;
                    }

                    var entry = pair.Value ?? new PolymorphRoleCropEntry();
                    Crops[roleId] = new PolymorphRoleCrop(
                        entry.X,
                        entry.Y,
                        entry.Size <= 0 ? defaultCropSize : entry.Size);
                }

                TerriasLog.Info("[PolymorphRoleCrop] loaded crop config: roles=" + Crops.Count);
            }
            catch (Exception ex)
            {
                Crops.Clear();
                defaultCropSize = 512;
                TerriasLog.Warn("[PolymorphRoleCrop] failed to load config; using defaults: " + ex.Message);
            }
        }
    }

    public static PolymorphRoleCrop CropFor(string roleId)
    {
        lock (SyncRoot)
        {
            var id = (roleId ?? "").Trim();
            return id.Length > 0 && Crops.TryGetValue(id, out var crop)
                ? crop
                : new PolymorphRoleCrop(0, 0, defaultCropSize);
        }
    }

    private sealed class PolymorphRoleCropDocument
    {
        public int DefaultCropSize { get; set; } = 512;

        public Dictionary<string, PolymorphRoleCropEntry>? Roles { get; set; }
    }

    private sealed class PolymorphRoleCropEntry
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Size { get; set; }
    }
}
