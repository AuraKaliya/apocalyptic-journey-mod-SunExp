using System;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.Logging;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Mod;

namespace AuraToolsExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        try
        {
            AuraToolsConfigService.Initialize(modConfig);
            AuraToolsFileLogRuntime.Initialize(modConfig);
            AuraToolsAudioRuntime.Initialize(modConfig);
            AuraToolsStarterDeckRuntime.Initialize(modConfig);
            AuraToolsSafeBoxRuntime.Initialize(modConfig);
            AuraToolsSkillCgRuntime.Initialize(modConfig);
            AuraToolsSettingsRuntime.Initialize(modConfig);

            AuraToolsLog.Info("Initialized " + AuraToolsIds.DisplayName + ".");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("Initialization failed", ex);
        }
    }
}
