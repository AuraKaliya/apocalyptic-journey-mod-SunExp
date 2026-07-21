using System;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class HeartChangeScripts
{
    public static void InitAction(ScriptExecutor self, string actionId)
    {
        try
        {
            HeartChangeIntentService.InitAction(self, Normalize(actionId));
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Heart Change action init failed: " + actionId, ex);
        }
    }

    public static void Target(ScriptExecutor self, string actionId)
    {
        try
        {
            HeartChangeIntentService.Target(self, Normalize(actionId));
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Heart Change action target failed: " + actionId, ex);
        }
    }

    public static void UseAction(ScriptExecutor self, string actionId)
    {
        try
        {
            HeartChangeIntentService.UseAction(self, Normalize(actionId));
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Heart Change action use failed: " + actionId, ex);
        }
    }

    private static string Normalize(string actionId)
    {
        return string.IsNullOrWhiteSpace(actionId)
            ? TerriasIds.HeartChangeActionStrike
            : actionId.Trim();
    }
}
