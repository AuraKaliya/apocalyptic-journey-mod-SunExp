using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.SkillCg;

public static class SkillCgExpLog
{
    private static readonly HashSet<string> InfoKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> WarnKeys = new(StringComparer.Ordinal);

    public static void Info(string message)
    {
        AuraToolsLog.Info("[SkillCG] " + message);
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
        AuraToolsLog.Warn("[SkillCG] " + message);
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
        AuraToolsLog.Error("[SkillCG] " + message, exception);
    }

    public static void DebugLog(string message)
    {
        if (IsDebugEnabled())
        {
            AuraToolsLog.Info("[SkillCG/Debug] " + message);
        }
    }

    private static bool IsDebugEnabled()
    {
        var value = Environment.GetEnvironmentVariable("AURATOOLS_SKILLCG_DEBUG");
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
