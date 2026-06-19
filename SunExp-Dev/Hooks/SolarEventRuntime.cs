using System;
using Witch.Core;

namespace SunExp.Dll.Hooks;

[Obsolete("Retired: Solar Memory content must never be injected into other game modes.")]
public static class SolarEventRuntime
{
    public static void EnsureInCurrentLayer(ModHookContext context)
    {
    }

    public static void RepairMapSelection(ModHookContext context)
    {
    }

    public static string CurrentEventId()
    {
        return "";
    }
}
