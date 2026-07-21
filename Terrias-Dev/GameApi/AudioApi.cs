using System;
using AuraAudio.Shared;
using AudioArbiter.Shared;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.GameApi;

public static class AudioApi
{
    private const string ModId = "SunExp";
    private const string ManifestPath = "audio.registry.json";
    private const string WunaCareerId = "wuna";
    private const string ColumbinaCareerId = "columbina";
    private const string WhiteSunPrayerKind = "SunExp.Wuna.WhiteSunPrayer";
    private const string GraveSongKind = "SunExp.Wuna.GraveSong";
    private const string EternalTideKind = "SunExp.Columbina.EternalTide";
    private const string HomesicknessKind = "SunExp.Columbina.Homesickness";

    private static ModConfig? currentModConfig;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (modConfig == null)
        {
            SunExpLog.Warn("Audio initialization skipped: mod config is null");
            return;
        }

        if (initialized && string.Equals(currentModConfig?.DirectoryName, modConfig.DirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        initialized = true;
        currentModConfig = modConfig;

        var audio = AuraAudioRuntime.Initialize(modConfig, ModId, ManifestPath);
        if (!audio.Success)
        {
            SunExpLog.Warn("Audio shared runtime initialization reported issues: " + audio.ErrorMessage);
        }

        BattleBgmProviderRuntime.Initialize(modConfig);
    }

    public static void PlayWhiteSunPrayer()
    {
        Request(WunaCareerId, WhiteSunPrayerKind, "White Sun Prayer");
    }

    public static void PlayGraveSong()
    {
        Request(WunaCareerId, GraveSongKind, "Grave Song");
    }

    public static void PlayColumbinaEternalTide()
    {
        Request(ColumbinaCareerId, EternalTideKind, "Columbina Eternal Tide");
    }

    public static void PlayColumbinaHomesickness()
    {
        Request(ColumbinaCareerId, HomesicknessKind, "Columbina Homesickness");
    }

    private static void Request(string careerId, string kind, string label)
    {
        if (currentModConfig == null)
        {
            SunExpLog.Warn("Audio request skipped before initialization: " + label);
            return;
        }

        AudioArbiterRuntime.RequestSound(new SoundPlaybackRequest
        {
            ModConfig = currentModConfig,
            OwnerModId = ModId,
            Kind = kind,
            CareerId = careerId,
            RoleId = careerId,
            SourceName = "SunExp.AudioApi." + label
        });
    }
}
