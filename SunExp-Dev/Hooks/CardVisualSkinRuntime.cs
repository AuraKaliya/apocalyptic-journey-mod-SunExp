using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class CardVisualSkinRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        SunExpCardPresentationRouter.Register("CardVisualSkin", new SunExpCardPresentationSubscription
        {
            Apply = ApplyPresentation
        });
        SunExpCardLifecycleRouter.Register("CardVisualSkin.UseGuards", new SunExpCardLifecycleSubscription
        {
            BeforeCommonCardUse = context => SuppressBurnoutFrameEffect(context, SunExpHookTargets.CommonCardItemTrueUse),
            BeforeAttackCardUse = context => SuppressBurnoutFrameEffect(context, SunExpHookTargets.AttackCardItemTrueUse)
        });

        SunExpLog.Info("Card visual skin runtime initialized");
    }

    private static void ApplyPresentation(SunExpCardPresentationContext context)
    {
        ApplySafely(context.Root, context.Config, context.Source);
    }

    private static void SuppressBurnoutFrameEffect(ModHookContext context, string source)
    {
        try
        {
            if (context.Target is not Item item)
            {
                return;
            }

            var config = item.dataConfig ?? CardConfigApi.FromActionPayload(context.Target);
            if (!HasBurnoutTag(context.Target as CardItem, config))
            {
                return;
            }

            var visualRoot = CardPresentationRootResolver.FindCardVisualRoot(item.transform);
            var marker = visualRoot == null ? null : visualRoot.GetComponent<CardVisualSkinMarker>();
            if (marker != null && marker.SuppressFrameEffectOverlay(config, source))
            {
                SunExpLog.Debug("Card visual skin suppressed burnout frame effect from " + source + ": " + CardConfigApi.Id(config));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card visual skin burnout frame-effect suppression failed from " + source, ex);
        }
    }

    private static bool HasBurnoutTag(CardItem? card, IDataConfig? config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.data, "Tag"), "Burnout")
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.Vars, "Tag"), "Burnout")
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card?.data, "Tag"), "Burnout")
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card?.Vars, "Tag"), "Burnout")
            || card?.Tags.Contains("Burnout") == true;
    }

    private static void ApplySafely(Transform? root, IDataConfig? config, string source, bool scheduleDeferred = true)
    {
        if (config == null)
        {
            return;
        }

        var visualRoot = CardPresentationRootResolver.FindCardVisualRoot(root);
        var applied = CardVisualSkinApplier.Apply(visualRoot, config);
        if (applied)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.Apply");
            SunExpLog.Debug("Card visual skin applied from " + source + ": " + DictionaryUtil.Get(config.data, "Id", "unknown"));
        }

        if (scheduleDeferred && visualRoot != null)
        {
            var key = "CardVisualSkinRuntime.Deferred." + source + "." + visualRoot.GetInstanceID();
            SunExpFrameScheduler.RunOnceNextFrame(key, () => ApplySafely(visualRoot, config, source + ".deferred", scheduleDeferred: false));
        }
    }
}
