using System;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks.Visual;

public static class PolymorphCardFaceRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        SunExpCardPresentationRouter.Register("PolymorphCardFace", new SunExpCardPresentationSubscription
        {
            Apply = ApplyPresentation
        });
        SunExpLog.Info("Polymorph card face runtime initialized");
    }

    public static void ReapplyActiveCombatCards(string source)
    {
        SunExpCardPresentationRouter.RequestActiveCombatCardsReapply(source);
    }

    private static void ApplyPresentation(SunExpCardPresentationContext context)
    {
        ApplySafely(context.Root, context.Config, context.Source);
    }

    private static void ApplySafely(Transform? root, IDataConfig? config, string source, bool scheduleDeferred = true)
    {
        if (config == null || !PolymorphCardFaceCache.IsPolymorphRoleCard(config))
        {
            return;
        }

        if (Apply(root, config, source))
        {
            SunExpPerformanceCounters.Record("Polymorph.CardFaceApply");
        }

        if (scheduleDeferred && root != null)
        {
            var key = "PolymorphCardFaceRuntime.Deferred." + source + "." + root.GetInstanceID();
            SunExpFrameScheduler.RunOnceNextFrame(key, () => ApplySafely(root, config, source + ".deferred", scheduleDeferred: false));
        }
    }

    private static bool Apply(Transform? root, IDataConfig? config, string source)
    {
        var visualRoot = CardPresentationRootResolver.FindCardVisualRoot(root);
        if (visualRoot == null)
        {
            return false;
        }

        var asset = PolymorphCardFaceCache.GetOrCreate(config);
        if (asset == null)
        {
            return false;
        }

        var icon = visualRoot.Find("Front/icon");
        if (icon == null)
        {
            return false;
        }

        var changed = false;
        var image = icon.GetComponent<Image>();
        if (image != null)
        {
            changed = !ReferenceEquals(image.sprite, asset.Sprite);
            image.sprite = asset.Sprite;
            image.preserveAspect = false;
            image.color = Color.white;
        }

        var mesh = icon.GetComponent<MeshRenderer>();
        var material = mesh != null ? mesh.material : null;
        if (material != null)
        {
            changed = changed || !ReferenceEquals(material.mainTexture, asset.Texture);
            material.mainTexture = asset.Texture;
        }

        if (changed)
        {
            SunExpLog.Debug("[PolymorphCardFace] applied " + DictionaryUtil.Get(config?.Vars, SunExpIds.PolymorphRoleIdKey) + " from " + source);
        }

        return changed;
    }
}
