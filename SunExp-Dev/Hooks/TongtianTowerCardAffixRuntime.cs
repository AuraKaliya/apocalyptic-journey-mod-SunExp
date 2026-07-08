using System;
using System.Collections.Generic;
using System.Reflection;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerCardAffixRuntime
{
    private const string CombatNormalizeFrameKey = "TongtianTowerCardAffix.NormalizeCombatCards";

    private static readonly FieldInfo? CardChoiceItemDataConfigField = typeof(CardChoiceItem).GetField(
        "dataConfig",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "CardChoiceItem.Initialize", ApplyToChoiceItem);
        RegisterBefore(modConfig, "CardChoiceUI.Select", ApplyToSelectedCard);
        RegisterAfter(modConfig, "CardItem.Init", ApplyToCardItem);
        RegisterAfter(modConfig, "AttackCardItem.Init", ApplyToCardItem);
        RegisterAfter(modConfig, "CardItem.DataUpdate", ApplyToCardItem);
        RegisterAfter(modConfig, "AttackCardItem.DataUpdate", ApplyToCardItem);
        RegisterAfter(modConfig, "FightUI.CreateCardItem", NormalizeCombatCards);
        RegisterAfter(modConfig, "FightUI.CreateCardItemInternal", NormalizeCombatCards);
        RegisterAfter(modConfig, "ScriptExecutor.GetCardFromDeck", NormalizeScriptExecutorCards);
        RegisterAfter(modConfig, "PlayerInfo.AddCard", NormalizeOwnedCards);
        RegisterAfter(modConfig, "PlayerInfo.AddCardById", NormalizeOwnedCards);
        RegisterAfter(modConfig, "PlayerInfo.RandomAddCard", NormalizeOwnedCards);
        RegisterAfter(modConfig, "ShopItem.Init", ApplyToFirstDataConfigArgument);
        RegisterAfter(modConfig, "PackShowItem.Init", ApplyToFirstDataConfigArgument);
        RegisterAfter(modConfig, "WarehouseItem.Init", ApplyToFirstDataConfigArgument);
        RegisterAfter(modConfig, "SafeBoxItem.Init", ApplyToFirstDataConfigArgument);
        SunExpLog.Info("Tongtian Tower card affix runtime initialized");
    }

    private static void ApplyToChoiceItem(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun()
                || context.Target is not CardChoiceItem item
                || CardChoiceItemDataConfigField?.GetValue(item) is not DataConfig config)
            {
                return;
            }

            if (!TongtianTowerCardAffixService.ApplyBurnout(config, "CardChoiceItem.Initialize"))
            {
                return;
            }

            RefreshChoiceItem(item, config);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] choice item hook failed", ex);
        }
    }

    private static void ApplyToSelectedCard(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                return;
            }

            var args = context.Arguments;
            if (args == null || args.Length < 2 || args[1] is not IDataConfig config)
            {
                return;
            }

            TongtianTowerCardAffixService.ApplyBurnout(config, "CardChoiceUI.Select");
            TongtianTowerCardAffixService.NormalizeOwnedCards("CardChoiceUI.Select");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] selected card hook failed", ex);
        }
    }

    private static void ApplyToCardItem(ModHookContext context)
    {
        try
        {
            if (TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                TongtianTowerCardAffixService.ApplyBurnout(context.Target as CardItem, "CardItem");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] card item hook failed", ex);
        }
    }

    private static void NormalizeCombatCards(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                return;
            }

            var handledTarget = false;
            if (context.Arguments != null)
            {
                foreach (var arg in context.Arguments)
                {
                    if (arg is CardItem card)
                    {
                        handledTarget = true;
                        TongtianTowerCardAffixService.ApplyBurnout(card, "FightUI.CreateCardItem:arg");
                    }
                    else if (arg is IDataConfig config)
                    {
                        handledTarget = true;
                        TongtianTowerCardAffixService.ApplyBurnout(config, "FightUI.CreateCardItem:arg");
                    }
                }
            }

            if (!handledTarget)
            {
                QueueCombatNormalize(null, "FightUI.CreateCardItem");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] combat card normalization failed", ex);
        }
    }

    private static void NormalizeScriptExecutorCards(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                return;
            }

            if (context.Arguments != null && context.Arguments.Length > 0 && context.Arguments[0] is IDataConfig config)
            {
                TongtianTowerCardAffixService.ApplyBurnout(config, "ScriptExecutor.GetCardFromDeck");
                return;
            }

            QueueCombatNormalize(context.Target as ScriptExecutor, "ScriptExecutor.GetCardFromDeck");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] script executor card normalization failed", ex);
        }
    }

    private static void NormalizeOwnedCards(ModHookContext context)
    {
        try
        {
            if (TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                var handledTarget = false;
                var changed = false;
                foreach (var config in DataConfigsFromArguments(context.Arguments))
                {
                    handledTarget = true;
                    changed |= TongtianTowerCardAffixService.ApplyBurnout(config, "PlayerInfo.AddCard:arg");
                }

                if (changed)
                {
                    TongtianTowerCardAffixService.TryPersistCurrentRole("PlayerInfo.AddCard:target");
                }
                else if (!handledTarget)
                {
                    TongtianTowerCardAffixService.NormalizeRecentOwnedCards(ParseGrantedCardCount(context.Arguments), "PlayerInfo.AddCard");
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] owned card normalization failed", ex);
        }
    }

    private static void ApplyToFirstDataConfigArgument(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                return;
            }

            var args = context.Arguments;
            if (args == null)
            {
                return;
            }

            foreach (var arg in args)
            {
                if (arg is IDataConfig config)
                {
                    TongtianTowerCardAffixService.ApplyBurnout(config, "CardDisplay.Init");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] display card hook failed", ex);
        }
    }

    private static void RefreshChoiceItem(CardChoiceItem item, DataConfig config)
    {
        ICard.SetCardMsg(item.transform, config, null);
        item.DataUpdate();
    }

    private static void QueueCombatNormalize(ScriptExecutor? executor, string source)
    {
        if (!SunExpFrameDispatcher.RunOnceNextFrame(
                CombatNormalizeFrameKey + ":" + source,
                () => TongtianTowerCardAffixService.NormalizeCombatCards(executor, source + ":deferred")))
        {
            SunExpPerformanceCounters.Record("TongtianTowerCardAffix.NormalizeCombatCards.Deduped");
        }
    }

    private static IEnumerable<IDataConfig> DataConfigsFromArguments(object[]? args)
    {
        if (args == null)
        {
            yield break;
        }

        foreach (var arg in args)
        {
            if (arg is IDataConfig config)
            {
                yield return config;
            }
        }
    }

    private static int ParseGrantedCardCount(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return 1;
        }

        var text = Convert.ToString(args[0])?.Trim() ?? "";
        return int.TryParse(text, out var count) && count > 0 ? Math.Min(16, count) : 1;
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian Tower card affix " + message));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian Tower card affix " + message));
    }
}
