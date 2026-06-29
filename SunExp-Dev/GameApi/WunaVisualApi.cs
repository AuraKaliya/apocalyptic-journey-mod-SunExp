using SunExp.Dll.Hooks;
using Witch.Core;

namespace SunExp.Dll.GameApi;

public static class WunaVisualApi
{
    public static void AttachOrbitFire(IScriptExecutor? executor, string action = "", string source = "GameApi")
    {
        WunaOrbitFireRuntime.AttachFromExecutor(executor, action, source);
    }
}
