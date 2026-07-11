using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class CardVisualSkinRuntime
{
    private static readonly HashSet<string> LoggedRootMisses = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedRootConfigMismatches = new(StringComparer.Ordinal);

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

        SunExpLog.InfoAlways("Card visual skin runtime initialized");
    }

    private static void ApplyPresentation(SunExpCardPresentationContext context)
    {
        ApplySafely(context);
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

            if (!CardVisualInterestIndex.MayAffect(config))
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

    private static void ApplySafely(SunExpCardPresentationContext context)
    {
        var root = context.Root;
        var config = context.Config;
        var source = context.Source;
        if (config == null)
        {
            return;
        }

        if (IsCombatSurface(context.Surface) && !RootMatchesCombatConfig(root, config, context.Card, source))
        {
            return;
        }

        var visualRoot = CardPresentationRootResolver.FindCardVisualRoot(root);
        if (visualRoot == null)
        {
            if (context.Surface == SunExpCardPresentationSurface.Display
                && CardPresentationRootResolver.IsCompactDisplayRoot(root))
            {
                SunExpPerformanceCounters.Record("CardVisualSkin.CompactDisplayHandled");
                var compactCardId = CardConfigApi.Id(config);
                SunExpLog.DebugOnce("CardVisualSkin.CompactDisplay." + compactCardId + "." + source,
                    "Card visual skin compact display uses native card art: cardId="
                    + compactCardId
                    + ", source="
                    + source);
                return;
            }

            if (CardVisualInterestIndex.MayAffect(config))
            {
                LogRootMiss(config, source, root);
            }

            return;
        }

        if (!CardVisualInterestIndex.MayAffect(config))
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.InterestMiss");
            CardVisualSkinApplier.ClearForUnmatchedCard(visualRoot);
            return;
        }

        var applied = CardVisualSkinApplier.Apply(visualRoot, config, source);
        if (applied)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.Apply");
            SunExpLog.Debug("Card visual skin applied from " + source + ": " + DictionaryUtil.Get(config.data, "Id", "unknown"));
        }
    }

    private static void LogRootMiss(IDataConfig config, string source, Transform? root)
    {
        var key = CardConfigApi.Id(config)
            + "|"
            + (source ?? "")
            + "|"
            + (root == null ? "null" : root.GetInstanceID().ToString());
        if (LoggedRootMisses.Count >= 32 || !LoggedRootMisses.Add(key))
        {
            return;
        }

        SunExpPerformanceCounters.Record("CardVisualSkin.RootMiss");
        SunExpLog.Warn("Card visual skin root missing: cardId="
            + CardConfigApi.Id(config)
            + ", source="
            + source
            + ", root="
            + (root == null ? "<null>" : root.name));
    }

    private static bool IsCombatSurface(SunExpCardPresentationSurface surface)
    {
        return surface == SunExpCardPresentationSurface.CombatCard
            || surface == SunExpCardPresentationSurface.CombatCardInternal
            || surface == SunExpCardPresentationSurface.PostCommit;
    }

    private static bool RootMatchesCombatConfig(
        Transform? root,
        IDataConfig config,
        CardItem? knownCard,
        string source)
    {
        var card = knownCard ?? FindCardItem(root);
        if (card?.dataConfig == null)
        {
            return true;
        }

        if (ReferenceEquals(card.dataConfig, config))
        {
            return true;
        }

        var rootId = CardConfigApi.Id(card.dataConfig);
        var configId = CardConfigApi.Id(config);
        if (string.Equals(rootId, configId, StringComparison.Ordinal))
        {
            return true;
        }

        LogRootConfigMismatch(rootId, configId, source, root);
        return false;
    }

    private static CardItem? FindCardItem(Transform? root)
    {
        if (root == null)
        {
            return null;
        }

        try
        {
            return root.GetComponent<CardItem>()
                ?? root.GetComponentInParent<CardItem>()
                ?? root.GetComponentInChildren<CardItem>();
        }
        catch
        {
            return null;
        }
    }

    private static void LogRootConfigMismatch(string rootId, string configId, string source, Transform? root)
    {
        var key = rootId
            + "|"
            + configId
            + "|"
            + (source ?? "")
            + "|"
            + (root == null ? "null" : root.GetInstanceID().ToString());
        if (LoggedRootConfigMismatches.Count >= 32 || !LoggedRootConfigMismatches.Add(key))
        {
            return;
        }

        SunExpPerformanceCounters.Record("CardVisualSkin.RootConfigMismatch");
        SunExpLog.Warn("Card visual skin skipped mismatched combat root: rootCardId="
            + rootId
            + ", configCardId="
            + configId
            + ", source="
            + source
            + ", root="
            + (root == null ? "<null>" : root.name));
    }
}
