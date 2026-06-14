using System;
using GoldExp.Dll.Infrastructure;
using GoldExp.Dll.Mechanics;

namespace GoldExp.Dll.Scripting;

public static class BuffScripts
{
    public static void Apply(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "false_gold":
                case "debt_1":
                case "debt_2":
                case "debt_3":
                case "golden_potential":
                    GoldDreamService.EnsureCombatHooks(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("Buff Apply failed: " + id, ex);
        }
    }
}
