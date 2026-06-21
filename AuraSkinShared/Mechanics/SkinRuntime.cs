using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Models;
using AuraSkin.Shared.Services;
using UnityEngine;

namespace AuraSkin.Shared.Mechanics;

public static class SkinRuntime
{
    private static readonly string[] AnimationStates =
    {
        "Idle", "Attack", "Hit", "Buff", "Debuff", "Skill", "Special", "Special1", "Special2", "Defend"
    };

    private static readonly Dictionary<string, string> AppliedAnimationSkin = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize()
    {
        Reload();
    }

    public static void Reload()
    {
        ResourceRedirectApi.RestoreAll();
        AppliedAnimationSkin.Clear();
        SkinRegistry.Reload();
        SkinSelectionStore.Load();
    }

    public static IReadOnlyList<SkinDefinition> GetSkins(string careerId) => SkinRegistry.GetForCareer(careerId);

    public static SkinDefinition? GetSelectedSkin(string careerId)
    {
        return SkinRegistry.Find(careerId, SkinSelectionStore.Get(careerId));
    }

    public static string GetSelectedSkinId(string careerId)
    {
        return GetSelectedSkin(careerId)?.SkinId ?? "";
    }

    public static void Select(DataConfig career, string skinId)
    {
        var careerId = CareerId(career);
        if (string.IsNullOrWhiteSpace(careerId))
        {
            return;
        }

        var skin = SkinRegistry.Find(careerId, skinId);
        SkinSelectionStore.Set(careerId, skin?.SkinId ?? "");
        ApplyAnimation(career, true);
    }

    public static void EnsureAnimation(DataConfig? career)
    {
        if (career != null)
        {
            ApplyAnimation(career, false);
        }
    }

    public static void ApplyAllKnownSelections()
    {
        var careerIds = SkinRegistry.CareerIds.Concat(SkinSelectionStore.CareerIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var careerId in careerIds)
        {
            var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
            if (!CareerConfigApi.TryCreate(careerId, out var career) || career == null)
            {
                if (!string.IsNullOrWhiteSpace(normalizedCareerId))
                {
                    SkinLog.Warn("Could not apply saved skin for missing career " + normalizedCareerId);
                }

                continue;
            }

            try
            {
                ApplyAnimation(career, false);
            }
            catch (Exception ex)
            {
                SkinLog.Warn("Could not apply saved skin for career " + normalizedCareerId + ": " + ex.Message);
            }
        }
    }

    public static Sprite? LoadSprite(DataConfig? career, string field)
    {
        if (career?.data == null || !career.data.TryGetValue(field, out var defaultPath))
        {
            return null;
        }

        var resourcePath = ResolveResourcePath(career, field, defaultPath);
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return null;
        }

        try
        {
            return ResourceLoader.Load<Sprite>(resourcePath, true);
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to load " + field + " for " + CareerId(career) + ": " + ex.Message);
            return null;
        }
    }

    public static Sprite? LoadPreview(DataConfig? career)
    {
        if (career == null)
        {
            return null;
        }

        var skin = GetSelectedSkin(CareerId(career));
        var path = skin?.PreviewPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return LoadSprite(career, "CareerImage");
        }

        try
        {
            return ResourceLoader.Load<Sprite>(SkinPaths.ToRawResourcePath(path ?? ""), true);
        }
        catch
        {
            return LoadSprite(career, "CareerImage");
        }
    }

    public static string CareerId(DataConfig? career)
    {
        return career?.data != null && career.data.TryGetValue("Id", out var id)
            ? CareerConfigApi.NormalizeId(id)
            : "";
    }

    private static string ResolveResourcePath(DataConfig career, string field, string defaultPath)
    {
        var skin = GetSelectedSkin(CareerId(career));
        var assetPath = skin?.Assets.Get(field) ?? "";
        return string.IsNullOrWhiteSpace(assetPath) ? defaultPath : SkinPaths.ToRawResourcePath(assetPath);
    }

    private static void ApplyAnimation(DataConfig career, bool force)
    {
        var careerId = CareerId(career);
        if (string.IsNullOrWhiteSpace(careerId) || career.data == null)
        {
            return;
        }

        var skin = GetSelectedSkin(careerId);
        var selectedId = skin?.SkinId ?? "";
        if (!force
            && AppliedAnimationSkin.TryGetValue(careerId, out var applied)
            && string.Equals(applied, selectedId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ResourceRedirectApi.RestoreCareer(careerId);
        AppliedAnimationSkin[careerId] = selectedId;
        if (skin == null
            || string.IsNullOrWhiteSpace(skin.Assets.Animation)
            || !career.data.TryGetValue("Animation", out var defaultAnimation)
            || string.IsNullOrWhiteSpace(defaultAnimation))
        {
            return;
        }

        var redirectedCount = 0;
        foreach (var state in AnimationStates)
        {
            var replacementDirectory = Path.Combine(skin.Assets.Animation, state);
            if (!Directory.Exists(replacementDirectory))
            {
                continue;
            }

            if (ResourceRedirectApi.TryRedirect(
                    careerId,
                    defaultAnimation.TrimEnd('/', '\\') + "/" + state,
                    SkinPaths.ToRawResourcePath(replacementDirectory)))
            {
                redirectedCount++;
            }
        }

        SkinLog.Info("Applied skin " + skin.SkinId + " to " + careerId + " with " + redirectedCount + " animation state(s)");
    }
}
