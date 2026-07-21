using System;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class CompanionThreatRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "ScriptExecutor.SetStatus", ExtendEnemyTargetsAfterSetStatus);
        SunExpLog.Info("Companion threat runtime initialized");
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "CompanionThreat");
    }

    private static void ExtendEnemyTargetsAfterSetStatus(ModHookContext context)
    {
        try
        {
            if (context.Target is not ScriptExecutor executor
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not string filter
                || executor.Self?.fatherObject is not Enemy)
            {
                return;
            }

            if (IsSingleEnemyTargetFilter(filter))
            {
                CompanionThreatService.TryRedirectEnemySingleTarget(executor);
                return;
            }

            if (IsAllEnemyTargetFilter(filter))
            {
                CompanionThreatService.AddActiveCompanionsToAllTargets(executor);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[CompanionThreat] target extension failed: " + ex.Message);
        }
    }

    private static bool IsSingleEnemyTargetFilter(string filter)
    {
        var clean = NormalizeFilter(filter);
        return string.Equals(clean, "Target", StringComparison.Ordinal);
    }

    private static bool IsAllEnemyTargetFilter(string filter)
    {
        var clean = NormalizeFilter(filter);
        return clean.StartsWith("All", StringComparison.Ordinal)
            && clean.Contains("Target")
            && !clean.StartsWith("AllRandom", StringComparison.Ordinal);
    }

    private static string NormalizeFilter(string filter)
    {
        var clean = (filter ?? "").Replace("ExSelf", "").Trim();
        foreach (var ch in "0123456789")
        {
            clean = clean.Replace(ch.ToString(), "");
        }

        return clean;
    }
}
