using System;
using Terrias.Dll.Application;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Scripting;

public static class OlimyaScripts
{
    public static void InitCareer(ScriptExecutor self)
    {
        try { PlayerApi.SetSkillTime(OlimyaIds.GoldenTouch, 0); }
        catch (Exception ex) { TerriasLog.Error("Olimya career initialization failed", ex); }
    }

    public static void Init(ScriptExecutor self)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
            ScriptDelegateApi.BindParameterized(self, "InitScript", "", InitBound);
        }
        catch (Exception ex) { TerriasLog.Error("Olimya skill initialization failed", ex); }
    }

    public static void Use(ScriptExecutor self)
    {
        try { OlimyaRoleApplication.UseGoldenTouch(self); }
        catch (Exception ex) { TerriasLog.Error("Olimya Golden Touch failed", ex); }
    }

    private static void InitBound(ScriptExecutor self, string _) => Init(self);
}
