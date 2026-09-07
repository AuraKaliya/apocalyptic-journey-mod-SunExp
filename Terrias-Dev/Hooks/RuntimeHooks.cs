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
    public static AuraSharedInitializationReport Initialization { get; } = new();
    private static Action? resetCardState;
    private static bool routerBootstrapComplete;
    public static void ConfigureCardStateReset(Action reset) => resetCardState = reset;
    private static readonly string[] RequiredRouters =
    {
        "battle lifecycle router", "card lifecycle router", "card interaction router", "card exit router",
        "combat action router", "script execution router", "status lifecycle router", "buff mutation router"
    };
    private static readonly System.Collections.Generic.HashSet<string> IndependentUi = new(StringComparer.Ordinal)
    {
        "dialogue flow", "library submenu", "witch archive", "dimension shop", "terrias UI lifecycle",
        "visual bundle validation", "animated blessing icons", "animated buff icons", "animated enemy dictionary icons",
        "solar memory map item animation", "map node card art", "spirit training registry", "spirit intent registry",
        "spirit capture registry", "spirit growth registry", "companion intent registry"
    };
    public static void Initialize(ModConfig modConfig)
    {
        Initialization.Reset();
        routerBootstrapComplete = false;
        RunHookStep("battle lifecycle router", () => TerriasBattleLifecycleRouter.Initialize(modConfig));
        RunHookStep("card script fight state", () =>
        {
            TerriasBattleLifecycleRouter.Register("CardScripts", new TerriasBattleLifecycleSubscription
            {
                BattleInitializing = _ =>
                {
                    LegacyBattleHookVarMigration.ReconcileCurrentRole();
                    if (resetCardState == null) throw new InvalidOperationException("Card state lifecycle is unavailable.");
                    resetCardState();
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
                BattleInitializing = _ => TerriasActionPassiveRegistry.Clear(),
                BattleRestarting = _ => TerriasActionPassiveRegistry.Clear(),
                BattleSettling = _ => TerriasActionPassiveRegistry.Clear()
            });
        });
        RunHookStep("companion scene lifecycle", () => CompanionSceneLifecycleRuntime.Initialize(modConfig));
        RunHookStep("card lifecycle router", () => TerriasCardLifecycleRouter.Initialize(modConfig));
        RunHookStep("card interaction router", () => TerriasCardInteractionRouter.Initialize(modConfig));
        RunHookStep("card exit router", () => TerriasCardExitRouter.Initialize(modConfig));
        RunHookStep("combat action router", () => TerriasCombatActionRouter.Initialize(modConfig));
        RunHookStep("script execution router", () => TerriasScriptExecutionRouter.Initialize(modConfig));
        RunHookStep("status lifecycle router", () => TerriasStatusLifecycleRouter.Initialize(modConfig));
        RunHookStep("buff mutation router", () => TerriasBuffMutationRouter.Initialize(modConfig));
        RunHookStep("remote target event compatibility", () => RemoteTargetEventRuntime.Initialize(modConfig));
        RunHookStep("elemental mechanics", () => ElementalMechanicsRuntime.Initialize(modConfig));
        RunHookStep("columbina and constellation", () => ColumbinaRuntime.Initialize(modConfig));
        RunHookStep("Olimya role", () => OlimyaRuntime.Initialize(modConfig));
        RunHookStep("moon homecoming card pack", () => MoonHomecomingRuntime.Initialize(modConfig));
        RunHookStep("origin milestones", () => OriginMilestoneRuntime.Initialize(modConfig));
        RunHookStep("solar card pack migration", () => SunCardPackMigrationRuntime.Initialize(modConfig));
        RunHookStep("field effect registry", () => FieldEffectRegistry.WarmupConfigCache("RuntimeHooks.Initialize"));
        RunHookStep("field runtime", () => FieldRuntime.Initialize(modConfig));
        RunHookStep("card presentation bridge", () => TerriasCardPresentationLifecycleBridge.Initialize(modConfig));
        RunHookStep("active card presentation index", () => TerriasActiveCardPresentationIndex.Initialize(modConfig));
        RunHookStep("fight presentation invalidation", TerriasFightPresentationInvalidationService.Initialize);
        RunHookStep("battle reward card presentation", () => BattleRewardCardPresentationRuntime.Initialize(modConfig));
        RunHookStep("combat card UI workload", () => TerriasCombatCardUiWorkloadRuntime.Initialize(modConfig));
        RunHookStep("combat card production boundary", TerriasCombatCardProductionRuntime.Initialize);
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
        TerriasLog.InfoAlways("Runtime hooks: " + Initialization.Summary);
        foreach (var step in Initialization.Steps)
            if (step.State == AuraInitializationState.Blocked) TerriasLog.Warn("Runtime hook blocked: " + step.Name + "; requires=" + step.Detail);
        foreach (var required in RequiredRouters)
            if (!Initialization.Ready(required)) throw new InvalidOperationException("Required gameplay router unavailable: " + required);
    }

    private static void RunHookStep(string name, Action action)
    {
        TerriasLog.InfoAlways("Runtime hook step start: " + name);
        var dependencies = name == "battle lifecycle router" || IndependentUi.Contains(name) ? Array.Empty<string>()
            : !routerBootstrapComplete || Array.IndexOf(RequiredRouters, name) >= 0 || name == "companion scene lifecycle"
                ? new[] { "battle lifecycle router" } : RequiredRouters;
        var ok = Initialization.Run(name, action, (step, ex) => TerriasLog.Error("Runtime hook step failed: " + step, ex), dependencies);
        if (name == "buff mutation router") routerBootstrapComplete = true;
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
