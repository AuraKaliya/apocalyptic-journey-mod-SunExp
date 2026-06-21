using System;
using System.IO;
using AudioArbiter.Shared;
using UnityEngine;
using Witch.Mod;

namespace CardUseCialloExp.Dll.Hooks;

public static class CardUseSoundRuntime
{
    private const string ModId = "CardUseCialloExp";
    private const string AudioFileName = "audio.mp3";
    private const float CardUseGainDb = 6f;

    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            Debug.Log("[CardUseCialloExp] audio provider already initialized");
            return;
        }

        initialized = true;
        AudioArbiterRuntime.Initialize(modConfig, ModId);

        var audioPath = Path.Combine(modConfig.DirectoryName, AudioFileName);
        Debug.Log("[CardUseCialloExp] registering card-use sound provider: " + audioPath);
        AudioArbiterRuntime.RegisterSoundProvider(
            modConfig,
            ModId,
            new FileSoundProvider(
                providerId: ModId + ".DefaultCardUse",
                ownerModId: ModId,
                audioPath: audioPath,
                priority: 0,
                bus: SoundBuses.Effect,
                policy: SoundPolicies.Replace,
                hardClaim: true,
                condition: IsCardUse,
                cooldownSeconds: 0.02f,
                sync: true,
                gainDb: CardUseGainDb));
    }

    private static bool IsCardUse(object? request)
    {
        return string.Equals(
            AudioArbiterRuntime.ReadString(request, "Kind"),
            SoundEventKinds.CardUse,
            StringComparison.OrdinalIgnoreCase);
    }
}
