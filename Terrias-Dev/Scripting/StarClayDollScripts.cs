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
            TerriasActionPassiveRegistry.Register(
                self,
                "Buff.StarClayDollTrait",
                AuraShared.Core.AuraCardActionPhase.Committed,
                _ => StarScoreService.AddStarlight(self, 1));
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
            TerriasActionPassiveRegistry.Unregister(self, "Buff.StarClayDollTrait");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Star Clay Doll trait clear failed", ex);
        }
    }
}
