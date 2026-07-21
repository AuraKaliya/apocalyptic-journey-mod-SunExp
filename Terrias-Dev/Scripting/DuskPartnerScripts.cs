using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class DuskPartnerScripts
{
    public static void ApplyTrait(ScriptExecutor self)
    {
        try
        {
            DuskAfterheatRecoveryService.ActivateTrait(self);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dusk trait apply failed", ex);
        }
    }

    public static void ClearTrait(ScriptExecutor self)
    {
        try
        {
            ExecutorApi.ClearHook(self, "SunExpDuskAfterheatHook", "SunExpDuskAfterheatToken");
            DuskAfterheatRecoveryService.Deactivate(self, "TraitCleared");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dusk trait clear failed", ex);
        }
    }

}
