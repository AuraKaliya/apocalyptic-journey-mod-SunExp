using System;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Network;
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
    private static FieldEffectPolicyFlags activePolicyFlags = FieldEffectPolicyFlags.None;

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

        if (!IsAuthoritativeFieldWriter())
        {
            FieldNetworkSync.RequestActivate(field, amount, source);
            return;
        }

        ActivateFieldAuthoritative(field, amount, source, broadcast: true);
    }

    internal static bool ActivateFieldAuthoritative(SunExpFieldId field, int amount, string source, bool broadcast)
    {
        if (field == SunExpFieldId.None || amount <= 0)
        {
            return false;
        }

        var current = ActiveFieldId();
        var currentStacks = ActiveFieldStacks();
        var maxStacks = MaxStacksFor(field);
        var nextStacks = Math.Min(maxStacks, Math.Max(1, current == field ? currentStacks + amount : amount));
        var changed = SetActiveFieldState(field, nextStacks, source);
        if (changed && broadcast)
        {
            FieldNetworkSync.BroadcastSnapshot(source);
        }

        SunExpLog.Debug("[FieldApi] activated field id=" + FieldSlug(field)
            + ", add=" + amount
            + ", stacks=" + nextStacks
            + ", max=" + maxStacks
            + ", source=" + (source ?? ""));
        return changed;
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
        if (!IsAuthoritativeFieldWriter())
        {
            FieldNetworkSync.RequestClear(requested, source);
            return false;
        }

        return TryClearActiveFieldAuthoritative(source, requested, broadcast: true);
    }

    public static bool TryClearActiveField(string source, SunExpFieldId field)
    {
        return TryClearActiveField(source, FieldSlug(field));
    }

    internal static bool TryClearActiveFieldAuthoritative(string source, SunExpFieldId requested, bool broadcast)
    {
        var active = ActiveFieldId();
        if (active == SunExpFieldId.None || (requested != SunExpFieldId.None && requested != active))
        {
            return false;
        }

        var changed = ClearActiveFieldState(source);
        if (changed && broadcast)
        {
            FieldNetworkSync.BroadcastSnapshot(source);
        }

        return changed;
    }

    public static void ClearAllFields(string source)
    {
        ResetFightState(source);
    }

    public static string FieldBuffId(string fieldId)
    {
        return FieldBuffId(ParseFieldId(fieldId));
    }

    public static string FieldBuffId(SunExpFieldId field)
    {
        return FieldEffectRegistry.FieldBuffId(field);
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

        return FieldEffectRegistry.FieldIdFromBuffId(buffId);
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
        return FieldEffectRegistry.FieldSlug(field);
    }

    public static SunExpFieldId ParseFieldId(string fieldId)
    {
        return FieldEffectRegistry.ParseFieldId(fieldId);
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

        if (!IsAuthoritativeFieldWriter())
        {
            if (stacks <= 0)
            {
                FieldNetworkSync.RequestClear(field, "FieldApi.SetSharedFieldState");
            }
            else
            {
                FieldNetworkSync.RequestSet(field, stacks, "FieldApi.SetSharedFieldState");
            }

            return;
        }

        SetSharedFieldStateAuthoritative(field, stacks, "FieldApi.SetSharedFieldState", broadcast: true);
    }

    internal static bool SetSharedFieldStateAuthoritative(SunExpFieldId field, int stacks, string source, bool broadcast)
    {
        if (field == SunExpFieldId.None)
        {
            return false;
        }

        if (stacks <= 0)
        {
            return TryClearActiveFieldAuthoritative(source, field, broadcast);
        }

        var changed = SetActiveFieldState(field, Math.Min(MaxStacksFor(field), stacks), source);
        if (changed && broadcast)
        {
            FieldNetworkSync.BroadcastSnapshot(source);
        }

        return changed;
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

        if (!IsAuthoritativeFieldWriter())
        {
            FieldNetworkSync.RequestSet(field, 1, "FieldApi.SetActiveField");
            return ActiveFieldEpoch();
        }

        if (ActiveFieldId() == field)
        {
            return ActiveFieldEpoch();
        }

        if (SetActiveFieldState(field, Math.Min(1, MaxStacksFor(field)), "FieldApi.SetActiveField"))
        {
            FieldNetworkSync.BroadcastSnapshot("FieldApi.SetActiveField");
        }

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
        if (!CanResolveFieldEffects())
        {
            FieldNetworkSync.RequestSnapshot(source);
            return false;
        }

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
            return new FieldBuffSnapshot(SunExpFieldId.None, "", "", 0, 0, ActiveFieldEpoch());
        }

        var stacks = ActiveFieldStacks();
        if (stacks <= 0)
        {
            return new FieldBuffSnapshot(SunExpFieldId.None, "", "", 0, 0, ActiveFieldEpoch());
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
        return FieldEffectRegistry.MaxStacks(field);
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

    public static bool IsAuthoritativeFieldWriter()
    {
        return !SunExpNetworkRuntime.IsMultiplayerSession() || !SunExpNetworkRuntime.IsClientOnly();
    }

    public static bool CanResolveFieldEffects()
    {
        return IsAuthoritativeFieldWriter();
    }

    public static bool HasActiveBuffAddedPolicy()
    {
        return (activePolicyFlags & FieldEffectPolicyFlags.BuffAdded) != 0;
    }

    public static bool HasActivePolicy(FieldEffectPolicyFlags flag)
    {
        return (activePolicyFlags & flag) == flag;
    }

    public static bool TryGetActiveField(out SunExpFieldId field, out int stacks, out int epoch)
    {
        field = ActiveFieldId();
        stacks = ActiveFieldStacks();
        epoch = ActiveFieldEpoch();
        return field != SunExpFieldId.None && stacks > 0;
    }

    public static void ApplyNetworkSnapshot(SunExpFieldId field, int stacks, int epoch, string source)
    {
        if (epoch < ActiveFieldEpoch())
        {
            SunExpLog.Debug("[FieldApi] ignored stale field snapshot: incoming="
                + epoch
                + ", local="
                + ActiveFieldEpoch()
                + ", source="
                + (source ?? ""));
            return;
        }

        if (field == SunExpFieldId.None || stacks <= 0)
        {
            SetActiveFieldStateDirect(SunExpFieldId.None, 0, Math.Max(0, epoch), source);
            return;
        }

        SetActiveFieldStateDirect(field, Math.Min(MaxStacksFor(field), stacks), Math.Max(0, epoch), source);
    }

    public static void ResetFightState(string source)
    {
        var hadState = ActiveFieldId() != SunExpFieldId.None
            || ActiveFieldStacks() > 0
            || ActiveFieldEpoch() > 0
            || CombatVarApi.GetInt(LastRoundStartKey) != 0
            || CombatVarApi.GetInt(TriggerLockKey) != 0;
        CombatVarApi.SetInt(ActiveFieldIdKey, (int)SunExpFieldId.None);
        CombatVarApi.SetInt(ActiveFieldStacksKey, 0);
        CombatVarApi.SetInt(ActiveFieldEpochKey, 0);
        CombatVarApi.SetInt(TriggerLockKey, 0);
        CombatVarApi.SetInt(LastRoundStartKey, 0);
        UpdateActivePolicyCache(SunExpFieldId.None, 0);
        if (hadState)
        {
            NotifyChanged(source);
        }
    }

    private static bool SetActiveFieldState(SunExpFieldId field, int stacks, string source)
    {
        var nextStacks = Math.Min(MaxStacksFor(field), Math.Max(0, stacks));
        var previous = ActiveFieldSnapshot();
        CombatVarApi.SetInt(ActiveFieldIdKey, (int)field);
        CombatVarApi.SetInt(ActiveFieldStacksKey, nextStacks);
        CombatVarApi.SetInt(TriggerLockKey, 0);
        UpdateActivePolicyCache(field, nextStacks);
        if (previous.Field != field || previous.Stacks != nextStacks)
        {
            CombatVarApi.AddInt(ActiveFieldEpochKey, 1);
            NotifyChanged(source);
            return true;
        }

        return false;
    }

    private static bool ClearActiveFieldState(string source)
    {
        var previous = ActiveFieldSnapshot();
        CombatVarApi.SetInt(ActiveFieldIdKey, (int)SunExpFieldId.None);
        CombatVarApi.SetInt(ActiveFieldStacksKey, 0);
        CombatVarApi.SetInt(TriggerLockKey, 0);
        CombatVarApi.SetInt(LastRoundStartKey, 0);
        UpdateActivePolicyCache(SunExpFieldId.None, 0);
        if (!previous.IsActive)
        {
            return false;
        }

        CombatVarApi.AddInt(ActiveFieldEpochKey, 1);
        NotifyChanged(source);
        SunExpLog.Debug("[FieldApi] cleared active field from " + (source ?? ""));
        return true;
    }

    private static void SetActiveFieldStateDirect(SunExpFieldId field, int stacks, int epoch, string source)
    {
        var previous = ActiveFieldSnapshot();
        var nextStacks = field == SunExpFieldId.None ? 0 : Math.Min(MaxStacksFor(field), Math.Max(0, stacks));
        CombatVarApi.SetInt(ActiveFieldIdKey, nextStacks <= 0 ? (int)SunExpFieldId.None : (int)field);
        CombatVarApi.SetInt(ActiveFieldStacksKey, nextStacks);
        CombatVarApi.SetInt(ActiveFieldEpochKey, Math.Max(0, epoch));
        CombatVarApi.SetInt(TriggerLockKey, 0);
        UpdateActivePolicyCache(field, nextStacks);
        if (previous.Field != field || previous.Stacks != nextStacks || previous.Epoch != epoch)
        {
            NotifyChanged(source);
        }
    }

    private static void UpdateActivePolicyCache(SunExpFieldId field, int stacks)
    {
        activePolicyFlags = field == SunExpFieldId.None || stacks <= 0
            ? FieldEffectPolicyFlags.None
            : FieldEffectRegistry.PolicyFlags(field);
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
