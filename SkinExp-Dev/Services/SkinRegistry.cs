using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SkinExp.Dll.GameApi;
using SkinExp.Dll.Infrastructure;
using SkinExp.Dll.Models;

namespace SkinExp.Dll.Services;

public static class SkinRegistry
{
    private const string CharacterManifestFileName = "character.json";
    private const string FolderSkinManifestFileName = "skin.json";
    private static readonly Dictionary<string, SkinDefinition> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<SkinDefinition>> ByCareer = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> CareerIds => ByCareer.Keys;

    public static void Reload()
    {
        ById.Clear();
        ByCareer.Clear();

        var root = SkinPaths.ModsRootDirectory;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            SkinLog.Warn("Skin package scan skipped: mods root does not exist");
            return;
        }

        string[] legacyManifests;
        try
        {
            legacyManifests = Directory.EnumerateFiles(root, "*.skin.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            SkinLog.Error("Failed to enumerate skin manifests", ex);
            return;
        }

        foreach (var path in legacyManifests)
        {
            TryLoadSkin(path, "", 1);
        }

        foreach (var path in DiscoverCharacterManifests(root))
        {
            TryLoadCharacterDirectory(path);
        }

        foreach (var list in ByCareer.Values)
        {
            list.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        }

        SkinLog.Info("Discovered " + ById.Count + " skin(s) for " + ByCareer.Count + " career(s)");
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

        var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
        return ById.TryGetValue(skinId, out var skin)
               && string.Equals(skin.TargetCareerId, normalizedCareerId, StringComparison.OrdinalIgnoreCase)
            ? skin
            : null;
    }

    private static IEnumerable<string> DiscoverCharacterManifests(string root)
    {
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(root, CharacterManifestFileName, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to enumerate character skin manifests: " + ex.Message);
            return Array.Empty<string>();
        }

        return candidates.Where(path =>
        {
            var characterDirectory = Directory.GetParent(path);
            return characterDirectory?.Parent != null
                   && string.Equals(characterDirectory.Parent.Name, "Skins", StringComparison.OrdinalIgnoreCase);
        });
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

            if (characterManifest.SchemaVersion != 2 || string.IsNullOrWhiteSpace(targetCareerId))
            {
                SkinLog.Warn("Ignored invalid character skin manifest: " + characterManifestPath);
                return;
            }

            foreach (var skinDirectory in Directory.EnumerateDirectories(characterDirectory)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var skinManifestPath = Path.Combine(skinDirectory, FolderSkinManifestFileName);
                if (File.Exists(skinManifestPath))
                {
                    TryLoadSkin(skinManifestPath, targetCareerId, 2);
                }
            }
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to load character skin directory " + characterManifestPath + ": " + ex.Message);
        }
    }

    private static void TryLoadSkin(string path, string inheritedCareerId, int expectedSchemaVersion)
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
            else if (!string.IsNullOrWhiteSpace(inheritedCareerId)
                     && !string.Equals(manifest.TargetCareerId, inheritedCareerId, StringComparison.OrdinalIgnoreCase))
            {
                SkinLog.Warn("Ignored skin whose targetCareerId differs from its character folder: " + path);
                return;
            }

            if (manifest.SchemaVersion != expectedSchemaVersion
                || string.IsNullOrWhiteSpace(manifest.SkinId)
                || string.IsNullOrWhiteSpace(manifest.TargetCareerId))
            {
                SkinLog.Warn("Ignored invalid skin manifest: " + path);
                return;
            }

            if (ById.ContainsKey(manifest.SkinId))
            {
                SkinLog.Warn("Ignored duplicate skin id " + manifest.SkinId + " from " + path);
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
                SkinLog.Warn("Ignored skin with no valid assets: " + path);
                return;
            }

            ById.Add(definition.SkinId, definition);
            if (!ByCareer.TryGetValue(definition.TargetCareerId, out var skins))
            {
                skins = new List<SkinDefinition>();
                ByCareer.Add(definition.TargetCareerId, skins);
            }

            skins.Add(definition);
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to load skin manifest " + path + ": " + ex.Message);
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
