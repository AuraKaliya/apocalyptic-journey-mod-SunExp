using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class SunExpCardPresentationLifecycleBridge
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
        SunExpCardLifecycleRouter.Register("CardPresentationBridge", new SunExpCardLifecycleSubscription
        {
            AfterSetCardStyle = ApplyFromSetCardStyle,
            AfterCardItemInit = context => ApplyFromLifecycle(context, "CardItem.Init", SunExpCardPresentationSurface.CombatCard),
            AfterAttackCardItemInit = context => ApplyFromLifecycle(context, "AttackCardItem.Init", SunExpCardPresentationSurface.CombatCard),
            AfterCardChoiceItemInitialize = context => ApplyFromLifecycle(context, "CardChoiceItem.Initialize", SunExpCardPresentationSurface.RewardChoice),
            AfterDictItemInit = context => ApplyFromLifecycle(context, "DictItem.Init", SunExpCardPresentationSurface.Dictionary),
            AfterDictionaryShowItemInit = context => ApplyFromLifecycle(context, "DictionaryShowItem.Init", SunExpCardPresentationSurface.Dictionary),
            AfterDisplayCardInit = context => ApplyFromLifecycle(context, "DisplayCard.Init", SunExpCardPresentationSurface.Display),
            AfterShowCardInit = context => ApplyFromLifecycle(context, "ShowCard.Init", SunExpCardPresentationSurface.Display),
            AfterSafeBoxItemInit = context => ApplyFromLifecycle(context, "SafeBoxItem.Init", SunExpCardPresentationSurface.SafeBox),
            AfterEnchCardItemInit = context => ApplyFromLifecycle(context, "EnchCardItem.Init", SunExpCardPresentationSurface.Display),
            AfterPackShowItemInit = context => ApplyFromLifecycle(context, "PackShowItem.Init", SunExpCardPresentationSurface.CardPack),
            AfterShopItemInit = context => ApplyFromLifecycle(context, "ShopItem.Init", SunExpCardPresentationSurface.Shop),
            AfterWarehouseItemInit = context => ApplyFromLifecycle(context, "WarehouseItem.Init", SunExpCardPresentationSurface.Warehouse)
        });
        SunExpLog.InfoAlways("Card presentation lifecycle bridge initialized with stable card UI lifecycle hooks");
    }

    private static void ApplyFromSetCardStyle(ModHookContext context)
    {
        var args = context.Arguments;
        observedSetCardStyle++;
        SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleObserved");

        if (!TryExtractSetCardStyleArguments(args, out var transform, out var config))
        {
            if (!loggedMissingArguments)
            {
                loggedMissingArguments = true;
                SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleArgumentMiss");
                SunExpLog.Warn("Card presentation SetCardStyle hook observed but arguments were not Transform + IDataConfig: "
                    + ArgumentShape(args)
                    + ", observed="
                    + observedSetCardStyle);
            }

            return;
        }

        SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleArgumentHit");
        if (!loggedFirstDispatch)
        {
            loggedFirstDispatch = true;
            SunExpLog.InfoAlways("Card presentation SetCardStyle hook dispatching: cardId="
                + CardConfigApi.Id(config)
                + ", root="
                + (transform == null ? "<null>" : transform.name)
                + ", shape="
                + ArgumentShape(args));
        }

        SunExpCardPresentationRouter.RequestApply(transform, config, SunExpHookTargets.ICardSetCardStyle, SunExpCardPresentationSurface.CardStyle);
    }

    private static void ApplyFromLifecycle(ModHookContext context, string source, SunExpCardPresentationSurface surface)
    {
        observedLifecycle++;
        SunExpPerformanceCounters.Record("CardPresentation.LifecycleObserved");
        if (!TryExtractLifecycleArguments(context, out var transform, out var config))
        {
            SunExpPerformanceCounters.Record("CardPresentation.LifecycleArgumentMiss");
            return;
        }

        SunExpPerformanceCounters.Record("CardPresentation.LifecycleArgumentHit");
        if (!loggedFirstLifecycleDispatch)
        {
            loggedFirstLifecycleDispatch = true;
            SunExpLog.InfoAlways("Card presentation lifecycle dispatching: source="
                + source
                + ", cardId="
                + CardConfigApi.Id(config)
                + ", root="
                + (transform == null ? "<null>" : transform.name)
                + ", observed="
                + observedLifecycle);
        }

        SunExpCardPresentationRouter.RequestApply(transform, config, source, surface);
    }

    private static bool TryExtractLifecycleArguments(ModHookContext context, out Transform? transform, out IDataConfig? config)
    {
        transform = null;
        config = null;

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
                SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleArgumentRecovered");
                SunExpLog.Warn("Card presentation SetCardStyle hook used recovered argument shape: " + ArgumentShape(args));
            }

            return true;
        }

        if (!loggedUnexpectedShape)
        {
            loggedUnexpectedShape = true;
            SunExpPerformanceCounters.Record("CardPresentation.SetCardStyleUnexpectedShape");
            SunExpLog.Warn("Card presentation SetCardStyle hook argument shape unsupported: " + ArgumentShape(args));
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
