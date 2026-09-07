using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class BuffApi
{
    private static Action<IStatusManager, int, string>? persistEmber;
    public static void ConfigureEmberPersistence(Action<IStatusManager, int, string> writer) => persistEmber = writer;
    public static event Action<ScriptExecutor, IStatusManager, int>? EmberConsumed;
    private static Func<string?, bool>? excludedFromPositiveEffects;
    public static void ConfigureClassification(Func<string?, bool> policy) => excludedFromPositiveEffects = policy;

    public static int Level(IStatusManager? status, string buffId)
    {
        return status?.GetBuff(buffId)?.buffConfig?.Level ?? 0;
    }

    public static int ReducePerTurn(IStatusManager? status, string buffId)
    {
        return Math.Max(0, status?.GetBuff(buffId)?.buffConfig?.ReducePerTurn ?? 0);
    }

    public static ScriptExecutor? Executor(IStatusManager? status, string buffId)
    {
        return status?.GetBuff(buffId)?.buffConfig?.dataConfig?.scriptExecutor as ScriptExecutor;
    }

    public static bool Has(IStatusManager? status, string buffId)
    {
        return status?.GetBuff(buffId) != null;
    }

    public static IReadOnlyDictionary<string, int> SnapshotLevels(IStatusManager? status)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (status == null) return result;
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var bar = status.GetType().GetField("buffBarUI", flags)?.GetValue(status)
                      ?? status.GetType().GetProperty("buffBarUI", flags)?.GetValue(status);
            if (bar == null) return result;
            var dictionary = bar.GetType().GetField("BuffDic", flags)?.GetValue(bar)
                             ?? bar.GetType().GetProperty("BuffDic", flags)?.GetValue(bar);
            if (dictionary is not IDictionary entries) return result;
            foreach (DictionaryEntry entry in entries)
            {
                var id = Convert.ToString(entry.Key)?.Trim() ?? "";
                var level = (entry.Value as IBuffItem)?.buffConfig?.Level ?? 0;
                if (id.Length > 0 && level > 0) result[id] = level;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[BuffApi] status snapshot unavailable: " + ex.Message);
        }
        return result;
    }

    public static bool PrepareRuntimePresentation(
        IBuffItemConfig? buffConfig,
        IReadOnlyDictionary<string, string> presentationFields)
    {
        var source = buffConfig?.dataConfig;
        var replacement = CreateRuntimePresentation(source, presentationFields);
        if (replacement == null)
        {
            return false;
        }

        buffConfig!.dataConfig = replacement;
        return true;
    }

    public static bool ApplyRuntimePresentation(
        IStatusManager? status,
        string buffId,
        IReadOnlyDictionary<string, string> presentationFields)
    {
        return ApplyRuntimePresentation(status?.GetBuff(buffId), presentationFields);
    }

    public static bool ApplyRuntimePresentation(
        IBuffItem? buff,
        IReadOnlyDictionary<string, string> presentationFields)
    {
        var buffConfig = buff?.buffConfig;
        var config = buffConfig?.dataConfig;
        if (config?.Vars == null || presentationFields == null || presentationFields.Count == 0)
        {
            return false;
        }

        if (!RuntimePresentationDiffers(config, presentationFields))
        {
            return false;
        }

        var replacement = CreateRuntimePresentation(config, presentationFields);
        if (replacement == null || !CopyRuntimeExecutorContext(config, replacement))
        {
            return false;
        }

        buffConfig!.dataConfig = replacement;

        try
        {
            buff!.UpdateMsg();
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[BuffApi] runtime presentation refresh skipped: " + ex.Message);
        }

        return true;
    }

    private static DataConfig? CreateRuntimePresentation(
        IDataConfig? source,
        IReadOnlyDictionary<string, string> presentationFields)
    {
        if (source?.data == null || source.Vars == null
            || presentationFields == null || presentationFields.Count == 0)
        {
            return null;
        }

        var mergedData = new Dictionary<string, string>(source.data, StringComparer.Ordinal);
        var mergedVars = new Dictionary<string, string>(source.Vars, StringComparer.Ordinal);
        foreach (var field in presentationFields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                continue;
            }

            mergedData[field.Key] = field.Value ?? "";
            mergedVars[field.Key] = field.Value ?? "";
        }

        var replacement = AuraGameDataHostApi.CloneWritable(source, mergedData, mergedVars, preCompile: false);
        var scripts = source.scriptExecutor?.ScriptDict;
        if (scripts != null && replacement.scriptExecutor != null)
        {
            replacement.scriptExecutor.ScriptDict = new Dictionary<string, Delegate>(scripts);
        }

        return replacement;
    }

    private static bool CopyRuntimeExecutorContext(IDataConfig source, IDataConfig replacement)
    {
        var sourceExecutor = source.scriptExecutor;
        var replacementExecutor = replacement.scriptExecutor;
        if (sourceExecutor == null || replacementExecutor == null)
        {
            return sourceExecutor == null && replacementExecutor == null;
        }

        try
        {
            replacementExecutor.Self = sourceExecutor.Self;
            replacementExecutor.status = sourceExecutor.status;
            replacementExecutor.Target = sourceExecutor.Target;
            replacementExecutor.Object.Clear();
            replacementExecutor.Object.AddRange(sourceExecutor.Object);
            return sourceExecutor.Self == null || replacementExecutor.Self != null;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[BuffApi] runtime executor context migration failed: " + ex.Message);
            return false;
        }
    }

    private static bool RuntimePresentationDiffers(
        IDataConfig config,
        IReadOnlyDictionary<string, string> presentationFields)
    {
        foreach (var field in presentationFields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                continue;
            }

            var expected = field.Value ?? "";
            if (!config.data.TryGetValue(field.Key, out var dataValue)
                || !string.Equals(dataValue, expected, StringComparison.Ordinal)
                || !string.Equals(DictionaryUtil.Get(config.Vars, field.Key), expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryAddBattleScopedBuffOnce(
        IStatusManager? status,
        string buffId,
        int level,
        string featureId,
        string operationId,
        string effectCategory = "buff")
    {
        if (status == null || string.IsNullOrWhiteSpace(buffId) || level <= 0)
        {
            return false;
        }

        if (!AuraFeatureSwitchRuntime.IsEnabled(TerriasIds.ModId, "Battle.StartTraitBuffs"))
        {
            return false;
        }

        var targetId = string.IsNullOrWhiteSpace(status.InstanceId)
            ? status.GetHashCode().ToString()
            : status.InstanceId;
        if (!AuraLifecycleOperationLedger.TryClaimBattleOperation(
                TerriasIds.ModId,
                featureId,
                operationId,
                targetId,
                effectCategory,
                buffId))
        {
            TerriasLog.Debug("Skipped duplicate battle-scoped buff: feature="
                            + featureId
                            + ", operation="
                            + operationId
                            + ", buff="
                            + buffId
                            + ", target="
                            + targetId);
            return false;
        }

        status.AddBuff(buffId, level);
        return true;
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

    public static int RemoveNegativeBuffsAndTotalExcept(ScriptExecutor executor, IStatusManager? status, params string[] excludeIds)
    {
        var excluded = new HashSet<string>(excludeIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var entries = NegativeBuffs(status)
            .Select(buff => new
            {
                Id = buff.buffConfig?.BuffId,
                Level = Math.Max(0, buff.buffConfig?.Level ?? 0)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id) && !excluded.Contains(entry.Id!))
            .ToList();

        var total = entries.Sum(entry => entry.Level);
        foreach (var id in entries
                     .Select(entry => entry.Id)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.Ordinal))
        {
            ExecutorApi.SetStatusForTarget(executor, status, "Self");
            executor.RemoveBuff(id!);
        }

        return total;
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
        return RemoveRandomPositiveBuffAndGetLevel(executor, status) > 0;
    }

    public static bool HasRemovablePositiveBuff(IStatusManager? status)
    {
        return PositiveBuffs(status).Any(buff => buff.buffConfig?.Level > 0);
    }

    public static int RemoveRandomPositiveBuffAndGetLevel(ScriptExecutor executor, IStatusManager? status)
    {
        var buffs = PositiveBuffs(status)
            .Where(buff => buff.buffConfig?.Level > 0 && !string.IsNullOrWhiteSpace(buff.buffConfig.BuffId))
            .GroupBy(buff => buff.buffConfig.BuffId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (buffs.Count <= 0)
        {
            return 0;
        }

        var buff = buffs[UnityEngine.Random.Range(0, buffs.Count)];
        var id = buff.buffConfig.BuffId;
        var removedLevel = Math.Max(0, buff.buffConfig.Level);
        ExecutorApi.SetStatusForTarget(executor, status, "Target");
        executor.RemoveBuff(id);
        return removedLevel;
    }

    public static bool RemoveRandomNegativeBuff(ScriptExecutor executor, IStatusManager? status)
    {
        var buffs = NegativeBuffs(status)
            .Where(buff => buff.buffConfig?.Level > 0 && !string.IsNullOrWhiteSpace(buff.buffConfig.BuffId))
            .GroupBy(buff => buff.buffConfig.BuffId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (buffs.Count <= 0)
        {
            return false;
        }

        var id = buffs[UnityEngine.Random.Range(0, buffs.Count)].buffConfig.BuffId;
        ExecutorApi.SetStatusForTarget(executor, status, "Self");
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

    public static int IncreaseAllPositiveBuffs(IStatusManager? status, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var increased = 0;
        foreach (var buff in PositiveBuffs(status).ToList())
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

    public static int DoubleAllNegativeBuffs(IStatusManager? status)
    {
        var doubled = 0;
        foreach (var buff in NegativeBuffs(status).ToList())
        {
            if (buff?.buffConfig == null || buff.buffConfig.Level <= 0)
            {
                continue;
            }

            buff.buffConfig.Level *= 2;
            doubled++;
        }

        return doubled;
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
        var field = FieldApi.FieldIdFromBuffId(buffId);
        if (field != TerriasFieldId.None)
        {
            FieldApi.SetSharedFieldState(field, level);
            RemoveFieldCarrierIfPresent(status, buffId, "BuffApi.SetExactLevel");
            return;
        }

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

    public static int SetExactLevelWithNativeRefresh(IStatusManager? status, string buffId, int nextLevel)
    {
        if (status == null || string.IsNullOrWhiteSpace(buffId))
        {
            return 0;
        }

        var requested = Math.Max(0, nextLevel);
        var current = Level(status, buffId);
        if (requested == current)
        {
            return current;
        }

        if (requested > current)
        {
            status.AddBuff(buffId, requested - current);
            var refreshed = Level(status, buffId);
            if (refreshed == requested)
            {
                return refreshed;
            }

            TerriasLog.Warn("[BuffApi] native level refresh did not reach the requested level; buff="
                + buffId
                + ", requested="
                + requested
                + ", actual="
                + refreshed
                + ". Applying exact fallback.");
        }

        SetExactLevel(status, buffId, requested);
        return Level(status, buffId);
    }

    public static void SetExactLevel(IStatusManager? status, string buffId, int nextLevel, bool keepZero)
    {
        if (!keepZero)
        {
            SetExactLevel(status, buffId, nextLevel);
            return;
        }

        if (status == null || string.IsNullOrWhiteSpace(buffId))
        {
            return;
        }

        var level = Math.Max(0, nextLevel);
        var field = FieldApi.FieldIdFromBuffId(buffId);
        if (field != TerriasFieldId.None)
        {
            FieldApi.SetSharedFieldState(field, level);
            RemoveFieldCarrierIfPresent(status, buffId, "BuffApi.SetExactLevelKeepZero");
            return;
        }

        var buff = status.GetBuff(buffId);
        if (buff?.buffConfig == null)
        {
            if (level > 0)
            {
                status.AddBuff(buffId, level);
            }

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

        var ember = status.GetBuff(TerriasIds.Ember);
        var burn = status.GetBuff(TerriasIds.Burn);
        var emberLevel = ember?.buffConfig?.Level ?? 0;
        var burnLevel = burn?.buffConfig?.Level ?? 0;
        var consumed = Math.Min(emberLevel, burnLevel);
        if (consumed <= 0)
        {
            return 0;
        }

        ExecutorApi.SetStatusForTarget(executor, status, "Self");
        SetLevelOrRemove(executor, status, TerriasIds.Burn, burnLevel - consumed);
        SetLevelOrRemove(executor, status, TerriasIds.Ember, emberLevel - consumed);
        if (emberLevel - consumed <= 0)
        {
            ClearEmberDamageBonus(executor, status);
        }
        else
        {
            SyncEmberDamageBonus(executor, status);
        }

        OnEmberConsumed(executor, status, consumed);
        TerriasLog.Debug("Ember consumed before burn: target=" + status.InstanceId + ", count=" + consumed);
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
        return "TerriasEmberDamageBonus_" + id;
    }

    public static int SyncEmberDamageBonus(ScriptExecutor? executor, IStatusManager? status)
    {
        status ??= executor?.Self;
        if (status == null)
        {
            return 0;
        }

        var level = Math.Max(0, Level(status, TerriasIds.Ember));
        var key = EmberDamageBonusKey(status);
        var applied = ExecutorApi.CombatIntGet(key);
        var delta = level - applied;
        if (delta != 0)
        {
            if (executor != null)
            {
                ExecutorApi.SetStatusForTarget(executor, status, "Self");
                executor.ChangeDynamicVarPercent("PercentDamage", delta.ToString());
            }
            else
            {
                StatusApi.AddDynamicPercent(status, "PercentDamage", delta);
            }

            ExecutorApi.CombatIntSet(key, level);
        }

        SavePersistentEmber(executor, status);
        return level;
    }

    public static int ClearEmberDamageBonus(ScriptExecutor? executor, IStatusManager? status)
    {
        status ??= executor?.Self;
        if (status == null)
        {
            return 0;
        }

        var key = EmberDamageBonusKey(status);
        var applied = ExecutorApi.CombatIntGet(key);
        if (applied != 0)
        {
            if (executor != null)
            {
                ExecutorApi.SetStatusForTarget(executor, status, "Self");
                executor.ChangeDynamicVarPercent("PercentDamage", (-applied).ToString());
            }
            else
            {
                StatusApi.AddDynamicPercent(status, "PercentDamage", -applied);
            }

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

        if (!ExecutorApi.IsSelf(executor, status))
        {
            return consumed;
        }

        SavePersistentEmber(executor, status);
        EmberConsumed?.Invoke(executor, status, consumed);
        return consumed;
    }

    public static int SavePersistentEmber(ScriptExecutor? executor, IStatusManager? status)
    {
        if (status == null)
        {
            return 0;
        }

        if (executor?.Self != null && !ExecutorApi.IsSelf(executor, status))
        {
            return 0;
        }

        var level = Math.Max(0, Math.Min(99, Level(status, TerriasIds.Ember)));
        if (persistEmber == null) throw new InvalidOperationException("Persistent ember service is unavailable.");
        persistEmber(status, level, "BuffApi.SavePersistentEmber");
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

            if (IsPositiveExcluded(buff.buffConfig.BuffId))
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
            var config = AuraGameDataHostApi.CopyRow(
                DataType.Buff,
                TerriasContentIdCompatibility.LookupCandidates(buffId, "terrias", "wuna", "columbina"));
            return predicate(DictionaryUtil.Get(config, "Type"));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPositiveExcluded(string? buffId) => (excludedFromPositiveEffects ?? throw new InvalidOperationException("Buff classification is not initialized."))(buffId);

    private static void RemoveFieldCarrierIfPresent(IStatusManager status, string buffId, string source)
    {
        FieldApi.RemoveFieldBuffCarrier(status, buffId, source);
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
            && PlayerApi.GetGameVar(TerriasIds.WunaActive, "0") == "1";
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
