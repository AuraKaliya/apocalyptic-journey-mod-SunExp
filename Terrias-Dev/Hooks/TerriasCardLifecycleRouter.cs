using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public sealed class TerriasCardLifecycleSubscription
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

public static class TerriasCardLifecycleRouter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, TerriasCardLifecycleSubscription> PendingSubscriptions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IDisposable> SharedRegistrations = new(StringComparer.Ordinal);
    private static ModConfig? activeConfig;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            activeConfig = modConfig;
            if (initialized)
            {
                return;
            }

            initialized = true;
            foreach (var pair in PendingSubscriptions)
            {
                RegisterWithSharedNoLock(pair.Key, pair.Value);
            }
        }
    }

    public static IDisposable Register(string id, TerriasCardLifecycleSubscription subscription)
    {
        if (string.IsNullOrWhiteSpace(id) || subscription == null)
        {
            return EmptyDisposable.Instance;
        }

        lock (SyncRoot)
        {
            var normalizedId = id.Trim();
            PendingSubscriptions[normalizedId] = subscription;
            if (initialized && activeConfig != null)
            {
                RegisterWithSharedNoLock(normalizedId, subscription);
            }
        }

        TerriasPerformanceCounters.Record("CardLifecycle.HandlerRegistered");
        return new RegistrationHandle(id.Trim());
    }

    private static void Unregister(string id)
    {
        lock (SyncRoot)
        {
            PendingSubscriptions.Remove(id);
            if (SharedRegistrations.TryGetValue(id, out var registration))
            {
                SharedRegistrations.Remove(id);
                registration.Dispose();
            }
        }
    }

    private static void RegisterWithSharedNoLock(string id, TerriasCardLifecycleSubscription subscription)
    {
        if (activeConfig == null)
        {
            return;
        }

        if (SharedRegistrations.TryGetValue(id, out var previous))
        {
            previous.Dispose();
        }

        SharedRegistrations[id] = AuraCardLifecycleRouter.Register(
            activeConfig,
            TerriasIds.ModId,
            id,
            ToSharedSubscription(subscription),
            TerriasLog.Info,
            message => TerriasLog.Warn(message));
    }

    private static AuraCardLifecycleSubscription ToSharedSubscription(TerriasCardLifecycleSubscription subscription)
    {
        return new AuraCardLifecycleSubscription
        {
            Priority = subscription.Priority,
            BeforeCommonCardUse = subscription.BeforeCommonCardUse,
            BeforeAttackCardUse = subscription.BeforeAttackCardUse,
            AfterCommonCardUse = subscription.AfterCommonCardUse,
            AfterAttackCardUse = subscription.AfterAttackCardUse,
            AfterSetCardStyle = subscription.AfterSetCardStyle,
            AfterCardItemInit = subscription.AfterCardItemInit,
            AfterAttackCardItemInit = subscription.AfterAttackCardItemInit,
            AfterCardItemDataUpdate = subscription.AfterCardItemDataUpdate,
            AfterAttackCardItemDataUpdate = subscription.AfterAttackCardItemDataUpdate,
            AfterCardItemDrawEffect = subscription.AfterCardItemDrawEffect,
            AfterCommonCardItemDrawEffect = subscription.AfterCommonCardItemDrawEffect,
            AfterAttackCardItemDrawEffect = subscription.AfterAttackCardItemDrawEffect,
            AfterFightUiCreateCardItem = subscription.AfterFightUiCreateCardItem,
            AfterFightUiCreateCardItemInternal = subscription.AfterFightUiCreateCardItemInternal,
            AfterScriptExecutorGetCardFromDeck = subscription.AfterScriptExecutorGetCardFromDeck,
            AfterScriptExecutorRandomAddCard = subscription.AfterScriptExecutorRandomAddCard,
            AfterCardChoiceItemInitialize = subscription.AfterCardChoiceItemInitialize,
            BeforeCardChoiceUiSelect = subscription.BeforeCardChoiceUiSelect,
            AfterDictItemInit = subscription.AfterDictItemInit,
            AfterDictionaryShowItemInit = subscription.AfterDictionaryShowItemInit,
            AfterDisplayCardInit = subscription.AfterDisplayCardInit,
            AfterShowCardInit = subscription.AfterShowCardInit,
            AfterSafeBoxItemInit = subscription.AfterSafeBoxItemInit,
            AfterEnchCardItemInit = subscription.AfterEnchCardItemInit,
            AfterPackShowItemInit = subscription.AfterPackShowItemInit,
            AfterShopItemInit = subscription.AfterShopItemInit,
            AfterWarehouseItemInit = subscription.AfterWarehouseItemInit,
            AfterPlayerInfoAddCard = subscription.AfterPlayerInfoAddCard,
            AfterPlayerInfoAddCardById = subscription.AfterPlayerInfoAddCardById,
            AfterPlayerInfoRandomAddCard = subscription.AfterPlayerInfoRandomAddCard
        };
    }

    private sealed class RegistrationHandle : IDisposable
    {
        private string? id;

        public RegistrationHandle(string id)
        {
            this.id = id;
        }

        public void Dispose()
        {
            var current = id;
            if (current == null)
            {
                return;
            }

            id = null;
            Unregister(current);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
