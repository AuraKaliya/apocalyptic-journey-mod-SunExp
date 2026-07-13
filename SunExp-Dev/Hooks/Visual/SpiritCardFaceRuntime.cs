using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Visual;

public static class SpiritCardFaceRuntime
{
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.Ordinal);

    public static void Initialize()
    {
        SunExpCardPresentationRouter.Register("SpiritCardFace", new SunExpCardPresentationSubscription { Apply = Apply });
    }

    private static void Apply(SunExpCardPresentationContext context)
    {
        if (!SpiritCardFactory.IsSpiritCard(context.Config))
        {
            return;
        }

        var snapshot = SpiritCardFactory.Read(context.Config);
        var root = CardPresentationRootResolver.FindCardVisualRoot(context.Root);
        var icon = root?.Find("Front/icon");
        if (snapshot == null || icon == null)
        {
            return;
        }

        var sprite = Resolve(snapshot.DictPath);
        if (sprite == null)
        {
            return;
        }

        var image = icon.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = Color.white;
        }

        var material = icon.GetComponent<MeshRenderer>()?.material;
        if (material != null)
        {
            material.mainTexture = sprite.texture;
        }
    }

    private static Sprite? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out var cached) && cached != null)
        {
            SunExpPerformanceCounters.Record("Spirit.CardFace.CacheHit");
            return cached;
        }

        var started = SunExpPerformanceCounters.Timestamp();
        var found = false;
        try
        {
            var sprites = SunExpResourceCache.LoadAll<Sprite>(path, "spirit-card-face");
            var sprite = sprites != null && sprites.Length > 0 ? sprites[0] : null;
            if (sprite != null)
            {
                Cache[path] = sprite;
                found = true;
            }

            return sprite;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[SpiritCardFace] load failed for " + path + ": " + ex.Message);
            return null;
        }
        finally
        {
            SunExpPerformanceCounters.RecordHotspot(
                "Spirit.CardFace.Load",
                started,
                "found=" + found + ", path=" + path,
                logFirstSample: true);
        }
    }
}
