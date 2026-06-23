using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

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

            BuffApi.SetExactLevel(self.Self, SunExpIds.StarClayBody, 1);
            var token = ExecutorApi.RegisterHook(self, "SunExpStarClayDollHook", "SunExpStarClayDollToken");
            if (token == null)
            {
                return;
            }

            self.AddEvent("ActionAfter", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "SunExpStarClayDollToken", token))
                {
                    StarScoreService.AddStarlight(self, 1);
                }
            }));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star Clay Doll trait apply failed", ex);
        }
    }

    public static void ClearTrait(ScriptExecutor self)
    {
        try
        {
            ExecutorApi.ClearHook(self, "SunExpStarClayDollHook", "SunExpStarClayDollToken");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Star Clay Doll trait clear failed", ex);
        }
    }
}
