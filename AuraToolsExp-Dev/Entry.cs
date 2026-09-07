using System;
using AuraCg.Shared;
using AuraGameData.Shared.GameApi;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Features.SharedResources;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using UiTransitionGuardShared;
using Witch.Mod;

namespace AuraToolsExp.Dll;

public static class Entry
{
    public static AuraSharedInitializationReport Initialization { get; } = new();
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        Initialization.Reset();
        RunStep("shared core", () => AuraSharedRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("shared game data", () =>
        {
            var result = AuraGameDataHostApi.RegisterNativeOwnershipV5(AuraToolsIds.ModId, "AuraToolsExp_");
            if (!result.Success)
            {
                throw new InvalidOperationException("AuraToolsExp v5 game-data ownership registration failed: " + result.Message);
            }
        });
        RunStep("journey runtime", () => AuraJourneyRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("mode runtime", () => AuraModeRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("rpc authority", () => AuraToolsRpcAuthorityRuntime.Initialize(modConfig));
        RunStep("config", () => AuraToolsConfigService.Initialize(modConfig));
        RunStep("preparation tool dock", () => AuraToolsPreparationDock.Initialize(modConfig), "shared core", "config");
        RunStep("bundled resources", () => AuraToolsResourceBootstrap.Initialize(modConfig));
        RunStep("CG registry", () => AuraCgRegistryRuntime.RegisterManifest(modConfig, AuraToolsIds.ModId));
        RunStep("content shared resource discovery", () => AuraToolsSharedResourceDiscoveryRuntime.Initialize(modConfig), "shared core", "config");
        RunStep("ui transition guard", () => UiTransitionGuardRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("tool modules", () => AuraToolModuleHost.Initialize(modConfig), "shared core", "shared game data", "rpc authority", "config");
        RunStep("settings", () => AuraToolsSettingsRuntime.Initialize(modConfig), "shared core", "config", "tool modules");

        AuraToolsLog.Info("Initialization " + AuraToolsIds.DisplayName + ": " + Initialization.Summary);
        foreach (var step in Initialization.Steps)
            if (step.State == AuraInitializationState.Blocked) AuraToolsLog.Warn("Initialization blocked: " + step.Name + "; requires=" + step.Detail);
    }

    private static void RunStep(string name, Action action, params string[] dependencies)
    {
        if (dependencies.Length == 0 && name != "shared core") dependencies = new[] { "shared core" };
        Initialization.Run(name, action, (step, ex) => AuraToolsLog.Error("Initialization step failed: " + step, ex), dependencies);
    }
}
