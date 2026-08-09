using System;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class BlessingScripts
{
    public static void Own(ScriptExecutor self, string id)
    {
        try
        {
            SolarBlessingService.ApplyOwnScript(self, id);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Blessing Own failed: " + id, ex);
        }
    }

    public static void Fight(ScriptExecutor self, string id)
    {
        RunFightStep(id, "origin", () => OriginMilestoneService.ApplyFightScript(self, id));
        RunFightStep(id, "solar", () => SolarBlessingService.ApplyFightScript(self, id));
        RunFightStep(id, "morning-star", () => MorningStarBlessingService.ApplyFightScript(self, id));
    }

    private static void RunFightStep(string id, string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Blessing Fight failed: " + id + ", step=" + step, ex);
        }
    }
}
