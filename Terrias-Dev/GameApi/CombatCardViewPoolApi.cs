using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class CombatCardViewPoolApi
{
    private static Func<ScriptExecutor, DataConfig, string, bool>? materialize;

    public static void Register(Func<ScriptExecutor, DataConfig, string, bool> provider)
    {
        materialize = provider;
    }

    public static bool TryMaterialize(ScriptExecutor self, DataConfig config, string source)
    {
        if (!TerriasPerformanceSettings.CombatCardViewPoolEnabled
            || materialize == null
            || self == null
            || config == null)
        {
            return false;
        }

        try
        {
            return materialize(self, config, source ?? "");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CombatCardViewPool] materialize facade fallback: " + ex.Message);
            return false;
        }
    }
}
