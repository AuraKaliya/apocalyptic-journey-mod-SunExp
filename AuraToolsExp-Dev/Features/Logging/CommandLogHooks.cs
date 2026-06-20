using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Logging;

public static class CommandLogHooks
{
    [HookAfter(typeof(Commands), nameof(Commands.Log))]
    public static void AfterLog(string tag, string message)
    {
        AuraToolsFileLogRuntime.RecordCommand("Log", tag, message);
    }

    [HookAfter(typeof(Commands), nameof(Commands.LogWarning))]
    public static void AfterLogWarning(string tag, string message)
    {
        AuraToolsFileLogRuntime.RecordCommand("Warning", tag, message);
    }

    [HookAfter(typeof(Commands), nameof(Commands.LogError))]
    public static void AfterLogError(string tag, string message)
    {
        AuraToolsFileLogRuntime.RecordCommand("Error", tag, message);
    }
}
