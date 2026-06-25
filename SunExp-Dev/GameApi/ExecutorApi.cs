using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class ExecutorApi
{
    private const int BurnUpperBoundFallback = 1;
    private const int SolarRadianceDefaultUpperBound = 12;
    private const int WunaSolarRadianceUpperBound = 15;

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

    public static int CombatIntAdd(string key, int amount)
    {
        return CombatIntSet(key, CombatIntGet(key) + amount);
    }

    public static string? RegisterHook(ScriptExecutor? executor, string hookKey, string tokenKey)
    {
        if (executor?.Vars == null)
        {
            return "0";
        }

        if (GetVar(executor, hookKey, "0") == "1")
        {
            return null;
        }

        var token = DictionaryUtil.ParseInt(GetVar(executor, tokenKey, "0")) + 1;
        SetVar(executor, hookKey, "1");
        SetVar(executor, tokenKey, token);
        return token.ToString();
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

    public static void SetBaseScript(ScriptExecutor executor, string baseScript, bool canSelf = true)
    {
        DictionaryUtil.Set(executor?.Vars, "BaseScript", baseScript);
        DictionaryUtil.Set(executor?.Vars, "CanSelf", canSelf ? "True" : "False");
    }

    public static int SelfBuffLevel(ScriptExecutor? executor, string buffId)
    {
        return BuffApi.Level(executor?.Self, buffId);
    }

    public static int StatusBuffLevel(IStatusManager? status, string buffId)
    {
        return BuffApi.Level(status, buffId);
    }

    public static int BurnUpperBound(IStatusManager? target)
    {
        return BuffUpperBound(target, SunExpIds.Burn, BurnUpperBoundFallback);
    }

    public static int SolarRadianceUpperBound(IStatusManager? target)
    {
        return BuffApi.IsWunaPlayerStatus(target)
            ? WunaSolarRadianceUpperBound
            : SolarRadianceDefaultUpperBound;
    }

    public static int BuffUpperBound(IStatusManager? target, string buffId, int fallback)
    {
        if (target != null && !string.IsNullOrWhiteSpace(buffId))
        {
            var liveUpperBound = target.GetBuff(buffId)?.buffConfig?.UpperBound ?? 0;
            if (liveUpperBound > 0)
            {
                return liveUpperBound;
            }
        }

        return ConfiguredBuffUpperBound(buffId, fallback);
    }

    private static int ConfiguredBuffUpperBound(string buffId, int fallback)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return fallback;
        }

        try
        {
            var data = Singleton<GameConfigManager>.Instance.GetOne(DataType.Buff, buffId);
            var configured = DictionaryUtil.ParseInt(DictionaryUtil.Get(data, "UpperBound"));
            return configured > 0 ? configured : fallback;
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Buff upper bound fallback used: id=" + buffId + ", fallback=" + fallback + ", error=" + ex.Message);
            return fallback;
        }
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

    public static List<IStatusManager> FriendlyTargets(ScriptExecutor? executor, bool includeSelf)
    {
        if (executor == null)
        {
            return new List<IStatusManager>();
        }

        var enemyIds = new HashSet<string>(EnemyTargets(executor).Select(target => target.InstanceId), StringComparer.Ordinal);
        executor.SetStatus("All");
        var result = new List<IStatusManager>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in executor.Object ?? new List<IStatusManager>())
        {
            if (target == null || target.InstanceId == null || enemyIds.Contains(target.InstanceId))
            {
                continue;
            }

            if (!includeSelf && IsSelf(executor, target))
            {
                continue;
            }

            if (seen.Add(target.InstanceId))
            {
                result.Add(target);
            }
        }

        if (includeSelf && executor.Self != null && seen.Add(executor.Self.InstanceId))
        {
            result.Add(executor.Self);
        }

        return result;
    }

    public static IStatusManager? RandomEnemyTarget(ScriptExecutor? executor, bool requireBurn)
    {
        var candidates = EnemyTargets(executor)
            .Where(target => !requireBurn || StatusBuffLevel(target, SunExpIds.Burn) > 0)
            .ToList();
        return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    public static IStatusManager? RandomFriendlyTarget(ScriptExecutor? executor, bool includeSelf)
    {
        var candidates = FriendlyTargets(executor, includeSelf);
        if (candidates.Count == 0)
        {
            return includeSelf ? executor?.Self : null;
        }

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    public static IStatusManager? PrimaryTarget(ScriptExecutor? executor)
    {
        if (executor == null)
        {
            return null;
        }

        if (executor.Target != null && !IsSelf(executor, executor.Target))
        {
            return executor.Target;
        }

        if (executor.Self == null)
        {
            return null;
        }

        try
        {
            executor.SetStatus("Target");
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Primary target unavailable while resolving script display: " + ex.Message);
            return null;
        }

        return executor.Object?.FirstOrDefault(target => target != null && !IsSelf(executor, target));
    }

    public static IStatusManager? PrimaryTargetIncludingSelf(ScriptExecutor? executor)
    {
        if (executor == null)
        {
            return null;
        }

        if (executor.Target != null)
        {
            return executor.Target;
        }

        if (executor.Self == null)
        {
            return null;
        }

        try
        {
            executor.SetStatus("Target");
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Primary target unavailable while resolving script display: " + ex.Message);
        }

        return executor.Object?.FirstOrDefault(target => target != null) ?? executor.Self;
    }

    public static bool IsSelf(ScriptExecutor? executor, IStatusManager? target)
    {
        return executor?.Self != null && target?.InstanceId != null && executor.Self.InstanceId == target.InstanceId;
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

        if (IsSelf(executor, target))
        {
            executor.SetStatus("Self");
            return true;
        }

        executor.SetStatusById(target.InstanceId);
        return true;
    }

    public static bool AddStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount, string fallbackStatus = "Target")
    {
        if (executor == null || string.IsNullOrWhiteSpace(buffId) || amount <= 0)
        {
            return false;
        }

        SetStatusForTarget(executor, target, fallbackStatus);
        executor.AddBuff(buffId, amount.ToString());
        return true;
    }

    public static bool RemoveStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, string fallbackStatus = "Self")
    {
        if (executor == null || string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        SetStatusForTarget(executor, target, fallbackStatus);
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

        SetStatusForTarget(executor, target, fallbackStatus);
        return DealDamage(executor, amount, damageType);
    }

    public static int StatusMaxHp(IStatusManager? status)
    {
        return ReadIntProperty(status, "MaxHp");
    }

    public static int DealTrueDamageAllEnemiesByMaxHp(ScriptExecutor? executor)
    {
        if (executor == null)
        {
            return 0;
        }

        var hit = 0;
        foreach (var target in EnemyTargets(executor))
        {
            var damage = Math.Max(1, StatusMaxHp(target));
            SetStatusForTarget(executor, target, "AllTarget");
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
            SetVar(executor, "DesVal" + index, value);
        }
    }

    public static int SolarMultiplier(ScriptExecutor? executor)
    {
        return BuffApi.Has(executor?.Self, SunExpIds.SolarCrown) ? 2 : 1;
    }

    public static int SolarCoefficient(ScriptExecutor? executor, IStatusManager? target)
    {
        var radiance = SelfBuffLevel(executor, SunExpIds.SolarRadiance);
        var flame = SelfBuffLevel(executor, SunExpIds.GatheredFlame);
        var burn = StatusBuffLevel(target, SunExpIds.Burn);
        return SolarMultiplier(executor) * (radiance * 2 + flame / 3 + burn / 2);
    }

    public static int SolarKeywordDamage(ScriptExecutor? executor, int baseDamage, IStatusManager? target, int coefficientScale = 1)
    {
        return baseDamage + SolarCoefficient(executor, target) * coefficientScale;
    }

    public static int SolarKeywordBlock(ScriptExecutor? executor, int baseBlock)
    {
        return baseBlock + SolarCoefficient(executor, null);
    }

    public static bool DealSolarKeywordDamage(ScriptExecutor? executor, int baseDamage, IStatusManager? target, string fallbackStatus = "Target", int coefficientScale = 1)
    {
        if (executor == null)
        {
            return false;
        }

        SetStatusForTarget(executor, target, fallbackStatus);
        return DealDamage(executor, SolarKeywordDamage(executor, baseDamage, target, coefficientScale));
    }

    public static int DealSolarKeywordDamageAllEnemies(ScriptExecutor? executor, int baseDamage, int coefficientScale)
    {
        var max = 0;
        foreach (var target in EnemyTargets(executor))
        {
            var damage = SolarKeywordDamage(executor, baseDamage, target, coefficientScale);
            max = Math.Max(max, damage);
            SetStatusForTarget(executor, target, "Target");
            DealDamage(executor, damage);
        }

        return max;
    }

    public static int ApplySolarKeywordSkill(ScriptExecutor? executor, int baseBlock)
    {
        if (executor == null)
        {
            return 0;
        }

        var block = SolarKeywordBlock(executor, baseBlock);
        if (block > 0)
        {
            executor.SetStatus("Self");
            executor.ChangeDefence(block.ToString());
        }

        return block;
    }

    public static bool TriggerBurn(ScriptExecutor? executor, IStatusManager? target, string fallbackStatus = "Target")
    {
        if (executor == null || target == null || StatusBuffLevel(target, SunExpIds.Burn) <= 0)
        {
            return false;
        }

        BuffApi.ConsumeEmberBeforeBurn(executor, target);
        SetStatusForTarget(executor, target, fallbackStatus);
        executor.RunImmediately(SunExpIds.Burn, "StartRound");
        return true;
    }

    public static int TriggerBurnAllEnemies(ScriptExecutor? executor, int times = 1)
    {
        if (executor == null)
        {
            return 0;
        }

        var count = Math.Max(1, times);
        var triggered = 0;
        for (var i = 0; i < count; i++)
        {
            foreach (var target in EnemyTargets(executor))
            {
                BuffApi.ConsumeEmberBeforeBurn(executor, target);
            }

            executor.SetStatus("AllTarget");
            executor.RunImmediately(SunExpIds.Burn, "StartRound");
            triggered++;
        }

        return triggered;
    }

    public static int TriggerBurnAll(ScriptExecutor? executor, int times = 1)
    {
        if (executor == null)
        {
            return 0;
        }

        var count = Math.Max(1, times);
        var triggered = 0;
        for (var i = 0; i < count; i++)
        {
            executor.SetStatus("All");
            foreach (var target in executor.Object ?? new List<IStatusManager>())
            {
                BuffApi.ConsumeEmberBeforeBurn(executor, target);
            }

            executor.SetStatus("All");
            executor.RunImmediately(SunExpIds.Burn, "StartRound");
            triggered++;
        }

        return triggered;
    }

    public static bool ApplySelfBurn(ScriptExecutor? executor, int amount, bool includePending)
    {
        if (executor == null || amount <= 0)
        {
            return false;
        }

        if (IsSelfBurnProtected(executor, includePending))
        {
            RemoveStatusBuff(executor, executor.Self, SunExpIds.Burn);
            return false;
        }

        executor.SetStatus("Self");
        executor.AddBuff(SunExpIds.Burn, amount.ToString());
        return true;
    }

    public static bool ClearSelfBurnIfProtected(ScriptExecutor? executor, bool includePending)
    {
        if (executor?.Self == null || !IsSelfBurnProtected(executor, includePending))
        {
            return false;
        }

        RemoveStatusBuff(executor, executor.Self, SunExpIds.Burn);
        return true;
    }

    public static bool IsSelfBurnProtected(ScriptExecutor? executor, bool includePending)
    {
        if (executor?.Self == null)
        {
            return false;
        }

        var ward = executor.Self.GetBuff(SunExpIds.EmberCloak);
        if (ward?.buffConfig != null && ward.buffConfig.Level > 0)
        {
            return true;
        }

        return includePending && GetVar(executor, "SunExpBurnWardPending", "0") == "1";
    }

    public static void ApplyFieldBuff(ScriptExecutor? executor, string fieldId, int amount)
    {
        var field = ParseFieldId(fieldId);
        if (executor == null || field == SunExpFieldId.None || amount <= 0)
        {
            return;
        }

        SetActiveField(executor, field);
        executor.SetStatus("Self");
        executor.AddBuff(FieldBuffId(field), amount.ToString());
        SyncFieldStacks(executor, field);
        SunExpLog.Debug("Field applied: id=" + FieldSlug(field) + ", add=" + amount + ", stacks=" + FieldStacks(field));
    }

    public static bool ClearFieldBuff(ScriptExecutor? executor, string fieldId)
    {
        var field = ParseFieldId(fieldId);
        var buffId = FieldBuffId(field);
        if (executor == null || string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        SetVar(executor, "SunExpFieldInternalClear", "1");
        try
        {
            executor.SetStatus("Self");
            executor.RemoveBuff(buffId);
            SetVar(executor, "SunExpActiveFieldId", "");
            SetVar(executor, "SunExpActiveFieldStacks", "0");
            SyncFieldStacks(executor, field);
            SunExpLog.Debug("Field cleared internally: id=" + FieldSlug(field));
            return true;
        }
        finally
        {
            SetVar(executor, "SunExpFieldInternalClear", "0");
        }
    }

    public static string FieldBuffId(string fieldId)
    {
        return FieldBuffId(ParseFieldId(fieldId));
    }

    public static string FieldBuffId(SunExpFieldId field)
    {
        return field == SunExpFieldId.ScorchingCanopy ? SunExpIds.ScorchingCanopy : "";
    }

    public static string FieldCombatKey(string fieldId, string name)
    {
        return FieldCombatKey(ParseFieldId(fieldId), name);
    }

    public static string FieldCombatKey(SunExpFieldId field, string name)
    {
        return "SunExpField_" + FieldSlug(field) + "_" + name;
    }

    public static string FieldSlug(SunExpFieldId field)
    {
        return field == SunExpFieldId.ScorchingCanopy ? "scorching_canopy" : "";
    }

    public static SunExpFieldId ParseFieldId(string fieldId)
    {
        return fieldId == "scorching_canopy" ? SunExpFieldId.ScorchingCanopy : SunExpFieldId.None;
    }

    public static void SetSharedFieldState(string fieldId, int stacks)
    {
        SetSharedFieldState(ParseFieldId(fieldId), stacks);
    }

    public static void SetSharedFieldState(SunExpFieldId field, int stacks)
    {
        if (field == SunExpFieldId.None)
        {
            return;
        }

        var count = Math.Max(0, stacks);
        var active = count > 0 ? 1 : 0;
        var activeKey = FieldCombatKey(field, "Active");
        var stacksKey = FieldCombatKey(field, "Stacks");
        if (CombatIntGet(activeKey) != active || CombatIntGet(stacksKey) != count)
        {
            CombatIntAdd(FieldCombatKey(field, "Epoch"), 1);
        }

        CombatIntSet(activeKey, active);
        CombatIntSet(stacksKey, count);
        if (count <= 0)
        {
            CombatIntSet(FieldCombatKey(field, "TriggerLock"), 0);
        }
    }

    public static bool IsSharedFieldActive(string fieldId)
    {
        return IsSharedFieldActive(ParseFieldId(fieldId));
    }

    public static bool IsSharedFieldActive(SunExpFieldId field)
    {
        return field != SunExpFieldId.None
            && CombatIntGet(FieldCombatKey(field, "Active")) == 1
            && CombatIntGet(FieldCombatKey(field, "Stacks")) > 0;
    }

    public static int FieldStacks(string fieldId)
    {
        return FieldStacks(ParseFieldId(fieldId));
    }

    public static int FieldStacks(SunExpFieldId field)
    {
        return field == SunExpFieldId.None ? 0 : CombatIntGet(FieldCombatKey(field, "Stacks"));
    }

    public static int SyncFieldStacks(ScriptExecutor? executor, string fieldId)
    {
        return SyncFieldStacks(executor, ParseFieldId(fieldId));
    }

    public static int SyncFieldStacks(ScriptExecutor? executor, SunExpFieldId field)
    {
        var buffId = FieldBuffId(field);
        if (field == SunExpFieldId.None || string.IsNullOrWhiteSpace(buffId))
        {
            return 0;
        }

        var total = TotalFieldBuffStacks(executor, buffId);
        SetSharedFieldState(field, total);
        if (executor != null && GetVar(executor, "SunExpActiveFieldId") == FieldSlug(field))
        {
            SetVar(executor, "SunExpActiveFieldStacks", SelfBuffLevel(executor, buffId));
        }

        return total;
    }

    private static int TotalFieldBuffStacks(ScriptExecutor? executor, string buffId)
    {
        var total = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var status in AllCombatStatuses(executor))
        {
            if (status == null)
            {
                continue;
            }

            var key = status.InstanceId ?? status.GetHashCode().ToString();
            if (seen.Add(key))
            {
                total += Math.Max(0, StatusBuffLevel(status, buffId));
            }
        }

        return total;
    }

    private static IEnumerable<IStatusManager> AllCombatStatuses(ScriptExecutor? executor)
    {
        if (FightManager.Instance?.statuses != null)
        {
            foreach (var status in FightManager.Instance.statuses.Values)
            {
                if (status != null)
                {
                    yield return status;
                }
            }
        }

        if (executor?.Self != null)
        {
            yield return executor.Self;
        }

        foreach (var target in executor?.Object ?? new List<IStatusManager>())
        {
            if (target != null)
            {
                yield return target;
            }
        }

        if (executor?.Target != null)
        {
            yield return executor.Target;
        }
    }

    public static int SetActiveField(ScriptExecutor? executor, string fieldId)
    {
        return SetActiveField(executor, ParseFieldId(fieldId));
    }

    public static int SetActiveField(ScriptExecutor? executor, SunExpFieldId field)
    {
        if (executor == null)
        {
            return 0;
        }

        var fieldId = FieldSlug(field);
        if (string.IsNullOrWhiteSpace(fieldId))
        {
            return 0;
        }

        var current = GetVar(executor, "SunExpActiveFieldId");
        if (current == fieldId)
        {
            return DictionaryUtil.ParseInt(GetVar(executor, "SunExpActiveFieldEpoch", "0"));
        }

        var epoch = DictionaryUtil.ParseInt(GetVar(executor, "SunExpActiveFieldEpoch", "0")) + 1;
        SetVar(executor, "SunExpActiveFieldId", fieldId);
        SetVar(executor, "SunExpActiveFieldEpoch", epoch);
        SetVar(executor, "SunExpActiveFieldStacks", "0");
        return epoch;
    }

    public static bool BeginSharedFieldStartRound(ScriptExecutor? executor, string fieldId)
    {
        return BeginSharedFieldStartRound(executor, ParseFieldId(fieldId));
    }

    public static bool BeginSharedFieldStartRound(ScriptExecutor? executor, SunExpFieldId field)
    {
        if (field == SunExpFieldId.None)
        {
            return false;
        }

        var lockKey = FieldCombatKey(field, "TriggerLock");
        if (CombatIntGet(lockKey) == 1)
        {
            return false;
        }

        CombatIntSet(lockKey, 1);
        try
        {
            executor?.AddTempEvent("StartRoundEnd", new Action(() => CombatIntSet(lockKey, 0)));
        }
        catch
        {
            CombatIntSet(lockKey, 0);
        }

        return true;
    }

    public static bool IsActiveField(ScriptExecutor? executor, string fieldId, int? epoch = null, string? token = null)
    {
        return IsActiveField(executor, ParseFieldId(fieldId), epoch, token);
    }

    public static bool IsActiveField(ScriptExecutor? executor, SunExpFieldId field, int? epoch = null, string? token = null)
    {
        if (executor == null || field == SunExpFieldId.None)
        {
            return false;
        }

        SyncFieldStacks(executor, field);
        var fieldId = FieldSlug(field);
        var localActive = GetVar(executor, "SunExpActiveFieldId") == fieldId;
        var sharedActive = IsSharedFieldActive(field);
        if (epoch == null && token == null && sharedActive)
        {
            return true;
        }

        if (epoch == null && token == null && localActive)
        {
            return SyncFieldStacks(executor, fieldId) > 0;
        }

        if (!localActive)
        {
            return false;
        }

        if (epoch != null && DictionaryUtil.ParseInt(GetVar(executor, "SunExpActiveFieldEpoch", "0")) != epoch.Value)
        {
            return false;
        }

        if (token != null && !IsHookTokenActive(executor, "SunExpField_" + fieldId + "Token", token))
        {
            return false;
        }

        return epoch != null || token != null ? sharedActive : true;
    }

    public static bool IsActiveField(ScriptExecutor? executor, string fieldId)
    {
        return IsActiveField(executor, fieldId, null, null);
    }

    public static int TransferSelfBurnToRandomFriendly(ScriptExecutor? executor)
    {
        if (executor?.Self == null)
        {
            return 0;
        }

        var burn = SelfBuffLevel(executor, SunExpIds.Burn);
        if (burn <= 0)
        {
            return 0;
        }

        var target = RandomFriendlyTarget(executor, true) ?? executor.Self;
        RemoveStatusBuff(executor, executor.Self, SunExpIds.Burn, "Self");
        AddStatusBuff(executor, target, SunExpIds.Burn, burn, "Self");
        return burn;
    }

    public static void AddBurnToRandomEnemy(ScriptExecutor? executor, int amount)
    {
        var target = RandomEnemyTarget(executor, false);
        if (target != null)
        {
            AddStatusBuff(executor, target, SunExpIds.Burn, amount);
        }
    }

    public static int NegativeBuffTotal(IStatusManager? status)
    {
        return BuffApi.NegativeTotal(status);
    }

    public static bool RemoveAllNegativeBuffs(ScriptExecutor? executor, IStatusManager? status)
    {
        return executor != null && BuffApi.RemoveNegativeBuffs(executor, status);
    }

    public static bool RemoveAllPositiveBuffs(ScriptExecutor? executor, IStatusManager? status)
    {
        return executor != null && BuffApi.RemovePositiveBuffs(executor, status);
    }

    public static bool AddEnemyAction(ScriptExecutor? executor, string enemyCardId)
    {
        if (executor == null || string.IsNullOrWhiteSpace(enemyCardId))
        {
            return false;
        }

        executor.SetStatus("Self");
        executor.AddEnemyAction(new DataConfig(enemyCardId, DataType.EnemyCard));
        return true;
    }

    public static int SolarCrownTier(ScriptExecutor? executor)
    {
        return SelfBuffLevel(executor, SunExpIds.SolarCrownTier);
    }

    public static void PrepareSolarRadianceUpperBound(IStatusManager? target, string buffId)
    {
        if (target == null || buffId != SunExpIds.SolarRadiance)
        {
            return;
        }

        ApplySolarRadianceUpperBound(target, SolarRadianceUpperBound(target));
    }

    public static void FinalizeSolarRadianceUpperBound(IStatusManager? target, string buffId, int amount)
    {
        if (target == null || buffId != SunExpIds.SolarRadiance)
        {
            return;
        }

        var upperBound = SolarRadianceUpperBound(target);
        var buff = target.GetBuff(SunExpIds.SolarRadiance);
        var current = buff?.buffConfig?.Level ?? 0;
        ApplySolarRadianceUpperBound(target, upperBound);

        if (amount <= 0 || !BuffApi.IsWunaPlayerStatus(target) || buff?.buffConfig == null)
        {
            return;
        }

        var before = Math.Max(0, current - amount);
        var desired = Math.Min(upperBound, before + amount);
        if (desired > buff.buffConfig.Level)
        {
            buff.buffConfig.Level = desired;
        }
    }

    private static void ApplySolarRadianceUpperBound(IStatusManager target, int upperBound)
    {
        var buff = target.GetBuff(SunExpIds.SolarRadiance);
        if (buff?.buffConfig == null)
        {
            return;
        }

        var nextUpperBound = Math.Max(1, upperBound);
        if (buff.buffConfig.UpperBound != nextUpperBound)
        {
            buff.buffConfig.UpperBound = nextUpperBound;
        }

        if (buff.buffConfig.Level > nextUpperBound)
        {
            buff.buffConfig.Level = nextUpperBound;
        }
    }

    public static void HandleBurnOverflow(IStatusManager? target, string buffId, int amount)
    {
        if (target == null || buffId != SunExpIds.Burn || amount <= 0 || !IsSharedFieldActive(SunExpFieldId.ScorchingCanopy))
        {
            return;
        }

        var ward = target.GetBuff(SunExpIds.EmberCloak);
        if (ward?.buffConfig != null && ward.buffConfig.Level > 0)
        {
            return;
        }

        var upperBound = BurnUpperBound(target);
        var overflow = StatusBuffLevel(target, SunExpIds.Burn) + amount - upperBound;
        if (overflow > 0)
        {
            SunExpLog.Debug("Burn overflow converted: target=" + target.InstanceId
                + ", burnBefore=" + StatusBuffLevel(target, SunExpIds.Burn)
                + ", add=" + amount
                + ", upperBound=" + upperBound
                + ", overflow=" + overflow);
            target.AddBuff(SunExpIds.BodyBurn, overflow);
        }
    }

    private static int ReadIntProperty(object? target, string name)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

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
