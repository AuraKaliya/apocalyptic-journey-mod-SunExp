using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// Lower-priority presentation layers run first. Tool/user overrides should
    /// use a positive priority so their final state is applied after native and
    /// content-owned layers.
    /// </summary>
    public int Priority { get; set; }

    public Action<AuraCardPresentationContext>? Apply { get; set; }

    public Action<AuraCardPresentationContext>? Reset { get; set; }
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
                AfterCommonCardUse = context => ReapplyAfterCardUse(context, AuraCardLifecycleRouter.CommonCardItemTrueUse),
                AfterAttackCardUse = context => ReapplyAfterCardUse(context, AuraCardLifecycleRouter.AttackCardItemTrueUse),
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
        if (context?.Config == null || context.Root == null) return;
        if (context.Surface == AuraCardPresentationSurface.CombatCard
            && !IsExactCombatContext(context))
        {
            AuraSharedLog.Warn(RuntimeOwnerId,
                "Rejected non-exact combat card presentation context: " + context.Source);
            return;
        }
        if (context.Surface == AuraCardPresentationSurface.CombatCard
            && !AcceptsCombatApply(context.Root))
        {
            return;
        }
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
        var candidates = new List<AuraCardPresentationBindingCandidate>();
        if (setCardStyle)
            candidates.Add(ExplicitSetCardStylePair(context.Arguments));
        else
        {
            candidates.Add(Candidate(context.Target));
            foreach (var argument in context.Arguments ?? Array.Empty<object>())
                candidates.Add(Candidate(argument));
        }
        if (!AuraCardPresentationBindingPolicy.TrySelectExact(candidates, out var binding)) return;
        RequestApply(new AuraCardPresentationContext
        {
            Root = binding.Root as Transform,
            Config = binding.Config as IDataConfig,
            Card = binding.Card as CardItem,
            Source = source,
            Surface = surface
        });
    }

    public static void RequestReset(AuraCardPresentationContext context)
    {
        if (context?.Root == null) return;
        foreach (var pair in Snapshot())
        {
            try
            {
                pair.Value.Reset?.Invoke(context);
            }
            catch (Exception ex)
            {
                AuraSharedLog.Warn(RuntimeOwnerId,
                    "Card presentation reset handler failed: " + pair.Key + " @ " + context.Source + " -> " + ex.Message);
            }
        }
    }

    private static void ReapplyAfterCardUse(ModHookContext context, string source)
    {
        // The native card-use path can rebuild/reflow every hand card after the
        // card's own TrueUse has returned. Apply once to the concrete hook
        // object, then coalesce one final pass on the following frame after the
        // native hierarchy has settled.
        Publish(context, source + ".Immediate", AuraCardPresentationSurface.CombatCard);
        AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
        {
            OwnerId = RuntimeOwnerId,
            Key = "FinalCombatCardPass",
            Source = source + ".FinalPresentation",
            Phase = AuraSharedFramePhase.Presentation,
            Priority = int.MaxValue,
            EstimatedCost = 4,
            Action = () => ReapplyActiveCombatCards(source + ".FinalPresentation")
        });
    }

    private static void ReapplyActiveCombatCards(string source)
    {
        var combatCards = AuraCombatCardZoneSnapshot.Capture(
            null,
            new AuraCombatCardZoneSnapshotOptions
            {
                IncludeFightUiActive = true,
                IncludeFightUiWait = true,
                IncludeExecutorHand = false,
                IncludeExecutorWait = false
            });
        foreach (var reference in combatCards.Cards)
        {
            if (reference.Config == null || reference.Root == null)
            {
                continue;
            }

            RequestApply(new AuraCardPresentationContext
            {
                Root = reference.Root,
                Config = reference.Config,
                Card = reference.Card,
                Source = source,
                Surface = AuraCardPresentationSurface.CombatCard
            });
        }
    }

    private static AuraCardPresentationBindingCandidate ExplicitSetCardStylePair(object[]? arguments)
    {
        var root = (arguments ?? Array.Empty<object>()).OfType<Transform>().FirstOrDefault();
        var config = (arguments ?? Array.Empty<object>()).OfType<IDataConfig>().FirstOrDefault();
        return new AuraCardPresentationBindingCandidate
        {
            Root = root,
            Config = config,
            ExplicitPair = root != null && config != null
        };
    }

    private static AuraCardPresentationBindingCandidate Candidate(object? value)
    {
        if (value is CardItem card && card.dataConfig != null)
        {
            var id = ReadInstanceId(card.dataConfig);
            return new AuraCardPresentationBindingCandidate
            {
                Root = card.transform,
                Config = card.dataConfig,
                Card = card,
                RootInstanceId = id,
                ConfigInstanceId = id,
                SameSource = true
            };
        }
        if (value is Item item && item.dataConfig != null)
        {
            var id = ReadInstanceId(item.dataConfig);
            return new AuraCardPresentationBindingCandidate
            {
                Root = item.transform,
                Config = item.dataConfig,
                RootInstanceId = id,
                ConfigInstanceId = id,
                SameSource = true
            };
        }
        return new AuraCardPresentationBindingCandidate
        {
            Root = (value as UnityEngine.Component)?.transform,
            Config = value as IDataConfig
        };
    }

    private static string ReadInstanceId(IDataConfig config)
    {
        try { return (config?.InstanceID ?? "").Trim(); }
        catch { return ""; }
    }

    private static bool IsExactCombatContext(AuraCardPresentationContext context)
    {
        var card = context.Card;
        if (card == null || card.dataConfig == null || !ReferenceEquals(context.Root, card.transform)) return false;
        if (ReferenceEquals(context.Config, card.dataConfig)) return true;
        var expected = ReadInstanceId(card.dataConfig);
        var actual = ReadInstanceId(context.Config!);
        return expected.Length > 0 && string.Equals(expected, actual, StringComparison.Ordinal);
    }

    private static bool AcceptsCombatApply(Transform root)
    {
        var marker = root.GetComponent<AuraCardPresentationViewMarker>();
        return marker == null || marker.AcceptsApply;
    }

    private static KeyValuePair<string, AuraCardPresentationSubscription>[] Snapshot()
    {
        var current = Volatile.Read(ref snapshot);
        if (current != null) return current;
        lock (Gate)
        {
            if (snapshot != null) return snapshot;
            snapshot = Subscriptions
                .OrderBy(pair => pair.Value.Priority)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
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
