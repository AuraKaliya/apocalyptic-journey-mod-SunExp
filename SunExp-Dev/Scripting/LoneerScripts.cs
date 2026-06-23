using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class LoneerScripts
{
    public static void InitCareer(ScriptExecutor self)
    {
        try
        {
            LoneerMiracleService.RegisterCareer(self);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Loneer InitCareer failed", ex);
        }
    }

    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, "CommonCardItem");
            self.AddDescription("1", "Value", "2");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Loneer Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            if (id == "*loneer_morning_star_prayer")
            {
                LoneerMiracleService.UseMorningStarPrayer(self);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Loneer Use failed: " + id, ex);
        }
    }
}
