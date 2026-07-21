using System;
using System.Reflection;
using AuraShared.Core;

namespace Terrias.Dll.Infrastructure;

public static class TerriasLog
{
    private const string DebugVarKey = "TerriasDebug";
    private const int DebugFlagRefreshMilliseconds = 1000;
    private static MethodInfo? getDebugVarMethod;
    private static bool debugVarMethodResolved;
    private static bool cachedDebugEnabled;
    private static int lastDebugFlagRefreshTick = int.MinValue;

    public static void Info(string message)
    {
        AuraSharedLog.DebugLog(TerriasIds.ModLogTag, message, IsDebugEnabled());
    }

    public static void InfoAlways(string message)
    {
        AuraSharedLog.Info(TerriasIds.ModLogTag, message);
    }

    public static void Warn(string message)
    {
        AuraSharedLog.Warn(TerriasIds.ModLogTag, message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        AuraSharedLog.Error(TerriasIds.ModLogTag, message, exception);
    }

    public static void Debug(string message)
    {
        AuraSharedLog.DebugLog(TerriasIds.ModLogTag, message, IsDebugEnabled());
    }

    public static void DebugOnce(string key, string message)
    {
        AuraSharedLog.DebugOnce(TerriasIds.ModLogTag, key, message);
    }

    public static void InfoOnce(string key, string message)
    {
        AuraSharedLog.DebugOnce(TerriasIds.ModLogTag, "info:" + key, message);
    }

    public static void InfoOnceAlways(string key, string message)
    {
        AuraSharedLog.InfoOnce(TerriasIds.ModLogTag, key, message);
    }

    public static void WarnOnce(string key, string message)
    {
        AuraSharedLog.WarnOnce(TerriasIds.ModLogTag, key, message);
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
            var text = Convert.ToString(value)?.Trim();
            cachedDebugEnabled = text == "1"
                || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            cachedDebugEnabled = false;
        }

        lastDebugFlagRefreshTick = now;
        return cachedDebugEnabled;
    }
}
