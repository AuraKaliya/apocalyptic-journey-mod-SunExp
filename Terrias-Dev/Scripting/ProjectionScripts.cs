using System;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class ProjectionScripts
{
    public static void InitAction(ScriptExecutor self, string actionId)
    {
        try
        {
            ProjectionStrategyService.InitAction(self, Normalize(actionId));
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Projection action init failed: " + actionId, ex);
        }
    }

    public static void Target(ScriptExecutor self, string actionId)
    {
        try
        {
            ProjectionStrategyService.Target(self, Normalize(actionId));
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Projection action target failed: " + actionId, ex);
        }
    }

    public static void UseAction(ScriptExecutor self, string actionId)
    {
        try
        {
            ProjectionStrategyService.UseAction(self, Normalize(actionId));
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Projection action use failed: " + actionId, ex);
        }
    }

    private static string Normalize(string actionId)
    {
        return (actionId ?? "").Trim();
    }
}
