using System;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class ScriptEventApi
{
    public static string? RegisterHook(ScriptExecutor? executor, string hookKey, string tokenKey)
    {
        if (executor?.Vars == null)
        {
            return "0";
        }

        if (ScriptVarApi.GetVar(executor, hookKey, "0") == "1")
        {
            return null;
        }

        var token = DictionaryUtil.ParseInt(ScriptVarApi.GetVar(executor, tokenKey, "0")) + 1;
        ScriptVarApi.SetVar(executor, hookKey, "1");
        ScriptVarApi.SetVar(executor, tokenKey, token);
        return token.ToString();
    }

    public static bool IsHookTokenActive(ScriptExecutor? executor, string tokenKey, string? token)
    {
        if (executor?.Vars == null)
        {
            return true;
        }

        return ScriptVarApi.GetVar(executor, tokenKey) == Convert.ToString(token);
    }

    public static void ClearHook(ScriptExecutor? executor, string hookKey, string tokenKey)
    {
        if (executor?.Vars == null)
        {
            return;
        }

        ScriptVarApi.SetVar(executor, hookKey, "0");
        ScriptVarApi.SetVar(executor, tokenKey, DictionaryUtil.ParseInt(ScriptVarApi.GetVar(executor, tokenKey, "0")) + 1);
    }

    public static bool TryAddEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")
    {
        if (executor == null || executor.Self == null || string.IsNullOrWhiteSpace(eventName) || script == null)
        {
            return false;
        }

        try
        {
            executor.AddEvent(eventName, script);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("TryAddEvent skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }

    public static bool TryAddTokenedEvent(ScriptExecutor? executor, string eventName, string tokenKey, string? token, Action script, string context = "")
    {
        if (string.IsNullOrWhiteSpace(tokenKey) || script == null)
        {
            return false;
        }

        return TryAddEvent(executor, eventName, new Action(() =>
        {
            if (IsHookTokenActive(executor, tokenKey, token))
            {
                script();
            }
        }), context);
    }

    public static bool TryAddOwnedEventListener(
        string eventName,
        Action script,
        object owner,
        EventDispose dispose = EventDispose.OnFightEnd,
        string context = "")
    {
        if (string.IsNullOrWhiteSpace(eventName) || script == null || owner == null)
        {
            return false;
        }

        try
        {
            EventCenter.Instance.AddEventListener(eventName, script, owner, dispose);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("TryAddOwnedEventListener skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }

    public static bool TryAddTempEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")
    {
        if (executor == null || executor.Self == null || string.IsNullOrWhiteSpace(eventName) || script == null)
        {
            return false;
        }

        try
        {
            executor.AddTempEvent(eventName, script);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("TryAddTempEvent skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }
}
