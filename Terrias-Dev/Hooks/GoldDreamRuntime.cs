using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class GoldDreamRuntime
{
    private static readonly Stack<IDataConfig> PendingGoldDreamCards = new();
    private static bool actionHandlerRegistered;
    private static INotifyPropertyChanged? observedRoleTable;

    public static void Initialize(ModConfig modConfig)
    {
        EnsureActionHandler();
        TerriasBattleLifecycleRouter.Register("GoldDream", new TerriasBattleLifecycleSubscription
        {
            FightInitializing = _ => Reset("FightInitializing"),
            FightStarted = _ => ActivateFromCombatDeck(),
            FightRestarting = _ => Reset("FightRestarting"),
            FightEnding = _ => Reset("FightEnding")
        });
        TerriasHookRegistry.After(
            modConfig,
            "ScriptExecutor.ChangeMoney",
            _ => GoldDreamEconomyService.NotifyMoneyChanged(),
            "GoldDream.MoneyChanged");
        GoldDreamEconomyService.PaymentStateChanged -= OnPaymentStateChanged;
        GoldDreamEconomyService.PaymentStateChanged += OnPaymentStateChanged;
    }

    private static void EnsureActionHandler()
    {
        if (actionHandlerRegistered)
        {
            return;
        }

        TerriasActionEventRouter.RegisterHandler("GoldDream", OnAction, OnActionAfter);
        actionHandlerRegistered = true;
    }

    private static void OnAction(TerriasActionEventContext context)
    {
        if (CardConfigApi.HasGoldDream(context.Config))
        {
            PendingGoldDreamCards.Push(context.Config!);
        }
    }

    private static void OnActionAfter()
    {
        if (PendingGoldDreamCards.Count == 0)
        {
            return;
        }

        var config = PendingGoldDreamCards.Pop();
        if (CardConfigApi.TryClaimGoldDreamSkipOnce(config))
        {
            return;
        }

        GoldDreamEconomyService.ApplyGoldDream(config.scriptExecutor as ScriptExecutor);
    }

    private static void ActivateFromCombatDeck()
    {
        BindMoneyChanges();
        try
        {
            var manager = FightCardManager.Instance;
            if (manager == null)
            {
                return;
            }

            var cards = manager.cardList.Cast<IDataConfig>()
                .Concat(manager.usedCardList.Cast<IDataConfig>());
            var config = cards.FirstOrDefault(IsGoldDreamPackCard);
            if (config?.scriptExecutor is ScriptExecutor executor)
            {
                GoldDreamEconomyService.Activate(executor);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Gold Dream deck activation skipped: " + ex.Message);
        }
    }

    private static void BindMoneyChanges()
    {
        var current = RoleTable.Instance as INotifyPropertyChanged;
        if (ReferenceEquals(observedRoleTable, current))
        {
            return;
        }

        if (observedRoleTable != null)
        {
            observedRoleTable.PropertyChanged -= OnRoleTableChanged;
        }

        observedRoleTable = current;
        if (observedRoleTable != null)
        {
            observedRoleTable.PropertyChanged += OnRoleTableChanged;
        }
    }

    private static void OnRoleTableChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, "Money", StringComparison.Ordinal))
        {
            GoldDreamEconomyService.NotifyMoneyChanged();
        }
    }

    private static bool IsGoldDreamPackCard(IDataConfig? config)
    {
        var id = TerriasContentIdCompatibility.LocalId(CardConfigApi.Id(config)).TrimStart('*');
        return id == TerriasIds.GildedButterflyCardShortId
            || id == TerriasIds.WagerCardShortId
            || id == TerriasIds.FortuneThrowCardShortId
            || id == TerriasIds.DisplayWealthCardShortId
            || id == TerriasIds.BlankCheckCardShortId
            || id == TerriasIds.GoldenDreamlandCardShortId;
    }

    private static void OnPaymentStateChanged(GoldDreamPaymentState state)
    {
        var executor = FightPlayer.Instance?.Status == null
            ? null
            : FindLocalExecutor();
        if (executor?.HandCard == null)
        {
            return;
        }

        TerriasCardRefreshQueue.RequestDataUpdateForHandCards(
            executor.HandCard,
            new[]
            {
                TerriasIds.WagerCardId,
                TerriasIds.WagerCardShortId,
                TerriasIds.FortuneThrowCardId,
                TerriasIds.FortuneThrowCardShortId
            },
            "GoldDream.PaymentStateChanged");
    }

    private static ScriptExecutor? FindLocalExecutor()
    {
        try
        {
            var manager = FightCardManager.Instance;
            return manager?.cardList
                .Select(config => config?.scriptExecutor as ScriptExecutor)
                .FirstOrDefault(executor => executor?.Self?.InstanceId == FightPlayer.Instance?.Status?.InstanceId);
        }
        catch
        {
            return null;
        }
    }

    private static void Reset(string source)
    {
        if (observedRoleTable != null)
        {
            observedRoleTable.PropertyChanged -= OnRoleTableChanged;
            observedRoleTable = null;
        }

        PendingGoldDreamCards.Clear();
        RuntimeCardAttachmentService.ClearTemporaryAttachments("GoldDream." + source);
        GoldDreamEconomyService.ClearCombatState(source);
    }
}
