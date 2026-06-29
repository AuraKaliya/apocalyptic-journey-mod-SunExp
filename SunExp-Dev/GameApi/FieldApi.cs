using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class FieldApi
{
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

        ScriptVarApi.SetVar(executor, "SunExpFieldInternalClear", "1");
        try
        {
            executor.SetStatus("Self");
            executor.RemoveBuff(buffId);
            ScriptVarApi.SetVar(executor, "SunExpActiveFieldId", "");
            ScriptVarApi.SetVar(executor, "SunExpActiveFieldStacks", "0");
            SyncFieldStacks(executor, field);
            SunExpLog.Debug("Field cleared internally: id=" + FieldSlug(field));
            return true;
        }
        finally
        {
            ScriptVarApi.SetVar(executor, "SunExpFieldInternalClear", "0");
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
        if (CombatVarApi.GetInt(activeKey) != active || CombatVarApi.GetInt(stacksKey) != count)
        {
            CombatVarApi.AddInt(FieldCombatKey(field, "Epoch"), 1);
        }

        CombatVarApi.SetInt(activeKey, active);
        CombatVarApi.SetInt(stacksKey, count);
        if (count <= 0)
        {
            CombatVarApi.SetInt(FieldCombatKey(field, "TriggerLock"), 0);
        }
    }

    public static bool IsSharedFieldActive(string fieldId)
    {
        return IsSharedFieldActive(ParseFieldId(fieldId));
    }

    public static bool IsSharedFieldActive(SunExpFieldId field)
    {
        return field != SunExpFieldId.None
            && CombatVarApi.GetInt(FieldCombatKey(field, "Active")) == 1
            && CombatVarApi.GetInt(FieldCombatKey(field, "Stacks")) > 0;
    }

    public static int FieldStacks(string fieldId)
    {
        return FieldStacks(ParseFieldId(fieldId));
    }

    public static int FieldStacks(SunExpFieldId field)
    {
        return field == SunExpFieldId.None ? 0 : CombatVarApi.GetInt(FieldCombatKey(field, "Stacks"));
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
        if (executor != null && ScriptVarApi.GetVar(executor, "SunExpActiveFieldId") == FieldSlug(field))
        {
            ScriptVarApi.SetVar(executor, "SunExpActiveFieldStacks", BuffApi.Level(executor.Self, buffId));
        }

        return total;
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

        var current = ScriptVarApi.GetVar(executor, "SunExpActiveFieldId");
        if (current == fieldId)
        {
            return DictionaryUtil.ParseInt(ScriptVarApi.GetVar(executor, "SunExpActiveFieldEpoch", "0"));
        }

        var epoch = DictionaryUtil.ParseInt(ScriptVarApi.GetVar(executor, "SunExpActiveFieldEpoch", "0")) + 1;
        ScriptVarApi.SetVar(executor, "SunExpActiveFieldId", fieldId);
        ScriptVarApi.SetVar(executor, "SunExpActiveFieldEpoch", epoch);
        ScriptVarApi.SetVar(executor, "SunExpActiveFieldStacks", "0");
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
        if (CombatVarApi.GetInt(lockKey) == 1)
        {
            return false;
        }

        CombatVarApi.SetInt(lockKey, 1);
        try
        {
            executor?.AddTempEvent("StartRoundEnd", new Action(() => CombatVarApi.SetInt(lockKey, 0)));
        }
        catch
        {
            CombatVarApi.SetInt(lockKey, 0);
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

        var fieldId = FieldSlug(field);
        var localActive = ScriptVarApi.GetVar(executor, "SunExpActiveFieldId") == fieldId;
        var sharedActive = IsSharedFieldActive(field);
        if (epoch == null && token == null && sharedActive)
        {
            return true;
        }

        if (epoch == null && token == null && localActive)
        {
            return LocalFieldStacks(executor, field) > 0;
        }

        if (!localActive)
        {
            return false;
        }

        if (epoch != null && DictionaryUtil.ParseInt(ScriptVarApi.GetVar(executor, "SunExpActiveFieldEpoch", "0")) != epoch.Value)
        {
            return false;
        }

        if (token != null && !ScriptEventApi.IsHookTokenActive(executor, "SunExpField_" + fieldId + "Token", token))
        {
            return false;
        }

        return epoch != null || token != null ? sharedActive : true;
    }

    private static int LocalFieldStacks(ScriptExecutor? executor, SunExpFieldId field)
    {
        var buffId = FieldBuffId(field);
        return executor?.Self == null || string.IsNullOrWhiteSpace(buffId)
            ? 0
            : Math.Max(0, BuffApi.Level(executor.Self, buffId));
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
                total += Math.Max(0, BuffApi.Level(status, buffId));
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
}
