using System;
using AuraAudio.Shared;
using AudioArbiter.Shared;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.GameApi;

public static class AudioApi
{
    private const string ModId = "Terrias";
    private const string ManifestPath = "audio.registry.json";
    private const string WunaCareerId = "wuna";
    private const string ColumbinaCareerId = "columbina";
    private const string WhiteSunPrayerKind = "Terrias.Wuna.WhiteSunPrayer";
    private const string GraveSongKind = "Terrias.Wuna.GraveSong";
    private const string EternalTideKind = "Terrias.Columbina.EternalTide";
    private const string HomesicknessKind = "Terrias.Columbina.Homesickness";

    private static ModConfig? currentModConfig;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (modConfig == null)
        {
            TerriasLog.Warn("Audio initialization skipped: mod config is null");
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
            TerriasLog.Warn("Audio shared runtime initialization reported issues: " + audio.ErrorMessage);
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
            TerriasLog.Warn("Audio request skipped before initialization: " + label);
            return;
        }

        AudioArbiterRuntime.RequestSound(new SoundPlaybackRequest
        {
            ModConfig = currentModConfig,
            OwnerModId = ModId,
            Kind = kind,
            CareerId = careerId,
            RoleId = careerId,
            SourceName = "Terrias.AudioApi." + label
        });
    }
}
