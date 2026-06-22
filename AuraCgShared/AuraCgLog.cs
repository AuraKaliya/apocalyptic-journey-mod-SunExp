using System;
using System.Collections.Generic;
using AuraShared.Core;

namespace AuraCg.Shared;

internal static class AuraCgLog
{
    private static readonly HashSet<string> Seen = new(StringComparer.OrdinalIgnoreCase);

    public static void InfoOnce(string key, string message)
    {
        if (Seen.Add("info:" + key))
        {
            AuraSharedLog.Info("AuraCG", message);
        }
    }

    public static void WarnOnce(string key, string message)
    {
        if (Seen.Add("warn:" + key))
        {
            AuraSharedLog.Warn("AuraCG", message);
        }
    }

    public static void DebugLog(string message)
    {
        AuraSharedLog.DebugLog("AuraCG", message, false);
    }
}
