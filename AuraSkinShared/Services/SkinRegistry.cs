using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Models;

namespace AuraSkin.Shared.Services;

public static class SkinRegistry
{
    private const string CharacterManifestFileName = "character.json";
    private const string SkinManifestFileName = "skin.json";
    private static readonly Dictionary<string, SkinDefinition> ByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<SkinDefinition>> ByCareer = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> CareerIds => ByCareer.Keys;

    public static void Reload()
    {
        ByKey.Clear();
        ByCareer.Clear();

        var root = SkinPaths.SkinRootDirectory;
        if (!Directory.Exists(root))
        {
            SkinLog.Warn("Shared skin scan skipped: " + root + " does not exist");
            return;
        }

        string[] characterDirectories;
        try
        {
            characterDirectories = Directory.EnumerateDirectories(root)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to enumerate shared character skin directories: " + ex.Message);
            return;
        }

        foreach (var characterDirectory in characterDirectories)
        {
            var manifestPath = Path.Combine(characterDirectory, CharacterManifestFileName);
            if (File.Exists(manifestPath))
            {
                TryLoadCharacterDirectory(manifestPath);
            }
        }

        foreach (var list in ByCareer.Values)
        {
            list.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        }

        SkinLog.Info("Discovered " + ByKey.Count + " shared skin(s) for " + ByCareer.Count + " career(s)");
    }

    public static IReadOnlyList<SkinDefinition> GetForCareer(string careerId)
    {
        var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
        return !string.IsNullOrWhiteSpace(normalizedCareerId) && ByCareer.TryGetValue(normalizedCareerId, out var list)
            ? list
            : Array.Empty<SkinDefinition>();
    }

    public static SkinDefinition? Find(string careerId, string skinId)
    {
        if (string.IsNullOrWhiteSpace(careerId) || string.IsNullOrWhiteSpace(skinId))
        {
            return null;
        }

        return ByKey.TryGetValue(ResourceKey(careerId, skinId), out var skin) ? skin : null;
    }

    public static string ResourceKey(string careerId, string skinId)
    {
        return CareerConfigApi.NormalizeId(careerId).Trim().ToLowerInvariant()
               + "::"
               + (skinId ?? "").Trim().ToLowerInvariant();
    }

    private static void TryLoadCharacterDirectory(string characterManifestPath)
    {
        try
        {
            var characterManifest = JsonConvert.DeserializeObject<CharacterSkinManifest>(
                File.ReadAllText(characterManifestPath));
            if (characterManifest == null || !characterManifest.Enabled)
            {
                return;
            }

            var characterDirectory = Path.GetDirectoryName(characterManifestPath) ?? "";
            var targetCareerId = characterManifest.TargetCareerId?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(targetCareerId))
            {
                targetCareerId = new DirectoryInfo(characterDirectory).Name;
            }
            targetCareerId = CareerConfigApi.NormalizeId(targetCareerId);
            var characterDirectoryName = new DirectoryInfo(characterDirectory).Name;

            if (characterManifest.SchemaVersion != 2
                || string.IsNullOrWhiteSpace(targetCareerId)
                || !string.Equals(characterDirectoryName, targetCareerId, StringComparison.OrdinalIgnoreCase))
            {
                SkinLog.Warn("Ignored non-canonical shared character skin directory: " + characterManifestPath);
                return;
            }

            foreach (var skinDirectory in Directory.EnumerateDirectories(characterDirectory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var skinManifestPath = Path.Combine(skinDirectory, SkinManifestFileName);
                if (File.Exists(skinManifestPath))
                {
                    TryLoadSkin(skinManifestPath, targetCareerId);
                }
            }
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to load shared character skin directory " + characterManifestPath + ": " + ex.Message);
        }
    }

    private static void TryLoadSkin(string path, string inheritedCareerId)
    {
        try
        {
            var manifest = JsonConvert.DeserializeObject<SkinManifest>(File.ReadAllText(path));
            if (manifest == null || !manifest.Enabled)
            {
                return;
            }

            manifest.SkinId = manifest.SkinId?.Trim() ?? "";
            manifest.TargetCareerId = CareerConfigApi.NormalizeId(manifest.TargetCareerId);
            if (string.IsNullOrWhiteSpace(manifest.TargetCareerId))
            {
                manifest.TargetCareerId = inheritedCareerId;
            }
            else if (!string.Equals(manifest.TargetCareerId, inheritedCareerId, StringComparison.OrdinalIgnoreCase))
            {
                SkinLog.Warn("Ignored skin whose targetCareerId differs from its shared character folder: " + path);
                return;
            }

            if (manifest.SchemaVersion != 2
                || string.IsNullOrWhiteSpace(manifest.SkinId)
                || string.IsNullOrWhiteSpace(manifest.TargetCareerId))
            {
                SkinLog.Warn("Ignored invalid shared skin manifest: " + path);
                return;
            }

            var skinDirectoryName = new DirectoryInfo(Path.GetDirectoryName(path) ?? "").Name;
            if (!string.Equals(skinDirectoryName, manifest.SkinId, StringComparison.OrdinalIgnoreCase))
            {
                SkinLog.Warn("Ignored non-canonical shared skin directory: " + path);
                return;
            }

            var key = ResourceKey(manifest.TargetCareerId, manifest.SkinId);
            if (ByKey.ContainsKey(key))
            {
                SkinLog.Warn("Ignored duplicate shared skin identity " + key + " from " + path);
                return;
            }

            var definition = new SkinDefinition
            {
                SkinId = manifest.SkinId,
                TargetCareerId = manifest.TargetCareerId,
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.SkinId : manifest.Name.Trim(),
                Author = manifest.Author?.Trim() ?? "",
                ManifestPath = path,
                PreviewPath = SkinPaths.ResolveManifestAsset(path, manifest.Preview, false),
                Assets = ResolveAssets(path, manifest.Assets ?? new SkinAssets())
            };

            if (string.IsNullOrWhiteSpace(definition.Assets.CareerImage)
                && string.IsNullOrWhiteSpace(definition.Assets.Avatar)
                && string.IsNullOrWhiteSpace(definition.Assets.Character)
                && string.IsNullOrWhiteSpace(definition.Assets.DollIcon)
                && string.IsNullOrWhiteSpace(definition.Assets.ChoiceIcon)
                && string.IsNullOrWhiteSpace(definition.Assets.Animation))
            {
                SkinLog.Warn("Ignored shared skin with no valid assets: " + path);
                return;
            }

            ByKey.Add(key, definition);
            if (!ByCareer.TryGetValue(definition.TargetCareerId, out var skins))
            {
                skins = new List<SkinDefinition>();
                ByCareer.Add(definition.TargetCareerId, skins);
            }

            skins.Add(definition);
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to load shared skin manifest " + path + ": " + ex.Message);
        }
    }

    private static SkinAssets ResolveAssets(string manifestPath, SkinAssets assets)
    {
        return new SkinAssets
        {
            CareerImage = SkinPaths.ResolveManifestAsset(manifestPath, assets.CareerImage, false),
            Avatar = SkinPaths.ResolveManifestAsset(manifestPath, assets.Avatar, false),
            Character = SkinPaths.ResolveManifestAsset(manifestPath, assets.Character, false),
            DollIcon = SkinPaths.ResolveManifestAsset(manifestPath, assets.DollIcon, false),
            ChoiceIcon = SkinPaths.ResolveManifestAsset(manifestPath, assets.ChoiceIcon, false),
            Animation = SkinPaths.ResolveManifestAsset(manifestPath, assets.Animation, true)
        };
    }
}
