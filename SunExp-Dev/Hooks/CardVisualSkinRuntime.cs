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
        RegisterAfter(modConfig, "CardItem.DrawEffect", context => ApplyFromItemRoot(context, "CardItem.DrawEffect"));
        RegisterAfter(modConfig, "CommonCardItem.DrawEffect", context => ApplyFromItemRoot(context, "CommonCardItem.DrawEffect"));
        RegisterAfter(modConfig, "AttackCardItem.DrawEffect", context => ApplyFromItemRoot(context, "AttackCardItem.DrawEffect"));
        RegisterAfter(modConfig, "FightUI.CreateCardItem", context => ReapplyActiveCombatCardsNowAndLater("FightUI.CreateCardItem"));
        RegisterAfter(modConfig, "FightUI.CreateCardItemInternal", ApplyFromFightUiCreateCardItemInternal);
        RegisterAfter(modConfig, "ScriptExecutor.GetCardFromDeck", context => ReapplyActiveCombatCardsNowAndLater("ScriptExecutor.GetCardFromDeck"));

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

    private static void ApplyFromFightUiCreateCardItemInternal(ModHookContext context)
    {
        try
        {
            var config = ConfigFromArgument(context.Arguments, 0);
            if (config != null)
            {
                ApplySafely(FindCombatCardRoot(config), config, "FightUI.CreateCardItemInternal");
                ReapplyActiveCombatCardsNowAndLater("FightUI.CreateCardItemInternal");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card visual skin FightUI.CreateCardItemInternal hook failed", ex);
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

    private static Transform? FindCombatCardRoot(IDataConfig config)
    {
        try
        {
            var items = FightUI.cardItemList;
            if (items == null)
            {
                return null;
            }

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (item != null && ReferenceEquals(item.dataConfig, config))
                {
                    return item.transform;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Card visual skin combat-card lookup failed: " + ex.Message);
        }

        return null;
    }

    private static void ReapplyActiveCombatCards(string source)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var items = FightUI.cardItemList;
            if (items == null || items.Count == 0)
            {
                return;
            }

            var applied = 0;
            foreach (var item in items)
            {
                if (item?.dataConfig == null)
                {
                    continue;
                }

                if (CardVisualSkinApplier.Apply(item.transform, item.dataConfig))
                {
                    applied++;
                }
            }

            if (applied > 0)
            {
                SunExpLog.Debug("Card visual skin reapplied from " + source + ": " + applied);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card visual skin active-combat reapply failed from " + source, ex);
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("CardVisualSkin.ReapplyActiveCombatCards", start);
        }
    }

    private static void ReapplyActiveCombatCardsNowAndLater(string source)
    {
        ReapplyActiveCombatCards(source);
        ReapplyActiveCombatCardsDelayed(source, 1);
    }

    private static void ReapplyActiveCombatCardsDelayed(string source, int pass)
    {
        SunExpFrameScheduler.RunOnceNextFrame(
            "CardVisualSkinRuntime.ReapplyActiveCombatCards.Pass" + pass,
            () =>
            {
                ReapplyActiveCombatCards(source + ".delayed" + pass);
            });
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
            SunExpPerformanceCounters.Record("CardVisualSkin.Apply");
            SunExpLog.Debug("Card visual skin applied from " + source + ": " + DictionaryUtil.Get(config.data, "Id", "unknown"));
        }
    }
}
