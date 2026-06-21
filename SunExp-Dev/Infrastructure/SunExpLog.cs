using System;
using System.Reflection;
using AuraShared.Core;

namespace SunExp.Dll.Infrastructure;

public static class SunExpLog
{
    private const string DebugVarKey = "SunExpDebug";

    public static void Info(string message)
    {
        AuraSharedLog.Info(SunExpIds.ModLogTag, message);
    }

    public static void Warn(string message)
    {
        AuraSharedLog.Warn(SunExpIds.ModLogTag, message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        AuraSharedLog.Error(SunExpIds.ModLogTag, message, exception);
    }

    public static void Debug(string message)
    {
        AuraSharedLog.DebugLog(SunExpIds.ModLogTag, message, IsDebugEnabled());
    }

    private static bool IsDebugEnabled()
    {
        try
        {
            var playerInfo = typeof(ScriptExecutor).GetNestedType("PlayerInfo", BindingFlags.Public | BindingFlags.NonPublic);
            var value = playerInfo?.GetMethod("GetGameVar", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { DebugVarKey });
            var text = Convert.ToString(value);
            return text == "1"
                || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
