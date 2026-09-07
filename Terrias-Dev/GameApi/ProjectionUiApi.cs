using System;

namespace Terrias.Dll.GameApi;

public static class ProjectionUiApi
{
    private static Func<ScriptExecutor, bool>? open;
    private static Action<string>? close;
    public static void Configure(Func<ScriptExecutor, bool> show, Action<string> hide) { open = show; close = hide; }
    public static bool OpenRoleSelection(ScriptExecutor self)
    {
        return open?.Invoke(self) ?? false;
    }

    public static void CloseRoleSelection(string source)
    {
        close?.Invoke(source);
    }
}
