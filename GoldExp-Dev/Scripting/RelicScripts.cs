using System;
using GoldExp.Dll.Infrastructure;

namespace GoldExp.Dll.Scripting;

public static class RelicScripts
{
    public static void Fight(ScriptExecutor self, string id)
    {
        try
        {
            GoldExpLog.Debug("Relic Fight ignored for unregistered relic id: " + id);
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Relic Fight failed: " + id, ex);
        }
    }
}
