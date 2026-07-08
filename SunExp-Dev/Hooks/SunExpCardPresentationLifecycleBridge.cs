using System;
using SunExp.Dll.GameApi;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class SunExpCardPresentationLifecycleBridge
{
    public static void Initialize()
    {
        SunExpCardLifecycleRouter.Register("CardPresentationBridge", new SunExpCardLifecycleSubscription
        {
            AfterSetCardStyle = ApplyFromSetCardStyle,
            AfterCardItemInit = context => ApplyFromItemRoot(context, SunExpHookTargets.CardItemInit, SunExpCardPresentationSurface.CombatCard),
            AfterAttackCardItemInit = context => ApplyFromItemRoot(context, SunExpHookTargets.AttackCardItemInit, SunExpCardPresentationSurface.CombatCard),
            AfterCardItemDataUpdate = context => ApplyFromItemRoot(context, SunExpHookTargets.CardItemDataUpdate, SunExpCardPresentationSurface.CombatCard),
            AfterAttackCardItemDataUpdate = context => ApplyFromItemRoot(context, SunExpHookTargets.AttackCardItemDataUpdate, SunExpCardPresentationSurface.CombatCard),
            AfterCardItemDrawEffect = context => ApplyFromItemRoot(context, SunExpHookTargets.CardItemDrawEffect, SunExpCardPresentationSurface.CombatCard),
            AfterCommonCardItemDrawEffect = context => ApplyFromItemRoot(context, SunExpHookTargets.CommonCardItemDrawEffect, SunExpCardPresentationSurface.CombatCard),
            AfterAttackCardItemDrawEffect = context => ApplyFromItemRoot(context, SunExpHookTargets.AttackCardItemDrawEffect, SunExpCardPresentationSurface.CombatCard),
            AfterFightUiCreateCardItem = context => SunExpCardPresentationRouter.RequestActiveCombatCardsReapply(SunExpHookTargets.FightUiCreateCardItem),
            AfterFightUiCreateCardItemInternal = ApplyFromFightUiCreateCardItemInternal,
            AfterScriptExecutorGetCardFromDeck = context => SunExpCardPresentationRouter.RequestActiveCombatCardsReapply(SunExpHookTargets.ScriptExecutorGetCardFromDeck),
            AfterDictItemInit = context => ApplyFromArgumentRoot(context, 0, null, SunExpHookTargets.DictItemInit, SunExpCardPresentationSurface.Dictionary),
            AfterDictionaryShowItemInit = context => ApplyFromArgumentRoot(context, 0, null, SunExpHookTargets.DictionaryShowItemInit, SunExpCardPresentationSurface.Dictionary),
            AfterDisplayCardInit = context => ApplyFromArgumentRoot(context, 0, null, SunExpHookTargets.DisplayCardInit, SunExpCardPresentationSurface.Display),
            AfterShowCardInit = ApplyFromShowCard,
            AfterSafeBoxItemInit = ApplyFromSafeBoxItem,
            AfterEnchCardItemInit = context => ApplyFromArgumentRoot(context, 0, null, SunExpHookTargets.EnchCardItemInit, SunExpCardPresentationSurface.Display),
            AfterCardChoiceItemInitialize = ApplyFromCardChoiceItem,
            AfterPackShowItemInit = context => ApplyFromArgumentRoot(context, 0, "CardItem", SunExpHookTargets.PackShowItemInit, SunExpCardPresentationSurface.CardPack),
            AfterShopItemInit = context => ApplyFromArgumentRoot(context, 0, "CardItem", SunExpHookTargets.ShopItemInit, SunExpCardPresentationSurface.Shop),
            AfterWarehouseItemInit = context => ApplyFromArgumentRoot(context, 2, "CardItem", SunExpHookTargets.WarehouseItemInit, SunExpCardPresentationSurface.Warehouse)
        });
    }

    private static void ApplyFromSetCardStyle(ModHookContext context)
    {
        var args = context.Arguments;
        if (args == null
            || args.Length < 2
            || args[0] is not Transform transform
            || args[1] is not IDataConfig config)
        {
            return;
        }

        SunExpCardPresentationRouter.RequestApply(transform, config, SunExpHookTargets.ICardSetCardStyle, SunExpCardPresentationSurface.CardStyle);
    }

    private static void ApplyFromItemRoot(ModHookContext context, string source, SunExpCardPresentationSurface surface)
    {
        if (context.Target is not Item item)
        {
            return;
        }

        SunExpCardPresentationRouter.RequestApply(new SunExpCardPresentationContext
        {
            Root = item.transform,
            Config = item.dataConfig,
            Source = source,
            Surface = surface
        });
    }

    private static void ApplyFromArgumentRoot(
        ModHookContext context,
        int configArgIndex,
        string? childPath,
        string source,
        SunExpCardPresentationSurface surface)
    {
        var config = ConfigFromArgument(context.Arguments, configArgIndex)
            ?? CardConfigApi.FromActionPayload(context.Target);
        var root = RootFromTarget(context.Target, childPath);
        SunExpCardPresentationRouter.RequestApply(root, config, source, surface);
    }

    private static void ApplyFromShowCard(ModHookContext context)
    {
        var args = context.Arguments;
        if (args != null && args.Length > 1 && args[1] is bool equipped && equipped)
        {
            return;
        }

        ApplyFromArgumentRoot(context, 0, null, SunExpHookTargets.ShowCardInit, SunExpCardPresentationSurface.Display);
    }

    private static void ApplyFromSafeBoxItem(ModHookContext context)
    {
        if (context.Target is SafeBoxItem { InBackPack: false })
        {
            return;
        }

        ApplyFromArgumentRoot(context, 0, null, SunExpHookTargets.SafeBoxItemInit, SunExpCardPresentationSurface.SafeBox);
    }

    private static void ApplyFromCardChoiceItem(ModHookContext context)
    {
        var args = context.Arguments;
        if (args == null || args.Length < 2)
        {
            return;
        }

        var cardId = Convert.ToString(args[1]);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        SunExpCardPresentationRouter.RequestApply(
            RootFromTarget(context.Target, null),
            new DataConfig(cardId, DataType.Card),
            SunExpHookTargets.CardChoiceItemInitialize,
            SunExpCardPresentationSurface.RewardChoice);
    }

    private static void ApplyFromFightUiCreateCardItemInternal(ModHookContext context)
    {
        var config = ConfigFromArgument(context.Arguments, 0);
        if (config == null)
        {
            return;
        }

        SunExpCardPresentationRouter.RequestApply(
            SunExpCardPresentationRouter.FindCombatCardRoot(config),
            config,
            SunExpHookTargets.FightUiCreateCardItemInternal,
            SunExpCardPresentationSurface.CombatCardInternal);
        SunExpCardPresentationRouter.RequestActiveCombatCardsReapply(SunExpHookTargets.FightUiCreateCardItemInternal);
    }

    private static IDataConfig? ConfigFromArgument(object[]? args, int index)
    {
        return args == null || index < 0 || args.Length <= index
            ? null
            : CardConfigApi.FromActionPayload(args[index]);
    }

    private static Transform? RootFromTarget(object? target, string? childPath)
    {
        if (target is not UnityEngine.Component component)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(childPath)
            ? component.transform
            : component.transform.Find(childPath);
    }
}
