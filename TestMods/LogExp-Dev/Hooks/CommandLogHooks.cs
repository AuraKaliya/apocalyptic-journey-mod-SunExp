using LogExp.Dll.Infrastructure;
using Witch.Mod;

namespace LogExp.Dll.Hooks;

public static class CommandLogHooks
{
    [HookAfter(typeof(Commands), nameof(Commands.Log))]
    public static void AfterLog(string tag, string message)
    {
        LogExpRuntime.RecordCommand("Log", tag, message);
    }

    [HookAfter(typeof(Commands), nameof(Commands.LogWarning))]
    public static void AfterLogWarning(string tag, string message)
    {
        LogExpRuntime.RecordCommand("Warning", tag, message);
    }

    [HookAfter(typeof(Commands), nameof(Commands.LogError))]
    public static void AfterLogError(string tag, string message)
    {
        LogExpRuntime.RecordCommand("Error", tag, message);
    }
}
