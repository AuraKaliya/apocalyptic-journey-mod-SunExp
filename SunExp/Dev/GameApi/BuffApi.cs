using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class BuffApi
{
    private const string PersistentEmberKey = "SunExpWunaPersistentEmber";

    private static readonly HashSet<string> PositiveExcludeIds = new(StringComparer.Ordinal)
    {
        "solar_radiance",
        "gathered_flame",
        "scorching_canopy",
        "ember_cloak",
        "solar_crown",
        "solar_crown_tier",
        "origin_core_radiance",
        "cycle_gathered_flame",
        "afterglow_omen",
        SunExpIds.SolarRadiance,
        SunExpIds.GatheredFlame,
        SunExpIds.SolarCrown,
        SunExpIds.SolarCrownTier
    };

    public static int Level(IStatusManager? status, string buffId)
    {
        return status?.GetBuff(buffId)?.buffConfig?.Level ?? 0;
    }

    public static bool Has(IStatusManager? status, string buffId)
    {
        return status?.GetBuff(buffId) != null;
    }

    public static int NegativeTotal(IStatusManager? status)
    {
        var total = 0;
        foreach (var buff in NegativeBuffs(status))
        {
            total += Math.Max(0, buff.buffConfig?.Level ?? 0);
        }

        return total;
    }

    public static bool RemoveNegativeBuffs(ScriptExecutor executor, IStatusManager? status)
    {
        var removed = false;
        foreach (var buff in NegativeBuffs(status))
        {
            var id = buff.buffConfig?.BuffId;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            ExecutorApi.SetStatusForTarget(executor, status, "Self");
            executor.RemoveBuff(id);
            removed = true;
        }

        return removed;
    }

    public static void SetLevelOrRemove(ScriptExecutor executor, IStatusManager status, string buffId, int nextLevel)
    {
        var buff = status.GetBuff(buffId);
        if (buff == null)
        {
            return;
        }

        if (nextLevel <= 0)
        {
            ExecutorApi.SetStatusForTarget(executor, status, "Self");
            executor.RemoveBuff(buffId);
            return;
        }

        buff.buffConfig.Level = nextLevel;
    }

    public static int ConsumeEmberBeforeBurn(ScriptExecutor executor, IStatusManager? status)
    {
        if (status == null)
        {
            return 0;
        }

        var ember = status.GetBuff(SunExpIds.Ember);
        var burn = status.GetBuff(SunExpIds.Burn);
        var emberLevel = ember?.buffConfig?.Level ?? 0;
        var burnLevel = burn?.buffConfig?.Level ?? 0;
        var consumed = Math.Min(emberLevel, burnLevel);
        if (consumed <= 0)
        {
            return 0;
        }

        ExecutorApi.SetStatusForTarget(executor, status, "Self");
        SetLevelOrRemove(executor, status, SunExpIds.Burn, burnLevel - consumed);
        SetLevelOrRemove(executor, status, SunExpIds.Ember, emberLevel - consumed);
        if (emberLevel - consumed <= 0)
        {
            ClearEmberDamageBonus(executor, status);
        }
        else
        {
            SyncEmberDamageBonus(executor, status);
        }

        OnEmberConsumed(executor, status, consumed);
        SunExpLog.Debug("Ember consumed before burn: target=" + status.InstanceId + ", count=" + consumed);
        return consumed;
    }

    public static string EmberDamageBonusKey(IStatusManager? status)
    {
        var id = status?.InstanceId ?? "unknown";
        return "SunExpEmberDamageBonus_" + id;
    }

    public static int SyncEmberDamageBonus(ScriptExecutor? executor, IStatusManager? status)
    {
        if (executor == null)
        {
            return 0;
        }

        status ??= executor.Self;
        if (status == null)
        {
            return 0;
        }

        var level = Math.Max(0, Level(status, SunExpIds.Ember));
        var key = EmberDamageBonusKey(status);
        var applied = ExecutorApi.CombatIntGet(key);
        var delta = level - applied;
        if (delta != 0)
        {
            ExecutorApi.SetStatusForTarget(executor, status, "Self");
            executor.ChangeDynamicVarPercent("PercentDamage", delta.ToString());
            ExecutorApi.CombatIntSet(key, level);
        }

        return level;
    }

    public static int ClearEmberDamageBonus(ScriptExecutor? executor, IStatusManager? status)
    {
        if (executor == null)
        {
            return 0;
        }

        status ??= executor.Self;
        if (status == null)
        {
            return 0;
        }

        var key = EmberDamageBonusKey(status);
        var applied = ExecutorApi.CombatIntGet(key);
        if (applied != 0)
        {
            ExecutorApi.SetStatusForTarget(executor, status, "Self");
            executor.ChangeDynamicVarPercent("PercentDamage", (-applied).ToString());
            ExecutorApi.CombatIntSet(key, 0);
        }

        return applied;
    }

    public static int OnEmberConsumed(ScriptExecutor? executor, IStatusManager? status, int consumed)
    {
        if (executor?.Self == null || status == null || consumed <= 0)
        {
            return 0;
        }

        if (!ExecutorApi.IsSelf(executor, status) || ExecutorApi.GetVar(executor, "SunExpWunaRadianceDone", null!) == null)
        {
            return consumed;
        }

        var maxHp = ReadIntProperty(status, "MaxHp");
        var heal = Math.Max(1, maxHp * consumed / 100);
        executor.SetStatus("Self");
        executor.ChangeHp(heal.ToString());
        executor.ChangeMaxHp(consumed.ToString());
        PlayerApi.SetGameVar(PersistentEmberKey, Math.Max(0, Math.Min(99, Level(status, SunExpIds.Ember))).ToString());
        return consumed;
    }

    private static IEnumerable<IBuffItem> NegativeBuffs(IStatusManager? status)
    {
        if (status == null)
        {
            yield break;
        }

        var buffs = status.GetBuffs();
        if (buffs == null)
        {
            yield break;
        }

        foreach (var buff in buffs)
        {
            if (buff?.buffConfig == null)
            {
                continue;
            }

            if (IsPositiveExcluded(buff.buffConfig.BuffId))
            {
                continue;
            }

            var type = buff.buffConfig.Type ?? "";
            if (type == "Negative" || type.Contains("负面"))
            {
                yield return buff;
            }
        }
    }

    private static bool IsPositiveExcluded(string? buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        const string prefix = "SunExp_sunexp_";
        var id = buffId ?? "";
        var normalized = id.StartsWith(prefix, StringComparison.Ordinal)
            ? id.Substring(prefix.Length)
            : id;
        return PositiveExcludeIds.Contains(id) || PositiveExcludeIds.Contains(normalized);
    }

    private static int ReadIntProperty(object target, string name)
    {
        try
        {
            var value = target.GetType().GetProperty(name)?.GetValue(target)
                ?? target.GetType().GetField(name)?.GetValue(target);
            return value is int intValue ? intValue : DictionaryUtil.ParseInt(Convert.ToString(value));
        }
        catch
        {
            return 0;
        }
    }
}
