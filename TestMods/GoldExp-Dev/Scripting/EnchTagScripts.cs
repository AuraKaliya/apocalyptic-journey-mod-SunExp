using System;
using GoldExp.Dll.Infrastructure;

namespace GoldExp.Dll.Scripting;

public static class EnchTagScripts
{
    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            if (id == "gold_dream_keyword")
            {
                GoldExpLog.Debug("Golden Dream EnchTag UseScript ignored; runtime ActionAfter hook resolves the effect.");
            }
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("EnchTag Use failed: " + id, ex);
        }
    }
}
