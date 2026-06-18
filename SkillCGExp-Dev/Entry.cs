using SkillCGExp.Dll.Hooks;
using Witch.Mod;

namespace SkillCGExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        SkillCgRuntime.Initialize(modConfig);
    }
}
