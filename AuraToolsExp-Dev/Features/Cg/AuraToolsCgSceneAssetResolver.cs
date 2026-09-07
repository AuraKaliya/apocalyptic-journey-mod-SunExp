using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Services;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.Cg;

public sealed class AuraToolsCgSceneAssetResolver
{
    public const string BackgroundAssetPrefix = "event.background.";
    public const string RolePortraitAssetPrefix = "event.role.";
    public const string ArtworkAssetPrefix = "event.art.";
    private static AuraToolsEventCgArtCatalog? catalog;
    private static string catalogRoot = "";
    private static bool catalogAttempted;

    public string ProviderId => AuraToolsIds.ModId + ".Cg.SceneAssets";
    public string OwnerModId => AuraToolsIds.ModId;
    public int Priority => 100;

    public AuraCgResolvedSceneAsset? ResolveSceneAsset(string assetId, string roleId, string roleVariantId)
    {
        var value = (assetId ?? "").Trim();
        if (value.StartsWith(BackgroundAssetPrefix, StringComparison.OrdinalIgnoreCase))
            return ResolveBackground(AuraToolsEventCgSceneIds.Normalize(value.Substring(BackgroundAssetPrefix.Length)));
        if (value.StartsWith(RolePortraitAssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = value.Substring(RolePortraitAssetPrefix.Length);
            var scene = AuraToolsEventCgSceneIds.All.FirstOrDefault(id =>
                string.Equals(id, suffix, StringComparison.OrdinalIgnoreCase)
                || suffix.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase));
            if (scene == null) return null;
            var portrait = ResolveRole(value, scene, roleId, roleVariantId);
            if (portrait != null) AuraToolsEventCgArtCatalog.ApplyMotionPreference(portrait.Artwork,
                AuraToolsConfigService.SkillCg.EventCg.GetScene(scene).MotionEnabled);
            return portrait;
        }
        if (value.StartsWith(ArtworkAssetPrefix, StringComparison.OrdinalIgnoreCase))
            return ResolveArtwork(value.Substring(ArtworkAssetPrefix.Length));
        return null;
    }

    public static string BackgroundAssetId(string sceneId) =>
        BackgroundAssetPrefix + AuraToolsEventCgSceneIds.Normalize(sceneId);
    public static string RoleAssetId(string sceneId) =>
        RolePortraitAssetPrefix + AuraToolsEventCgSceneIds.Normalize(sceneId);

    internal static IReadOnlyList<string> PreviewRoleIds => EnsureCatalog()?.PreviewRoles ?? new List<string>();
    internal static string CoverageSummary
    {
        get
        {
            var current = EnsureCatalog();
            return current == null ? "主题素材未就绪"
                : current.Characters.Count + " 套形象 · " + current.PoseCount + " 张事件姿势 · " + current.Themes.Count + " 个主题";
        }
    }

    internal static void ReloadCatalog()
    {
        catalogAttempted = false;
        catalog = null;
        EnsureCatalog();
    }

    private static AuraToolsEventCgArtCatalog? EnsureCatalog()
    {
        var directory = Path.Combine(AuraToolsPaths.PackageDirectory, "SharedResources", "EventCg");
        if (catalogAttempted && string.Equals(directory, catalogRoot, StringComparison.OrdinalIgnoreCase)) return catalog;
        catalogRoot = directory;
        catalogAttempted = true;
        try
        {
            catalog = AuraToolsEventCgArtCatalog.Parse(File.ReadAllText(Path.Combine(directory, "event-cg.art.json")));
            foreach (var asset in catalog.Assets.Values)
                if (!File.Exists(AuraToolsEventCgArtCatalog.ResolveAssetPath(directory, asset.Path)))
                    throw new FileNotFoundException("Packaged event CG artwork is missing: " + asset.Path);
        }
        catch (Exception error)
        {
            catalog = null;
            AuraToolsLog.Warn("[EventCG] artwork catalog unavailable: " + error.Message);
        }
        return catalog;
    }

    private static AuraCgResolvedSceneAsset? ResolveBackground(string sceneId)
    {
        var current = EnsureCatalog();
        if (current == null || !current.Themes.TryGetValue(sceneId, out var theme)) return null;
        var background = ResolveArtwork(theme.Background);
        if (background == null) return null;
        var custom = AuraToolsConfigService.SkillCg.EventCg.GetScene(sceneId).BackgroundResource;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            var replacement = ResolvePortraitResource(custom, BackgroundAssetId(sceneId) + "." + StableHash(custom), "");
            if (replacement != null) background = replacement;
            else AuraToolsLog.Warn("[EventCG] custom background unavailable; using the registered theme.");
        }
        background.Artwork = new AuraCgSceneArtwork
        {
            DarkTitle = string.IsNullOrWhiteSpace(custom) && theme.DarkTitle,
            CameraPush = theme.CameraPush,
            Layers = theme.Layers.Select(ToSharedLayer).ToList()
        };
        AuraToolsEventCgArtCatalog.ApplyMotionPreference(background.Artwork,
            AuraToolsConfigService.SkillCg.EventCg.GetScene(sceneId).MotionEnabled);
        return background;
    }

    private static AuraCgResolvedSceneAsset? ResolveRole(string assetId, string sceneId, string roleId, string roleVariantId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole)) return null;
        var current = EnsureCatalog();
        var character = current?.FindCharacter(normalizedRole, roleVariantId);
        var displayName = RoleCatalog.GetDisplayName(normalizedRole);
        if (character == null && !string.IsNullOrWhiteSpace(roleVariantId))
        {
            var skin = SkinRegistry.FindQualified(roleVariantId, effectiveOnly: false);
            foreach (var resource in new[] { skin?.Assets?.Character, skin?.Assets?.CareerImage })
            {
                if (string.IsNullOrWhiteSpace(resource)) continue;
                var portrait = ResolvePortraitResource(resource!, assetId + "." + StableHash(normalizedRole + "|" + roleVariantId + "|" + resource), displayName);
                if (portrait != null) return portrait;
            }
        }
        character ??= current?.FindCharacter(normalizedRole);
        if (current != null && character != null)
        {
            var portrait = ResolveArtwork(current.ResolvePose(character, sceneId));
            if (portrait != null) { portrait.DisplayName = displayName; return portrait; }
        }
        if (!CareerConfigApi.TryCreate(normalizedRole, out var career) || career == null) return null;
        foreach (var field in new[] { "Character", "CareerImage" })
        {
            var path = ReadData(career, field);
            if (string.IsNullOrWhiteSpace(path)) continue;
            var portrait = ResolvePortraitResource(path, assetId + "." + StableHash(normalizedRole + "|" + path), displayName);
            if (portrait != null) return portrait;
        }
        return null;
    }

    private static AuraCgResolvedSceneAsset? ResolveArtwork(string id)
    {
        var current = EnsureCatalog();
        if (current == null || !current.Assets.TryGetValue(id, out var asset)) return null;
        var path = AuraToolsEventCgArtCatalog.ResolveAssetPath(catalogRoot, asset.Path);
        if (!File.Exists(path)) return null;
        return new AuraCgResolvedSceneAsset
        {
            OwnerModId = AuraToolsIds.ModId,
            AssetId = ArtworkAssetPrefix + id + "." + StableHash(current.Revision),
            ImagePath = path, MediaType = SkillCgMediaTypes.Image, Loop = false, FrameSeconds = 1f,
            Artwork = new AuraCgSceneArtwork { Portrait = asset.Portrait, Layers = asset.Layers.Select(ToSharedLayer).ToList() }
        };
    }

    private static AuraCgSceneArtLayerSpec ToSharedLayer(AuraToolsEventCgCompanionArt layer) => new()
    {
        Asset = new AuraCgSceneAssetReference { OwnerModId = AuraToolsIds.ModId, AssetId = ArtworkAssetPrefix + layer.Asset },
        Foreground = layer.Foreground, Required = layer.Required, Opacity = layer.Opacity,
        MotionX = layer.MotionX, MotionY = layer.MotionY, Pulse = layer.Pulse
    };

    private static AuraCgResolvedSceneAsset? ResolvePortraitResource(string resource, string assetId, string name)
    {
        try
        {
            var path = File.Exists(resource) ? Path.GetFullPath(resource) : AuraToolsConfigService.ResolveConfiguredPath(resource);
            if (File.Exists(path))
                return new AuraCgResolvedSceneAsset
                {
                    OwnerModId = AuraToolsIds.ModId, AssetId = assetId, DisplayName = name,
                    ImagePath = path, MediaType = SkillCgMediaTypes.Image, Loop = false, FrameSeconds = 1f
                };
            var sprite = AuraToolsResourceCache.Load<Sprite>(resource, true);
            var ownsSprite = false;
            if (sprite == null)
            {
                var texture = AuraToolsResourceCache.Load<Texture2D>(resource, true);
                if (texture == null) return null;
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                ownsSprite = true;
            }
            return new AuraCgResolvedSceneAsset
            {
                OwnerModId = AuraToolsIds.ModId, AssetId = assetId, DisplayName = name,
                DirectSprites = new List<Sprite> { sprite }, OwnsDirectSprites = ownsSprite,
                MediaType = SkillCgMediaTypes.Image, Loop = false, FrameSeconds = 1f
            };
        }
        catch (Exception error)
        {
            AuraToolsLog.Warn("[EventCG] portrait unavailable: " + name + ", " + error.Message);
            return null;
        }
    }

    private static string ReadData(DataConfig config, string key) =>
        config.data != null && config.data.TryGetValue(key, out var value) ? value ?? "" : "";

    private static string StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in (value ?? "").ToLowerInvariant()) { hash ^= character; hash *= 16777619u; }
            return hash.ToString("x8");
        }
    }
}
