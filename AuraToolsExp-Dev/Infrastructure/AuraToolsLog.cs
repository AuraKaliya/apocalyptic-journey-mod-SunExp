using System;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsLog
{
    private const string Tag = "AuraTools";

    public static void Info(string message)
    {
        AuraSharedLog.DebugLog(Tag, message, IsDebugEnabled());
    }

    public static void Debug(string message)
    {
        AuraSharedLog.DebugLog(Tag, message, IsDebugEnabled());
    }

    public static void Performance(string message)
    {
        if (AuraToolsPerformanceSettings.DiagnosticsEnabled)
        {
            AuraSharedLog.Info(Tag, message);
        }
    }

    public static void DebugOnce(string key, string message)
    {
        AuraSharedLog.DebugOnce(Tag, key, message);
    }

    public static void InfoOnce(string key, string message)
    {
        AuraSharedLog.DebugOnce(Tag, "info:" + key, message);
    }

    public static void Warn(string message)
    {
        AuraSharedLog.Warn(Tag, message);
    }

    public static void WarnOnce(string key, string message)
    {
        AuraSharedLog.WarnOnce(Tag, key, message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        AuraSharedLog.Error(Tag, message, ex);
    }

    private static bool IsDebugEnabled()
    {
        try
        {
            return AuraToolsConfigService.Logging.Enabled
                   && string.Equals(AuraToolsConfigService.Logging.MinimumLevel, LoggingLevelNames.Debug, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
