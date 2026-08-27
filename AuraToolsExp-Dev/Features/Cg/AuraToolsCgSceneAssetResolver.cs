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
    public const string RoleIdleAssetId = "role.idle";

    public string ProviderId => AuraToolsIds.ModId + ".Cg.SceneAssets";

    public string OwnerModId => AuraToolsIds.ModId;

    public int Priority => 100;

    public AuraCgResolvedSceneAsset? ResolveSceneAsset(
        string assetId,
        string roleId,
        string roleVariantId)
    {
        if (TryResolveBackgroundScene(assetId, out var sceneId))
        {
            return ResolveBackground(assetId, sceneId);
        }

        return string.Equals(assetId, RoleIdleAssetId, StringComparison.OrdinalIgnoreCase)
            ? ResolveRoleIdle(assetId, roleId, roleVariantId)
            : null;
    }

    public static string BackgroundAssetId(string sceneId)
    {
        return BackgroundAssetPrefix + AuraToolsEventCgSceneIds.Normalize(sceneId);
    }

    private static bool TryResolveBackgroundScene(string assetId, out string sceneId)
    {
        var value = (assetId ?? "").Trim();
        if (!value.StartsWith(BackgroundAssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            sceneId = "";
            return false;
        }

        sceneId = AuraToolsEventCgSceneIds.Normalize(value.Substring(BackgroundAssetPrefix.Length));
        return true;
    }

    private static AuraCgResolvedSceneAsset? ResolveBackground(string assetId, string sceneId)
    {
        var resource = AuraToolsConfigService.SkillCg.EventCg.GetScene(sceneId).EffectiveBackgroundResource;
        var resolvedAssetId = assetId + "." + StableHash(resource);
        try
        {
            var sprite = AuraToolsResourceCache.Load<Sprite>(resource, true);
            if (sprite != null)
            {
                return Direct(resolvedAssetId, new[] { sprite }, ownsSprites: false, frameSeconds: 1f, loop: false);
            }
        }
        catch
        {
        }

        var path = AuraToolsConfigService.ResolveConfiguredPath(resource);
        return File.Exists(path)
            ? new AuraCgResolvedSceneAsset
            {
                OwnerModId = AuraToolsIds.ModId,
                AssetId = resolvedAssetId,
                ImagePath = path,
                MediaType = SkillCgMediaTypes.Image,
                Loop = false
            }
            : null;
    }

    private static AuraCgResolvedSceneAsset? ResolveRoleIdle(
        string assetId,
        string roleId,
        string roleVariantId)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            return null;
        }
        var resolvedAssetId = assetId + "." + CacheSegment(normalizedRole);

        var skin = SkinRegistry.FindQualified(roleVariantId, effectiveOnly: false);
        var animationDirectory = skin?.Assets?.Animation ?? "";
        if (!string.IsNullOrWhiteSpace(animationDirectory))
        {
            var skinAssetId = resolvedAssetId
                              + "." + CacheSegment(roleVariantId)
                              + "." + StableHash(animationDirectory);
            var idleDirectory = Path.Combine(animationDirectory, "Idle");
            if (Directory.Exists(idleDirectory)
                && Directory.EnumerateFiles(idleDirectory, "*.png", SearchOption.TopDirectoryOnly).Any())
            {
                var spec = AuraToolsCgAnimationSpec.FromJson(
                    ReadFile(Path.Combine(idleDirectory, "config.json")),
                    Directory.EnumerateFiles(idleDirectory, "*.png", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileNameWithoutExtension));
                return new AuraCgResolvedSceneAsset
                {
                    OwnerModId = AuraToolsIds.ModId,
                    AssetId = skinAssetId,
                    ImagePath = idleDirectory,
                    MediaType = SkillCgMediaTypes.Sequence,
                    FrameSeconds = spec.FrameSeconds,
                    Loop = spec.Loop
                };
            }
        }

        return ResolveRegisteredCareerIdle(resolvedAssetId + ".career", normalizedRole);
    }

    private static AuraCgResolvedSceneAsset? ResolveRegisteredCareerIdle(
        string assetId,
        string roleId)
    {
        if (!CareerConfigApi.TryCreate(roleId, out var career) || career == null)
        {
            return null;
        }

        var animation = ReadData(career, "Animation");
        if (string.IsNullOrWhiteSpace(animation))
        {
            return null;
        }

        var idleDirectory = animation.TrimEnd('/', '\\') + "/Idle";
        var textures = AuraToolsResourceCache.LoadAll<Texture2D>(idleDirectory)
            .Where(texture => texture != null)
            .ToList();
        if (textures.Count == 0)
        {
            return null;
        }

        var byName = textures.ToDictionary(texture => texture.name, StringComparer.OrdinalIgnoreCase);
        var spec = AuraToolsCgAnimationSpec.FromJson(
            LoadResourceConfig(idleDirectory),
            byName.Keys);
        var sprites = new List<Sprite>();
        foreach (var name in spec.OrderedFrameNames)
        {
            if (!byName.TryGetValue(name, out var texture)) continue;
            sprites.Add(Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f));
        }

        return sprites.Count == 0
            ? null
            : Direct(
                assetId,
                sprites,
                ownsSprites: true,
                frameSeconds: spec.FrameSeconds,
                loop: spec.Loop);
    }

    private static AuraCgResolvedSceneAsset Direct(
        string assetId,
        IEnumerable<Sprite> sprites,
        bool ownsSprites,
        float frameSeconds,
        bool loop)
    {
        return new AuraCgResolvedSceneAsset
        {
            OwnerModId = AuraToolsIds.ModId,
            AssetId = assetId,
            MediaType = SkillCgMediaTypes.Sequence,
            FrameSeconds = frameSeconds,
            Loop = loop,
            DirectSprites = sprites.Where(sprite => sprite != null).ToList(),
            OwnsDirectSprites = ownsSprites
        };
    }

    private static string ReadData(DataConfig dataConfig, string key)
    {
        try
        {
            return dataConfig.data != null && dataConfig.data.TryGetValue(key, out var value)
                ? value ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string LoadResourceConfig(string idleDirectory)
    {
        foreach (var path in new[] { idleDirectory + "/config", idleDirectory + "/config.json" })
        {
            try
            {
                var asset = AuraToolsResourceCache.Load<TextAsset>(path, true);
                if (asset != null && !string.IsNullOrWhiteSpace(asset.text)) return asset.text;
            }
            catch
            {
            }
        }

        return "";
    }

    private static string ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch
        {
            return "";
        }
    }

    private static string CacheSegment(string value)
    {
        var characters = (value ?? "")
            .Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? char.ToLowerInvariant(character)
                : '_')
            .ToArray();
        var normalized = new string(characters).Trim('_');
        return normalized.Length == 0 ? "role" : normalized;
    }

    private static string StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in (value ?? "").ToLowerInvariant())
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash.ToString("x8");
        }
    }
}
