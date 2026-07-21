using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

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
            TerriasLog.Error("Dusk trait apply failed", ex);
        }
    }

    public static void ClearTrait(ScriptExecutor self)
    {
        try
        {
            ExecutorApi.ClearHook(self, "TerriasDuskAfterheatHook", "TerriasDuskAfterheatToken");
            DuskAfterheatRecoveryService.Deactivate(self, "TraitCleared");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Dusk trait clear failed", ex);
        }
    }

}
