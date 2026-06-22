using System;
using AuraAudio.Shared;
using AudioArbiter.Shared;
using SanGuoShaExp.Dll.Infrastructure;
using Witch.Mod;

namespace SanGuoShaExp.Dll.GameApi;

public static class AudioApi
{
    private const string ModId = "SanGuoShaExp";
    private const string ManifestPath = "audio.registry.json";
    private const string QixingKind = "SanGuoShaExp.Qixing";
    private const string GaleKind = "SanGuoShaExp.Gale";
    private const string MistKind = "SanGuoShaExp.Mist";
    private const string ShenZhugeLiangRoleId = "shen_zhugeliang";

    private static ModConfig? currentModConfig;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (modConfig == null)
        {
            SanGuoShaExpLog.Warn("Audio initialization skipped: mod config is null");
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
            SanGuoShaExpLog.Warn("Audio shared runtime initialization reported issues: " + audio.ErrorMessage);
        }
    }

    public static void PlayQixing()
    {
        Request(QixingKind, "Seven Stars");
    }

    public static void PlayRandomWindMist()
    {
        Request(UnityEngine.Random.Range(0, 2) == 0 ? GaleKind : MistKind, "Gale or Great Fog");
    }

    private static void Request(string kind, string label)
    {
        if (currentModConfig == null)
        {
            SanGuoShaExpLog.Warn("Audio request skipped before initialization: " + label);
            return;
        }

        AudioArbiterRuntime.RequestSound(new SoundPlaybackRequest
        {
            ModConfig = currentModConfig,
            OwnerModId = ModId,
            Kind = kind,
            CareerId = ShenZhugeLiangRoleId,
            RoleId = ShenZhugeLiangRoleId,
            SourceName = "SanGuoShaExp.AudioApi." + label
        });
    }
}
