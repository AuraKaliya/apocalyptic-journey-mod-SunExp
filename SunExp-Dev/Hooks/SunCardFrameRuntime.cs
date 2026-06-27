using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunCardFrameRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        CardVisualSkinRuntime.Initialize(modConfig);
    }
}
