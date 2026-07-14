using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SpiritRuntime
{
    private static readonly Func<CommonCardItem, bool> SpiritCardUseChecker = CanUseSpiritCard;
    private static readonly Dictionary<int, bool> AttackUseGate = new();

    public static void Initialize(ModConfig modConfig)
    {
        ProjectionIntentPresenter.Initialize();
        SpiritAttachmentPresenter.Initialize();
        SpiritCardFaceRuntime.Initialize();
        RegisterSpiritCardUseChecker();
        RegisterBefore(modConfig, SunExpHookTargets.AttackCardItemTrueUse, GateCaptureUse);
        RegisterAfter(modConfig, SunExpHookTargets.AttackCardItemTrueUse, RestoreCaptureUse);
        RegisterAfter(modConfig, SunExpHookTargets.EnemyManagerAddEnemy, ObserveEnemyAdded);
        SunExpBattleLifecycleRouter.Register("Spirit", new SunExpBattleLifecycleSubscription
        {
            FightStarted = _ => ClearBattle("FightStarted"),
            PlayerRoundStarted = _ => SpiritSummonService.FlushPendingCardReturns("PlayerRoundStarted"),
            FightEnding = _ => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, SunExpHookTargets.FightWinInit, _ => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightLossInit, _ => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightEscapeInit, _ => ClearBattle("Fight_Escape.Init:before"));
        SunExpStatusLifecycleRouter.Register("Spirit", new SunExpStatusLifecycleSubscription
        {
            AfterHit = context => RetireIfDead(context, "StatusManager.Hit"),
            AfterCurHpChanged = context => RetireIfDead(context, "StatusManager.CurHp"),
            AfterMaxHpChanged = context => RetireIfDead(context, "StatusManager.MaxHp")
        });
        SunExpLog.Info("Spirit runtime initialized");
    }

    internal static void ClearBattle(string source)
    {
        RunCleanupStep("SummonDedupe", source, SpiritSummonService.ResetBattleSynchronization);
        RunCleanupStep("CaptureDedupe", source, SpiritCaptureService.ResetBattleSynchronization);
        RunCleanupStep("StateStore", source, () => SpiritStateStore.ClearAll(source));
        RunCleanupStep("VisualProxies", source, () => SpiritAttachmentPresenter.ClearAll(source));
        RunCleanupStep("UseGates", source, ResetUseGates);
    }

    private static void RunCleanupStep(string step, string source, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Spirit cleanup step failed: " + step + " @ " + source, ex);
        }
    }

    private static void ResetUseGates()
    {
        AttackUseGate.Clear();
    }

    private static void RegisterSpiritCardUseChecker()
    {
        if (!CommonCardItem.UseChecker.Contains(SpiritCardUseChecker))
        {
            CommonCardItem.UseChecker.Add(SpiritCardUseChecker);
        }
    }

    private static bool CanUseSpiritCard(CommonCardItem card)
    {
        try
        {
            if (card == null || !SpiritCardFactory.IsSpiritCard(card.dataConfig))
            {
                return true;
            }

            var owner = card.status ?? FightPlayer.Instance?.Status;
            if (owner == null || !ProjectionStateStore.HasForOwner("", owner.InstanceId))
            {
                return true;
            }

            PlayerApi.ShowCaption("精灵：投影位置已被占用。");
            SunExpPerformanceCounters.Record("Spirit.CardUseRejected.ProjectionOccupied");
            return false;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Spirit card use preflight failed: " + ex.Message);
            return true;
        }
    }

    private static void GateCaptureUse(ModHookContext context)
    {
        if (context.Target is not CardItem card || !SpiritCardFactory.IsSpiritBall(card.dataConfig))
        {
            return;
        }

        var result = EnemyCatalogApi.Inspect(card.dataConfig?.scriptExecutor?.Target, "preflight");
        if (result.Eligible)
        {
            return;
        }

        AttackUseGate[card.GetInstanceID()] = card.hasUse;
        card.hasUse = true;
        PlayerApi.ShowCaption("精灵球：" + result.Reason);
    }

    private static void RestoreCaptureUse(ModHookContext context)
    {
        if (context.Target is not CardItem card || !AttackUseGate.TryGetValue(card.GetInstanceID(), out var previous))
        {
            return;
        }

        card.hasUse = previous;
        AttackUseGate.Remove(card.GetInstanceID());
    }

    private static void ObserveEnemyAdded(ModHookContext context)
    {
        var enemyId = context.Arguments != null && context.Arguments.Length > 0
            ? Convert.ToString(context.Arguments[0]) ?? ""
            : "";
        EnemyCaptureSettlementApi.ObserveEnemyAdded(enemyId);
    }

    private static void RetireIfDead(ModHookContext context, string source)
    {
        if (context.Target is not IStatusManager status)
        {
            return;
        }

        SpiritStateStore.RetireIfDead(status, source);
        SpiritAttachmentPresenter.RefreshByOwner(status, source);
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "Spirit");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "Spirit");
    }
}
