using System;
using System.Collections.Generic;
using System.Linq;
using GoldExp.Dll.Infrastructure;

namespace GoldExp.Dll.GameApi;

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
        DictionaryUtil.Set(executor?.Vars, key, value);
    }

    public static int CombatIntGet(string key, int fallback = 0)
    {
        var map = FightManager.Instance?.TempVarsMap;
        if (map == null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        return map.TryGetValue(key, out var value) ? value : fallback;
    }

    public static int CombatIntSet(string key, int value)
    {
        var map = FightManager.Instance?.TempVarsMap;
        if (map == null || string.IsNullOrWhiteSpace(key))
        {
            return value;
        }

        map[key] = value;
        return value;
    }

    public static int CombatIntAdd(string key, int delta)
    {
        var next = CombatIntGet(key) + delta;
        CombatIntSet(key, next);
        return next;
    }

    public static void SetBaseScript(ScriptExecutor executor, string baseScript, bool canSelf = true)
    {
        DictionaryUtil.Set(executor?.Vars, "BaseScript", baseScript);
        DictionaryUtil.Set(executor?.Vars, "CanSelf", canSelf ? "True" : "False");
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
            GoldExpLog.Debug("TryAddEvent skipped: " + context + ", event=" + eventName + ", error=" + ex.Message);
            return false;
        }
    }

    public static int SelfBuffLevel(ScriptExecutor? executor, string buffId)
    {
        return BuffApi.Level(executor?.Self, buffId);
    }

    public static bool DealDamage(ScriptExecutor? executor, int amount, string damageType = "")
    {
        if (executor == null || amount <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(damageType))
        {
            executor.Damage(amount.ToString());
        }
        else
        {
            executor.Damage(amount.ToString(), damageType);
        }

        return true;
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

    public static IStatusManager? RandomEnemyTarget(ScriptExecutor? executor)
    {
        var candidates = EnemyTargets(executor);
        return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    public static bool SetStatusForTarget(ScriptExecutor? executor, IStatusManager? target, string fallbackStatus = "Self")
    {
        if (executor == null)
        {
            return false;
        }

        if (target == null)
        {
            executor.SetStatus(fallbackStatus);
            return true;
        }

        if (executor.Self != null && target.InstanceId == executor.Self.InstanceId)
        {
            executor.SetStatus("Self");
            return true;
        }

        executor.SetStatusById(target.InstanceId);
        return true;
    }

    public static void DealDamageAllEnemies(ScriptExecutor? executor, int amount, string damageType = "")
    {
        if (executor == null || amount <= 0)
        {
            return;
        }

        foreach (var target in EnemyTargets(executor))
        {
            SetStatusForTarget(executor, target, "Target");
            DealDamage(executor, amount, damageType);
        }
    }

    public static void DealDamageRandomEnemy(ScriptExecutor? executor, int amount, string damageType = "")
    {
        var target = RandomEnemyTarget(executor);
        SetStatusForTarget(executor, target, "Target");
        DealDamage(executor, amount, damageType);
    }

    public static int RemoveSelfBuffStacks(ScriptExecutor? executor, string buffId, int amount)
    {
        if (executor?.Self == null || string.IsNullOrWhiteSpace(buffId) || amount <= 0)
        {
            return 0;
        }

        var buff = executor.Self.GetBuff(buffId);
        var level = buff?.buffConfig?.Level ?? 0;
        var removed = Math.Min(level, amount);
        if (removed <= 0)
        {
            return 0;
        }

        var next = level - removed;
        executor.SetStatus("Self");
        if (next <= 0)
        {
            executor.RemoveBuff(buffId);
        }
        else if (buff?.buffConfig != null)
        {
            buff.buffConfig.Level = next;
        }

        return removed;
    }

    public static void AddDescription(ScriptExecutor? executor, string index, string type, int amount)
    {
        if (executor == null)
        {
            return;
        }

        try
        {
            executor.AddDescription(index, type, Math.Max(0, amount).ToString());
        }
        catch (Exception ex)
        {
            GoldExpLog.Warn("AddDescription fallback used: index=" + index + ", error=" + ex.Message);
            SetVar(executor, "DesVal" + index, Math.Max(0, amount));
        }
    }
}
