using System;
using System.Collections.Generic;
using System.Reflection;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class EndlessSeaCardAffixRuntime
{
    private const string CombatNormalizeFrameKey = "EndlessSeaCardAffix.NormalizeCombatCards";
    private static bool cardLifecycleRegistered;
    private static IDisposable? cardLifecycleRegistration;

    private static readonly FieldInfo? CardChoiceItemDataConfigField = typeof(CardChoiceItem).GetField(
        "dataConfig",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Initialize(ModConfig modConfig)
    {
        TerriasBattleLifecycleRouter.Register("EndlessSeaCardAffix.Activator", new TerriasBattleLifecycleSubscription
        {
            BattleOpening = _ => EnsureCardLifecycleRegisteredForEndlessSea(),
            BattleRestarting = _ => ReleaseCardLifecycle(),
            BattleSettling = _ => ReleaseCardLifecycle()
        });
        EnsureCardLifecycleRegisteredForEndlessSea();
        TerriasLog.Info("Endless Sea card affix runtime initialized");
    }

    private static void EnsureCardLifecycleRegisteredForEndlessSea()
    {
        if (EndlessSeaModeRuntime.IsEndlessSeaRun())
        {
            EnsureCardLifecycleRegistered();
        }
    }

    private static void EnsureCardLifecycleRegistered()
    {
        if (cardLifecycleRegistered)
        {
            return;
        }

        cardLifecycleRegistered = true;
        cardLifecycleRegistration = TerriasCardLifecycleRouter.Register("EndlessSeaCardAffix", new TerriasCardLifecycleSubscription
        {
            AfterCardChoiceItemInitialize = ApplyToChoiceItem,
            BeforeCardChoiceUiSelect = ApplyToSelectedCard,
            AfterCardItemInit = ApplyToCardItem,
            AfterAttackCardItemInit = ApplyToCardItem,
            AfterCardItemDataUpdate = ApplyToCardItem,
            AfterAttackCardItemDataUpdate = ApplyToCardItem,
            AfterFightUiCreateCardItem = NormalizeCombatCards,
            AfterFightUiCreateCardItemInternal = NormalizeCombatCards,
            AfterScriptExecutorGetCardFromDeck = NormalizeScriptExecutorCards,
            AfterPlayerInfoAddCard = NormalizeOwnedCards,
            AfterPlayerInfoAddCardById = NormalizeOwnedCards,
            AfterPlayerInfoRandomAddCard = NormalizeOwnedCards,
            AfterShopItemInit = ApplyToFirstDataConfigArgument,
            AfterPackShowItemInit = ApplyToFirstDataConfigArgument,
            AfterWarehouseItemInit = ApplyToFirstDataConfigArgument,
            AfterSafeBoxItemInit = ApplyToFirstDataConfigArgument
        });
    }

    private static void ReleaseCardLifecycle()
    {
        cardLifecycleRegistration?.Dispose();
        cardLifecycleRegistration = null;
        cardLifecycleRegistered = false;
    }

    private static void ApplyToChoiceItem(ModHookContext context)
    {
        try
        {
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun()
                || context.Target is not CardChoiceItem item
                || CardChoiceItemDataConfigField?.GetValue(item) is not DataConfig config)
            {
                return;
            }

            if (!EndlessSeaCardAffixService.ApplyBurnout(config, "CardChoiceItem.Initialize"))
            {
                return;
            }

            RefreshChoiceItem(item);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[EndlessSeaCardAffix] choice item hook failed", ex);
        }
    }

    private static void ApplyToSelectedCard(ModHookContext context)
    {
        try
        {
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                return;
            }

            var args = context.Arguments;
            if (args == null || args.Length < 2 || args[1] is not IDataConfig config)
            {
                return;
            }

            EndlessSeaCardAffixService.ApplyBurnout(config, "CardChoiceUI.Select");
            EndlessSeaCardAffixService.NormalizeOwnedCards("CardChoiceUI.Select");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[EndlessSeaCardAffix] selected card hook failed", ex);
        }
    }

    private static void ApplyToCardItem(ModHookContext context)
    {
        try
        {
            if (EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                EndlessSeaCardAffixService.ApplyBurnout(context.Target as CardItem, "CardItem");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[EndlessSeaCardAffix] card item hook failed", ex);
        }
    }

    private static void NormalizeCombatCards(ModHookContext context)
    {
        try
        {
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
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
                        EndlessSeaCardAffixService.ApplyBurnout(card, "FightUI.CreateCardItem:arg");
                    }
                    else if (arg is IDataConfig config)
                    {
                        handledTarget = true;
                        EndlessSeaCardAffixService.ApplyBurnout(config, "FightUI.CreateCardItem:arg");
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
            TerriasLog.Error("[EndlessSeaCardAffix] combat card normalization failed", ex);
        }
    }

    private static void NormalizeScriptExecutorCards(ModHookContext context)
    {
        try
        {
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                return;
            }

            if (context.Arguments != null && context.Arguments.Length > 0 && context.Arguments[0] is IDataConfig config)
            {
                EndlessSeaCardAffixService.ApplyBurnout(config, "ScriptExecutor.GetCardFromDeck");
                return;
            }

            QueueCombatNormalize(context.Target as ScriptExecutor, "ScriptExecutor.GetCardFromDeck");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[EndlessSeaCardAffix] script executor card normalization failed", ex);
        }
    }

    private static void NormalizeOwnedCards(ModHookContext context)
    {
        try
        {
            if (EndlessSeaModeRuntime.IsEndlessSeaRun())
            {
                var handledTarget = false;
                var changed = false;
                foreach (var config in DataConfigsFromArguments(context.Arguments))
                {
                    handledTarget = true;
                    changed |= EndlessSeaCardAffixService.ApplyBurnout(config, "PlayerInfo.AddCard:arg");
                }

                if (changed)
                {
                    EndlessSeaCardAffixService.TryPersistCurrentRole("PlayerInfo.AddCard:target");
                }
                else if (!handledTarget)
                {
                    EndlessSeaCardAffixService.NormalizeRecentOwnedCards(ParseGrantedCardCount(context.Arguments), "PlayerInfo.AddCard");
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[EndlessSeaCardAffix] owned card normalization failed", ex);
        }
    }

    private static void ApplyToFirstDataConfigArgument(ModHookContext context)
    {
        try
        {
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
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
                    EndlessSeaCardAffixService.ApplyBurnout(config, "CardDisplay.Init");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[EndlessSeaCardAffix] display card hook failed", ex);
        }
    }

    private static void RefreshChoiceItem(CardChoiceItem item)
    {
        item.DataUpdate();
    }

    private static void QueueCombatNormalize(ScriptExecutor? executor, string source)
    {
        if (!TerriasFrameDispatcher.RunOnceNextFrame(
                CombatNormalizeFrameKey + ":" + source,
                () => EndlessSeaCardAffixService.NormalizeCombatCards(executor, source + ":deferred")))
        {
            TerriasPerformanceCounters.Record("EndlessSeaCardAffix.NormalizeCombatCards.Deduped");
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

}
