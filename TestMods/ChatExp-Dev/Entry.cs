using AuraOnline.Shared;
using ChatExp.Dll.Hooks;
using ChatExp.Dll.Infrastructure;
using Witch.Mod;

namespace ChatExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        AuraChatRuntime.Initialize(ChatExpIds.ModId, ChatExpIds.MaxMessages);
        ChatExpRuntimeHooks.Initialize(modConfig);
        ChatExpLog.Info("Initialized.");
    }
}
