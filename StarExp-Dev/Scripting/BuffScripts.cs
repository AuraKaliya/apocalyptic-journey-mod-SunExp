using System;
using StarExp.Dll.Infrastructure;
using StarExp.Dll.Mechanics;

namespace StarExp.Dll.Scripting;

public static class BuffScripts
{
    public static void Apply(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "miracle_pouch":
                case "miracle_clock":
                case "clock_debt":
                case "starlight":
                case "time_erosion":
                case "white_stone_power":
                    StarMiracleService.EnsureCombatHooks(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            StarExpLog.Error("Buff Apply failed: " + id, ex);
        }
    }
}
