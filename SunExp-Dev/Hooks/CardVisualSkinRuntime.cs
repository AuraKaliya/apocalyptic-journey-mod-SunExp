using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class CardVisualSkinRuntime
{
    private static readonly object ReapplySync = new();
    private static string pendingReapplySource = "";
    private static int pendingReapplyCount;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "ICard.SetCardStyle", ApplyFromSetCardStyle);

        RegisterBefore(modConfig, "CommonCardItem.TrueUse", context => SuppressBurnoutFrameEffect(context, "CommonCardItem.TrueUse"));
        RegisterBefore(modConfig, "AttackCardItem.TrueUse", context => SuppressBurnoutFrameEffect(context, "AttackCardItem.TrueUse"));

        RegisterAfter(modConfig, "CardItem.Init", context => ApplyFromItemRoot(context, "CardItem.Init"));
        RegisterAfter(modConfig, "AttackCardItem.Init", context => ApplyFromItemRoot(context, "AttackCardItem.Init"));
        RegisterAfter(modConfig, "CardItem.DataUpdate", context => ApplyFromItemRoot(context, "CardItem.DataUpdate"));
        RegisterAfter(modConfig, "CardItem.DrawEffect", context => ApplyFromItemRoot(context, "CardItem.DrawEffect"));
        RegisterAfter(modConfig, "CommonCardItem.DrawEffect", context => ApplyFromItemRoot(context, "CommonCardItem.DrawEffect"));
        RegisterAfter(modConfig, "AttackCardItem.DrawEffect", context => ApplyFromItemRoot(context, "AttackCardItem.DrawEffect"));
        RegisterAfter(modConfig, "FightUI.CreateCardItem", context => RequestActiveCombatCardsReapply("FightUI.CreateCardItem"));
        RegisterAfter(modConfig, "FightUI.CreateCardItemInternal", ApplyFromFightUiCreateCardItemInternal);
        RegisterAfter(modConfig, "ScriptExecutor.GetCardFromDeck", context => RequestActiveCombatCardsReapply("ScriptExecutor.GetCardFromDeck"));

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

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Card visual skin " + message));
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

    private static void SuppressBurnoutFrameEffect(ModHookContext context, string source)
    {
        try
        {
            if (context.Target is not Item item)
            {
                return;
            }

            var config = item.dataConfig ?? CardConfigApi.FromActionPayload(context.Target);
            if (!HasBurnoutTag(context.Target as CardItem, config))
            {
                return;
            }

            var visualRoot = FindCardVisualRoot(item.transform);
            var marker = visualRoot == null ? null : visualRoot.GetComponent<CardVisualSkinMarker>();
            if (marker != null && marker.SuppressFrameEffectOverlay(config, source))
            {
                SunExpLog.Debug("Card visual skin suppressed burnout frame effect from " + source + ": " + CardConfigApi.Id(config));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Card visual skin burnout frame-effect suppression failed from " + source, ex);
        }
    }

    private static bool HasBurnoutTag(CardItem? card, IDataConfig? config)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.data, "Tag"), "Burnout")
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.Vars, "Tag"), "Burnout")
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card?.data, "Tag"), "Burnout")
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card?.Vars, "Tag"), "Burnout")
            || card?.Tags.Contains("Burnout") == true;
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
            var config = ConfigFromArgument(context.Arguments, configArgIndex)
                ?? CardConfigApi.FromActionPayload(context.Target);
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
                RequestActiveCombatCardsReapply("FightUI.CreateCardItemInternal");
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

        return CardConfigApi.FromActionPayload(args[index]);
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

    private static Transform? FindCardVisualRoot(Transform? root)
    {
        if (root == null)
        {
            return null;
        }

        if (HasCardVisualNodes(root))
        {
            return root;
        }

        foreach (var path in new[] { "CardItem", "cardItem", "Card", "card", "ShowCard", "DisplayCard", "Item", "Root" })
        {
            var child = root.Find(path);
            if (HasCardVisualNodes(child))
            {
                return child;
            }
        }

        var queue = new Queue<Transform>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited++ < 96)
        {
            var current = queue.Dequeue();
            if (!ReferenceEquals(current, root) && HasCardVisualNodes(current))
            {
                return current;
            }

            for (var i = 0; i < current.childCount; i++)
            {
                queue.Enqueue(current.GetChild(i));
            }
        }

        return root;
    }

    private static bool HasCardVisualNodes(Transform? root)
    {
        return root != null
            && (root.Find("Front/background") != null || root.Find("Front/FrontBack") != null);
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

    private static void RequestActiveCombatCardsReapply(string source)
    {
        lock (ReapplySync)
        {
            pendingReapplySource = source;
            pendingReapplyCount++;
        }

        if (!SunExpFrameScheduler.RunOnceNextFrame("CardVisualSkinRuntime.ReapplyActiveCombatCards", FlushActiveCombatCardsReapply))
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.ReapplyDeduped");
        }
    }

    private static void FlushActiveCombatCardsReapply()
    {
        string source;
        int count;
        lock (ReapplySync)
        {
            source = pendingReapplySource;
            count = pendingReapplyCount;
            pendingReapplySource = "";
            pendingReapplyCount = 0;
        }

        ReapplyActiveCombatCards(count > 1 ? source + ".merged" + count : source + ".merged");
    }

    private static void ApplySafely(Transform? root, IDataConfig? config, string source, bool scheduleDeferred = true)
    {
        if (config == null)
        {
            return;
        }

        var visualRoot = FindCardVisualRoot(root);
        var applied = CardVisualSkinApplier.Apply(visualRoot, config);
        if (applied)
        {
            SunExpPerformanceCounters.Record("CardVisualSkin.Apply");
            SunExpLog.Debug("Card visual skin applied from " + source + ": " + DictionaryUtil.Get(config.data, "Id", "unknown"));
        }

        if (scheduleDeferred && visualRoot != null)
        {
            var key = "CardVisualSkinRuntime.Deferred." + source + "." + visualRoot.GetInstanceID();
            SunExpFrameScheduler.RunOnceNextFrame(key, () => ApplySafely(visualRoot, config, source + ".deferred", scheduleDeferred: false));
        }
    }
}
