using System;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Scripting;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class RuntimeHooks
{
    public static void Initialize(ModConfig modConfig)
    {
        RunHookStep("battle lifecycle router", () => TerriasBattleLifecycleRouter.Initialize(modConfig));
        RunHookStep("card script fight state", () =>
        {
            TerriasBattleLifecycleRouter.Register("CardScripts", new TerriasBattleLifecycleSubscription
            {
                FightInitializing = _ =>
                {
                    LegacyBattleHookVarMigration.ReconcileCurrentRole();
                    CardScripts.ResetFightState();
                }
            });
        });
        RunHookStep("Terrias action passive registry", () =>
        {
            AuraCardActionTransactionRouter.Register(
                modConfig,
                TerriasIds.ModId,
                "ActionPassives",
                new AuraCardActionSubscription
                {
                    Phases = AuraCardActionPhase.NativeStarted | AuraCardActionPhase.Committed,
                    Priority = -100,
                    Handler = TerriasActionPassiveRegistry.Dispatch
                },
                TerriasLog.Debug,
                TerriasLog.Warn);
            TerriasBattleLifecycleRouter.Register("ActionPassives", new TerriasBattleLifecycleSubscription
            {
                FightInitializing = _ => TerriasActionPassiveRegistry.Clear(),
                FightRestarting = _ => TerriasActionPassiveRegistry.Clear(),
                FightEnding = _ => TerriasActionPassiveRegistry.Clear()
            });
        });
        RunHookStep("companion scene lifecycle", () => CompanionSceneLifecycleRuntime.Initialize(modConfig));
        RunHookStep("card lifecycle router", () => TerriasCardLifecycleRouter.Initialize(modConfig));
        RunHookStep("combat action router", () => TerriasCombatActionRouter.Initialize(modConfig));
        RunHookStep("status lifecycle router", () => TerriasStatusLifecycleRouter.Initialize(modConfig));
        RunHookStep("buff mutation router", () => TerriasBuffMutationRouter.Initialize(modConfig));
        RunHookStep("remote target event compatibility", () => RemoteTargetEventRuntime.Initialize(modConfig));
        RunHookStep("elemental mechanics", () => ElementalMechanicsRuntime.Initialize(modConfig));
        RunHookStep("columbina and constellation", () => ColumbinaRuntime.Initialize(modConfig));
        RunHookStep("origin milestones", () => OriginMilestoneRuntime.Initialize(modConfig));
        RunHookStep("solar card pack migration", () => SunCardPackMigrationRuntime.Initialize(modConfig));
        RunHookStep("field effect registry", () => FieldEffectRegistry.WarmupConfigCache("RuntimeHooks.Initialize"));
        RunHookStep("field runtime", () => FieldRuntime.Initialize(modConfig));
        RunHookStep("card visual skin", () => CardVisualSkinRuntime.Initialize(modConfig));
        RunHookStep("card presentation bridge", TerriasCardPresentationLifecycleBridge.Initialize);
        RunHookStep("active card presentation index", () => TerriasActiveCardPresentationIndex.Initialize(modConfig));
        RunHookStep("fight presentation invalidation", TerriasFightPresentationInvalidationService.Initialize);
        RunHookStep("battle reward card presentation", () => BattleRewardCardPresentationRuntime.Initialize(modConfig));
        RunHookStep("combat card UI workload", () => TerriasCombatCardUiWorkloadRuntime.Initialize(modConfig));
        RunHookStep("combat card view pool", () => Ui.TerriasCombatCardViewPool.Initialize(modConfig));
        RunHookStep("status buff handlers", () =>
        {
            TerriasBuffMutationRouter.Register("RuntimeStatusBuff", new TerriasBuffMutationSubscription
            {
                BeforeAdd = OnStatusManagerAddBuffBefore,
                Changed = OnStatusManagerBuffChanged
            });
        });
        RunHookStep("dialogue flow", () => DialogueFlowRuntime.Initialize(modConfig));
        RunHookStep("library submenu", () => TerriasLibrarySubMenuRuntime.Initialize(modConfig));
        RunHookStep("familiar growth", () => FamiliarGrowthRuntime.Initialize(modConfig));
        RunHookStep("witch archive", () => WitchArchiveRuntime.Initialize(modConfig));
        RunHookStep("dusk partner", () => DuskPartnerRuntime.Initialize(modConfig));
        RunHookStep("star clay doll", () => StarClayDollRuntime.Initialize(modConfig));
        RunHookStep("sandrone cat", () => SandroneCatRuntime.Initialize(modConfig));
        RunHookStep("mode context", () => TerriasModeContextRuntime.Initialize(modConfig));
        RunHookStep("solar memory mode", () => SolarMemoryModeRuntime.Initialize(modConfig));
        RunHookStep("solar memory combat", () => SolarMemoryCombatRuntime.Initialize(modConfig));
        RunHookStep("solar memory reward", () => SolarMemoryRewardRuntime.Initialize());
        RunHookStep("ember adventure state", () => EmberAdventureStateRuntime.Initialize(modConfig));
        RunHookStep("gold dream runtime", () => GoldDreamRuntime.Initialize(modConfig));
        RunHookStep("terrias UI lifecycle", () => TerriasUiLifecycleRuntime.Initialize(modConfig));
        RunHookStep("endless sea mode", () => EndlessSeaModeRuntime.Initialize(modConfig));
        RunHookStep("endless sea combat", () => EndlessSeaCombatRuntime.Initialize(modConfig));
        RunHookStep("endless sea reward", () => EndlessSeaRewardRuntime.Initialize(modConfig));
        RunHookStep("endless sea post battle", () => EndlessSeaRewardRuntime.InitializePostBattleHooks(modConfig));
        RunHookStep("endless sea card affix", () => EndlessSeaCardAffixRuntime.Initialize(modConfig));
        RunHookStep("endless sea intro board", () => EndlessSeaIntroBoardRuntime.Initialize(modConfig));
        RunHookStep("endless abyss evacuation", () => EndlessAbyssEvacuationRuntime.Initialize(modConfig));
        RunHookStep("dimension shop", () => DimensionShopRuntime.Initialize(modConfig));
        RunHookStep("battle reward adjustment", () => BattleRewardAdjustmentRuntime.Initialize(modConfig));
        RunHookStep("solar memory content isolation", () => SolarMemoryContentIsolationRuntime.Initialize(modConfig));
        RunHookStep("solar memory starter deck", () => SolarMemoryStarterDeckRuntime.Initialize(modConfig));
        RunHookStep("hard tags", () => TerriasHardTagRuntime.Initialize(modConfig));
        RunHookStep("visual bundle validation", VisualBundleRuntimeValidator.ValidateDeclaredBundles);
        RunHookStep("resource preloader", () => TerriasResourcePreloader.Initialize(modConfig));
        RunHookStep("animated blessing icons", () => AnimatedBlessingIconRuntime.Initialize(modConfig));
        RunHookStep("animated buff icons", () => AnimatedBuffIconRuntime.Initialize(modConfig));
        RunHookStep("animated enemy dictionary icons", () => AnimatedEnemyDictIconRuntime.Initialize(modConfig));
        RunHookStep("solar memory map item animation", () => SolarMemoryMapItemAnimationRuntime.Initialize(modConfig));
        RunHookStep("map node card art", () => MapNodeCardArtRuntime.Initialize(modConfig));
        RunHookStep("polymorph runtime", () => PolymorphRuntime.Initialize(modConfig));
        RunHookStep("companion intent registry", () => CompanionIntentRegistry.Load(modConfig));
        RunHookStep("spirit training registry", () => SpiritTrainingRegistry.Load(modConfig));
        RunHookStep("spirit intent registry", () => SpiritIntentRegistry.Load(modConfig));
        RunHookStep("spirit capture registry", () => SpiritCaptureRegistry.Load(modConfig));
        RunHookStep("spirit growth registry", () => SpiritGrowthRegistry.Load(modConfig));
        RunHookStep("companion threat runtime", () => CompanionThreatRuntime.Initialize(modConfig));
        RunHookStep("heart change control", () => HeartChangeControlRuntime.Initialize(modConfig));
        RunHookStep("projection runtime", () => ProjectionRuntime.Initialize(modConfig));
        RunHookStep("spirit runtime", () => SpiritRuntime.Initialize(modConfig));
        RunHookStep("role action animation", () => RoleActionAnimationRuntime.Initialize(modConfig));
        RunHookStep("wuna orbit fire", () => WunaOrbitFireRuntime.Initialize(modConfig));
        RunHookStep("star score runtime", () => StarScoreRuntime.Initialize(modConfig));
        RunHookStep("star score HUD", () => StarScoreHudRuntime.Initialize(modConfig));
        RunHookStep("loneer runtime", () => LoneerRuntime.Initialize(modConfig));
        TerriasLog.InfoAlways("Runtime hooks registered");
    }

    private static void RunHookStep(string name, Action action)
    {
        TerriasLog.InfoAlways("Runtime hook step start: " + name);
        var ok = AuraSharedHooks.RunStep(name, action, (step, ex) => TerriasLog.Error("Runtime hook step failed: " + step, ex));
        TerriasLog.InfoAlways("Runtime hook step " + (ok ? "ok: " : "failed: ") + name);
    }

    private static void OnStatusManagerAddBuffBefore(TerriasBuffMutationContext context)
    {
        try
        {
            var target = context.Status;
            var buffId = context.BuffId;
            if (target == null)
            {
                return;
            }

            var amount = context.RequestedLevel;
            ExecutorApi.PrepareSolarRadianceUpperBound(target, buffId);
            if (buffId != TerriasIds.Burn || amount <= 0)
            {
                return;
            }

            FieldEffectHandlers.HandleBuffAdded(target, buffId, amount, "StatusManager.AddBuff:before");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Status AddBuff before hook failed", ex);
        }
    }

    private static void OnStatusManagerBuffChanged(TerriasBuffMutationContext context)
    {
        try
        {
            if (context.Kind == TerriasBuffMutationKind.Add)
            {
                ExecutorApi.FinalizeSolarRadianceUpperBound(
                    context.Status,
                    context.BuffId,
                    context.RequestedLevel);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Status AddBuff after hook failed", ex);
        }
    }

}
