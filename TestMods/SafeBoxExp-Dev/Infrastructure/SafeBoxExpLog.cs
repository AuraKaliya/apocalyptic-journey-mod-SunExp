using System;

namespace SafeBoxExp.Dll.Infrastructure;

public static class SafeBoxExpLog
{
    public static void Info(string message)
    {
        UnityEngine.Debug.Log(Format(message));
    }

    public static void Warn(string message)
    {
        UnityEngine.Debug.LogWarning(Format(message));
    }

    public static void Error(string message, Exception? exception = null)
    {
        UnityEngine.Debug.LogError(Format(exception == null ? message : message + "\n" + exception));
    }

    public static void Debug(string message)
    {
        if (IsDebugEnabled())
        {
            UnityEngine.Debug.Log(Format(message));
        }
    }

    private static string Format(string message)
    {
        return "[" + SafeBoxExpIds.ModLogTag + "] " + message;
    }

    private static bool IsDebugEnabled()
    {
        var value = Environment.GetEnvironmentVariable("SAFEBOXEXP_DEBUG");
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
