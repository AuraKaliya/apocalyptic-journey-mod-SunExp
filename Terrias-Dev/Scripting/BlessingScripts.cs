using System;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class BlessingScripts
{
    public static void Fight(ScriptExecutor self, string id)
    {
        try
        {
            OriginMilestoneService.ApplyFightScript(self, id);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Blessing Fight failed: " + id, ex);
        }
    }
}
