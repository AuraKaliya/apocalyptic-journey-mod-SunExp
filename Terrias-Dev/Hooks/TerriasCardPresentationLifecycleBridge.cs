using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class TerriasCardPresentationLifecycleBridge
{
    private static int observedSetCardStyle;
    private static int observedLifecycle;
    private static bool loggedUnexpectedShape;
    private static bool loggedMissingArguments;
    private static bool loggedRecoveredShape;
    private static bool loggedFirstDispatch;
    private static bool loggedFirstLifecycleDispatch;

    public static void Initialize()
    {
        TerriasCardLifecycleRouter.Register("CardPresentationBridge", new TerriasCardLifecycleSubscription
        {
            AfterSetCardStyle = ApplyFromSetCardStyle,
            AfterCardItemInit = context => ApplyFromLifecycle(context, "CardItem.Init", TerriasCardPresentationSurface.CombatCard),
            AfterAttackCardItemInit = context => ApplyFromLifecycle(context, "AttackCardItem.Init", TerriasCardPresentationSurface.CombatCard),
            AfterCardChoiceItemInitialize = context => ApplyFromLifecycle(context, "CardChoiceItem.Initialize", TerriasCardPresentationSurface.RewardChoice),
            AfterDictItemInit = context => ApplyFromLifecycle(context, "DictItem.Init", TerriasCardPresentationSurface.Dictionary),
            AfterDictionaryShowItemInit = context => ApplyFromLifecycle(context, "DictionaryShowItem.Init", TerriasCardPresentationSurface.Dictionary),
            AfterDisplayCardInit = context => ApplyFromLifecycle(context, "DisplayCard.Init", TerriasCardPresentationSurface.Display),
            AfterShowCardInit = context => ApplyFromLifecycle(context, "ShowCard.Init", TerriasCardPresentationSurface.Display),
            AfterSafeBoxItemInit = context => ApplyFromLifecycle(context, "SafeBoxItem.Init", TerriasCardPresentationSurface.SafeBox),
            AfterEnchCardItemInit = context => ApplyFromLifecycle(context, "EnchCardItem.Init", TerriasCardPresentationSurface.Display),
            AfterPackShowItemInit = context => ApplyFromLifecycle(context, "PackShowItem.Init", TerriasCardPresentationSurface.CardPack),
            AfterShopItemInit = context => ApplyFromLifecycle(context, "ShopItem.Init", TerriasCardPresentationSurface.Shop),
            AfterWarehouseItemInit = context => ApplyFromLifecycle(context, "WarehouseItem.Init", TerriasCardPresentationSurface.Warehouse)
        });
        TerriasLog.InfoAlways("Card presentation lifecycle bridge initialized with stable card UI lifecycle hooks");
    }

    private static void ApplyFromSetCardStyle(ModHookContext context)
    {
        var args = context.Arguments;
        observedSetCardStyle++;
        TerriasPerformanceCounters.Record("CardPresentation.SetCardStyleObserved");

        if (!TryExtractSetCardStyleArguments(args, out var transform, out var config))
        {
            if (!loggedMissingArguments)
            {
                loggedMissingArguments = true;
                TerriasPerformanceCounters.Record("CardPresentation.SetCardStyleArgumentMiss");
                TerriasLog.Warn("Card presentation SetCardStyle hook observed but arguments were not Transform + IDataConfig: "
                    + ArgumentShape(args)
                    + ", observed="
                    + observedSetCardStyle);
            }

            return;
        }

        TerriasPerformanceCounters.Record("CardPresentation.SetCardStyleArgumentHit");
        if (!loggedFirstDispatch)
        {
            loggedFirstDispatch = true;
            TerriasLog.InfoAlways("Card presentation SetCardStyle hook dispatching: cardId="
                + CardConfigApi.Id(config)
                + ", root="
                + (transform == null ? "<null>" : transform.name)
                + ", shape="
                + ArgumentShape(args));
        }

        TerriasCardPresentationRouter.RequestApply(transform, config, TerriasHookTargets.ICardSetCardStyle, TerriasCardPresentationSurface.CardStyle);
    }

    private static void ApplyFromLifecycle(ModHookContext context, string source, TerriasCardPresentationSurface surface)
    {
        observedLifecycle++;
        TerriasPerformanceCounters.Record("CardPresentation.LifecycleObserved");
        if (!TryExtractLifecycleArguments(context, out var transform, out var config, out var card))
        {
            TerriasPerformanceCounters.Record("CardPresentation.LifecycleArgumentMiss");
            return;
        }

        TerriasPerformanceCounters.Record("CardPresentation.LifecycleArgumentHit");
        if (!loggedFirstLifecycleDispatch)
        {
            loggedFirstLifecycleDispatch = true;
            TerriasLog.InfoAlways("Card presentation lifecycle dispatching: source="
                + source
                + ", cardId="
                + CardConfigApi.Id(config)
                + ", root="
                + (transform == null ? "<null>" : transform.name)
                + ", observed="
                + observedLifecycle);
        }

        TerriasCardPresentationRouter.RequestApply(new TerriasCardPresentationContext
        {
            Root = transform,
            Config = config,
            Card = card,
            Source = source,
            Surface = surface
        });
    }

    private static bool TryExtractLifecycleArguments(
        ModHookContext context,
        out Transform? transform,
        out IDataConfig? config,
        out CardItem? card)
    {
        transform = null;
        config = null;
        card = context.Target as CardItem;

        ReadLifecycleObject(context.Target, ref transform, ref config);
        var args = context.Arguments;
        if (args != null)
        {
            foreach (var arg in args)
            {
                ReadLifecycleObject(arg, ref transform, ref config);
                if (transform != null && config != null)
                {
                    break;
                }
            }
        }

        if (card?.dataConfig != null)
        {
            transform = card.transform;
            config = card.dataConfig;
        }

        return config != null;
    }

    private static void ReadLifecycleObject(object? value, ref Transform? transform, ref IDataConfig? config)
    {
        if (value == null)
        {
            return;
        }

        if (value is IDataConfig dataConfig)
        {
            config ??= dataConfig;
        }

        if (value is Item item)
        {
            transform ??= item.transform;
            config ??= item.dataConfig;
        }

        if (value is UnityEngine.Component component)
        {
            transform ??= component.transform;
        }
    }

    private static bool TryExtractSetCardStyleArguments(object[]? args, out Transform? transform, out IDataConfig? config)
    {
        transform = null;
        config = null;
        if (args == null || args.Length == 0)
        {
            return false;
        }

        if (args.Length >= 2 && args[0] is Transform directTransform && args[1] is IDataConfig directConfig)
        {
            transform = directTransform;
            config = directConfig;
            return true;
        }

        foreach (var arg in args)
        {
            transform ??= arg as Transform;
            config ??= arg as IDataConfig;
        }

        if (transform != null && config != null)
        {
            if (!loggedRecoveredShape)
            {
                loggedRecoveredShape = true;
                TerriasPerformanceCounters.Record("CardPresentation.SetCardStyleArgumentRecovered");
                TerriasLog.Warn("Card presentation SetCardStyle hook used recovered argument shape: " + ArgumentShape(args));
            }

            return true;
        }

        if (!loggedUnexpectedShape)
        {
            loggedUnexpectedShape = true;
            TerriasPerformanceCounters.Record("CardPresentation.SetCardStyleUnexpectedShape");
            TerriasLog.Warn("Card presentation SetCardStyle hook argument shape unsupported: " + ArgumentShape(args));
        }

        return false;
    }

    private static string ArgumentShape(object[]? args)
    {
        if (args == null)
        {
            return "<null>";
        }

        if (args.Length == 0)
        {
            return "<empty>";
        }

        var parts = new string[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            parts[i] = args[i]?.GetType().FullName ?? "<null>";
        }

        return string.Join(", ", parts);
    }
}
