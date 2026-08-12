using System;
using System.Collections.Generic;
using AuraCombatAi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Hooks.Visual;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class ProjectionRuntime
{
    private static readonly Dictionary<string, bool> ProjectionUseGate = new(StringComparer.Ordinal);
    private static IDisposable? automationRegistration;

    public static void Initialize(ModConfig modConfig)
    {
        ProjectionAttachmentPresenter.Initialize();
        automationRegistration ??= CombatActionAutomationRegistry.Register(
            TerriasIds.ModId,
            "projection-card-runtime",
            new ProjectionCardAutomationProvider(),
            priority: 100);
        RegisterBefore(modConfig, TerriasHookTargets.CommonCardItemOnBeginDrag,
            context => GateDuplicateProjectionUseBefore(context, "OnBeginDrag"));
        RegisterAfter(modConfig, TerriasHookTargets.CommonCardItemOnBeginDrag,
            context => RestoreProjectionUseGate(context, "OnBeginDrag"));
        RegisterBefore(modConfig, TerriasHookTargets.CommonCardItemUseCardDirectly,
            context => GateDuplicateProjectionUseBefore(context, "UseCardDirectly"));
        RegisterAfter(modConfig, TerriasHookTargets.CommonCardItemUseCardDirectly,
            context => RestoreProjectionUseGate(context, "UseCardDirectly"));
        TerriasBattleLifecycleRouter.Register("Projection", new TerriasBattleLifecycleSubscription
        {
            FightStarted = context => BeginBattle("Fight_Start.Init"),
            FightRestarting = context => ClearBattle("FightRestarting"),
            FightEnding = context => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, TerriasHookTargets.FightWinInit, context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightLossInit, context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightEscapeInit, context => ClearBattle("Fight_Escape.Init:before"));
        RegisterAfter(modConfig, TerriasHookTargets.FightPlayerTurnInit,
            context => ProjectionTurnCoordinator.BeginPlayerRound("Fight_PlayerTurn.Init"));
        TerriasStatusLifecycleRouter.Register("Projection", new TerriasStatusLifecycleSubscription
        {
            AfterHit = RetireProjectionAfterDamage,
            AfterCurHpChanged = RetireProjectionAfterHpChange,
            AfterMaxHpChanged = RetireProjectionAfterHpChange
        });
        TerriasLog.Info("Projection runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "Projection");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "Projection");
    }

    internal static void ClearBattle(string source, bool sweepVisualOrphans = true)
    {
        RunCleanupStep("TurnCoordinator", source, () => ProjectionTurnCoordinator.ClearBattle(source));
        RunCleanupStep("StateStore", source, () => ProjectionActivationService.ClearBattle(source));
        RunCleanupStep("VisualProxies", source, () => ProjectionAttachmentPresenter.ClearAll(source, sweepVisualOrphans));
        RunCleanupStep("RoleSelection", source, () => ProjectionUiApi.CloseRoleSelection(source));
        RunCleanupStep("NetworkDedupe", source, ProjectionSummonService.ResetBattleSynchronization);
        RunCleanupStep("UseGate", source, ResetProjectionUseGate);
    }

    private static void RunCleanupStep(string step, string source, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Projection cleanup step failed: " + step + " @ " + source, ex);
        }
    }

    private static void ResetProjectionUseGate()
    {
        foreach (var previous in ProjectionUseGate.Values)
        {
            CardItem.canUse = previous;
            break;
        }

        ProjectionUseGate.Clear();
    }

    private static void GateDuplicateProjectionUseBefore(ModHookContext context, string source)
    {
        if (context.Target is not CardItem card || !IsProjectionRoleCard(card.dataConfig))
        {
            return;
        }

        var owner = FightPlayer.Instance?.Status;
        var ownerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(owner?.InstanceId ?? "");
        if (owner != null
            && (!FriendlyRoleSeatLedger.CanReserve(ownerPlayerId, owner.InstanceId, out var reason)))
        {
            ProjectionUseGate[UseGateKey(card, source)] = CardItem.canUse;
            CardItem.canUse = false;
            PlayerApi.ShowCaption(reason == "friendly role seats are full"
                ? "拜托了：友方角色位置已达到4人上限。"
                : "拜托了：投影位置已被占用。");
        }
    }

    private static void RestoreProjectionUseGate(ModHookContext context, string source)
    {
        if (context.Target is not CardItem card)
        {
            return;
        }

        var key = UseGateKey(card, source);
        if (ProjectionUseGate.TryGetValue(key, out var previous))
        {
            CardItem.canUse = previous;
            ProjectionUseGate.Remove(key);
        }
    }

    private static string UseGateKey(CardItem card, string source)
    {
        return source + ":" + card.GetInstanceID();
    }

    private static bool IsProjectionRoleCard(IDataConfig? config)
    {
        return config != null && DictionaryUtil.ContainsToken(
            DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey),
            TerriasIds.ProjectionRoleCardMarker);
    }

    private static void BeginBattle(string source)
    {
        ClearBattle(source);
        CompanionAuthorityService.BeginBattleEpoch();
        FriendlyRoleSeatLedger.BeginBattle();
        ProjectionCardPresentationService.ResetBattle();
        ProjectionTurnCoordinator.BeginBattle(source);
    }

    private static void RetireProjectionAfterDamage(ModHookContext context)
    {
        RetireProjectionIfDead(context, "StatusManager.Hit");
    }

    private static void RetireProjectionAfterHpChange(ModHookContext context)
    {
        RetireProjectionIfDead(context, "StatusManager.HpChanged");
    }

    private static void RetireProjectionIfDead(ModHookContext context, string source)
    {
        try
        {
            if (context.Target is IStatusManager status)
            {
                ProjectionStateStore.RetireIfDead(status, source);
                ProjectionAttachmentPresenter.RefreshByOwner(status, source);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Projection death cleanup failed from " + source, ex);
        }
    }

}
