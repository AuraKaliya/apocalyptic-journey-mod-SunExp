using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class BuffApi
{
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
        SunExpIds.SolarCrownTier,
        SunExpIds.StarStonePouch,
        SunExpIds.MiracleClock,
        SunExpIds.Starlight,
        SunExpIds.StarBlessing,
        SunExpIds.StarScore,
        SunExpIds.Resonance,
        SunExpIds.StarClayBody,
        SunExpIds.StarClayDollTrait
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

    public static int PositiveTotal(IStatusManager? status)
    {
        var total = 0;
        foreach (var buff in PositiveBuffs(status))
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

    public static int RemoveNegativeBuffsAndCount(ScriptExecutor executor, IStatusManager? status)
    {
        var buffIds = NegativeBuffs(status)
            .Select(buff => buff.buffConfig?.BuffId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var id in buffIds)
        {
            ExecutorApi.SetStatusForTarget(executor, status, "Self");
            executor.RemoveBuff(id);
        }

        return buffIds.Count;
    }

    public static bool RemovePositiveBuffs(ScriptExecutor executor, IStatusManager? status)
    {
        var removed = false;
        foreach (var buff in PositiveBuffs(status))
        {
            var id = buff.buffConfig?.BuffId;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            ExecutorApi.SetStatusForTarget(executor, status, "Target");
            executor.RemoveBuff(id);
            removed = true;
        }

        return removed;
    }

    public static bool RemoveRandomPositiveBuff(ScriptExecutor executor, IStatusManager? status)
    {
        var buffIds = PositiveBuffs(status)
            .Select(buff => buff.buffConfig?.BuffId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (buffIds.Count <= 0)
        {
            return false;
        }

        var id = buffIds[UnityEngine.Random.Range(0, buffIds.Count)];
        ExecutorApi.SetStatusForTarget(executor, status, "Target");
        executor.RemoveBuff(id);
        return true;
    }

    public static int RemoveBuffsExceptAndCount(ScriptExecutor executor, IStatusManager? status, params string[] excludeIds)
    {
        if (status == null)
        {
            return 0;
        }

        var excluded = new HashSet<string>(excludeIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var buffIds = (status.GetBuffs() ?? Array.Empty<IBuffItem>())
            .Where(buff => buff?.buffConfig != null && buff.buffConfig.Level > 0)
            .Select(buff => buff.buffConfig.BuffId)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !excluded.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var id in buffIds)
        {
            ExecutorApi.SetStatusForTarget(executor, status, "Target");
            executor.RemoveBuff(id);
        }

        return buffIds.Count;
    }

    public static int PositiveKindCount(IStatusManager? status)
    {
        return PositiveBuffs(status).Count();
    }

    public static int NegativeKindCount(IStatusManager? status)
    {
        return NegativeBuffs(status).Count();
    }

    public static int BuffKindCount(IStatusManager? status)
    {
        if (status == null)
        {
            return 0;
        }

        var buffs = status.GetBuffs();
        if (buffs == null)
        {
            return 0;
        }

        return buffs
            .Where(buff => buff?.buffConfig != null && (buff.buffConfig.Level > 0))
            .Select(buff => buff.buffConfig.BuffId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    public static int PartyBuffKindSum(IEnumerable<IStatusManager> statuses)
    {
        var total = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var status in statuses)
        {
            if (status == null)
            {
                continue;
            }

            var key = status.InstanceId ?? status.GetHashCode().ToString();
            if (seen.Add(key))
            {
                total += BuffKindCount(status);
            }
        }

        return total;
    }

    public static bool IncreaseRandomPositiveBuff(IStatusManager? status, int amount)
    {
        return IncreaseRandomBuff(PositiveBuffs(status).ToList(), amount);
    }

    public static bool IncreaseRandomNegativeBuff(IStatusManager? status, int amount)
    {
        return IncreaseRandomBuff(NegativeBuffs(status).ToList(), amount);
    }

    public static int IncreaseAllNegativeBuffs(IStatusManager? status, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var increased = 0;
        foreach (var buff in NegativeBuffs(status).ToList())
        {
            if (buff?.buffConfig == null)
            {
                continue;
            }

            buff.buffConfig.Level += amount;
            increased++;
        }

        return increased;
    }

    public static bool IsPositiveBuffId(string buffId)
    {
        return IsBuffType(buffId, IsPositiveType);
    }

    public static bool IsNegativeBuffId(string buffId)
    {
        return IsBuffType(buffId, IsNegativeType);
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

    public static void SetExactLevel(IStatusManager? status, string buffId, int nextLevel)
    {
        if (status == null || string.IsNullOrWhiteSpace(buffId))
        {
            return;
        }

        var level = Math.Max(0, nextLevel);
        var buff = status.GetBuff(buffId);
        if (level <= 0)
        {
            if (buff != null)
            {
                status.RemoveBuff(buffId);
            }

            return;
        }

        if (buff?.buffConfig == null)
        {
            status.AddBuff(buffId, level);
            return;
        }

        buff.buffConfig.Level = level;
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

    private static bool IncreaseRandomBuff(IReadOnlyList<IBuffItem> buffs, int amount)
    {
        if (buffs.Count <= 0 || amount <= 0)
        {
            return false;
        }

        var selected = buffs[UnityEngine.Random.Range(0, buffs.Count)];
        if (selected?.buffConfig == null)
        {
            return false;
        }

        selected.buffConfig.Level += amount;
        return true;
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

        SavePersistentEmber(executor, status);
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

        if (!ExecutorApi.IsSelf(executor, status) || !IsWunaActive())
        {
            return consumed;
        }

        var maxHp = ReadIntProperty(status, "MaxHp");
        var heal = Math.Max(1, maxHp * consumed / 100);
        executor.SetStatus("Self");
        executor.ChangeHp(heal.ToString());
        executor.ChangeMaxHp(consumed.ToString());
        SavePersistentEmber(executor, status);
        return consumed;
    }

    public static int SavePersistentEmber(ScriptExecutor? executor, IStatusManager? status)
    {
        if (executor?.Self == null || status == null || !ExecutorApi.IsSelf(executor, status) || !IsWunaActive())
        {
            return 0;
        }

        var level = Math.Max(0, Math.Min(99, Level(status, SunExpIds.Ember)));
        PlayerApi.SetScopedGameVar(SunExpIds.WunaPersistentEmber, status, level.ToString());
        return level;
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
            if (IsNegativeType(type))
            {
                yield return buff;
            }
        }
    }

    private static IEnumerable<IBuffItem> PositiveBuffs(IStatusManager? status)
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

            var type = buff.buffConfig.Type ?? "";
            if (IsPositiveType(type))
            {
                yield return buff;
            }
        }
    }

    private static bool IsNegativeType(string type)
    {
        return type == "Negative"
            || type.Contains("\u8d1f\u9762")
            || type.Contains("璐熼潰");
    }

    private static bool IsPositiveType(string type)
    {
        return type == "Positive"
            || type.Contains("\u6b63\u9762")
            || type.Contains("姝ｉ潰");
    }

    private static bool IsBuffType(string buffId, Func<string, bool> predicate)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        try
        {
            var config = Singleton<GameConfigManager>.Instance.GetOne(DataType.Buff, buffId);
            return predicate(DictionaryUtil.Get(config, "Type"));
        }
        catch
        {
            return false;
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

    public static bool IsWunaPlayerStatus(IStatusManager? status)
    {
        if (status == null || !IsWunaActive())
        {
            return false;
        }

        var localPlayerId = PlayerApi.LocalPlayerStatusId();
        if (!string.IsNullOrWhiteSpace(localPlayerId))
        {
            return string.Equals(status.InstanceId, localPlayerId, StringComparison.Ordinal);
        }

        return string.Equals(status.fatherObject?.GetType().Name, "FightPlayer", StringComparison.Ordinal);
    }

    public static bool IsWunaActive()
    {
        var careerId = PlayerApi.GetCurrentCareerId();
        if (!string.IsNullOrWhiteSpace(careerId)
            && careerId.IndexOf("wuna", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(careerId)
            && PlayerApi.GetGameVar(SunExpIds.WunaActive, "0") == "1";
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
