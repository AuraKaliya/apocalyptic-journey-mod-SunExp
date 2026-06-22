using System;
using AuraShared.Core;

namespace SanGuoShaExp.Dll.Infrastructure;

public static class SanGuoShaExpLog
{
    public static void Info(string message)
    {
        AuraSharedLog.Info(SanGuoShaExpIds.ModLogTag, message);
    }

    public static void Warn(string message)
    {
        AuraSharedLog.Warn(SanGuoShaExpIds.ModLogTag, message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        AuraSharedLog.Error(SanGuoShaExpIds.ModLogTag, message, exception);
    }

    public static void Debug(string message)
    {
        AuraSharedLog.DebugLog(SanGuoShaExpIds.ModLogTag, message, IsDebugEnabled());
    }

    private static bool IsDebugEnabled()
    {
        var value = Environment.GetEnvironmentVariable("SANGUOSHAEXP_DEBUG");
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
