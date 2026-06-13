using System;
using System.Reflection;

namespace SunExp.Dll.Infrastructure;

public static class SunExpLog
{
    private const string DebugVarKey = "SunExpDebug";

    public static void Info(string message)
    {
        Commands.Log(SunExpIds.ModLogTag, message);
    }

    public static void Warn(string message)
    {
        Commands.Log(SunExpIds.ModLogTag, "[WARN] " + message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        var text = exception == null ? message : message + " :: " + exception;
        Commands.Log(SunExpIds.ModLogTag, "[ERROR] " + text);
    }

    public static void Debug(string message)
    {
        if (IsDebugEnabled())
        {
            Commands.Log(SunExpIds.ModLogTag, "[DEBUG] " + message);
        }
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
