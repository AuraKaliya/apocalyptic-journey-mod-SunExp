using System;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;

namespace SunExp.Dll.GameApi;

public sealed class FieldBuffSnapshot
{
    public static readonly FieldBuffSnapshot Empty = new(SunExpFieldId.None, "", "", 0, 0, 0);

    public FieldBuffSnapshot(SunExpFieldId field, string slug, string buffId, int stacks, int maxStacks, int epoch)
    {
        Field = field;
        Slug = slug;
        BuffId = buffId;
        Stacks = stacks;
        MaxStacks = maxStacks;
        Epoch = epoch;
    }

    public SunExpFieldId Field { get; }

    public string Slug { get; }

    public string BuffId { get; }

    public int Stacks { get; }

    public int MaxStacks { get; }

    public int Epoch { get; }

    public bool IsActive => Field != SunExpFieldId.None && Stacks > 0;
}

public static class FieldApi
{
    private const string ActiveFieldIdKey = "SunExpField_ActiveId";
    private const string ActiveFieldStacksKey = "SunExpField_ActiveStacks";
    private const string ActiveFieldEpochKey = "SunExpField_ActiveEpoch";
    private const string LastRoundStartKey = "SunExpField_LastRoundStart";
    private const string TriggerLockKey = "SunExpField_TriggerLock";
    private const string PendingCarrierFieldKey = "SunExpField_PendingCarrierField";
    private const string PendingCarrierCountKey = "SunExpField_PendingCarrierCount";
    private const int ScorchingCanopyFallbackMaxStacks = 9;

    public static event Action<FieldBuffSnapshot>? Changed;

    public static void ApplyFieldBuff(ScriptExecutor? executor, string fieldId, int amount)
    {
        ActivateField(executor, fieldId, amount, "FieldApi.ApplyFieldBuff");
    }

    public static void ActivateField(ScriptExecutor? executor, string fieldId, int amount, string source = "")
    {
        ActivateField(executor, ParseFieldId(fieldId), amount, source);
    }

    public static void ActivateField(ScriptExecutor? executor, SunExpFieldId field, int amount, string source = "")
    {
        if (field == SunExpFieldId.None || amount <= 0)
        {
            return;
        }

        var current = ActiveFieldId();
        var currentStacks = ActiveFieldStacks();
        var maxStacks = MaxStacksFor(field);
        var nextStacks = Math.Min(maxStacks, Math.Max(1, current == field ? currentStacks + amount : amount));
        SetActiveFieldState(field, nextStacks, source);
        SunExpLog.Debug("[FieldApi] activated field id=" + FieldSlug(field)
            + ", add=" + amount
            + ", stacks=" + nextStacks
            + ", max=" + maxStacks
            + ", source=" + (source ?? ""));
    }

    public static bool TryRedirectStatusFieldBuffAdd(IStatusManager? target, string buffId, int amount, string source, bool expectCarrierApply = true)
    {
        var field = FieldIdFromBuffId(buffId);
        if (target == null || field == SunExpFieldId.None || amount <= 0)
        {
            return false;
        }

        ActivateField(null, field, amount, source);
        if (expectCarrierApply)
        {
            MarkPendingCarrier(field);
        }

        SunExpLog.Debug("[FieldApi] redirected status buff add to field module: buff="
            + buffId
            + ", amount=" + amount
            + ", target=" + (target.InstanceId ?? "")
            + ", source=" + (source ?? ""));
        return true;
    }

    public static bool TryConsumePendingCarrier(SunExpFieldId field)
    {
        if (field == SunExpFieldId.None || CombatVarApi.GetInt(PendingCarrierFieldKey) != (int)field)
        {
            return false;
        }

        var count = Math.Max(0, CombatVarApi.GetInt(PendingCarrierCountKey));
        if (count <= 0)
        {
            CombatVarApi.SetInt(PendingCarrierFieldKey, 0);
            return false;
        }

        count--;
        CombatVarApi.SetInt(PendingCarrierCountKey, count);
        if (count <= 0)
        {
            CombatVarApi.SetInt(PendingCarrierFieldKey, 0);
        }

        return true;
    }

    public static bool RemoveFieldBuffCarrier(IStatusManager? target, string buffId, string source)
    {
        if (target == null || !IsFieldBuffId(buffId) || target.GetBuff(buffId) == null)
        {
            return false;
        }

        target.RemoveBuff(buffId);
        SunExpLog.Debug("[FieldApi] removed field buff carrier from status: buff="
            + buffId
            + ", target=" + (target.InstanceId ?? "")
            + ", source=" + (source ?? ""));
        return true;
    }

    public static bool ClearFieldBuff(ScriptExecutor? executor, string fieldId)
    {
        return TryClearActiveField("FieldApi.ClearFieldBuff", fieldId);
    }

    public static bool TryClearActiveField(string source, string fieldId = "")
    {
        var requested = string.IsNullOrWhiteSpace(fieldId) ? SunExpFieldId.None : ParseFieldId(fieldId);
        var active = ActiveFieldId();
        if (active == SunExpFieldId.None || (requested != SunExpFieldId.None && requested != active))
        {
            return false;
        }

        ClearActiveFieldState(source);
        return true;
    }

    public static bool TryClearActiveField(string source, SunExpFieldId field)
    {
        return TryClearActiveField(source, FieldSlug(field));
    }

    public static void ClearAllFields(string source)
    {
        if (ActiveFieldId() == SunExpFieldId.None && ActiveFieldStacks() <= 0)
        {
            return;
        }

        ClearActiveFieldState(source);
    }

    public static string FieldBuffId(string fieldId)
    {
        return FieldBuffId(ParseFieldId(fieldId));
    }

    public static string FieldBuffId(SunExpFieldId field)
    {
        return field == SunExpFieldId.ScorchingCanopy ? SunExpIds.ScorchingCanopy : "";
    }

    public static bool IsFieldBuffId(string? buffId)
    {
        return FieldIdFromBuffId(buffId) != SunExpFieldId.None;
    }

    public static SunExpFieldId FieldIdFromBuffId(string? buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return SunExpFieldId.None;
        }

        var id = buffId!.Trim();
        return string.Equals(id, SunExpIds.ScorchingCanopy, StringComparison.Ordinal)
               || string.Equals(id, "scorching_canopy", StringComparison.Ordinal)
            ? SunExpFieldId.ScorchingCanopy
            : SunExpFieldId.None;
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
        return string.Equals(fieldId, "scorching_canopy", StringComparison.Ordinal)
            ? SunExpFieldId.ScorchingCanopy
            : SunExpFieldId.None;
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

        if (stacks <= 0)
        {
            TryClearActiveField("FieldApi.SetSharedFieldState", field);
            return;
        }

        SetActiveFieldState(field, Math.Min(MaxStacksFor(field), stacks), "FieldApi.SetSharedFieldState");
    }

    public static bool IsSharedFieldActive(string fieldId)
    {
        return IsSharedFieldActive(ParseFieldId(fieldId));
    }

    public static bool IsSharedFieldActive(SunExpFieldId field)
    {
        return ActiveFieldId() == field && ActiveFieldStacks() > 0;
    }

    public static int FieldStacks(string fieldId)
    {
        return FieldStacks(ParseFieldId(fieldId));
    }

    public static int FieldStacks(SunExpFieldId field)
    {
        return IsSharedFieldActive(field) ? ActiveFieldStacks() : 0;
    }

    public static int SyncFieldStacks(ScriptExecutor? executor, string fieldId)
    {
        return SyncFieldStacks(executor, ParseFieldId(fieldId));
    }

    public static int SyncFieldStacks(ScriptExecutor? executor, SunExpFieldId field)
    {
        return FieldStacks(field);
    }

    public static int SetActiveField(ScriptExecutor? executor, string fieldId)
    {
        return SetActiveField(executor, ParseFieldId(fieldId));
    }

    public static int SetActiveField(ScriptExecutor? executor, SunExpFieldId field)
    {
        if (field == SunExpFieldId.None)
        {
            return 0;
        }

        if (ActiveFieldId() == field)
        {
            return ActiveFieldEpoch();
        }

        SetActiveFieldState(field, Math.Min(1, MaxStacksFor(field)), "FieldApi.SetActiveField");
        return ActiveFieldEpoch();
    }

    public static bool BeginSharedFieldStartRound(ScriptExecutor? executor, string fieldId)
    {
        return BeginSharedFieldStartRound(executor, ParseFieldId(fieldId));
    }

    public static bool BeginSharedFieldStartRound(ScriptExecutor? executor, SunExpFieldId field)
    {
        if (!IsSharedFieldActive(field) || CombatVarApi.GetInt(TriggerLockKey) == 1)
        {
            return false;
        }

        CombatVarApi.SetInt(TriggerLockKey, 1);
        return true;
    }

    public static bool ResolveRoundStart(ScriptExecutor? executor, string roundKey, string source)
    {
        var snapshot = ActiveFieldSnapshot();
        if (!snapshot.IsActive)
        {
            return false;
        }

        var key = StableRoundKey(roundKey);
        if (key != 0 && CombatVarApi.GetInt(LastRoundStartKey) == key)
        {
            return false;
        }

        if (key != 0)
        {
            CombatVarApi.SetInt(LastRoundStartKey, key);
        }

        CombatVarApi.SetInt(TriggerLockKey, 0);
        return FieldEffectHandlers.ResolveRoundStart(executor, snapshot, source);
    }

    public static bool IsActiveField(ScriptExecutor? executor, string fieldId, int? epoch = null, string? token = null)
    {
        return IsActiveField(executor, ParseFieldId(fieldId), epoch, token);
    }

    public static bool IsActiveField(ScriptExecutor? executor, SunExpFieldId field, int? epoch = null, string? token = null)
    {
        if (!IsSharedFieldActive(field))
        {
            return false;
        }

        return epoch == null || ActiveFieldEpoch() == epoch.Value;
    }

    public static FieldBuffSnapshot ActiveFieldSnapshot()
    {
        var field = ActiveFieldId();
        if (field == SunExpFieldId.None)
        {
            return FieldBuffSnapshot.Empty;
        }

        var stacks = ActiveFieldStacks();
        if (stacks <= 0)
        {
            return FieldBuffSnapshot.Empty;
        }

        return new FieldBuffSnapshot(
            field,
            FieldSlug(field),
            FieldBuffId(field),
            stacks,
            MaxStacksFor(field),
            ActiveFieldEpoch());
    }

    public static int MaxStacksFor(string fieldId)
    {
        return MaxStacksFor(ParseFieldId(fieldId));
    }

    public static int MaxStacksFor(SunExpFieldId field)
    {
        var fallback = field == SunExpFieldId.ScorchingCanopy ? ScorchingCanopyFallbackMaxStacks : 1;
        var buffId = FieldBuffId(field);
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
            SunExpLog.Debug("[FieldApi] field max stack fallback used: id=" + buffId + ", error=" + ex.Message);
            return fallback;
        }
    }

    private static SunExpFieldId ActiveFieldId()
    {
        return Enum.IsDefined(typeof(SunExpFieldId), CombatVarApi.GetInt(ActiveFieldIdKey))
            ? (SunExpFieldId)CombatVarApi.GetInt(ActiveFieldIdKey)
            : SunExpFieldId.None;
    }

    private static int ActiveFieldStacks()
    {
        return Math.Max(0, CombatVarApi.GetInt(ActiveFieldStacksKey));
    }

    private static int ActiveFieldEpoch()
    {
        return Math.Max(0, CombatVarApi.GetInt(ActiveFieldEpochKey));
    }

    private static void SetActiveFieldState(SunExpFieldId field, int stacks, string source)
    {
        var nextStacks = Math.Min(MaxStacksFor(field), Math.Max(0, stacks));
        var previous = ActiveFieldSnapshot();
        CombatVarApi.SetInt(ActiveFieldIdKey, (int)field);
        CombatVarApi.SetInt(ActiveFieldStacksKey, nextStacks);
        CombatVarApi.SetInt(TriggerLockKey, 0);
        if (previous.Field != field || previous.Stacks != nextStacks)
        {
            CombatVarApi.AddInt(ActiveFieldEpochKey, 1);
            NotifyChanged(source);
        }
    }

    private static void ClearActiveFieldState(string source)
    {
        CombatVarApi.SetInt(ActiveFieldIdKey, (int)SunExpFieldId.None);
        CombatVarApi.SetInt(ActiveFieldStacksKey, 0);
        CombatVarApi.SetInt(TriggerLockKey, 0);
        CombatVarApi.SetInt(LastRoundStartKey, 0);
        CombatVarApi.SetInt(PendingCarrierFieldKey, 0);
        CombatVarApi.SetInt(PendingCarrierCountKey, 0);
        CombatVarApi.AddInt(ActiveFieldEpochKey, 1);
        NotifyChanged(source);
        SunExpLog.Debug("[FieldApi] cleared active field from " + (source ?? ""));
    }

    private static void MarkPendingCarrier(SunExpFieldId field)
    {
        if (field == SunExpFieldId.None)
        {
            return;
        }

        if (CombatVarApi.GetInt(PendingCarrierFieldKey) != (int)field)
        {
            CombatVarApi.SetInt(PendingCarrierFieldKey, (int)field);
            CombatVarApi.SetInt(PendingCarrierCountKey, 0);
        }

        CombatVarApi.AddInt(PendingCarrierCountKey, 1);
    }

    private static void NotifyChanged(string source)
    {
        try
        {
            Changed?.Invoke(ActiveFieldSnapshot());
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[FieldApi] field changed handler failed from " + (source ?? "") + ": " + ex.Message);
        }
    }

    private static int StableRoundKey(string roundKey)
    {
        if (string.IsNullOrWhiteSpace(roundKey))
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var ch in roundKey)
            {
                hash = hash * 31 + ch;
            }

            return hash == 0 ? 1 : hash;
        }
    }
}
