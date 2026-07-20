using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Mechanics;
using AuraSkin.Shared.Models;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;

internal sealed class DamageSettlementCgIdleClip
{
    public string Source { get; set; } = "";

    public float FrameSeconds { get; set; } = DamageSettlementCgAnimationSpec.DefaultFrameSeconds;

    public bool Loop { get; set; } = true;

    public List<Sprite> Frames { get; set; } = new();
}

internal static class DamageSettlementCgIdleResolver
{
    public static DamageSettlementCgIdleClip? Resolve(DamageSettlementCgEntry entry)
    {
        if (entry == null)
        {
            return null;
        }

        var roleId = RoleCatalog.NormalizeRoleId(entry.RoleId);
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return null;
        }

        try
        {
            var skinClip = TryResolveSelectedSkin(roleId, entry.PlayerId, entry.InstanceId);
            if (skinClip != null)
            {
                return skinClip;
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SettlementCG] selected skin idle resolve failed: role="
                              + roleId + ", error=" + ex.Message);
        }

        try
        {
            return TryResolveCareerAnimation(roleId);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SettlementCG] default idle resolve failed: role="
                              + roleId + ", error=" + ex.Message);
            return null;
        }
    }

    private static DamageSettlementCgIdleClip? TryResolveSelectedSkin(
        string roleId,
        string playerId,
        string instanceId)
    {
        var skin = SelectedSkin(roleId, playerId, instanceId);
        var animationDirectory = skin?.Assets?.Animation ?? "";
        if (string.IsNullOrWhiteSpace(animationDirectory))
        {
            return null;
        }

        var idleDirectory = Path.Combine(animationDirectory, "Idle");
        if (!Directory.Exists(idleDirectory))
        {
            return null;
        }

        var frameFiles = Directory.EnumerateFiles(idleDirectory, "*.png", SearchOption.TopDirectoryOnly)
            .ToList();
        if (frameFiles.Count == 0)
        {
            return null;
        }

        var orderedNames = DamageSettlementCgAnimationSpec.OrderFrameNames(
            frameFiles.Select(Path.GetFileNameWithoutExtension));
        var framesByName = frameFiles.ToDictionary(
            file => Path.GetFileNameWithoutExtension(file),
            StringComparer.OrdinalIgnoreCase);
        var configPath = Path.Combine(idleDirectory, "config.json");
        var configJson = File.Exists(configPath) ? File.ReadAllText(configPath) : "";
        var spec = DamageSettlementCgAnimationSpec.FromJson(configJson, orderedNames);
        var frames = new List<Sprite>();
        foreach (var name in spec.OrderedFrameNames)
        {
            if (!framesByName.TryGetValue(name, out var file))
            {
                continue;
            }

            var sprite = LoadSpriteFromFile(file);
            if (sprite != null)
            {
                frames.Add(sprite);
            }
        }

        return frames.Count == 0
            ? null
            : new DamageSettlementCgIdleClip
            {
                Source = SkinPaths.ToRawResourcePath(idleDirectory),
                FrameSeconds = spec.FrameSeconds,
                Loop = spec.Loop,
                Frames = frames
            };
    }

    private static SkinDefinition? SelectedSkin(string roleId, string playerId, string instanceId)
    {
        var skin = SkinRuntime.GetSelectedSkin(roleId, playerId);
        if (skin != null)
        {
            return skin;
        }

        if (!string.Equals(playerId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            skin = SkinRuntime.GetSelectedSkin(roleId, instanceId);
            if (skin != null)
            {
                return skin;
            }
        }

        return SkinRuntime.GetSelectedSkin(roleId);
    }

    internal static DamageSettlementCgIdleClip? TryResolveCareerAnimation(string roleId)
    {
        if (!CareerConfigApi.TryCreate(roleId, out var career) || career == null)
        {
            AuraToolsLog.Warn("[SettlementCG] registered career definition unavailable for role=" + roleId + ".");
            return null;
        }

        var animation = ReadData(career, "Animation");
        if (string.IsNullOrWhiteSpace(animation))
        {
            AuraToolsLog.Warn("[SettlementCG] no idle animation path resolved for role=" + roleId + ".");
            return null;
        }

        var idleDirectory = animation.TrimEnd('/', '\\') + "/Idle";
        var textures = AuraToolsResourceCache.LoadAll<Texture2D>(idleDirectory);
        var valid = textures.Where(texture => texture != null).ToList();
        if (valid.Count == 0)
        {
            AuraToolsLog.Warn("[SettlementCG] no idle animation frames resolved for role="
                              + roleId + ", path=" + idleDirectory + ".");
            return null;
        }

        var orderedNames = DamageSettlementCgAnimationSpec.OrderFrameNames(valid.Select(texture => texture.name));
        var byName = valid.ToDictionary(texture => texture.name, StringComparer.OrdinalIgnoreCase);
        var spec = DamageSettlementCgAnimationSpec.FromJson(LoadResourceConfig(idleDirectory), orderedNames);
        var frames = new List<Sprite>();
        foreach (var name in spec.OrderedFrameNames)
        {
            if (!byName.TryGetValue(name, out var texture))
            {
                continue;
            }

            frames.Add(CreateSprite(texture));
        }

        return frames.Count == 0
            ? null
            : new DamageSettlementCgIdleClip
            {
                Source = idleDirectory,
                FrameSeconds = spec.FrameSeconds,
                Loop = spec.Loop,
                Frames = frames
            };
    }

    private static string LoadResourceConfig(string idleDirectory)
    {
        foreach (var path in new[] { idleDirectory + "/config", idleDirectory + "/config.json" })
        {
            try
            {
                var asset = AuraToolsResourceCache.Load<TextAsset>(path, true);
                if (asset != null && !string.IsNullOrWhiteSpace(asset.text))
                {
                    return asset.text;
                }
            }
            catch
            {
            }
        }

        return "";
    }

    internal static string ReadData(DataConfig? dataConfig, string key)
    {
        try
        {
            return dataConfig?.data != null && dataConfig.data.TryGetValue(key, out var value)
                ? value ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    internal static Sprite? LoadSpriteFromFile(string file)
    {
        try
        {
            var texture = LoadTextureFromFile(file);
            if (texture == null)
            {
                return null;
            }

            texture.name = Path.GetFileNameWithoutExtension(file);
            return CreateSprite(texture);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SettlementCG] failed to load idle frame: " + file + ", error=" + ex.Message);
            return null;
        }
    }

    private static Texture2D? LoadTextureFromFile(string file)
    {
        var rawResourcePath = SkinPaths.ToRawResourcePath(file);
        try
        {
            var texture = AuraToolsResourceCache.Load<Texture2D>(rawResourcePath, true);
            if (texture != null)
            {
                return texture;
            }
        }
        catch
        {
        }

        Texture2D? loaded = null;
        try
        {
            loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var method = typeof(ImageConversion).GetMethods()
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, "LoadImage", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = candidate.GetParameters();
                    return parameters.Length == 3
                           && parameters[0].ParameterType == typeof(Texture2D)
                           && parameters[1].ParameterType == typeof(byte[])
                           && parameters[2].ParameterType == typeof(bool);
                });
            if (method?.Invoke(null, new object[] { loaded, File.ReadAllBytes(file), false }) is true)
            {
                return loaded;
            }
        }
        catch
        {
        }

        if (loaded != null)
        {
            UnityEngine.Object.Destroy(loaded);
        }

        return null;
    }

    internal static Sprite CreateSprite(Texture2D texture)
    {
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }
}
