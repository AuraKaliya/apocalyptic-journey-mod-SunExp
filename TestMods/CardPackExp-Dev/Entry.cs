using CardPackExp.Dll.Hooks;
using CardPackExp.Dll.Infrastructure;
using Witch.Mod;

namespace CardPackExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        CardPackExpLog.Info("CardPackExp loaded: card-pack sync + one-shot starter deck editor");
        CardPackSelectionRuntime.Initialize(modConfig);
    }
}
