using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class ExecutorApi
{
    public const int BurnUpperBound = 49;

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
        if (executor == null || string.IsNullOrWhiteSpace(eventName) || script == null)
        {
            return false;
        }

        try
        {
            executor.AddEvent(eventName, script);
            return true;
        }
        catch (NullReferenceException)
        {
            return false;
        }
    }

    public static void SetBaseScript(ScriptExecutor executor, string baseScript, bool canSelf = true)
    {
        DictionaryUtil.Set(executor?.Vars, "BaseScript", baseScript);
        if (!canSelf)
        {
            DictionaryUtil.Set(executor?.Vars, "CanSelf", "False");
        }
    }

    public static int SelfBuffLevel(ScriptExecutor? executor, string buffId)
    {
        return BuffApi.Level(executor?.Self, buffId);
    }

    public static int StatusBuffLevel(IStatusManager? status, string buffId)
    {
        return BuffApi.Level(status, buffId);
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
        if (executor == null || fieldId != "scorching_canopy" || amount <= 0)
        {
            return;
        }

        SetActiveField(executor, fieldId);
        executor.SetStatus("Self");
        executor.AddBuff(SunExpIds.ScorchingCanopy, amount.ToString());
        SyncFieldStacks(executor, fieldId);
        SunExpLog.Debug("Field applied: id=" + fieldId + ", add=" + amount + ", stacks=" + SelfBuffLevel(executor, SunExpIds.ScorchingCanopy));
    }

    public static bool ClearFieldBuff(ScriptExecutor? executor, string fieldId)
    {
        var buffId = FieldBuffId(fieldId);
        if (executor == null || string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        SetVar(executor, "SunExpFieldInternalClear", "1");
        try
        {
            executor.SetStatus("Self");
            executor.RemoveBuff(buffId);
            SetSharedFieldState(fieldId, 0);
            SetVar(executor, "SunExpActiveFieldId", "");
            SetVar(executor, "SunExpActiveFieldStacks", "0");
            SunExpLog.Debug("Field cleared internally: id=" + fieldId);
            return true;
        }
        finally
        {
            SetVar(executor, "SunExpFieldInternalClear", "0");
        }
    }

    public static string FieldBuffId(string fieldId)
    {
        return fieldId == "scorching_canopy" ? SunExpIds.ScorchingCanopy : "";
    }

    public static string FieldCombatKey(string fieldId, string name)
    {
        return "SunExpField_" + fieldId + "_" + name;
    }

    public static void SetSharedFieldState(string fieldId, int stacks)
    {
        var count = Math.Max(0, stacks);
        CombatIntSet(FieldCombatKey(fieldId, "Active"), count > 0 ? 1 : 0);
        CombatIntSet(FieldCombatKey(fieldId, "Stacks"), count);
        if (count <= 0)
        {
            CombatIntSet(FieldCombatKey(fieldId, "TriggerLock"), 0);
        }
    }

    public static bool IsSharedFieldActive(string fieldId)
    {
        return CombatIntGet(FieldCombatKey(fieldId, "Active")) == 1
            && CombatIntGet(FieldCombatKey(fieldId, "Stacks")) > 0;
    }

    public static int SyncFieldStacks(ScriptExecutor? executor, string fieldId)
    {
        var buffId = FieldBuffId(fieldId);
        if (executor?.Self == null || string.IsNullOrWhiteSpace(buffId))
        {
            return 0;
        }

        var level = SelfBuffLevel(executor, buffId);
        if (GetVar(executor, "SunExpActiveFieldId") == fieldId)
        {
            SetVar(executor, "SunExpActiveFieldStacks", level);
            SetSharedFieldState(fieldId, level);
        }

        return level;
    }

    public static int SetActiveField(ScriptExecutor? executor, string fieldId)
    {
        if (executor == null)
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
        var lockKey = FieldCombatKey(fieldId, "TriggerLock");
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
        if (executor == null || string.IsNullOrWhiteSpace(fieldId))
        {
            return false;
        }

        var localActive = GetVar(executor, "SunExpActiveFieldId") == fieldId;
        var sharedActive = IsSharedFieldActive(fieldId);
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

    public static int SolarCrownTier(ScriptExecutor? executor)
    {
        return SelfBuffLevel(executor, SunExpIds.SolarCrownTier);
    }

    public static void HandleBurnOverflow(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount)
    {
        if (executor == null || target == null || buffId != SunExpIds.Burn || !IsActiveField(executor, "scorching_canopy"))
        {
            return;
        }

        if (IsSelf(executor, target) && IsSelfBurnProtected(executor, true))
        {
            return;
        }

        var overflow = StatusBuffLevel(target, SunExpIds.Burn) + amount - BurnUpperBound;
        if (overflow > 0)
        {
            SunExpLog.Debug("Burn overflow converted: target=" + target.InstanceId
                + ", burnBefore=" + StatusBuffLevel(target, SunExpIds.Burn)
                + ", add=" + amount
                + ", overflow=" + overflow);
            AddStatusBuff(executor, target, SunExpIds.BodyBurn, overflow, "Target");
        }
    }
}
