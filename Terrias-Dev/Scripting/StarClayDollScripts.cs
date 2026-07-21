using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class StarClayDollScripts
{
    public static void ApplyTrait(ScriptExecutor self)
    {
        try
        {
            if (self?.Self == null)
            {
                return;
            }

            BuffApi.SetExactLevel(self.Self, TerriasIds.StarClayBody, 1);
            var token = ExecutorApi.RegisterHook(self, "TerriasStarClayDollHook", "TerriasStarClayDollToken");
            if (token == null)
            {
                return;
            }

            ExecutorApi.TryAddTokenedEvent(self, "ActionAfter", "TerriasStarClayDollToken", token,
                new Action(() => StarScoreService.AddStarlight(self, 1)), "star_clay_doll");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Star Clay Doll trait apply failed", ex);
        }
    }

    public static void ClearTrait(ScriptExecutor self)
    {
        try
        {
            ExecutorApi.ClearHook(self, "TerriasStarClayDollHook", "TerriasStarClayDollToken");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Star Clay Doll trait clear failed", ex);
        }
    }
}
