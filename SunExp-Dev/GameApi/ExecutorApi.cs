using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class ExecutorApi
{
    public static string GetVar(ScriptExecutor? executor, string key, string fallback = "")
    {
        return ScriptVarApi.GetVar(executor, key, fallback);
    }

    public static void SetVar(ScriptExecutor? executor, string key, object value)
    {
        ScriptVarApi.SetVar(executor, key, value);
    }

    public static int CombatIntGet(string key, int fallback = 0)
    {
        return CombatVarApi.GetInt(key, fallback);
    }

    public static int CombatIntSet(string key, int value)
    {
        return CombatVarApi.SetInt(key, value);
    }

    public static int CombatIntAdd(string key, int amount)
    {
        return CombatVarApi.AddInt(key, amount);
    }

    public static string? RegisterHook(ScriptExecutor? executor, string hookKey, string tokenKey)
    {
        return ScriptEventApi.RegisterHook(executor, hookKey, tokenKey);
    }

    public static bool IsHookTokenActive(ScriptExecutor? executor, string tokenKey, string? token)
    {
        return ScriptEventApi.IsHookTokenActive(executor, tokenKey, token);
    }

    public static void ClearHook(ScriptExecutor? executor, string hookKey, string tokenKey)
    {
        ScriptEventApi.ClearHook(executor, hookKey, tokenKey);
    }

    public static bool TryAddEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")
    {
        return ScriptEventApi.TryAddEvent(executor, eventName, script, context);
    }

    public static bool TryAddTokenedEvent(ScriptExecutor? executor, string eventName, string tokenKey, string? token, Action script, string context = "")
    {
        return ScriptEventApi.TryAddTokenedEvent(executor, eventName, tokenKey, token, script, context);
    }

    public static bool TryAddTempEvent(ScriptExecutor? executor, string eventName, Action script, string context = "")
    {
        return ScriptEventApi.TryAddTempEvent(executor, eventName, script, context);
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
        return BuffOverflowApi.BurnUpperBound(target);
    }

    public static int SolarRadianceUpperBound(IStatusManager? target)
    {
        return BuffOverflowApi.SolarRadianceUpperBound(target);
    }

    public static int BuffUpperBound(IStatusManager? target, string buffId, int fallback)
    {
        return BuffOverflowApi.BuffUpperBound(target, buffId, fallback);
    }

    public static List<IStatusManager> EnemyTargets(ScriptExecutor? executor)
    {
        return TargetApi.EnemyTargets(executor);
    }

    public static List<IStatusManager> FriendlyTargets(ScriptExecutor? executor, bool includeSelf)
    {
        return TargetApi.FriendlyTargets(executor, includeSelf);
    }

    public static IStatusManager? RandomEnemyTarget(ScriptExecutor? executor, bool requireBurn)
    {
        return TargetApi.RandomEnemyTarget(executor, requireBurn);
    }

    public static IStatusManager? RandomFriendlyTarget(ScriptExecutor? executor, bool includeSelf)
    {
        return TargetApi.RandomFriendlyTarget(executor, includeSelf);
    }

    public static IStatusManager? PrimaryTarget(ScriptExecutor? executor)
    {
        return TargetApi.PrimaryTarget(executor);
    }

    public static IStatusManager? PrimaryTargetIncludingSelf(ScriptExecutor? executor)
    {
        return TargetApi.PrimaryTargetIncludingSelf(executor);
    }

    public static bool IsSelf(ScriptExecutor? executor, IStatusManager? target)
    {
        return TargetApi.IsSelf(executor, target);
    }

    public static bool SetStatusForTarget(ScriptExecutor? executor, IStatusManager? target, string fallbackStatus = "Self")
    {
        return TargetApi.SetStatusForTarget(executor, target, fallbackStatus);
    }

    public static bool AddStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount, string fallbackStatus = "Target")
    {
        return DamageApi.AddStatusBuff(executor, target, buffId, amount, fallbackStatus);
    }

    public static bool RemoveStatusBuff(ScriptExecutor? executor, IStatusManager? target, string buffId, string fallbackStatus = "Self")
    {
        return DamageApi.RemoveStatusBuff(executor, target, buffId, fallbackStatus);
    }

    public static int RemoveBuffStacks(ScriptExecutor? executor, IStatusManager? target, string buffId, int amount)
    {
        return DamageApi.RemoveBuffStacks(executor, target, buffId, amount);
    }

    public static bool DealDamage(ScriptExecutor? executor, int amount, string damageType = "")
    {
        return DamageApi.DealDamage(executor, amount, damageType);
    }

    public static bool DealDamageToTarget(
        ScriptExecutor? executor,
        IStatusManager? target,
        int amount,
        string fallbackStatus = "Target",
        string damageType = "")
    {
        return DamageApi.DealDamageToTarget(executor, target, amount, fallbackStatus, damageType);
    }

    public static int StatusMaxHp(IStatusManager? status)
    {
        return StatusApi.MaxHp(status);
    }

    public static int DealTrueDamageAllEnemiesByMaxHp(ScriptExecutor? executor)
    {
        return DamageApi.DealTrueDamageAllEnemiesByMaxHp(executor);
    }

    public static void AddDamageDescription(ScriptExecutor? executor, string index, int amount)
    {
        DamageApi.AddDamageDescription(executor, index, amount);
    }

    public static void AddValueDescription(ScriptExecutor? executor, string index, int amount)
    {
        DamageApi.AddValueDescription(executor, index, amount);
    }

    public static int SolarMultiplier(ScriptExecutor? executor)
    {
        return SolarCombatApi.SolarMultiplier(executor);
    }

    public static int SolarCoefficient(ScriptExecutor? executor, IStatusManager? target)
    {
        return SolarCombatApi.SolarCoefficient(executor, target);
    }

    public static int SolarKeywordDamage(ScriptExecutor? executor, int baseDamage, IStatusManager? target, int coefficientScale = 1)
    {
        return SolarCombatApi.SolarKeywordDamage(executor, baseDamage, target, coefficientScale);
    }

    public static int SolarKeywordBlock(ScriptExecutor? executor, int baseBlock)
    {
        return SolarCombatApi.SolarKeywordBlock(executor, baseBlock);
    }

    public static bool DealSolarKeywordDamage(ScriptExecutor? executor, int baseDamage, IStatusManager? target, string fallbackStatus = "Target", int coefficientScale = 1)
    {
        return SolarCombatApi.DealSolarKeywordDamage(executor, baseDamage, target, fallbackStatus, coefficientScale);
    }

    public static int DealSolarKeywordDamageAllEnemies(ScriptExecutor? executor, int baseDamage, int coefficientScale)
    {
        return SolarCombatApi.DealSolarKeywordDamageAllEnemies(executor, baseDamage, coefficientScale);
    }

    public static int ApplySolarKeywordSkill(ScriptExecutor? executor, int baseBlock)
    {
        return SolarCombatApi.ApplySolarKeywordSkill(executor, baseBlock);
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
        FieldApi.ApplyFieldBuff(executor, fieldId, amount);
    }

    public static void ActivateField(ScriptExecutor? executor, string fieldId, int amount, string source = "")
    {
        FieldApi.ActivateField(executor, fieldId, amount, source);
    }

    public static void ActivateField(ScriptExecutor? executor, SunExpFieldId field, int amount, string source = "")
    {
        FieldApi.ActivateField(executor, field, amount, source);
    }

    public static bool TryConsumePendingFieldBuffCarrier(SunExpFieldId field)
    {
        return FieldApi.TryConsumePendingCarrier(field);
    }

    public static bool ClearFieldBuff(ScriptExecutor? executor, string fieldId)
    {
        return FieldApi.ClearFieldBuff(executor, fieldId);
    }

    public static bool TryClearActiveField(ScriptExecutor? executor, string fieldId, string source = "")
    {
        return FieldApi.TryClearActiveField(source, fieldId);
    }

    public static bool TryClearActiveField(ScriptExecutor? executor, SunExpFieldId field, string source = "")
    {
        return FieldApi.TryClearActiveField(source, field);
    }

    public static FieldBuffSnapshot ActiveFieldSnapshot()
    {
        return FieldApi.ActiveFieldSnapshot();
    }

    public static string FieldBuffId(string fieldId)
    {
        return FieldApi.FieldBuffId(fieldId);
    }

    public static string FieldBuffId(SunExpFieldId field)
    {
        return FieldApi.FieldBuffId(field);
    }

    public static string FieldCombatKey(string fieldId, string name)
    {
        return FieldApi.FieldCombatKey(fieldId, name);
    }

    public static string FieldCombatKey(SunExpFieldId field, string name)
    {
        return FieldApi.FieldCombatKey(field, name);
    }

    public static string FieldSlug(SunExpFieldId field)
    {
        return FieldApi.FieldSlug(field);
    }

    public static SunExpFieldId ParseFieldId(string fieldId)
    {
        return FieldApi.ParseFieldId(fieldId);
    }

    public static void SetSharedFieldState(string fieldId, int stacks)
    {
        FieldApi.SetSharedFieldState(fieldId, stacks);
    }

    public static void SetSharedFieldState(SunExpFieldId field, int stacks)
    {
        FieldApi.SetSharedFieldState(field, stacks);
    }

    public static bool IsSharedFieldActive(string fieldId)
    {
        return FieldApi.IsSharedFieldActive(fieldId);
    }

    public static bool IsSharedFieldActive(SunExpFieldId field)
    {
        return FieldApi.IsSharedFieldActive(field);
    }

    public static int FieldStacks(string fieldId)
    {
        return FieldApi.FieldStacks(fieldId);
    }

    public static int FieldStacks(SunExpFieldId field)
    {
        return FieldApi.FieldStacks(field);
    }

    public static int SyncFieldStacks(ScriptExecutor? executor, string fieldId)
    {
        return FieldApi.SyncFieldStacks(executor, fieldId);
    }

    public static int SyncFieldStacks(ScriptExecutor? executor, SunExpFieldId field)
    {
        return FieldApi.SyncFieldStacks(executor, field);
    }

    public static int SetActiveField(ScriptExecutor? executor, string fieldId)
    {
        return FieldApi.SetActiveField(executor, fieldId);
    }

    public static int SetActiveField(ScriptExecutor? executor, SunExpFieldId field)
    {
        return FieldApi.SetActiveField(executor, field);
    }

    public static bool BeginSharedFieldStartRound(ScriptExecutor? executor, string fieldId)
    {
        return FieldApi.BeginSharedFieldStartRound(executor, fieldId);
    }

    public static bool BeginSharedFieldStartRound(ScriptExecutor? executor, SunExpFieldId field)
    {
        return FieldApi.BeginSharedFieldStartRound(executor, field);
    }

    public static bool IsActiveField(ScriptExecutor? executor, string fieldId, int? epoch = null, string? token = null)
    {
        return FieldApi.IsActiveField(executor, fieldId, epoch, token);
    }

    public static bool IsActiveField(ScriptExecutor? executor, SunExpFieldId field, int? epoch = null, string? token = null)
    {
        return FieldApi.IsActiveField(executor, field, epoch, token);
    }

    public static bool IsActiveField(ScriptExecutor? executor, string fieldId)
    {
        return FieldApi.IsActiveField(executor, fieldId);
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

    public static bool RemoveRandomPositiveBuff(ScriptExecutor? executor, IStatusManager? status)
    {
        return executor != null && BuffApi.RemoveRandomPositiveBuff(executor, status);
    }

    public static int RemoveBuffsExceptAndCount(ScriptExecutor? executor, IStatusManager? status, params string[] excludeIds)
    {
        return executor == null ? 0 : BuffApi.RemoveBuffsExceptAndCount(executor, status, excludeIds);
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
        BuffOverflowApi.PrepareSolarRadianceUpperBound(target, buffId);
    }

    public static void FinalizeSolarRadianceUpperBound(IStatusManager? target, string buffId, int amount)
    {
        BuffOverflowApi.FinalizeSolarRadianceUpperBound(target, buffId, amount);
    }

    public static void HandleBurnOverflow(IStatusManager? target, string buffId, int amount)
    {
        BuffOverflowApi.HandleBurnOverflow(target, buffId, amount);
    }
}
