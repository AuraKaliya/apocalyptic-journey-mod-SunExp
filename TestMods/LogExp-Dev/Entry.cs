using LogExp.Dll.Infrastructure;
using Witch.Mod;

namespace LogExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        LogExpRuntime.Initialize(modConfig);
    }
}
