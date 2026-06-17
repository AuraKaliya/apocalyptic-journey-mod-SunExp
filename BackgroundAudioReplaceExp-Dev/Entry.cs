using BackgroundAudioReplaceExp.Dll.Hooks;
using Witch.Mod;

namespace BackgroundAudioReplaceExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        BackgroundBattleMusicRuntime.Initialize(modConfig);
    }
}
