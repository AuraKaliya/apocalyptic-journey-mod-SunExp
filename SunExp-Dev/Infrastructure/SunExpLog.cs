using System;
using System.Reflection;
using AuraShared.Core;

namespace SunExp.Dll.Infrastructure;

public static class SunExpLog
{
    private const string DebugVarKey = "SunExpDebug";
    private const int DebugFlagRefreshMilliseconds = 1000;
    private static MethodInfo? getDebugVarMethod;
    private static bool debugVarMethodResolved;
    private static bool cachedDebugEnabled;
    private static int lastDebugFlagRefreshTick = int.MinValue;

    public static void Info(string message)
    {
        AuraSharedLog.DebugLog(SunExpIds.ModLogTag, message, IsDebugEnabled());
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

    public static void DebugOnce(string key, string message)
    {
        AuraSharedLog.DebugOnce(SunExpIds.ModLogTag, key, message);
    }

    public static void InfoOnce(string key, string message)
    {
        AuraSharedLog.DebugOnce(SunExpIds.ModLogTag, "info:" + key, message);
    }

    public static void WarnOnce(string key, string message)
    {
        AuraSharedLog.WarnOnce(SunExpIds.ModLogTag, key, message);
    }

    private static bool IsDebugEnabled()
    {
        var now = Environment.TickCount;
        if ((uint)(now - lastDebugFlagRefreshTick) < DebugFlagRefreshMilliseconds)
        {
            return cachedDebugEnabled;
        }

        try
        {
            if (!debugVarMethodResolved)
            {
                var playerInfo = typeof(ScriptExecutor).GetNestedType("PlayerInfo", BindingFlags.Public | BindingFlags.NonPublic);
                getDebugVarMethod = playerInfo?.GetMethod("GetGameVar", BindingFlags.Public | BindingFlags.Static);
                debugVarMethodResolved = true;
            }

            var value = getDebugVarMethod?.Invoke(null, new object[] { DebugVarKey });
            var text = Convert.ToString(value);
            cachedDebugEnabled = text == "1"
                || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            cachedDebugEnabled = false;
        }

        lastDebugFlagRefreshTick = now;
        return cachedDebugEnabled;
    }
}
