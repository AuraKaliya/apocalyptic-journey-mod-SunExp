using System;
using System.Collections.Generic;
using System.Linq;
using SanGuoShaExp.Dll.Infrastructure;

namespace SanGuoShaExp.Dll.GameApi;

public static class ExecutorApi
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

    public static bool IsHookTokenActive(ScriptExecutor? executor, string tokenKey, string? token)
    {
        if (executor?.Vars == null)
        {
            return true;
        }

        return GetVar(executor, tokenKey) == Convert.ToString(token);
    }

    public static void ClearHook(ScriptExecutor? executor, string hookKey, string tokenKey)
    {
        if (executor?.Vars == null)
        {
            return;
        }

        SetVar(executor, hookKey, "0");
        SetVar(executor, tokenKey, DictionaryUtil.ParseInt(GetVar(executor, tokenKey, "0")) + 1);
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
            SanGuoShaExpLog.Debug("TryAddEvent skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }

    public static void SetBaseScript(ScriptExecutor executor, string baseScript, bool canSelf = true)
    {
        DictionaryUtil.Set(executor?.Vars, "BaseScript", baseScript);
        DictionaryUtil.Set(executor?.Vars, "CanSelf", canSelf ? "True" : "False");
    }

    public static List<IStatusManager> EnemyTargets(ScriptExecutor? executor)
    {
        if (executor == null)
        {
            return new List<IStatusManager>();
        }

        executor.SetStatus("AllTarget");
        var selfId = executor.Self?.InstanceId;
        return executor.Object?
            .Where(target => target != null && target.InstanceId != selfId)
            .ToList() ?? new List<IStatusManager>();
    }

    public static bool AddStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount, string fallbackStatus = "Target")
    {
        if (executor == null || target == null || string.IsNullOrWhiteSpace(buffId) || amount <= 0)
        {
            return false;
        }

        if (target.InstanceId == executor.Self?.InstanceId)
        {
            executor.SetStatus("Self");
        }
        else
        {
            executor.SetStatusById(target.InstanceId);
        }

        executor.AddBuff(buffId, amount.ToString());
        return true;
    }
}
