using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Hooks.Visual;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class ProjectionRuntime
{
    private static readonly Dictionary<string, bool> ProjectionUseGate = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        ProjectionAttachmentPresenter.Initialize();
        ProjectionIntentPresenter.Initialize();
        RegisterBefore(modConfig, SunExpHookTargets.CommonCardItemOnBeginDrag,
            context => GateDuplicateProjectionUseBefore(context, "OnBeginDrag"));
        RegisterAfter(modConfig, SunExpHookTargets.CommonCardItemOnBeginDrag,
            context => RestoreProjectionUseGate(context, "OnBeginDrag"));
        RegisterBefore(modConfig, SunExpHookTargets.CommonCardItemUseCardDirectly,
            context => GateDuplicateProjectionUseBefore(context, "UseCardDirectly"));
        RegisterAfter(modConfig, SunExpHookTargets.CommonCardItemUseCardDirectly,
            context => RestoreProjectionUseGate(context, "UseCardDirectly"));
        SunExpBattleLifecycleRouter.Register("Projection", new SunExpBattleLifecycleSubscription
        {
            FightStarted = context => BeginBattle("Fight_Start.Init"),
            FightEnding = context => ClearBattle("FightEnding")
        });
        RegisterBefore(modConfig, SunExpHookTargets.FightWinInit, context => ClearBattle("Fight_Win.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightLossInit, context => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, SunExpHookTargets.FightEscapeInit, context => ClearBattle("Fight_Escape.Init:before"));
        RegisterAfter(modConfig, SunExpHookTargets.FightPlayerTurnInit,
            context => ProjectionTurnCoordinator.BeginPlayerRound("Fight_PlayerTurn.Init"));
        SunExpStatusLifecycleRouter.Register("Projection", new SunExpStatusLifecycleSubscription
        {
            AfterAddBuff = RefreshOwnerProjectionAfterBuffChange,
            AfterRemoveBuff = RefreshOwnerProjectionAfterBuffChange,
            AfterBuffLevelChanged = RefreshOwnerProjectionAfterBuffLevelChange,
            AfterHit = RetireProjectionAfterDamage,
            AfterCurHpChanged = RetireProjectionAfterHpChange,
            AfterMaxHpChanged = RetireProjectionAfterHpChange
        });
        SunExpLog.Info("Projection runtime initialized");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "Projection");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "Projection");
    }

    internal static void ClearBattle(string source)
    {
        RunCleanupStep("TurnCoordinator", source, () => ProjectionTurnCoordinator.ClearBattle(source));
        RunCleanupStep("StateStore", source, () => ProjectionActivationService.ClearBattle(source));
        RunCleanupStep("VisualProxies", source, () => ProjectionAttachmentPresenter.ClearAll(source));
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
            SunExpLog.Error("Projection cleanup step failed: " + step + " @ " + source, ex);
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
        if (owner != null && CompanionPositionOwnershipService.HasForOwner(ownerPlayerId, owner.InstanceId))
        {
            ProjectionUseGate[UseGateKey(card, source)] = CardItem.canUse;
            CardItem.canUse = false;
            PlayerApi.ShowCaption("拜托了：投影位置已被占用。");
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
            DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey),
            SunExpIds.ProjectionRoleCardMarker);
    }

    private static void BeginBattle(string source)
    {
        ClearBattle(source);
        CompanionAuthorityService.BeginBattleEpoch();
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
            SunExpLog.Error("Projection death cleanup failed from " + source, ex);
        }
    }

    private static void RefreshOwnerProjectionAfterBuffChange(ModHookContext context)
    {
        if (!CompanionAuthorityService.IsAuthoritative()
            || context.Target is not IStatusManager owner
            || ProjectionStateStore.IsProjection(owner))
        {
            return;
        }

        QueueOwnerIntentRefresh(owner);
    }

    private static void RefreshOwnerProjectionAfterBuffLevelChange(ModHookContext context)
    {
        if (!CompanionAuthorityService.IsAuthoritative()
            || context.Target is not BuffItemConfig config
            || config.status == null
            || ProjectionStateStore.IsProjection(config.status))
        {
            return;
        }

        QueueOwnerIntentRefresh(config.status);
    }

    private static void QueueOwnerIntentRefresh(IStatusManager owner)
    {
        var state = ProjectionStateStore.FindByOwner("", owner.InstanceId);
        if (state?.Projection == null)
        {
            return;
        }

        SunExpFrameScheduler.RunOnceNextFrame(
            "ProjectionIntent.OwnerBuff." + owner.InstanceId,
            () => state.Projection.RefreshCommittedIntentValues("OwnerBuffChanged"));
    }
}
