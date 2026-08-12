using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SpiritRuntime
{
    private static readonly Dictionary<int, bool> AttackUseGate = new();
    private static IDisposable? autoBattlePreflightRegistration;

    public static void Initialize(ModConfig modConfig)
    {
        SpiritCollectionApi.Initialize(modConfig);
        SpiritAdventureButtonRuntime.Initialize(modConfig);
        TerriasLibrarySubMenuRuntime.Register(new TerriasLibrarySubMenuEntry(
            "spirit-warehouse",
            "Terrias_SpiritWarehouseLibraryButton",
            () => "精灵仓库",
            TerriasLibrarySubMenuSlot.TopLeftUpper,
            SpiritManagementPanel.OpenWarehouse));
        ProjectionIntentPresenter.Initialize();
        SpiritAttachmentPresenter.Initialize();
        SpiritCardFaceRuntime.Initialize();
        SpiritPartnerTurnOrderRuntime.Initialize(modConfig);
        autoBattlePreflightRegistration ??= CombatAiRegistry.RegisterRuntimePreflightRule(
            TerriasIds.ModId,
            "SpiritCards",
            new SpiritAutoBattlePreflightRule(),
            100);
        RegisterBefore(modConfig, TerriasHookTargets.AttackCardItemTrueUse, GateCaptureUse);
        RegisterAfter(modConfig, TerriasHookTargets.AttackCardItemTrueUse, RestoreCaptureUse);
        RegisterAfter(modConfig, TerriasHookTargets.EnemyManagerAddEnemy, ObserveEnemyAdded);
        RegisterAfter(modConfig, "OtherObj.EndRound", OnFightObjectRoundCompleted);
        RegisterAfter(modConfig, "FightPlayer.EndRound", OnFightObjectRoundCompleted);
        RegisterAfter(modConfig, "OtherPlayer.EndRound", OnFightObjectRoundCompleted);
        RegisterAfter(modConfig, "FightObject.EndRound", OnFightObjectRoundCompleted);
        TerriasBattleLifecycleRouter.Register("Spirit", new TerriasBattleLifecycleSubscription
        {
            AdventureStarting = _ => SpiritCollectionApi.BeginAdventure(),
            FightInitializing = _ => ClearBattle("FightInitializing"),
            FightInitialized = BeginBattle,
            PlayerRoundStarted = _ => SpiritSummonService.FlushPendingCardReturns("PlayerRoundStarted"),
            FightRestarting = _ => ClearBattle("FightRestarting"),
            FightEnding = _ => ClearBattle("FightEnding")
        });
        RegisterAfter(modConfig, "GameEntryUI.NormalGame", _ => SpiritCollectionApi.BeginAdventure());
        RegisterBefore(modConfig, TerriasHookTargets.FightWinInit, GrantBattleExperienceAndClear);
        RegisterBefore(modConfig, TerriasHookTargets.FightLossInit, _ => ClearBattle("Fight_Loss.Init:before"));
        RegisterBefore(modConfig, TerriasHookTargets.FightEscapeInit, _ => ClearBattle("Fight_Escape.Init:before"));
        TerriasStatusLifecycleRouter.Register("Spirit", new TerriasStatusLifecycleSubscription
        {
            AfterHit = context => RetireIfDead(context, "StatusManager.Hit"),
            AfterStateChanged = context => RetireIfDead(context, "StatusManager.State")
        });
        TerriasLog.Info("Spirit runtime initialized");
    }

    internal static void ClearBattle(string source, bool sweepVisualOrphans = true)
    {
        RunCleanupStep("SummonDedupe", source, SpiritSummonService.ResetBattleSynchronization);
        RunCleanupStep("CaptureDedupe", source, SpiritCaptureService.ResetBattleSynchronization);
        RunCleanupStep("CaptureSettlement", source, EnemyCaptureSettlementApi.ResetBattleSynchronization);
        RunCleanupStep("BattleDeployment", source, SpiritBattleDeploymentService.Clear);
        RunCleanupStep("TrainingBattleRuntime", source, SpiritTrainingBattleRuntime.Clear);
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

    private static void BeginBattle(ModHookContext context)
    {
        try
        {
            var party = SpiritCollectionApi.CurrentParty();
            var collection = SpiritCollectionApi.Collection();
            SpiritBattleDeploymentService.Begin(
                party,
                collection,
                CompanionAuthorityService.BattleEpoch,
                ResolveBattleExperience());
            var snapshot = SpiritBattleDeploymentService.DeploymentCardSnapshot();
            var status = FightPlayer.Instance?.Status;
            var executor = status?.MirrorSc as ScriptExecutor;
            if (snapshot == null || status == null || executor == null)
            {
                return;
            }

            executor.Self = status;
            var grant = SpiritCardFactory.GrantDeploymentToHand(executor, snapshot);
            if (!grant.Success)
            {
                TerriasLog.Warn("[SpiritCollection] deployment card grant failed: " + grant.FailureReason);
                PlayerApi.ShowCaption("精灵：出战卡生成失败。");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SpiritCollection] battle snapshot failed: " + ex.Message);
        }
    }

    private static void GrantBattleExperienceAndClear(ModHookContext context)
    {
        try
        {
            var snapshot = SpiritBattleDeploymentService.ExperienceSnapshot();
            var results = SpiritCollectionApi.GrantBattleExperience(
                snapshot.PartyUids,
                snapshot.ActiveUid,
                snapshot.Experience,
                snapshot.BattleToken);
            foreach (var result in results)
            {
                if (result.LeveledUp)
                {
                    var unlock = result.UnlockedAbilityIds.Count == 0
                        ? ""
                        : " · 解锁 " + string.Join("、", result.UnlockedAbilityIds.Select(SpiritTrainingRegistry.AbilityDisplayName));
                    PlayerApi.ShowCaption("精灵成长：" + result.Instance.Snapshot.DisplayName + " Lv." + result.Instance.Level + unlock);
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[SpiritCollection] battle experience failed: " + ex.Message);
        }
        finally
        {
            ClearBattle("Fight_Win.Init:before");
        }
    }

    private static int ResolveBattleExperience()
    {
        try
        {
            var rarity = EnemyManager.Instance?.enemyList?
                .Where(enemy => enemy?.dataConfig?.data != null)
                .Select(enemy => DictionaryUtil.GetInt(enemy.dataConfig.data, "Rarity"))
                .DefaultIfEmpty(1)
                .Max() ?? 1;
            return rarity >= 3 ? 80 : rarity == 2 ? 40 : 20;
        }
        catch
        {
            return 20;
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
        SpiritBattleDeploymentService.RaiseExperienceReward(ResolveBattleExperience());
    }

    private static void RetireIfDead(ModHookContext context, string source)
    {
        if (context.Target is not IStatusManager status)
        {
            return;
        }

        var retired = SpiritStateStore.RetireIfDead(status, source);
        if (!retired && !SpiritStateStore.IsSpirit(status))
        {
            SpiritAttachmentPresenter.RefreshByOwner(status, source);
        }

        SpiritTrainingBattleRuntime.OnStatusHit(status);
    }

    private static void OnFightObjectRoundCompleted(ModHookContext context)
    {
        var actor = context.Target as FightObject;
        SpiritTrainingBattleRuntime.OnActorTurnCompleted(actor);
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
