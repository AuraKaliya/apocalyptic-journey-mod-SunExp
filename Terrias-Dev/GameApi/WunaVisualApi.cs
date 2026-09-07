using System;
using Witch.Core;

namespace Terrias.Dll.GameApi;

public static class WunaVisualApi
{
    private static Action<IScriptExecutor?, string, string>? attach;
    public static void Configure(Action<IScriptExecutor?, string, string> handler) => attach = handler;
    public static void AttachOrbitFire(IScriptExecutor? executor, string action = "", string source = "GameApi")
    {
        attach?.Invoke(executor, action, source);
    }
}
