using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillCGExp.Dll.Infrastructure;

public static class SkillCgExpLog
{
    private const string Tag = "SkillCGExp";
    private static readonly HashSet<string> InfoKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> WarnKeys = new(StringComparer.Ordinal);

    public static void Info(string message)
    {
        Debug.Log(Format(message));
    }

    public static void InfoOnce(string key, string message)
    {
        if (InfoKeys.Add(key))
        {
            Info(message);
        }
    }

    public static void Warn(string message)
    {
        Debug.LogWarning(Format(message));
    }

    public static void WarnOnce(string key, string message)
    {
        if (WarnKeys.Add(key))
        {
            Warn(message);
        }
    }

    public static void Error(string message, Exception? exception = null)
    {
        Debug.LogError(Format(exception == null ? message : message + "\n" + exception));
    }

    public static void DebugLog(string message)
    {
        if (IsDebugEnabled())
        {
            Debug.Log(Format(message));
        }
    }

    private static string Format(string message)
    {
        return "[" + Tag + "] " + message;
    }

    private static bool IsDebugEnabled()
    {
        var value = Environment.GetEnvironmentVariable("SKILLCGEXP_DEBUG");
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
