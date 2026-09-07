using Terrias.Dll.Application;
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
        ProjectionTurnCoordinator.ConfigureAuthoritativePublisher(
            ProjectionSummonService.BroadcastTurnTransaction);
        ProjectionAttachmentPresenter.Initialize();
        automationRegistration ??= CombatActionAutomationRegistry.Register(
            TerriasIds.ModId,
            "projection-card-runtime",
            new ProjectionCardAutomationProvider(),
            priority: 100);
        TerriasCardInteractionRouter.Register("Projection", new TerriasCardInteractionSubscription
        {
            Priority = 100,
            BeforeCommonBeginDrag = context => GateDuplicateProjectionUseBefore(context, "OnBeginDrag"),
            AfterCommonBeginDrag = context => RestoreProjectionUseGate(context, "OnBeginDrag"),
            BeforeCommonUseDirectly = context => GateDuplicateProjectionUseBefore(context, "UseCardDirectly"),
            AfterCommonUseDirectly = context => RestoreProjectionUseGate(context, "UseCardDirectly")
        });
        TerriasBattleLifecycleRouter.Register("Projection", new TerriasBattleLifecycleSubscription
        {
            BattleOpening = context => BeginBattle("BattleOpening"),
            PlayerRoundReady = context => ProjectionTurnCoordinator.BeginPlayerRound("PlayerRoundReady"),
            PlayerTurnCompleted = context => ProjectionTurnCoordinator.CompletePlayerTurnWithPendingProjections(
                "PlayerTurnCompleted"),
            BattleRestarting = context => ClearBattle("BattleRestarting"),
            OutcomeEntering = context => ClearBattle("OutcomeEntering." + context.Outcome)
        });
        TerriasStatusLifecycleRouter.Register("Projection", new TerriasStatusLifecycleSubscription
        {
            AfterHit = RetireProjectionAfterDamage,
            AfterCurHpChanged = RetireProjectionAfterHpChange,
            AfterMaxHpChanged = RetireProjectionAfterHpChange
        });
        TerriasLog.Info("Projection runtime initialized");
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
        var ownerPlayerId = CompanionOwnershipService.ResolveSemanticOwnerPlayerId(owner?.InstanceId ?? "");
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
                if (!ProjectionStateStore.RetireIfDead(status, source))
                {
                    ScheduleProjectionStateSync(status, source);
                }
                ProjectionAttachmentPresenter.RefreshByOwner(status, source);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Projection death cleanup failed from " + source, ex);
        }
    }

    private static void ScheduleProjectionStateSync(IStatusManager status, string source)
    {
        var state = ProjectionStateStore.Find(status?.InstanceId ?? "");
        if (state == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }
        var observedRevision = state.Replication.StateRevision;
        TerriasFrameDispatcher.RunOnceAfterFrames(
            "Projection.PublicStateSync." + state.StatusId,
            1,
            () =>
            {
                var current = ProjectionStateStore.Find(state.StatusId);
                if (current == null
                    || current.Replication.StateRevision != observedRevision
                    || current.Projection.Status == null
                    || current.Projection.Status.state == IStatusManager.State.Dead)
                {
                    return;
                }
                ProjectionSummonService.BroadcastExternalStateChange(
                    current.Projection,
                    source);
            });
    }

}
