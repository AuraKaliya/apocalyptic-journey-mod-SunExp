using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraShared.Core;

public enum AuraCardPresentationSurface
{
    Unknown,
    CombatCard,
    CardStyle,
    RewardChoice,
    Display,
    Shop,
    Warehouse,
    SafeBox,
    Dictionary,
    CardPack
}

public sealed class AuraCardPresentationContext
{
    public Transform? Root { get; set; }
    public IDataConfig? Config { get; set; }
    public CardItem? Card { get; set; }
    public string Source { get; set; } = "";
    public AuraCardPresentationSurface Surface { get; set; }
}

public sealed class AuraCardPresentationSubscription
{
    public Action<AuraCardPresentationContext>? Apply { get; set; }
}

public static class AuraCardPresentationRuntime
{
    private const string RuntimeOwnerId = "AuraCardPresentationShared";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, AuraCardPresentationSubscription> Subscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    private static KeyValuePair<string, AuraCardPresentationSubscription>[]? snapshot;
    private static bool initialized;
    private static IDisposable? lifecycleRegistration;

    public static void Initialize(ModConfig modConfig)
    {
        lock (Gate)
        {
            if (initialized) return;
            initialized = true;
        }

        lifecycleRegistration = AuraCardLifecycleRouter.Register(
            modConfig,
            RuntimeOwnerId,
            "Presentation",
            new AuraCardLifecycleSubscription
            {
                AfterSetCardStyle = context => Publish(context, AuraCardLifecycleRouter.ICardSetCardStyle, AuraCardPresentationSurface.CardStyle, true),
                AfterCardItemInit = context => Publish(context, AuraCardLifecycleRouter.CardItemInit, AuraCardPresentationSurface.CombatCard),
                AfterAttackCardItemInit = context => Publish(context, AuraCardLifecycleRouter.AttackCardItemInit, AuraCardPresentationSurface.CombatCard),
                AfterCardItemDataUpdate = context => Publish(context, AuraCardLifecycleRouter.CardItemDataUpdate, AuraCardPresentationSurface.CombatCard),
                AfterAttackCardItemDataUpdate = context => Publish(context, AuraCardLifecycleRouter.AttackCardItemDataUpdate, AuraCardPresentationSurface.CombatCard),
                AfterCardItemDrawEffect = context => Publish(context, AuraCardLifecycleRouter.CardItemDrawEffect, AuraCardPresentationSurface.CombatCard),
                AfterCommonCardItemDrawEffect = context => Publish(context, AuraCardLifecycleRouter.CommonCardItemDrawEffect, AuraCardPresentationSurface.CombatCard),
                AfterAttackCardItemDrawEffect = context => Publish(context, AuraCardLifecycleRouter.AttackCardItemDrawEffect, AuraCardPresentationSurface.CombatCard),
                AfterFightUiCreateCardItem = context => Publish(context, AuraCardLifecycleRouter.FightUiCreateCardItem, AuraCardPresentationSurface.CombatCard),
                AfterFightUiCreateCardItemInternal = context => Publish(context, AuraCardLifecycleRouter.FightUiCreateCardItemInternal, AuraCardPresentationSurface.CombatCard),
                AfterCardChoiceItemInitialize = context => Publish(context, AuraCardLifecycleRouter.CardChoiceItemInitialize, AuraCardPresentationSurface.RewardChoice),
                AfterDictItemInit = context => Publish(context, AuraCardLifecycleRouter.DictItemInit, AuraCardPresentationSurface.Dictionary),
                AfterDictionaryShowItemInit = context => Publish(context, AuraCardLifecycleRouter.DictionaryShowItemInit, AuraCardPresentationSurface.Dictionary),
                AfterDisplayCardInit = context => Publish(context, AuraCardLifecycleRouter.DisplayCardInit, AuraCardPresentationSurface.Display),
                AfterShowCardInit = context => Publish(context, AuraCardLifecycleRouter.ShowCardInit, AuraCardPresentationSurface.Display),
                AfterSafeBoxItemInit = context => Publish(context, AuraCardLifecycleRouter.SafeBoxItemInit, AuraCardPresentationSurface.SafeBox),
                AfterEnchCardItemInit = context => Publish(context, AuraCardLifecycleRouter.EnchCardItemInit, AuraCardPresentationSurface.Display),
                AfterPackShowItemInit = context => Publish(context, AuraCardLifecycleRouter.PackShowItemInit, AuraCardPresentationSurface.CardPack),
                AfterShopItemInit = context => Publish(context, AuraCardLifecycleRouter.ShopItemInit, AuraCardPresentationSurface.Shop),
                AfterWarehouseItemInit = context => Publish(context, AuraCardLifecycleRouter.WarehouseItemInit, AuraCardPresentationSurface.Warehouse)
            },
            message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
            message => AuraSharedLog.Warn(RuntimeOwnerId, message));
        AuraSharedLog.InfoOnce(RuntimeOwnerId, "initialized", "Shared card presentation lifecycle initialized.");
    }

    public static IDisposable Register(
        ModConfig modConfig,
        string ownerModId,
        string handlerId,
        AuraCardPresentationSubscription subscription)
    {
        Initialize(modConfig);
        var id = Qualify(ownerModId, handlerId);
        if (id.Length == 0 || subscription == null)
        {
            return EmptyDisposable.Instance;
        }

        lock (Gate)
        {
            Subscriptions[id] = subscription;
            snapshot = null;
        }

        return new Registration(id, subscription);
    }

    public static void RequestApply(AuraCardPresentationContext context)
    {
        if (context?.Config == null) return;
        foreach (var pair in Snapshot())
        {
            try
            {
                pair.Value.Apply?.Invoke(context);
            }
            catch (Exception ex)
            {
                AuraSharedLog.Warn(RuntimeOwnerId,
                    "Card presentation handler failed: " + pair.Key + " @ " + context.Source + " -> " + ex.Message);
            }
        }
    }

    private static void Publish(
        ModHookContext context,
        string source,
        AuraCardPresentationSurface surface,
        bool setCardStyle = false)
    {
        Transform? root = null;
        IDataConfig? config = null;
        var card = context.Target as CardItem;
        if (setCardStyle && context.Arguments != null)
        {
            foreach (var argument in context.Arguments)
            {
                root ??= argument as Transform;
                config ??= argument as IDataConfig;
            }
        }
        else
        {
            ReadPresentationObject(context.Target, ref root, ref config);
            foreach (var argument in context.Arguments ?? Array.Empty<object>())
            {
                ReadPresentationObject(argument, ref root, ref config);
                card ??= argument as CardItem;
            }
        }

        if (card?.dataConfig != null)
        {
            root = card.transform;
            config = card.dataConfig;
        }

        if (config == null) return;
        RequestApply(new AuraCardPresentationContext
        {
            Root = root,
            Config = config,
            Card = card,
            Source = source,
            Surface = surface
        });
    }

    private static void ReadPresentationObject(object? value, ref Transform? root, ref IDataConfig? config)
    {
        if (value is IDataConfig dataConfig) config ??= dataConfig;
        if (value is Item item)
        {
            root ??= item.transform;
            config ??= item.dataConfig;
        }
        else if (value is UnityEngine.Component component)
        {
            root ??= component.transform;
        }
    }

    private static KeyValuePair<string, AuraCardPresentationSubscription>[] Snapshot()
    {
        var current = Volatile.Read(ref snapshot);
        if (current != null) return current;
        lock (Gate)
        {
            if (snapshot != null) return snapshot;
            snapshot = new KeyValuePair<string, AuraCardPresentationSubscription>[Subscriptions.Count];
            var index = 0;
            foreach (var pair in Subscriptions) snapshot[index++] = pair;
            return snapshot;
        }
    }

    private static string Qualify(string ownerModId, string handlerId)
    {
        var owner = (ownerModId ?? "").Trim();
        var handler = (handlerId ?? "").Trim();
        return owner.Length == 0 || handler.Length == 0 ? "" : owner + ":" + handler;
    }

    private static void Unregister(string id, AuraCardPresentationSubscription subscription)
    {
        lock (Gate)
        {
            if (Subscriptions.TryGetValue(id, out var current) && ReferenceEquals(current, subscription))
            {
                Subscriptions.Remove(id);
                snapshot = null;
            }
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly AuraCardPresentationSubscription subscription;
        private string? id;

        public Registration(string id, AuraCardPresentationSubscription subscription)
        {
            this.id = id;
            this.subscription = subscription;
        }

        public void Dispose()
        {
            var value = id;
            if (value == null) return;
            id = null;
            Unregister(value, subscription);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
