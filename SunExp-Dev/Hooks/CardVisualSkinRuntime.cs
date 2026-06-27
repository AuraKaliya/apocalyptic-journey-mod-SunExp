using System;
using AuraShared.Core;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class CardVisualSkinRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "ICard.SetCardStyle", ApplyFromSetCardStyle);

        RegisterAfter(modConfig, "CardItem.Init", context => ApplyFromItemRoot(context, "CardItem.Init"));
        RegisterAfter(modConfig, "AttackCardItem.Init", context => ApplyFromItemRoot(context, "AttackCardItem.Init"));
        RegisterAfter(modConfig, "CardItem.DataUpdate", context => ApplyFromItemRoot(context, "CardItem.DataUpdate"));

        RegisterAfter(modConfig, "DictItem.Init", context => ApplyFromArgumentRoot(context, 0, null, "DictItem.Init"));
        RegisterAfter(modConfig, "DictionaryShowItem.Init", context => ApplyFromArgumentRoot(context, 0, null, "DictionaryShowItem.Init"));
        RegisterAfter(modConfig, "DisplayCard.Init", context => ApplyFromArgumentRoot(context, 0, null, "DisplayCard.Init"));
        RegisterAfter(modConfig, "ShowCard.Init", ApplyFromShowCard);
        RegisterAfter(modConfig, "SafeBoxItem.Init", ApplyFromSafeBoxItem);
        RegisterAfter(modConfig, "EnchCardItem.Init", context => ApplyFromArgumentRoot(context, 0, null, "EnchCardItem.Init"));
        RegisterAfter(modConfig, "CardChoiceItem.Initialize", ApplyFromCardChoiceItem);

        RegisterAfter(modConfig, "PackShowItem.Init", context => ApplyFromArgumentRoot(context, 0, "CardItem", "PackShowItem.Init"));
        RegisterAfter(modConfig, "ShopItem.Init", context => ApplyFromArgumentRoot(context, 0, "CardItem", "ShopItem.Init"));
        RegisterAfter(modConfig, "WarehouseItem.Init", context => ApplyFromArgumentRoot(context, 2, "CardItem", "WarehouseItem.Init"));

        SunExpLog.Info("Card visual skin runtime initialized");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Card visual skin " + message));
    }

    private static void ApplyFromSetCardStyle(ModHookContext context)
    {
        try
        {
            var args = context.Arguments;
            if (args == null
                || args.Length < 2
                || args[0] is not Transform transform
                || args[1] is not IDataConfig config)
            {
                return;
            }

            ApplySafely(transform, config, "ICard.SetCardStyle");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card visual skin SetCardStyle hook failed", ex);
        }
    }

    private static void ApplyFromItemRoot(ModHookContext context, string source)
    {
        try
        {
            if (context.Target is not Item item)
            {
                return;
            }

            ApplySafely(item.transform, item.dataConfig, source);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card visual skin " + source + " hook failed", ex);
        }
    }

    private static void ApplyFromArgumentRoot(ModHookContext context, int configArgIndex, string? childPath, string source)
    {
        try
        {
            var config = ConfigFromArgument(context.Arguments, configArgIndex);
            var root = RootFromTarget(context.Target, childPath);
            ApplySafely(root, config, source);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card visual skin " + source + " hook failed", ex);
        }
    }

    private static void ApplyFromShowCard(ModHookContext context)
    {
        var args = context.Arguments;
        if (args != null && args.Length > 1 && args[1] is bool equipped && equipped)
        {
            return;
        }

        ApplyFromArgumentRoot(context, 0, null, "ShowCard.Init");
    }

    private static void ApplyFromSafeBoxItem(ModHookContext context)
    {
        if (context.Target is SafeBoxItem { InBackPack: false })
        {
            return;
        }

        ApplyFromArgumentRoot(context, 0, null, "SafeBoxItem.Init");
    }

    private static void ApplyFromCardChoiceItem(ModHookContext context)
    {
        try
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

            ApplySafely(RootFromTarget(context.Target, null), new DataConfig(cardId, DataType.Card), "CardChoiceItem.Initialize");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card visual skin CardChoiceItem.Initialize hook failed", ex);
        }
    }

    private static IDataConfig? ConfigFromArgument(object[]? args, int index)
    {
        if (args == null || index < 0 || args.Length <= index)
        {
            return null;
        }

        return args[index] as IDataConfig;
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

    private static void ApplySafely(Transform? root, IDataConfig? config, string source)
    {
        if (config == null)
        {
            return;
        }

        var applied = CardVisualSkinApplier.Apply(root, config);
        if (applied)
        {
            SunExpLog.Debug("Card visual skin applied from " + source + ": " + DictionaryUtil.Get(config.data, "Id", "unknown"));
        }
    }
}
