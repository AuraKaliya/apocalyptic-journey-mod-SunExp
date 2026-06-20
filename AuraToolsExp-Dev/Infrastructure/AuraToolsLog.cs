using System;
using UnityEngine;
using Witch;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsLog
{
    private const string Tag = "AuraTools";

    public static void Info(string message)
    {
        Debug.Log("[" + Tag + "] " + message);
        TryCommandLog(message);
    }

    public static void Warn(string message)
    {
        Debug.LogWarning("[" + Tag + "] " + message);
        TryCommandLog("[WARN] " + message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        var text = ex == null ? message : message + " -> " + ex;
        Debug.LogError("[" + Tag + "] " + text);
        TryCommandLog("[ERROR] " + text);
    }

    private static void TryCommandLog(string message)
    {
        try
        {
            Commands.Log(Tag, message);
        }
        catch
        {
            // Commands may not be available during the earliest load phase.
        }
    }
}
