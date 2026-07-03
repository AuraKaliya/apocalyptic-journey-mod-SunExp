using System;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

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
            SunExpLog.Error("Projection action init failed: " + actionId, ex);
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
            SunExpLog.Error("Projection action target failed: " + actionId, ex);
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
            SunExpLog.Error("Projection action use failed: " + actionId, ex);
        }
    }

    private static string Normalize(string actionId)
    {
        return (actionId ?? "").Trim();
    }
}
