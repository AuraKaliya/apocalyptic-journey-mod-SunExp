using System;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class ProjectionScripts
{
    public static void RegisterCardCapability(
        string cardId,
        string executionMode,
        string targetMode,
        int targetCount = 1,
        bool includeSelf = false,
        bool lifecycleSafe = false,
        string targetSetKinds = "")
    {
        try
        {
            var normalized = Normalize(cardId);
            if (normalized.Length == 0
                || !Enum.TryParse(executionMode, true, out ProjectionCardExecutionMode execution)
                || !Enum.TryParse(targetMode, true, out ProjectionCardTargetMode targeting))
            {
                TerriasLog.Warn("Projection card capability declaration rejected: " + cardId);
                return;
            }
            ProjectionCardExecutionPolicy.Register(new ProjectionCardExecutionDeclaration
            {
                CardId = normalized,
                Mode = execution,
                LifecycleSafe = lifecycleSafe
            });
            ProjectionCardTargetPolicy.Register(new ProjectionCardTargetDeclaration
            {
                CardId = normalized,
                Mode = targeting,
                Count = Math.Max(1, targetCount),
                IncludeSelf = includeSelf,
                SetKinds = targetSetKinds ?? ""
            });
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Projection card capability registration failed: " + cardId, ex);
        }
    }

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
