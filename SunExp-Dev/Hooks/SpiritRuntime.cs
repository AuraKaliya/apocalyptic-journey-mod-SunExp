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
    private static readonly Dictionary<string, bool> UseGate = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, bool> AttackUseGate = new();

    public static void Initialize(ModConfig modConfig)
    {
        SpiritAttachmentPresenter.Initialize();
        SpiritCardFaceRuntime.Initialize();
        RegisterBefore(modConfig, SunExpHookTargets.CommonCardItemOnBeginDrag, context => GateUse(context, "OnBeginDrag"));
        RegisterAfter(modConfig, SunExpHookTargets.CommonCardItemOnBeginDrag, context => RestoreUse(context, "OnBeginDrag"));
        RegisterBefore(modConfig, SunExpHookTargets.CommonCardItemUseCardDirectly, context => GateUse(context, "UseCardDirectly"));
        RegisterAfter(modConfig, SunExpHookTargets.CommonCardItemUseCardDirectly, context => RestoreUse(context, "UseCardDirectly"));
        RegisterBefore(modConfig, SunExpHookTargets.AttackCardItemTrueUse, GateCaptureUse);
        RegisterAfter(modConfig, SunExpHookTargets.AttackCardItemTrueUse, RestoreCaptureUse);
        RegisterAfter(modConfig, SunExpHookTargets.EnemyManagerAddEnemy, ObserveEnemyAdded);
        SunExpBattleLifecycleRouter.Register("Spirit", new SunExpBattleLifecycleSubscription
        {
            FightStarted = _ =>
            {
                SpiritSummonService.ResetBattleSynchronization();
                SpiritCaptureService.ResetBattleSynchronization();
                SpiritStateStore.ClearAll("FightStarted");
            },
            FightEnding = _ => SpiritStateStore.ClearAll("FightEnding")
        });
        RegisterBefore(modConfig, SunExpHookTargets.FightWinInit, _ => SpiritStateStore.ClearAll("Fight_Win.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightLossInit, _ => SpiritStateStore.ClearAll("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightEscapeInit, _ => SpiritStateStore.ClearAll("Fight_Escape.Init:before"));
        SunExpStatusLifecycleRouter.Register("Spirit", new SunExpStatusLifecycleSubscription
        {
            AfterHit = context => RetireIfDead(context, "StatusManager.Hit"),
            AfterCurHpChanged = context => RetireIfDead(context, "StatusManager.CurHp"),
            AfterMaxHpChanged = context => RetireIfDead(context, "StatusManager.MaxHp")
        });
        SunExpLog.Info("Spirit runtime initialized");
    }

    private static void GateUse(ModHookContext context, string source)
    {
        if (context.Target is not CardItem card || !SpiritCardFactory.IsSpiritCard(card.dataConfig))
        {
            return;
        }

        var owner = FightPlayer.Instance?.Status;
        if (SpiritSummonService.CanSummon(card.dataConfig, owner, out var reason))
        {
            return;
        }

        UseGate[Key(card, source)] = CardItem.canUse;
        CardItem.canUse = false;
        PlayerApi.ShowCaption("精灵：" + reason);
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

    private static void RestoreUse(ModHookContext context, string source)
    {
        if (context.Target is not CardItem card || !UseGate.TryGetValue(Key(card, source), out var previous))
        {
            return;
        }

        CardItem.canUse = previous;
        UseGate.Remove(Key(card, source));
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

    private static string Key(CardItem card, string source) => source + ":" + card.GetInstanceID();

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "Spirit");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "Spirit");
    }
}
