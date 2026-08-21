using AuraToolsExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Logging;

public static class CommandLogHooks
{
    public static void Initialize(ModConfig config)
    {
        AuraToolsHookRegistry.After(
            config,
            "Commands.Log",
            context => Record("Log", context),
            "FileLogging");
        AuraToolsHookRegistry.After(
            config,
            "Commands.LogWarning",
            context => Record("Warning", context),
            "FileLogging");
        AuraToolsHookRegistry.After(
            config,
            "Commands.LogError",
            context => Record("Error", context),
            "FileLogging");
    }

    private static void Record(string level, ModHookContext context)
    {
        var arguments = context.Arguments;
        AuraToolsFileLogRuntime.RecordCommand(
            level,
            arguments != null && arguments.Length > 0
                ? arguments[0]?.ToString()
                : "",
            arguments != null && arguments.Length > 1
                ? arguments[1]?.ToString()
                : "");
    }
}
