using System;
using AuraCg.Shared;
using AuraGameData.Shared.GameApi;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.AutoBattle;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.Diagnostics;
using AuraToolsExp.Dll.Features.Feast;
using AuraToolsExp.Dll.Features.Logging;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.PixelEmoji;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Features.Skin;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
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
        RunStep("file logging", () => AuraToolsFileLogRuntime.Initialize(modConfig));
        RunStep("bundled resources", () => AuraToolsResourceBootstrap.Initialize(modConfig));
        RunStep("CG registry", () => AuraCgRegistryRuntime.RegisterManifest(modConfig, AuraToolsIds.ModId));
        RunStep("ui transition guard", () => UiTransitionGuardRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("skin", () => AuraToolsSkinRuntime.Initialize(modConfig));
        RunStep("audio", () => AuraToolsAudioRuntime.Initialize(modConfig));
        RunStep("starter deck", () => AuraToolsStarterDeckRuntime.Initialize(modConfig));
        RunStep("feast", () => AuraToolsFeastRuntime.Initialize(modConfig));
        RunStep("safe box", () => AuraToolsSafeBoxRuntime.Initialize(modConfig));
        RunStep("card refresh", () => AuraToolsCardRefreshRuntime.Initialize(modConfig));
        RunStep("pixel emoji", () => AuraToolsPixelEmojiRuntime.Initialize(modConfig));
        RunStep("auto battle", () => AuraToolsAutoBattleRuntime.Initialize(modConfig));
        RunStep("mod sync", () => AuraToolsModSyncRuntime.Initialize(modConfig));
        RunStep("DPS meter", () => AuraToolsDamageMeterRuntime.Initialize(modConfig));
        RunStep("card UI benchmark", () => AuraToolsCardUiBenchmarkRuntime.Initialize(modConfig));
        RunStep("skill CG", () => AuraToolsSkillCgRuntime.Initialize(modConfig));
        RunStep("settings", () => AuraToolsSettingsRuntime.Initialize(modConfig));

        AuraToolsLog.Info("Initialized " + AuraToolsIds.DisplayName + ".");
    }

    private static void RunStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => AuraToolsLog.Error("Initialization step failed: " + step, ex));
    }
}
