using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpResourcePreloader
{
    public static void Initialize(ModConfig modConfig)
    {
        SunExpFrameScheduler.RunOnceNextFrame("SunExpResourcePreloader.Warmup", WarmupCoreVisuals);
    }

    private static void WarmupCoreVisuals()
    {
        if (SunExpPerformanceSettings.Quality == SunExpPerformanceQuality.UltraLow)
        {
            return;
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            SunExpResourceCache.Preload<Texture2D>(CoreTexturePaths(), "visual");
            SunExpResourceCache.Preload<Sprite>(CoreSpritePaths(), "ui");
            SunExpResourceCache.Preload<Sprite>(
                PolymorphRoleRegistry.CardFacePaths(12),
                SunExpIds.PolymorphSourceResourceCategory);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[ResourcePreloader] warmup skipped: " + ex.Message);
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("ResourcePreloader.WarmupCoreVisuals", start);
        }
    }

    private static IEnumerable<string> CoreTexturePaths()
    {
        var eventCard = VisualRegistry.TexturePath("solar_memory.event_map_card") ?? "";
        if (!string.IsNullOrWhiteSpace(eventCard))
        {
            yield return eventCard;
        }

        foreach (var spec in VisualRegistry.MapNodeArtSpecs())
        {
            if (!string.IsNullOrWhiteSpace(spec.TexturePath))
            {
                yield return spec.TexturePath;
            }
        }

        foreach (var effectId in new[]
                 {
                     SunExpIds.CardFaceFoilHoloVisualEffectId,
                     SunExpIds.CardFaceStardustVisualEffectId,
                     "sunexp.wuna.orbit_fire.core.back",
                     "sunexp.wuna.orbit_fire.core.front",
                     "sunexp.wuna.orbit_fire.back",
                     "sunexp.wuna.orbit_fire.front"
                 })
        {
            var effect = VisualRegistry.Effect(effectId);
            if (effect?.Textures == null)
            {
                continue;
            }

            foreach (var path in effect.Textures.Values)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> CoreSpritePaths()
    {
        var modeEntry = VisualRegistry.ModeEntry("solar_memory");
        var normalTitleSprite = modeEntry?.NormalTitleSprite ?? "";
        if (!string.IsNullOrWhiteSpace(normalTitleSprite))
        {
            yield return normalTitleSprite;
        }

        var highlightedTitleSprite = modeEntry?.HighlightedTitleSprite ?? "";
        if (!string.IsNullOrWhiteSpace(highlightedTitleSprite))
        {
            yield return highlightedTitleSprite;
        }
    }
}
