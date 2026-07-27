using System;
using System.Collections.Generic;
using AuraCombatAi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SpiritRuntime
{
    private static readonly Func<CommonCardItem, bool> SpiritCardUseChecker = CanUseSpiritCard;
    private static readonly Dictionary<int, bool> AttackUseGate = new();
    private static IDisposable? autoBattlePreflightRegistration;

    public static void Initialize(ModConfig modConfig)
    {
        ProjectionIntentPresenter.Initialize();
        SpiritAttachmentPresenter.Initialize();
        SpiritCardFaceRuntime.Initialize();
        RegisterSpiritCardUseChecker();
        autoBattlePreflightRegistration ??= CombatAiRegistry.RegisterRuntimePreflightRule(
            TerriasIds.ModId,
            "SpiritCards",
            new SpiritAutoBattlePreflightRule(),
            100);
        RegisterBefore(modConfig, TerriasHookTargets.AttackCardItemTrueUse, GateCaptureUse);
        RegisterAfter(modConfig, TerriasHookTargets.AttackCardItemTrueUse, RestoreCaptureUse);
        RegisterAfter(modConfig, TerriasHookTargets.EnemyManagerAddEnemy, ObserveEnemyAdded);
        TerriasBattleLifecycleRouter.Register("Spirit", new TerriasBattleLifecycleSubscription
        {
            FightStarted = _ => ClearBattle("FightStarted"),
            PlayerRoundStarted = _ => SpiritSummonService.FlushPendingCardReturns("PlayerRoundStarted"),
            FightEnding = _ => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, TerriasHookTargets.FightWinInit, _ => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightLossInit, _ => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightEscapeInit, _ => ClearBattle("Fight_Escape.Init:before"));
        TerriasStatusLifecycleRouter.Register("Spirit", new TerriasStatusLifecycleSubscription
        {
            AfterHit = context => RetireIfDead(context, "StatusManager.Hit"),
            AfterCurHpChanged = context => RetireIfDead(context, "StatusManager.CurHp"),
            AfterMaxHpChanged = context => RetireIfDead(context, "StatusManager.MaxHp")
        });
        TerriasLog.Info("Spirit runtime initialized");
    }

    internal static void ClearBattle(string source, bool sweepVisualOrphans = true)
    {
        RunCleanupStep("SummonDedupe", source, SpiritSummonService.ResetBattleSynchronization);
        RunCleanupStep("CaptureDedupe", source, SpiritCaptureService.ResetBattleSynchronization);
        RunCleanupStep("StateStore", source, () => SpiritStateStore.ClearAll(source));
        RunCleanupStep("VisualProxies", source, () => SpiritAttachmentPresenter.ClearAll(source, sweepVisualOrphans));
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
            TerriasLog.Error("Spirit cleanup step failed: " + step + " @ " + source, ex);
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
            TerriasPerformanceCounters.Record("Spirit.CardUseRejected.ProjectionOccupied");
            return false;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("Spirit card use preflight failed: " + ex.Message);
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
        TerriasHookRegistry.Before(config, target, action, "Spirit");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "Spirit");
    }

    private sealed class SpiritAutoBattlePreflightRule : ICombatRuntimePreflightRule
    {
        public bool IsLegal(
            CombatStateObservation state,
            CombatActionObservation action,
            CombatRuntimeActionContext runtime,
            out string reason)
        {
            if (runtime.SourceHandle is not CommonCardItem card)
            {
                reason = "";
                return true;
            }

            if (SpiritCardFactory.IsSpiritCard(card.dataConfig))
            {
                var owner = card.status ?? FightPlayer.Instance?.Status;
                if (owner != null && ProjectionStateStore.HasForOwner("", owner.InstanceId))
                {
                    reason = "spirit projection position is occupied";
                    return false;
                }
            }

            if (SpiritCardFactory.IsSpiritBall(card.dataConfig))
            {
                var inspection = EnemyCatalogApi.Inspect(
                    runtime.TargetHandle as IStatusManager,
                    "auto-battle-preflight");
                if (!inspection.Eligible)
                {
                    reason = inspection.Reason;
                    return false;
                }
            }

            reason = "";
            return true;
        }
    }
}
