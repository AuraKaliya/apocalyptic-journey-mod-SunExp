using SafeBoxExp.Dll.Hooks;
using SafeBoxExp.Dll.Infrastructure;
using Witch.Mod;

namespace SafeBoxExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        SafeBoxExpLog.Info("SafeBoxExp loaded: portable SafeBox entry + unlocked official limits");
        SafeBoxRuntime.Initialize(modConfig);
    }
}
