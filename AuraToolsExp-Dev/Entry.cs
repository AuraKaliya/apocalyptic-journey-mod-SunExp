using System;
using AuraCg.Shared;
using AuraGameData.Shared.GameApi;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using UiTransitionGuardShared;
using Witch.Mod;

namespace AuraToolsExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
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
        RunStep("bundled resources", () => AuraToolsResourceBootstrap.Initialize(modConfig));
        RunStep("CG registry", () => AuraCgRegistryRuntime.RegisterManifest(modConfig, AuraToolsIds.ModId));
        RunStep("ui transition guard", () => UiTransitionGuardRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("tool modules", () => AuraToolModuleHost.Initialize(modConfig));
        RunStep("settings", () => AuraToolsSettingsRuntime.Initialize(modConfig));

        AuraToolsLog.Info("Initialized " + AuraToolsIds.DisplayName + ".");
    }

    private static void RunStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => AuraToolsLog.Error("Initialization step failed: " + step, ex));
    }
}
