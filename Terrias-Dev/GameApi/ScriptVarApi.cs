using System;

namespace Terrias.Dll.GameApi;

public static class ScriptVarApi
{
    public static string GetVar(ScriptExecutor? executor, string key, string fallback = "")
    {
        if (executor?.Vars == null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        return executor.Vars.TryGetValue(key, out var value) && value != null ? value : fallback;
    }

    public static void SetVar(ScriptExecutor? executor, string key, object value)
    {
        if (executor?.Vars == null || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        executor.Vars[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
}
