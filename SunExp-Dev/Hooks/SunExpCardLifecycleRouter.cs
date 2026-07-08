using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public sealed class SunExpCardLifecycleSubscription
{
    public Action<ModHookContext>? BeforeCommonCardUse { get; set; }
    public Action<ModHookContext>? BeforeAttackCardUse { get; set; }
    public Action<ModHookContext>? AfterCommonCardUse { get; set; }
    public Action<ModHookContext>? AfterAttackCardUse { get; set; }

    public Action<ModHookContext>? AfterSetCardStyle { get; set; }
    public Action<ModHookContext>? AfterCardItemInit { get; set; }
    public Action<ModHookContext>? AfterAttackCardItemInit { get; set; }
    public Action<ModHookContext>? AfterCardItemDataUpdate { get; set; }
    public Action<ModHookContext>? AfterAttackCardItemDataUpdate { get; set; }
    public Action<ModHookContext>? AfterCardItemDrawEffect { get; set; }
    public Action<ModHookContext>? AfterCommonCardItemDrawEffect { get; set; }
    public Action<ModHookContext>? AfterAttackCardItemDrawEffect { get; set; }
    public Action<ModHookContext>? AfterFightUiCreateCardItem { get; set; }
    public Action<ModHookContext>? AfterFightUiCreateCardItemInternal { get; set; }
    public Action<ModHookContext>? AfterScriptExecutorGetCardFromDeck { get; set; }
    public Action<ModHookContext>? AfterScriptExecutorRandomAddCard { get; set; }
    public Action<ModHookContext>? AfterCardChoiceItemInitialize { get; set; }
    public Action<ModHookContext>? BeforeCardChoiceUiSelect { get; set; }

    public Action<ModHookContext>? AfterDictItemInit { get; set; }
    public Action<ModHookContext>? AfterDictionaryShowItemInit { get; set; }
    public Action<ModHookContext>? AfterDisplayCardInit { get; set; }
    public Action<ModHookContext>? AfterShowCardInit { get; set; }
    public Action<ModHookContext>? AfterSafeBoxItemInit { get; set; }
    public Action<ModHookContext>? AfterEnchCardItemInit { get; set; }
    public Action<ModHookContext>? AfterPackShowItemInit { get; set; }
    public Action<ModHookContext>? AfterShopItemInit { get; set; }
    public Action<ModHookContext>? AfterWarehouseItemInit { get; set; }

    public Action<ModHookContext>? AfterPlayerInfoAddCard { get; set; }
    public Action<ModHookContext>? AfterPlayerInfoAddCardById { get; set; }
    public Action<ModHookContext>? AfterPlayerInfoRandomAddCard { get; set; }
}

public static class SunExpCardLifecycleRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, SunExpCardLifecycleSubscription> Subscriptions = new(StringComparer.Ordinal);
    private static KeyValuePair<string, SunExpCardLifecycleSubscription>[]? cachedSubscriptions;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        Before(modConfig, SunExpHookTargets.CommonCardItemTrueUse, subscription => subscription.BeforeCommonCardUse);
        Before(modConfig, SunExpHookTargets.AttackCardItemTrueUse, subscription => subscription.BeforeAttackCardUse);
        After(modConfig, SunExpHookTargets.CommonCardItemTrueUse, subscription => subscription.AfterCommonCardUse);
        After(modConfig, SunExpHookTargets.AttackCardItemTrueUse, subscription => subscription.AfterAttackCardUse);

        After(modConfig, SunExpHookTargets.ICardSetCardStyle, subscription => subscription.AfterSetCardStyle);
        After(modConfig, SunExpHookTargets.CardItemInit, subscription => subscription.AfterCardItemInit);
        After(modConfig, SunExpHookTargets.AttackCardItemInit, subscription => subscription.AfterAttackCardItemInit);
        After(modConfig, SunExpHookTargets.CardItemDataUpdate, subscription => subscription.AfterCardItemDataUpdate);
        After(modConfig, SunExpHookTargets.AttackCardItemDataUpdate, subscription => subscription.AfterAttackCardItemDataUpdate);
        After(modConfig, SunExpHookTargets.CardItemDrawEffect, subscription => subscription.AfterCardItemDrawEffect);
        After(modConfig, SunExpHookTargets.CommonCardItemDrawEffect, subscription => subscription.AfterCommonCardItemDrawEffect);
        After(modConfig, SunExpHookTargets.AttackCardItemDrawEffect, subscription => subscription.AfterAttackCardItemDrawEffect);
        After(modConfig, SunExpHookTargets.FightUiCreateCardItem, subscription => subscription.AfterFightUiCreateCardItem);
        After(modConfig, SunExpHookTargets.FightUiCreateCardItemInternal, subscription => subscription.AfterFightUiCreateCardItemInternal);
        After(modConfig, SunExpHookTargets.ScriptExecutorGetCardFromDeck, subscription => subscription.AfterScriptExecutorGetCardFromDeck);
        After(modConfig, SunExpHookTargets.ScriptExecutorRandomAddCard, subscription => subscription.AfterScriptExecutorRandomAddCard);
        After(modConfig, SunExpHookTargets.CardChoiceItemInitialize, subscription => subscription.AfterCardChoiceItemInitialize);
        Before(modConfig, SunExpHookTargets.CardChoiceUiSelect, subscription => subscription.BeforeCardChoiceUiSelect);

        After(modConfig, SunExpHookTargets.DictItemInit, subscription => subscription.AfterDictItemInit);
        After(modConfig, SunExpHookTargets.DictionaryShowItemInit, subscription => subscription.AfterDictionaryShowItemInit);
        After(modConfig, SunExpHookTargets.DisplayCardInit, subscription => subscription.AfterDisplayCardInit);
        After(modConfig, SunExpHookTargets.ShowCardInit, subscription => subscription.AfterShowCardInit);
        After(modConfig, SunExpHookTargets.SafeBoxItemInit, subscription => subscription.AfterSafeBoxItemInit);
        After(modConfig, SunExpHookTargets.EnchCardItemInit, subscription => subscription.AfterEnchCardItemInit);
        After(modConfig, SunExpHookTargets.PackShowItemInit, subscription => subscription.AfterPackShowItemInit);
        After(modConfig, SunExpHookTargets.ShopItemInit, subscription => subscription.AfterShopItemInit);
        After(modConfig, SunExpHookTargets.WarehouseItemInit, subscription => subscription.AfterWarehouseItemInit);

        After(modConfig, SunExpHookTargets.PlayerInfoAddCard, subscription => subscription.AfterPlayerInfoAddCard);
        After(modConfig, SunExpHookTargets.PlayerInfoAddCardById, subscription => subscription.AfterPlayerInfoAddCardById);
        After(modConfig, SunExpHookTargets.PlayerInfoRandomAddCard, subscription => subscription.AfterPlayerInfoRandomAddCard);
    }

    public static void Register(string id, SunExpCardLifecycleSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            Subscriptions[id.Trim()] = subscription;
            cachedSubscriptions = null;
        }

        SunExpPerformanceCounters.Record("CardLifecycle.HandlerRegistered");
    }

    private static void Before(
        ModConfig config,
        string target,
        Func<SunExpCardLifecycleSubscription, Action<ModHookContext>?> selector)
    {
        SunExpHookRegistry.BeforeRouted(config, target, context => Dispatch(target, context, selector), "CardLifecycle");
    }

    private static void After(
        ModConfig config,
        string target,
        Func<SunExpCardLifecycleSubscription, Action<ModHookContext>?> selector)
    {
        SunExpHookRegistry.AfterRouted(config, target, context => Dispatch(target, context, selector), "CardLifecycle");
    }

    private static void Dispatch(
        string target,
        ModHookContext context,
        Func<SunExpCardLifecycleSubscription, Action<ModHookContext>?> selector)
    {
        foreach (var pair in SnapshotSubscriptions())
        {
            var action = selector(pair.Value);
            if (action == null)
            {
                continue;
            }

            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                SunExpLog.Error("Card lifecycle handler failed: " + pair.Key + " @ " + target, ex);
            }
        }
    }

    private static KeyValuePair<string, SunExpCardLifecycleSubscription>[] SnapshotSubscriptions()
    {
        lock (SyncRoot)
        {
            if (cachedSubscriptions != null)
            {
                return cachedSubscriptions;
            }

            cachedSubscriptions = new KeyValuePair<string, SunExpCardLifecycleSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions)
            {
                cachedSubscriptions[index++] = pair;
            }

            return cachedSubscriptions;
        }
    }
}
