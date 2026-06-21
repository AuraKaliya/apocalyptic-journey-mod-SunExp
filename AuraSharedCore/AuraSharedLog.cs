using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuraShared.Core;

public static class AuraSharedLog
{
    private static readonly HashSet<string> OnceKeys = new(StringComparer.OrdinalIgnoreCase);

    public static void Info(string owner, string message, bool mirrorCommands = true)
    {
        Debug.Log(Format(owner, message));
        if (mirrorCommands)
        {
            TryCommandLog(owner, message);
        }
    }

    public static void Warn(string owner, string message, bool mirrorCommands = true)
    {
        Debug.LogWarning(Format(owner, message));
        if (mirrorCommands)
        {
            TryCommandLog(owner, "[WARN] " + message);
        }
    }

    public static void Error(string owner, string message, Exception? exception = null, bool mirrorCommands = true)
    {
        var text = exception == null ? message : message + " -> " + exception;
        Debug.LogError(Format(owner, text));
        if (mirrorCommands)
        {
            TryCommandLog(owner, "[ERROR] " + text);
        }
    }

    public static void DebugLog(string owner, string message, bool enabled, bool mirrorCommands = true)
    {
        if (!enabled)
        {
            return;
        }

        Debug.Log(Format(owner, "[DEBUG] " + message));
        if (mirrorCommands)
        {
            TryCommandLog(owner, "[DEBUG] " + message);
        }
    }

    public static void InfoOnce(string owner, string key, string message, bool mirrorCommands = true)
    {
        if (OnceKeys.Add(owner + ":info:" + key))
        {
            Info(owner, message, mirrorCommands);
        }
    }

    public static void WarnOnce(string owner, string key, string message, bool mirrorCommands = true)
    {
        if (OnceKeys.Add(owner + ":warn:" + key))
        {
            Warn(owner, message, mirrorCommands);
        }
    }

    private static string Format(string owner, string message)
    {
        return "[" + (string.IsNullOrWhiteSpace(owner) ? "AuraShared" : owner.Trim()) + "] " + message;
    }

    private static void TryCommandLog(string owner, string message)
    {
        try
        {
            global::Commands.Log(string.IsNullOrWhiteSpace(owner) ? "AuraShared" : owner.Trim(), message);
        }
        catch
        {
            // Commands can be unavailable during early load or non-game tests.
        }
    }
}
