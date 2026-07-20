using System;
using AuraGameData.Shared.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class DamageApi
{
    public static bool HasNativeDamageIdentity(ScriptExecutor? executor)
    {
        return !string.IsNullOrWhiteSpace(DictionaryUtil.Get(executor?.dataConfig?.data, "Id"));
    }

    public static ScriptExecutor? CreateCardSourceExecutor(
        IStatusManager? source,
        string sourceCardId,
        string origin)
    {
        if (source == null || string.IsNullOrWhiteSpace(sourceCardId))
        {
            return null;
        }

        try
        {
            var resolvedCardId = CardApi.ResolveCardId(sourceCardId);
            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, resolvedCardId);
            var config = handle == null
                ? null
                : AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest { Definition = handle }).Instance as DataConfig;
            if (config == null)
            {
                SunExpLog.Warn("[DamageSource] registered card definition unavailable; origin=" + origin + ", card=" + resolvedCardId + ".");
                return null;
            }
            var executor = config.scriptExecutor as ScriptExecutor;
            if (executor == null)
            {
                SunExpLog.Warn("[DamageSource] card executor unavailable; origin=" + origin + ", card=" + resolvedCardId + ".");
                return null;
            }

            executor.Self = source;
            executor.SetStatus("Self");
            if (!HasNativeDamageIdentity(executor))
            {
                SunExpLog.Warn("[DamageSource] card executor has no native Id; origin=" + origin + ", card=" + resolvedCardId + ".");
                return null;
            }

            return executor;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DamageSource] failed to create card executor; origin=" + origin + ", card=" + sourceCardId + ".", ex);
            return null;
        }
    }

    public static bool AddStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount, string fallbackStatus = "Target")
    {
        if (executor == null || string.IsNullOrWhiteSpace(buffId) || amount <= 0)
        {
            return false;
        }

        TargetApi.SetStatusForTarget(executor, target, fallbackStatus);
        executor.AddBuff(buffId, amount.ToString());
        return true;
    }

    public static bool RemoveStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, string fallbackStatus = "Self")
    {
        if (executor == null || string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        TargetApi.SetStatusForTarget(executor, target, fallbackStatus);
        executor.RemoveBuff(buffId);
        return true;
    }

    public static int RemoveBuffStacks(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount)
    {
        if (target == null || string.IsNullOrWhiteSpace(buffId) || amount <= 0)
        {
            return 0;
        }

        var buff = target.GetBuff(buffId);
        var level = buff?.buffConfig?.Level ?? 0;
        var removed = Math.Min(level, amount);
        if (removed <= 0)
        {
            return 0;
        }

        var next = level - removed;
        if (next <= 0)
        {
            if (executor != null)
            {
                RemoveStatusBuff(executor, target, buffId);
            }
            else
            {
                target.RemoveBuff(buffId);
            }
        }
        else if (buff?.buffConfig != null)
        {
            buff.buffConfig.Level = next;
        }

        return removed;
    }

    public static bool DealDamage(ScriptExecutor? executor, int amount, string damageType = "")
    {
        if (executor == null || amount <= 0)
        {
            return false;
        }

        if (!HasNativeDamageIdentity(executor))
        {
            SunExpLog.WarnOnce(
                "damage-source-missing-id",
                "[DamageSource] rejected native damage because the executor has no data Id. Use CreateCardSourceExecutor for status-triggered damage.");
            return false;
        }

        try
        {
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
        catch (Exception ex)
        {
            SunExpLog.Error("[DamageSource] native damage failed; id="
                + DictionaryUtil.Get(executor.dataConfig?.data, "Id")
                + ", amount="
                + amount
                + ", type="
                + (string.IsNullOrWhiteSpace(damageType) ? "Normal" : damageType)
                + ".", ex);
            return false;
        }
    }

    public static bool DealDamageToTarget(
        ScriptExecutor? executor,
        IStatusManager? target,
        int amount,
        string fallbackStatus = "Target",
        string damageType = "")
    {
        if (executor == null || amount <= 0)
        {
            return false;
        }

        TargetApi.SetStatusForTarget(executor, target, fallbackStatus);
        return DealDamage(executor, amount, damageType);
    }

    public static int DealTrueDamageAllEnemiesByMaxHp(ScriptExecutor? executor)
    {
        if (executor == null)
        {
            return 0;
        }

        var hit = 0;
        foreach (var target in TargetApi.EnemyTargets(executor))
        {
            var damage = Math.Max(1, StatusApi.MaxHp(target));
            TargetApi.SetStatusForTarget(executor, target, "AllTarget");
            if (DealDamage(executor, damage, "True"))
            {
                hit++;
            }
        }

        return hit;
    }

    public static void AddDamageDescription(ScriptExecutor? executor, string index, int amount)
    {
        AddDescription(executor, index, "Damage", Math.Max(0, amount));
    }

    public static void AddValueDescription(ScriptExecutor? executor, string index, int amount)
    {
        AddDescription(executor, index, "Value", Math.Max(0, amount));
    }

    private static void AddDescription(ScriptExecutor? executor, string index, string type, int amount)
    {
        if (executor == null)
        {
            return;
        }

        var value = Math.Max(0, amount).ToString();
        try
        {
            executor.AddDescription(index, type, value);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("AddDescription fallback used: index=" + index + ", type=" + type + ", value=" + value + ", error=" + ex.Message);
            ScriptVarApi.SetVar(executor, "DesVal" + index, value);
        }
    }
}
