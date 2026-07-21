using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Visual;

public static class SpiritCardFaceRuntime
{
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.Ordinal);

    public static void Initialize()
    {
        TerriasCardPresentationRouter.Register("SpiritCardFace", new TerriasCardPresentationSubscription { Apply = Apply });
    }

    private static void Apply(TerriasCardPresentationContext context)
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
            TerriasPerformanceCounters.Record("Spirit.CardFace.CacheHit");
            return cached;
        }

        var started = TerriasPerformanceCounters.Timestamp();
        var found = false;
        try
        {
            var sprites = TerriasResourceCache.LoadAll<Sprite>(path, "spirit-card-face");
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
            TerriasLog.Debug("[SpiritCardFace] load failed for " + path + ": " + ex.Message);
            return null;
        }
        finally
        {
            TerriasPerformanceCounters.RecordHotspot(
                "Spirit.CardFace.Load",
                started,
                "found=" + found + ", path=" + path,
                logFirstSample: true);
        }
    }
}
