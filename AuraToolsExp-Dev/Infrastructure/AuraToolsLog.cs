using System;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsLog
{
    private const string Tag = "AuraTools";

    public static void Info(string message)
    {
        AuraSharedLog.Info(Tag, message);
    }

    public static void Debug(string message)
    {
        AuraSharedLog.DebugLog(Tag, message, IsDebugEnabled());
    }

    public static void Warn(string message)
    {
        AuraSharedLog.Warn(Tag, message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        AuraSharedLog.Error(Tag, message, ex);
    }

    private static bool IsDebugEnabled()
    {
        try
        {
            return AuraToolsConfigService.Root.Logging.Enabled
                   && AuraToolsConfigService.Logging.Enabled
                   && string.Equals(AuraToolsConfigService.Logging.MinimumLevel, LoggingLevelNames.Debug, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
