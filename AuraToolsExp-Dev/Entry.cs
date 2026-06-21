using System;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.Logging;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.Settings;
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
        RunStep("config", () => AuraToolsConfigService.Initialize(modConfig));
        RunStep("file logging", () => AuraToolsFileLogRuntime.Initialize(modConfig));
        RunStep("ui transition guard", () => UiTransitionGuardRuntime.Initialize(modConfig, AuraToolsIds.ModId));
        RunStep("audio", () => AuraToolsAudioRuntime.Initialize(modConfig));
        RunStep("starter deck", () => AuraToolsStarterDeckRuntime.Initialize(modConfig));
        RunStep("safe box", () => AuraToolsSafeBoxRuntime.Initialize(modConfig));
        RunStep("skill CG", () => AuraToolsSkillCgRuntime.Initialize(modConfig));
        RunStep("settings", () => AuraToolsSettingsRuntime.Initialize(modConfig));

        AuraToolsLog.Info("Initialized " + AuraToolsIds.DisplayName + ".");
    }

    private static void RunStep(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("Initialization step failed: " + name, ex);
        }
    }
}
