using System;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class FamiliarGrowthRuntime
{
    private const string LogPrefix = "[FamiliarGrowth]";

    public static void Initialize(ModConfig modConfig)
    {
        FamiliarGrowthApi.Initialize(modConfig);
        TerriasLibrarySubMenuRuntime.Register(new TerriasLibrarySubMenuEntry(
            "familiar-archive",
            "Terrias_FamiliarArchiveLibraryButton",
            () => "\u4f7f\u9b54\u6863\u6848",
            TerriasLibrarySubMenuSlot.BottomRight,
            OpenPanel));
        RegisterAfter(modConfig, "GameEntryUI.NormalGame", MarkActiveForRun);
        RegisterBefore(modConfig, TerriasHookTargets.FightWinResetStates, GrantBattleWinExperience);
        TerriasStatusLifecycleRouter.Register("FamiliarGrowth", new TerriasStatusLifecycleSubscription
        {
            BeforeHit = FamiliarFinalBlessingService.BeforeHit,
            AfterHit = FamiliarFinalBlessingService.AfterHit,
            BeforeEnemyDead = FamiliarFinalBlessingService.BeforeEnemyDead,
            AfterEnemyDead = FamiliarFinalBlessingService.AfterEnemyDead
        });
        AuraShared.Core.AuraCardActionTransactionRouter.Register(
            modConfig,
            TerriasIds.ModId,
            "FamiliarGrowth",
            new AuraShared.Core.AuraCardActionSubscription
            {
                Phases = AuraShared.Core.AuraCardActionPhase.Committed,
                Handler = context =>
                {
                    FamiliarBlessingEffectRuntime.AfterPlayerAction();
                    FamiliarFinalBlessingService.OnActionCommitted(context.Config);
                }
            },
            TerriasLog.Debug,
            TerriasLog.Warn);
        BattleRewardAdjustmentService.Register(new BattleRewardAdjustmentRule(
            "FamiliarGrowth.ExtraChoices",
            context => BattleRewardApi.IsCurrentBattleReward()
                       && FamiliarBlessingEffectRuntime.EffectAmount("BattleRewardExtraChoice") > 0,
            context => FamiliarBlessingEffectRuntime.ApplyBattleRewardExtraChoices(context.RewardUi)));
        TerriasBattleLifecycleRouter.Register("FamiliarGrowth", new TerriasBattleLifecycleSubscription
        {
            AdventureStarting = MarkActiveForRun,
            FightInitialized = ApplySelectedCombatStartEffects,
            PlayerRoundStarted = context => FamiliarBlessingEffectRuntime.BeginPlayerRound(),
            FightRestarting = context => FamiliarBlessingEffectRuntime.EndEpoch(),
            FightEnding = context => FamiliarBlessingEffectRuntime.EndEpoch()
        });
        TerriasLog.Info(LogPrefix + " runtime initialized.");
    }

    public static void OpenPanel()
    {
        FamiliarGrowthApi.RefreshCurrentPartner();
        FamiliarGrowthPanel.Open();
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "FamiliarGrowth");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(config, target, action, "FamiliarGrowth");
    }

    private static void MarkActiveForRun(ModHookContext context)
    {
        try
        {
            FamiliarBlessingEffectRuntime.BeginRun();
            var active = FamiliarGrowthApi.BeginRunFromCurrentPartner();
            PlayerApi.SetGameVar(TerriasIds.FamiliarRunActivePartnerKey, active?.FullSpeciesId ?? "");
            TerriasLog.Info(LogPrefix + " active run familiar: " + (active?.FullSpeciesId ?? "none"));
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " failed to snapshot active familiar: " + ex.Message);
        }
    }

    private static void GrantBattleWinExperience(ModHookContext context)
    {
        try
        {
            var active = FamiliarGrowthApi.Active();
            if (active == null
                || !AuraLifecycleOperationLedger.TryClaimBattleOperation(
                    TerriasIds.ModId,
                    "FamiliarGrowth",
                    "VictoryProgress",
                    active.FullSpeciesId,
                    "progress",
                    "experience-and-victory-effects"))
            {
                return;
            }

            ApplySelectedBattleWinEffects();
            FamiliarFinalBlessingService.ApplyBattleWinEffects();
            var result = FamiliarGrowthApi.GrantActiveExperience(FamiliarRosterService.BattleWinExperience);
            if (result == null)
            {
                return;
            }

            if (result.Value.LeveledUp)
            {
                PlayerApi.ShowCaption("\u4f7f\u9b54\u6210\u957f\uff1a" + result.Value.Instance.Name + " Lv." + result.Value.Instance.Level);
            }

            TerriasLog.Debug(LogPrefix + " battle win exp +" + result.Value.GainedExperience + " -> " + result.Value.Instance.InstanceId);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " failed to grant battle experience: " + ex.Message);
        }
    }

    private static void ApplySelectedCombatStartEffects(ModHookContext context)
    {
        try
        {
            var status = FightPlayer.Instance?.Status;
            if (status == null)
            {
                return;
            }

            var applied = FamiliarBlessingEffectRuntime.BeginEpoch(status);
            if (applied > 0)
            {
                TerriasLog.Debug(LogPrefix + " applied combat start effects: " + applied);
            }

            var unsupported = FamiliarBlessingEffectRuntime.UnsupportedSelectedEffectKinds();
            if (unsupported.Count > 0)
            {
                TerriasLog.Warn(LogPrefix + " selected effects have no runtime handler: " + string.Join(",", unsupported));
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " failed to apply combat start effects: " + ex.Message);
        }
    }

    private static void ApplySelectedBattleWinEffects()
    {
        var active = FamiliarGrowthApi.Active();
        if (active == null)
        {
            return;
        }

        var gold = FamiliarGrowthService.BlessingsFor(active)
            .SelectMany(blessing => blessing.Effects)
            .Where(effect => string.Equals(effect.Kind, "BattleWinGold", StringComparison.OrdinalIgnoreCase))
            .Sum(effect => Math.Max(0, effect.Amount));
        if (gold <= 0)
        {
            return;
        }

        if (PlayerApi.AddMoney(gold))
        {
            PlayerApi.ShowCaption("\u4f7f\u9b54\u795d\u798f\uff1a\u91d1\u5e01+" + gold);
        }
    }
}
