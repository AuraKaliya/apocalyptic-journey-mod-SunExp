using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AuraShared.Core;

public static class AuraSharedLog
{
    private static readonly HashSet<string> OnceKeys = new(StringComparer.OrdinalIgnoreCase);
    private const int DebugFlagRefreshMilliseconds = 1000;
    private static MethodInfo? getGameVarMethod;
    private static bool gameVarMethodResolved;
    private static readonly Dictionary<string, CachedDebugFlag> DebugFlags = new(StringComparer.OrdinalIgnoreCase);

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
        if (!enabled && !IsDebugEnabled(owner))
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

    public static void DebugOnce(string owner, string key, string message, bool mirrorCommands = true)
    {
        if (OnceKeys.Add(owner + ":debug:" + key))
        {
            DebugLog(owner, message, false, mirrorCommands);
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

    private static bool IsDebugEnabled(string owner)
    {
        var normalizedOwner = string.IsNullOrWhiteSpace(owner) ? "AuraShared" : owner.Trim();
        var now = Environment.TickCount;
        if (DebugFlags.TryGetValue(normalizedOwner, out var cached)
            && (uint)(now - cached.Tick) < DebugFlagRefreshMilliseconds)
        {
            return cached.Enabled;
        }

        var enabled = ReadDebugFlag("AuraSharedDebug") || ReadDebugFlag(normalizedOwner + "Debug");
        DebugFlags[normalizedOwner] = new CachedDebugFlag(enabled, now);
        return enabled;
    }

    private static bool ReadDebugFlag(string key)
    {
        var text = ReadGameVar(key).Trim();
        return text == "1"
               || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadGameVar(string key)
    {
        try
        {
            if (!gameVarMethodResolved)
            {
                var playerInfo = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(assembly => assembly.GetType("ScriptExecutor"))
                    .FirstOrDefault(type => type != null)
                    ?.GetNestedType("PlayerInfo", BindingFlags.Public | BindingFlags.NonPublic);
                getGameVarMethod = playerInfo?.GetMethod("GetGameVar", BindingFlags.Public | BindingFlags.Static);
                gameVarMethodResolved = true;
            }

            var value = getGameVarMethod?.Invoke(null, new object[] { key });
            return Convert.ToString(value) ?? "";
        }
        catch
        {
            return "";
        }
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

    private readonly struct CachedDebugFlag
    {
        public CachedDebugFlag(bool enabled, int tick)
        {
            Enabled = enabled;
            Tick = tick;
        }

        public bool Enabled { get; }

        public int Tick { get; }
    }
}
