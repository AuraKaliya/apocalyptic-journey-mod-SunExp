using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class CardVisualSkinRuntime
{
    private static readonly HashSet<string> LoggedRootMisses = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedRootConfigMismatches = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        TerriasCardPresentationRouter.Register("CardVisualSkin", new TerriasCardPresentationSubscription
        {
            Apply = ApplyPresentation
        });
        TerriasCardLifecycleRouter.Register("CardVisualSkin.UseGuards", new TerriasCardLifecycleSubscription
        {
            BeforeCommonCardUse = context => SuppressBurnoutFrameEffect(context, "CardLifecycle.BeforeCommonCardUse"),
            BeforeAttackCardUse = context => SuppressBurnoutFrameEffect(context, "CardLifecycle.BeforeAttackCardUse")
        });
        TerriasCardExitRouter.Register("CardVisualSkin.BurnHandoff", new TerriasCardExitSubscription
        {
            Priority = 100,
            BeforeBurn = PrepareBurnVisualHandoff
        });

        TerriasLog.InfoAlways("Card visual skin runtime initialized");
    }

    private static void PrepareBurnVisualHandoff(ModHookContext context)
    {
        try
        {
            if (context.Target is not CardItem card)
            {
                return;
            }

            var visualRoot = CardPresentationRootResolver.FindCardVisualRoot(card.transform);
            var marker = visualRoot?.GetComponent<CardVisualSkinMarker>();
            if (marker == null)
            {
                return;
            }

            var changed = marker.PrepareForBurnVisualHandoff();
            TerriasPerformanceCounters.Record(changed
                ? "CardVisualSkin.BurnHandoff.Applied"
                : "CardVisualSkin.BurnHandoff.NoChange");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Card visual skin burn handoff failed", ex);
        }
    }

    private static void ApplyPresentation(TerriasCardPresentationContext context)
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
                TerriasLog.Debug("Card visual skin suppressed burnout frame effect from " + source + ": " + CardConfigApi.Id(config));
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Card visual skin burnout frame-effect suppression failed from " + source, ex);
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

    private static void ApplySafely(TerriasCardPresentationContext context)
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
            if (context.Surface == TerriasCardPresentationSurface.Display
                && CardPresentationRootResolver.IsCompactDisplayRoot(root))
            {
                TerriasPerformanceCounters.Record("CardVisualSkin.CompactDisplayHandled");
                var compactCardId = CardConfigApi.Id(config);
                TerriasLog.DebugOnce("CardVisualSkin.CompactDisplay." + compactCardId + "." + source,
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
            TerriasPerformanceCounters.Record("CardVisualSkin.InterestMiss");
            CardVisualSkinApplier.ClearForUnmatchedCard(visualRoot);
            return;
        }

        var applied = CardVisualSkinApplier.Apply(visualRoot, config, source);
        if (applied)
        {
            TerriasPerformanceCounters.Record("CardVisualSkin.Apply");
            TerriasLog.Debug("Card visual skin applied from " + source + ": " + DictionaryUtil.Get(config.data, "Id", "unknown"));
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

        TerriasPerformanceCounters.Record("CardVisualSkin.RootMiss");
        TerriasLog.Warn("Card visual skin root missing: cardId="
            + CardConfigApi.Id(config)
            + ", source="
            + source
            + ", root="
            + (root == null ? "<null>" : root.name));
    }

    private static bool IsCombatSurface(TerriasCardPresentationSurface surface)
    {
        return surface == TerriasCardPresentationSurface.CombatCard
            || surface == TerriasCardPresentationSurface.CombatCardInternal
            || surface == TerriasCardPresentationSurface.PostCommit;
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

        TerriasPerformanceCounters.Record("CardVisualSkin.RootConfigMismatch");
        TerriasLog.Warn("Card visual skin skipped mismatched combat root: rootCardId="
            + rootId
            + ", configCardId="
            + configId
            + ", source="
            + source
            + ", root="
            + (root == null ? "<null>" : root.name));
    }
}
