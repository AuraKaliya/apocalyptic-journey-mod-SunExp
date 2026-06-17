using CardUseCialloExp.Dll.Hooks;
using Witch.Mod;

namespace CardUseCialloExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        CardUseSoundRuntime.Initialize(modConfig);
    }
}
