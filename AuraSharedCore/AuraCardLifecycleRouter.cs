using System;
using System.Collections.Generic;
using System.Linq;
using Witch.Core;
using Witch.Mod;

namespace AuraShared.Core;

public enum AuraCardLifecyclePhase
{
    BeforeCommonCardUse,
    BeforeAttackCardUse,
    AfterCommonCardUse,
    AfterAttackCardUse,
    AfterSetCardStyle,
    AfterCardItemInit,
    AfterAttackCardItemInit,
    AfterCardItemDataUpdate,
    AfterAttackCardItemDataUpdate,
    AfterCardItemDrawEffect,
    AfterCommonCardItemDrawEffect,
    AfterAttackCardItemDrawEffect,
    AfterFightUiCreateCardItem,
    AfterFightUiCreateCardItemInternal,
    AfterScriptExecutorGetCardFromDeck,
    AfterScriptExecutorRandomAddCard,
    AfterCardChoiceItemInitialize,
    BeforeCardChoiceUiSelect,
    AfterDictItemInit,
    AfterDictionaryShowItemInit,
    AfterDisplayCardInit,
    AfterShowCardInit,
    AfterSafeBoxItemInit,
    AfterEnchCardItemInit,
    AfterPackShowItemInit,
    AfterShopItemInit,
    AfterWarehouseItemInit,
    AfterPlayerInfoAddCard,
    AfterPlayerInfoAddCardById,
    AfterPlayerInfoRandomAddCard
}

public sealed class AuraCardLifecycleSubscription
{
    public int Priority { get; set; }

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

public static class AuraCardLifecycleRouter
{
    public const string CommonCardItemTrueUse = "CommonCardItem.TrueUse";
    public const string AttackCardItemTrueUse = "AttackCardItem.TrueUse";
    public const string ICardSetCardStyle = "ICard.SetCardStyle";
    public const string CardItemInit = "CardItem.Init";
    public const string AttackCardItemInit = "AttackCardItem.Init";
    public const string CardItemDataUpdate = "CardItem.DataUpdate";
    public const string AttackCardItemDataUpdate = "AttackCardItem.DataUpdate";
    public const string CardItemDrawEffect = "CardItem.DrawEffect";
    public const string CommonCardItemDrawEffect = "CommonCardItem.DrawEffect";
    public const string AttackCardItemDrawEffect = "AttackCardItem.DrawEffect";
    public const string FightUiCreateCardItem = "FightUI.CreateCardItem";
    public const string FightUiCreateCardItemInternal = "FightUI.CreateCardItemInternal";
    public const string ScriptExecutorGetCardFromDeck = "ScriptExecutor.GetCardFromDeck";
    public const string ScriptExecutorRandomAddCard = "ScriptExecutor.RandomAddCard";
    public const string CardChoiceItemInitialize = "CardChoiceItem.Initialize";
    public const string CardChoiceUiSelect = "CardChoiceUI.Select";
    public const string DictItemInit = "DictItem.Init";
    public const string DictionaryShowItemInit = "DictionaryShowItem.Init";
    public const string DisplayCardInit = "DisplayCard.Init";
    public const string ShowCardInit = "ShowCard.Init";
    public const string SafeBoxItemInit = "SafeBoxItem.Init";
    public const string EnchCardItemInit = "EnchCardItem.Init";
    public const string PackShowItemInit = "PackShowItem.Init";
    public const string ShopItemInit = "ShopItem.Init";
    public const string WarehouseItemInit = "WarehouseItem.Init";
    public const string PlayerInfoAddCard = "PlayerInfo.AddCard";
    public const string PlayerInfoAddCardById = "PlayerInfo.AddCardById";
    public const string PlayerInfoRandomAddCard = "PlayerInfo.RandomAddCard";

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<AuraCardLifecyclePhase> RegisteredPhases = new();
    private static Handler[]? cachedHandlers;
    private static AuraHookRegistry? registry;

    public static IDisposable Register(
        ModConfig modConfig,
        string ownerModId,
        string handlerId,
        AuraCardLifecycleSubscription subscription,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        if (subscription == null)
        {
            return EmptyDisposable.Instance;
        }

        var owner = string.IsNullOrWhiteSpace(ownerModId) ? "AuraShared" : ownerModId.Trim();
        var localId = string.IsNullOrWhiteSpace(handlerId) ? Guid.NewGuid().ToString("N") : handlerId.Trim();
        var id = owner + ":" + localId;
        Handler handler;
        lock (Gate)
        {
            handler = new Handler(id, subscription, warn);
            Handlers[id] = handler;
            cachedHandlers = null;
            EnsurePhaseRegistrationsNoLock(modConfig, subscription, info, warn);
        }

        AuraSharedLog.DebugLog(owner, "[CardLifecycle] handler registered: " + id, false);
        return new Subscription(id, handler);
    }

    private static void EnsureRegistryNoLock(ModConfig modConfig, Action<string>? info, Action<string>? warn)
    {
        registry ??= new AuraHookRegistry(modConfig, "AuraCardLifecycle", info, warn);
    }

    private static void EnsurePhaseRegistrationsNoLock(
        ModConfig modConfig,
        AuraCardLifecycleSubscription subscription,
        Action<string>? info,
        Action<string>? warn)
    {
        if (!HasAnyPhase(subscription))
        {
            return;
        }

        EnsureRegistryNoLock(modConfig, info, warn);
        if (subscription.BeforeCommonCardUse != null) EnsureBeforeNoLock(AuraCardLifecyclePhase.BeforeCommonCardUse, CommonCardItemTrueUse, context => Dispatch(context, AuraCardLifecyclePhase.BeforeCommonCardUse, CommonCardItemTrueUse, h => h.Subscription.BeforeCommonCardUse), "BeforeCommonCardUse");
        if (subscription.BeforeAttackCardUse != null) EnsureBeforeNoLock(AuraCardLifecyclePhase.BeforeAttackCardUse, AttackCardItemTrueUse, context => Dispatch(context, AuraCardLifecyclePhase.BeforeAttackCardUse, AttackCardItemTrueUse, h => h.Subscription.BeforeAttackCardUse), "BeforeAttackCardUse");
        if (subscription.AfterCommonCardUse != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterCommonCardUse, CommonCardItemTrueUse, context => Dispatch(context, AuraCardLifecyclePhase.AfterCommonCardUse, CommonCardItemTrueUse, h => h.Subscription.AfterCommonCardUse), "AfterCommonCardUse");
        if (subscription.AfterAttackCardUse != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterAttackCardUse, AttackCardItemTrueUse, context => Dispatch(context, AuraCardLifecyclePhase.AfterAttackCardUse, AttackCardItemTrueUse, h => h.Subscription.AfterAttackCardUse), "AfterAttackCardUse");

        if (subscription.AfterSetCardStyle != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterSetCardStyle, ICardSetCardStyle, context => Dispatch(context, AuraCardLifecyclePhase.AfterSetCardStyle, ICardSetCardStyle, h => h.Subscription.AfterSetCardStyle), "AfterSetCardStyle");
        if (subscription.AfterCardItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterCardItemInit, CardItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterCardItemInit, CardItemInit, h => h.Subscription.AfterCardItemInit), "AfterCardItemInit");
        if (subscription.AfterAttackCardItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterAttackCardItemInit, AttackCardItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterAttackCardItemInit, AttackCardItemInit, h => h.Subscription.AfterAttackCardItemInit), "AfterAttackCardItemInit");
        if (subscription.AfterCardItemDataUpdate != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterCardItemDataUpdate, CardItemDataUpdate, context => Dispatch(context, AuraCardLifecyclePhase.AfterCardItemDataUpdate, CardItemDataUpdate, h => h.Subscription.AfterCardItemDataUpdate), "AfterCardItemDataUpdate");
        if (subscription.AfterAttackCardItemDataUpdate != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterAttackCardItemDataUpdate, AttackCardItemDataUpdate, context => Dispatch(context, AuraCardLifecyclePhase.AfterAttackCardItemDataUpdate, AttackCardItemDataUpdate, h => h.Subscription.AfterAttackCardItemDataUpdate), "AfterAttackCardItemDataUpdate");
        if (subscription.AfterCardItemDrawEffect != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterCardItemDrawEffect, CardItemDrawEffect, context => Dispatch(context, AuraCardLifecyclePhase.AfterCardItemDrawEffect, CardItemDrawEffect, h => h.Subscription.AfterCardItemDrawEffect), "AfterCardItemDrawEffect");
        if (subscription.AfterCommonCardItemDrawEffect != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterCommonCardItemDrawEffect, CommonCardItemDrawEffect, context => Dispatch(context, AuraCardLifecyclePhase.AfterCommonCardItemDrawEffect, CommonCardItemDrawEffect, h => h.Subscription.AfterCommonCardItemDrawEffect), "AfterCommonCardItemDrawEffect");
        if (subscription.AfterAttackCardItemDrawEffect != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterAttackCardItemDrawEffect, AttackCardItemDrawEffect, context => Dispatch(context, AuraCardLifecyclePhase.AfterAttackCardItemDrawEffect, AttackCardItemDrawEffect, h => h.Subscription.AfterAttackCardItemDrawEffect), "AfterAttackCardItemDrawEffect");
        if (subscription.AfterFightUiCreateCardItem != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterFightUiCreateCardItem, FightUiCreateCardItem, context => Dispatch(context, AuraCardLifecyclePhase.AfterFightUiCreateCardItem, FightUiCreateCardItem, h => h.Subscription.AfterFightUiCreateCardItem), "AfterFightUiCreateCardItem");
        if (subscription.AfterFightUiCreateCardItemInternal != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterFightUiCreateCardItemInternal, FightUiCreateCardItemInternal, context => Dispatch(context, AuraCardLifecyclePhase.AfterFightUiCreateCardItemInternal, FightUiCreateCardItemInternal, h => h.Subscription.AfterFightUiCreateCardItemInternal), "AfterFightUiCreateCardItemInternal");
        if (subscription.AfterScriptExecutorGetCardFromDeck != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterScriptExecutorGetCardFromDeck, ScriptExecutorGetCardFromDeck, context => Dispatch(context, AuraCardLifecyclePhase.AfterScriptExecutorGetCardFromDeck, ScriptExecutorGetCardFromDeck, h => h.Subscription.AfterScriptExecutorGetCardFromDeck), "AfterScriptExecutorGetCardFromDeck");
        if (subscription.AfterScriptExecutorRandomAddCard != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterScriptExecutorRandomAddCard, ScriptExecutorRandomAddCard, context => Dispatch(context, AuraCardLifecyclePhase.AfterScriptExecutorRandomAddCard, ScriptExecutorRandomAddCard, h => h.Subscription.AfterScriptExecutorRandomAddCard), "AfterScriptExecutorRandomAddCard");
        if (subscription.AfterCardChoiceItemInitialize != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterCardChoiceItemInitialize, CardChoiceItemInitialize, context => Dispatch(context, AuraCardLifecyclePhase.AfterCardChoiceItemInitialize, CardChoiceItemInitialize, h => h.Subscription.AfterCardChoiceItemInitialize), "AfterCardChoiceItemInitialize");
        if (subscription.BeforeCardChoiceUiSelect != null) EnsureBeforeNoLock(AuraCardLifecyclePhase.BeforeCardChoiceUiSelect, CardChoiceUiSelect, context => Dispatch(context, AuraCardLifecyclePhase.BeforeCardChoiceUiSelect, CardChoiceUiSelect, h => h.Subscription.BeforeCardChoiceUiSelect), "BeforeCardChoiceUiSelect");

        if (subscription.AfterDictItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterDictItemInit, DictItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterDictItemInit, DictItemInit, h => h.Subscription.AfterDictItemInit), "AfterDictItemInit");
        if (subscription.AfterDictionaryShowItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterDictionaryShowItemInit, DictionaryShowItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterDictionaryShowItemInit, DictionaryShowItemInit, h => h.Subscription.AfterDictionaryShowItemInit), "AfterDictionaryShowItemInit");
        if (subscription.AfterDisplayCardInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterDisplayCardInit, DisplayCardInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterDisplayCardInit, DisplayCardInit, h => h.Subscription.AfterDisplayCardInit), "AfterDisplayCardInit");
        if (subscription.AfterShowCardInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterShowCardInit, ShowCardInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterShowCardInit, ShowCardInit, h => h.Subscription.AfterShowCardInit), "AfterShowCardInit");
        if (subscription.AfterSafeBoxItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterSafeBoxItemInit, SafeBoxItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterSafeBoxItemInit, SafeBoxItemInit, h => h.Subscription.AfterSafeBoxItemInit), "AfterSafeBoxItemInit");
        if (subscription.AfterEnchCardItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterEnchCardItemInit, EnchCardItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterEnchCardItemInit, EnchCardItemInit, h => h.Subscription.AfterEnchCardItemInit), "AfterEnchCardItemInit");
        if (subscription.AfterPackShowItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterPackShowItemInit, PackShowItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterPackShowItemInit, PackShowItemInit, h => h.Subscription.AfterPackShowItemInit), "AfterPackShowItemInit");
        if (subscription.AfterShopItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterShopItemInit, ShopItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterShopItemInit, ShopItemInit, h => h.Subscription.AfterShopItemInit), "AfterShopItemInit");
        if (subscription.AfterWarehouseItemInit != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterWarehouseItemInit, WarehouseItemInit, context => Dispatch(context, AuraCardLifecyclePhase.AfterWarehouseItemInit, WarehouseItemInit, h => h.Subscription.AfterWarehouseItemInit), "AfterWarehouseItemInit");

        if (subscription.AfterPlayerInfoAddCard != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterPlayerInfoAddCard, PlayerInfoAddCard, context => Dispatch(context, AuraCardLifecyclePhase.AfterPlayerInfoAddCard, PlayerInfoAddCard, h => h.Subscription.AfterPlayerInfoAddCard), "AfterPlayerInfoAddCard");
        if (subscription.AfterPlayerInfoAddCardById != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterPlayerInfoAddCardById, PlayerInfoAddCardById, context => Dispatch(context, AuraCardLifecyclePhase.AfterPlayerInfoAddCardById, PlayerInfoAddCardById, h => h.Subscription.AfterPlayerInfoAddCardById), "AfterPlayerInfoAddCardById");
        if (subscription.AfterPlayerInfoRandomAddCard != null) EnsureAfterNoLock(AuraCardLifecyclePhase.AfterPlayerInfoRandomAddCard, PlayerInfoRandomAddCard, context => Dispatch(context, AuraCardLifecyclePhase.AfterPlayerInfoRandomAddCard, PlayerInfoRandomAddCard, h => h.Subscription.AfterPlayerInfoRandomAddCard), "AfterPlayerInfoRandomAddCard");
    }

    private static bool HasAnyPhase(AuraCardLifecycleSubscription subscription)
    {
        return subscription.BeforeCommonCardUse != null
            || subscription.BeforeAttackCardUse != null
            || subscription.AfterCommonCardUse != null
            || subscription.AfterAttackCardUse != null
            || subscription.AfterSetCardStyle != null
            || subscription.AfterCardItemInit != null
            || subscription.AfterAttackCardItemInit != null
            || subscription.AfterCardItemDataUpdate != null
            || subscription.AfterAttackCardItemDataUpdate != null
            || subscription.AfterCardItemDrawEffect != null
            || subscription.AfterCommonCardItemDrawEffect != null
            || subscription.AfterAttackCardItemDrawEffect != null
            || subscription.AfterFightUiCreateCardItem != null
            || subscription.AfterFightUiCreateCardItemInternal != null
            || subscription.AfterScriptExecutorGetCardFromDeck != null
            || subscription.AfterScriptExecutorRandomAddCard != null
            || subscription.AfterCardChoiceItemInitialize != null
            || subscription.BeforeCardChoiceUiSelect != null
            || subscription.AfterDictItemInit != null
            || subscription.AfterDictionaryShowItemInit != null
            || subscription.AfterDisplayCardInit != null
            || subscription.AfterShowCardInit != null
            || subscription.AfterSafeBoxItemInit != null
            || subscription.AfterEnchCardItemInit != null
            || subscription.AfterPackShowItemInit != null
            || subscription.AfterShopItemInit != null
            || subscription.AfterWarehouseItemInit != null
            || subscription.AfterPlayerInfoAddCard != null
            || subscription.AfterPlayerInfoAddCardById != null
            || subscription.AfterPlayerInfoRandomAddCard != null;
    }

    private static void EnsureBeforeNoLock(
        AuraCardLifecyclePhase phase,
        string target,
        Action<ModHookContext> action,
        string handlerId)
    {
        if (RegisteredPhases.Add(phase))
        {
            registry!.BeforeRouted(target, action, handlerId);
        }
    }

    private static void EnsureAfterNoLock(
        AuraCardLifecyclePhase phase,
        string target,
        Action<ModHookContext> action,
        string handlerId)
    {
        if (RegisteredPhases.Add(phase))
        {
            registry!.AfterRouted(target, action, handlerId);
        }
    }

    private static void Dispatch(
        ModHookContext context,
        AuraCardLifecyclePhase phase,
        string source,
        Func<Handler, Action<ModHookContext>?> selector)
    {
        foreach (var handler in SnapshotHandlers())
        {
            var action = selector(handler);
            if (action == null)
            {
                continue;
            }

            handler.Invoke(phase, source, action, context);
        }
    }

    private static Handler[] SnapshotHandlers()
    {
        lock (Gate)
        {
            if (cachedHandlers != null)
            {
                return cachedHandlers;
            }

            cachedHandlers = Handlers.Values
                .OrderByDescending(handler => handler.Subscription.Priority)
                .ThenBy(handler => handler.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return cachedHandlers;
        }
    }

    private sealed class Handler
    {
        private readonly Action<string>? warn;

        public Handler(string id, AuraCardLifecycleSubscription subscription, Action<string>? warn)
        {
            Id = id;
            Subscription = subscription;
            this.warn = warn;
        }

        public string Id { get; }

        public AuraCardLifecycleSubscription Subscription { get; }

        public void Invoke(AuraCardLifecyclePhase phase, string source, Action<ModHookContext> action, ModHookContext context)
        {
            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                warn?.Invoke("[AuraCardLifecycle] handler failed: " + Id + " @ " + phase + "/" + source + " -> " + ex.Message);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string id;
        private readonly Handler handler;
        private bool disposed;

        public Subscription(string id, Handler handler)
        {
            this.id = id;
            this.handler = handler;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (Gate)
            {
                if (Handlers.TryGetValue(id, out var current) && ReferenceEquals(current, handler))
                {
                    Handlers.Remove(id);
                    cachedHandlers = null;
                }
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
