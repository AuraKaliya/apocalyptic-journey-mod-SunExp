using System.IO;
using BattleBgmArbiter.Shared;
using Witch.Mod;

namespace BackgroundAudioReplaceExp.Dll.Hooks;

public static class BackgroundBattleMusicRuntime
{
    private const string ModId = "BackgroundAudioReplaceExp";
    private const string AudioFileName = "BGM.mp3";

    public static void Initialize(ModConfig modConfig)
    {
        BattleBgmArbiterRuntime.Initialize(modConfig, ModId);
        BattleBgmArbiterRuntime.RegisterProvider(
            modConfig,
            ModId,
            new FileBattleBgmProvider(
                providerId: ModId + ".DefaultBattleBgm",
                ownerModId: ModId,
                audioPath: Path.Combine(modConfig.DirectoryName, AudioFileName),
                priority: 0,
                hardClaim: true,
                silenceWhenLoading: true,
                fallbackToOriginalWhenFailed: true,
                adventureCondition: null,
                battleCondition: null,
                allowMidBattleSwitch: false));
    }
}
