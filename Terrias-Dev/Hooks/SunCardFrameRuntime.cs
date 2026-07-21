using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class SunCardFrameRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        CardVisualSkinRuntime.Initialize(modConfig);
    }
}
