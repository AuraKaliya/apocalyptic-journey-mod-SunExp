using System;
using System.Collections.Generic;

namespace Terrias.Dll.GameApi;

public static class ScriptDelegateApi
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Action<ScriptExecutor>> Cache = new(StringComparer.Ordinal);

    public static void BindParameterized(
        ScriptExecutor? executor,
        string scriptName,
        string id,
        Action<ScriptExecutor, string> handler)
    {
        if (executor?.ScriptDict == null
            || string.IsNullOrWhiteSpace(scriptName)
            || handler == null)
        {
            return;
        }

        var normalizedId = id ?? "";
        if (handler.Target != null)
        {
            executor.ScriptDict[scriptName.Trim()] = new Action<ScriptExecutor>(
                current => handler(current, normalizedId));
            return;
        }

        var method = handler.Method;
        var key = (method.DeclaringType?.FullName ?? "handler")
                  + ":"
                  + method.Name
                  + ":"
                  + scriptName.Trim()
                  + ":"
                  + normalizedId;
        Action<ScriptExecutor> direct;
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out direct!))
            {
                direct = current => handler(current, normalizedId);
                Cache[key] = direct;
            }
        }

        executor.ScriptDict[scriptName.Trim()] = direct;
    }
}
