using System;
using AuraJourney.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.Logging;
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
        RunStep("shared registry", () => AuraSharedRegistry.RegisterManifest(modConfig, AuraToolsIds.ModId));
        RunStep("journey runtime", () => AuraJourneyRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("config", () => AuraToolsConfigService.Initialize(modConfig));
        RunStep("file logging", () => AuraToolsFileLogRuntime.Initialize(modConfig));
        RunStep("bundled resources", () => AuraToolsResourceBootstrap.Initialize(modConfig));
        RunStep("ui transition guard", () => UiTransitionGuardRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("skin", () => AuraToolsSkinRuntime.Initialize(modConfig));
        RunStep("audio", () => AuraToolsAudioRuntime.Initialize(modConfig));
        RunStep("starter deck", () => AuraToolsStarterDeckRuntime.Initialize(modConfig));
        RunStep("safe box", () => AuraToolsSafeBoxRuntime.Initialize(modConfig));
        RunStep("DPS meter", () => AuraToolsDamageMeterRuntime.Initialize(modConfig));
        RunStep("skill CG", () => AuraToolsSkillCgRuntime.Initialize(modConfig));
        RunStep("settings", () => AuraToolsSettingsRuntime.Initialize(modConfig));

        AuraToolsLog.Info("Initialized " + AuraToolsIds.DisplayName + ".");
    }

    private static void RunStep(string name, Action action)
    {
        AuraSharedHooks.RunStep(name, action, (step, ex) => AuraToolsLog.Error("Initialization step failed: " + step, ex));
    }
}
