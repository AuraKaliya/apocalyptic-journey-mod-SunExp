using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.DamageMeter.Resolution;

internal delegate void DamageAttributionPartSink(
    string sourceId,
    string sourceName,
    DamageTeam sourceTeam,
    int hpDamage,
    int shieldDamage,
    int finalDamage,
    DamageAttributionConfidence confidence);

internal sealed class BuffAttributionEngine
{
    private const int PendingWindowFrames = 4;
    private const int MaxPending = 128;
    private const int MaxPooledPending = 64;
    private const int MaxPooledTargets = 256;

    private readonly Dictionary<string, BuffAttributionState> states =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<IBuffItemConfig, string> configKeys =
        new(ReferenceComparer<IBuffItemConfig>.Instance);

    private readonly List<PendingBuffApplication> pending = new();
    private readonly Stack<PendingBuffApplication> pendingPool = new();
    private readonly Stack<PendingBuffTarget> targetPool = new();
    private readonly List<IBuffItemConfig> configsToRemove = new();
    private readonly List<OwnerSlot> activeOwners = new();
    private int[] hpParts = Array.Empty<int>();
    private int[] shieldParts = Array.Empty<int>();
    private int[] finalParts = Array.Empty<int>();
    private long[] splitRemainders = Array.Empty<long>();
    private long nextPendingId;

    public void Clear()
    {
        states.Clear();
        configKeys.Clear();
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            ReleasePending(pending[i]);
        }

        pending.Clear();
        activeOwners.Clear();
    }

    public long BeginApplication(IScriptExecutor? executor, string buffId, int frame)
    {
        buffId = buffId?.Trim() ?? "";
        if (executor == null || string.IsNullOrWhiteSpace(buffId))
        {
            return 0;
        }

        Prune(frame);
        var source = executor.Self;
        var sourceId = SafeStatusId(source);
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return 0;
        }

        var item = RentPending();
        item.Id = ++nextPendingId;
        item.Frame = frame;
        item.BuffId = buffId;
        item.SourceId = sourceId;
        item.SourceName = CombatantTeamResolver.DisplayName(source, sourceId);
        item.SourceTeam = CombatantTeamResolver.Resolve(source, sourceId);

        CaptureTargets(executor, buffId, item.Targets);
        if (item.Targets.Count == 0)
        {
            ReleasePending(item);
            return 0;
        }

        pending.Add(item);
        if (pending.Count > MaxPending)
        {
            var overflow = pending[0];
            pending.RemoveAt(0);
            ReleasePending(overflow);
        }

        return item.Id;
    }

    public void CompleteApplication(long pendingId)
    {
        var index = FindPending(pendingId);
        if (index < 0)
        {
            return;
        }

        var item = pending[index];
        pending.RemoveAt(index);
        for (var i = 0; i < item.Targets.Count; i++)
        {
            var target = item.Targets[i];
            var targetId = SafeStatusId(target.Target);
            var added = Math.Max(0, BuffLevel(target.Target, item.BuffId) - target.BeforeLevel);
            if (added <= 0 || string.IsNullOrWhiteSpace(targetId))
            {
                continue;
            }

            var state = GetState(targetId, item.BuffId);
            state.Add(
                item.SourceId,
                item.SourceName,
                item.SourceTeam,
                added,
                DamageAttributionConfidence.Exact);
            TryBindConfig(target.Target, item.BuffId, state.Key);
        }

        ReleasePending(item);
    }

    public void CancelApplication(long pendingId)
    {
        var index = FindPending(pendingId);
        if (index < 0)
        {
            return;
        }

        var item = pending[index];
        pending.RemoveAt(index);
        ReleasePending(item);
    }

    public void RemoveBuff(IStatusManager? target, string buffId)
    {
        var key = Key(SafeStatusId(target), buffId);
        if (!string.IsNullOrWhiteSpace(key))
        {
            RemoveState(key);
        }
    }

    public void OnLevelChanged(IBuffItemConfig? config, int newLevel, int frame)
    {
        Prune(frame);
        if (pending.Count > 0 || config == null || !configKeys.TryGetValue(config, out var key))
        {
            return;
        }

        if (newLevel <= 0)
        {
            RemoveState(key);
            return;
        }

        if (!states.TryGetValue(key, out var state))
        {
            return;
        }

        ReconcileLevel(state, newLevel);
    }

    public bool EmitSplit(
        IStatusManager? target,
        string buffId,
        int hpDamage,
        int shieldDamage,
        int finalDamage,
        DamageAttributionPartSink sink)
    {
        if (sink == null || hpDamage <= 0 && shieldDamage <= 0 && finalDamage <= 0)
        {
            return false;
        }

        var key = Key(SafeStatusId(target), buffId);
        if (string.IsNullOrWhiteSpace(key) || !states.TryGetValue(key, out var state))
        {
            return false;
        }

        BuildActiveOwners(state);
        var count = activeOwners.Count;
        if (count == 0)
        {
            return false;
        }

        EnsureSplitCapacity(count);
        SplitInto(hpDamage, count, hpParts);
        SplitInto(shieldDamage, count, shieldParts);
        SplitInto(finalDamage, count, finalParts);

        var emitted = false;
        var mixed = count > 1;
        for (var i = 0; i < count; i++)
        {
            var hp = hpParts[i];
            var shield = shieldParts[i];
            var final = finalParts[i];
            if (hp <= 0 && shield <= 0 && final <= 0)
            {
                continue;
            }

            var owner = activeOwners[i];
            sink(
                owner.SourceId,
                owner.SourceName,
                owner.SourceTeam,
                hp,
                shield,
                final,
                mixed ? DamageAttributionConfidence.Mixed : owner.Confidence);
            emitted = true;
        }

        return emitted;
    }

    private void ReconcileLevel(BuffAttributionState state, int newLevel)
    {
        var oldTotal = state.TotalUnits();
        if (oldTotal <= 0)
        {
            state.AddUnknown(newLevel);
            return;
        }

        if (newLevel == oldTotal)
        {
            return;
        }

        if (newLevel > oldTotal)
        {
            state.AddUnknown(newLevel - oldTotal);
            return;
        }

        BuildActiveOwners(state);
        var count = activeOwners.Count;
        if (count == 0)
        {
            state.AddUnknown(newLevel);
            return;
        }

        EnsureSplitCapacity(count);
        SplitInto(newLevel, count, hpParts);
        for (var i = 0; i < count; i++)
        {
            activeOwners[i].Units = hpParts[i];
            if (count > 1 && activeOwners[i].Confidence < DamageAttributionConfidence.Derived)
            {
                activeOwners[i].Confidence = DamageAttributionConfidence.Derived;
            }
        }

        state.RemoveEmptyOwners();
    }

    private BuffAttributionState GetState(string targetId, string buffId)
    {
        var key = Key(targetId, buffId);
        if (!states.TryGetValue(key, out var state))
        {
            state = new BuffAttributionState(key, targetId.Trim(), buffId.Trim());
            states[key] = state;
        }

        return state;
    }

    private void RemoveState(string key)
    {
        states.Remove(key);
        configsToRemove.Clear();
        foreach (var pair in configKeys)
        {
            if (string.Equals(pair.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                configsToRemove.Add(pair.Key);
            }
        }

        for (var i = 0; i < configsToRemove.Count; i++)
        {
            configKeys.Remove(configsToRemove[i]);
        }

        configsToRemove.Clear();
    }

    private void BuildActiveOwners(BuffAttributionState state)
    {
        activeOwners.Clear();
        for (var i = 0; i < state.Owners.Count; i++)
        {
            var owner = state.Owners[i];
            if (owner.Units > 0)
            {
                activeOwners.Add(owner);
            }
        }

        activeOwners.Sort(CompareOwner);
    }

    private void SplitInto(int amount, int count, int[] result)
    {
        Array.Clear(result, 0, count);
        amount = Math.Max(0, amount);
        if (amount <= 0)
        {
            return;
        }

        var totalWeight = 0L;
        for (var i = 0; i < count; i++)
        {
            totalWeight += Math.Max(0, activeOwners[i].Units);
        }

        if (totalWeight <= 0)
        {
            return;
        }

        var assigned = 0;
        for (var i = 0; i < count; i++)
        {
            var weight = Math.Max(0, activeOwners[i].Units);
            var weightedAmount = (long)amount * weight;
            result[i] = (int)(weightedAmount / totalWeight);
            splitRemainders[i] = weightedAmount % totalWeight;
            assigned += result[i];
        }

        var extra = amount - assigned;
        for (var step = 0; step < extra; step++)
        {
            var bestIndex = -1;
            var bestRemainder = -1L;
            for (var i = 0; i < count; i++)
            {
                if (activeOwners[i].Units <= 0 || splitRemainders[i] < 0)
                {
                    continue;
                }

                if (bestIndex < 0 || splitRemainders[i] > bestRemainder)
                {
                    bestIndex = i;
                    bestRemainder = splitRemainders[i];
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            result[bestIndex]++;
            splitRemainders[bestIndex] = -1;
        }
    }

    private void EnsureSplitCapacity(int count)
    {
        if (hpParts.Length >= count)
        {
            return;
        }

        hpParts = new int[count];
        shieldParts = new int[count];
        finalParts = new int[count];
        splitRemainders = new long[count];
    }

    private void Prune(int frame)
    {
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            if (frame - pending[i].Frame <= PendingWindowFrames)
            {
                continue;
            }

            var item = pending[i];
            pending.RemoveAt(i);
            ReleasePending(item);
        }

        while (pending.Count > MaxPending)
        {
            var item = pending[0];
            pending.RemoveAt(0);
            ReleasePending(item);
        }
    }

    private int FindPending(long pendingId)
    {
        for (var i = pending.Count - 1; i >= 0; i--)
        {
            if (pending[i].Id == pendingId)
            {
                return i;
            }
        }

        return -1;
    }

    private void CaptureTargets(IScriptExecutor executor, string buffId, List<PendingBuffTarget> targets)
    {
        if (executor.Object != null && executor.Object.Count > 0)
        {
            foreach (var target in executor.Object)
            {
                AppendTarget(target, buffId, targets);
            }

            return;
        }

        if (executor.status != null)
        {
            AppendTarget(executor.status, buffId, targets);
        }
    }

    private void AppendTarget(IStatusManager? target, string buffId, List<PendingBuffTarget> targets)
    {
        if (target == null || ContainsTarget(targets, target))
        {
            return;
        }

        var item = RentTarget();
        item.Target = target;
        item.BeforeLevel = BuffLevel(target, buffId);
        targets.Add(item);
    }

    private static bool ContainsTarget(List<PendingBuffTarget> targets, IStatusManager target)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            if (ReferenceEquals(targets[i].Target, target))
            {
                return true;
            }
        }

        return false;
    }

    private void TryBindConfig(IStatusManager target, string buffId, string key)
    {
        try
        {
            var config = target.GetBuff(buffId)?.buffConfig;
            if (config != null)
            {
                configKeys[config] = key;
            }
        }
        catch
        {
        }
    }

    private PendingBuffApplication RentPending()
    {
        return pendingPool.Count > 0 ? pendingPool.Pop() : new PendingBuffApplication();
    }

    private void ReleasePending(PendingBuffApplication item)
    {
        for (var i = item.Targets.Count - 1; i >= 0; i--)
        {
            ReleaseTarget(item.Targets[i]);
        }

        item.Reset();
        if (pendingPool.Count < MaxPooledPending)
        {
            pendingPool.Push(item);
        }
    }

    private PendingBuffTarget RentTarget()
    {
        return targetPool.Count > 0 ? targetPool.Pop() : new PendingBuffTarget();
    }

    private void ReleaseTarget(PendingBuffTarget target)
    {
        target.Reset();
        if (targetPool.Count < MaxPooledTargets)
        {
            targetPool.Push(target);
        }
    }

    private static int BuffLevel(IStatusManager target, string buffId)
    {
        try
        {
            return target.GetBuff(buffId)?.buffConfig?.Level ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeStatusId(IStatusManager? status)
    {
        try
        {
            return status?.InstanceId?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string Key(string targetId, string buffId)
    {
        return string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(buffId)
            ? ""
            : targetId.Trim() + "|" + buffId.Trim();
    }

    private static int CompareOwner(OwnerSlot left, OwnerSlot right)
    {
        return string.Compare(
            left.SourceId ?? "",
            right.SourceId ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BuffAttributionState
    {
        public BuffAttributionState(string key, string targetId, string buffId)
        {
            Key = key;
            TargetId = targetId;
            BuffId = buffId;
        }

        public string Key { get; }
        public string TargetId { get; }
        public string BuffId { get; }
        public List<OwnerSlot> Owners { get; } = new();

        public void Add(
            string sourceId,
            string sourceName,
            DamageTeam sourceTeam,
            int units,
            DamageAttributionConfidence confidence)
        {
            if (units <= 0)
            {
                return;
            }

            var owner = FindOwner(sourceId);
            if (owner == null)
            {
                owner = new OwnerSlot
                {
                    SourceId = string.IsNullOrWhiteSpace(sourceId) ? "unknown" : sourceId.Trim(),
                    SourceName = string.IsNullOrWhiteSpace(sourceName) ? "未知来源" : sourceName.Trim(),
                    SourceTeam = sourceTeam,
                    Confidence = confidence
                };
                Owners.Add(owner);
            }

            owner.SourceName = string.IsNullOrWhiteSpace(sourceName) ? owner.SourceName : sourceName.Trim();
            if (sourceTeam != DamageTeam.Unknown)
            {
                owner.SourceTeam = sourceTeam;
            }

            if (confidence > owner.Confidence)
            {
                owner.Confidence = confidence;
            }

            owner.Units += units;
        }

        public void AddUnknown(int units)
        {
            Add("unknown", "未知来源", DamageTeam.Unknown, units, DamageAttributionConfidence.Unknown);
        }

        public int TotalUnits()
        {
            var total = 0;
            for (var i = 0; i < Owners.Count; i++)
            {
                total += Math.Max(0, Owners[i].Units);
            }

            return total;
        }

        public void RemoveEmptyOwners()
        {
            for (var i = Owners.Count - 1; i >= 0; i--)
            {
                if (Owners[i].Units <= 0)
                {
                    Owners.RemoveAt(i);
                }
            }
        }

        private OwnerSlot? FindOwner(string sourceId)
        {
            sourceId = string.IsNullOrWhiteSpace(sourceId) ? "unknown" : sourceId.Trim();
            for (var i = 0; i < Owners.Count; i++)
            {
                if (string.Equals(Owners[i].SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                {
                    return Owners[i];
                }
            }

            return null;
        }
    }

    private sealed class OwnerSlot
    {
        public string SourceId { get; set; } = "";
        public string SourceName { get; set; } = "";
        public DamageTeam SourceTeam { get; set; }
        public int Units { get; set; }
        public DamageAttributionConfidence Confidence { get; set; }
    }

    private sealed class PendingBuffApplication
    {
        public long Id { get; set; }
        public int Frame { get; set; }
        public string BuffId { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceName { get; set; } = "";
        public DamageTeam SourceTeam { get; set; }
        public List<PendingBuffTarget> Targets { get; } = new();

        public void Reset()
        {
            Id = 0;
            Frame = 0;
            BuffId = "";
            SourceId = "";
            SourceName = "";
            SourceTeam = DamageTeam.Unknown;
            Targets.Clear();
        }
    }

    private sealed class PendingBuffTarget
    {
        public IStatusManager Target { get; set; } = null!;
        public int BeforeLevel { get; set; }

        public void Reset()
        {
            Target = null!;
            BeforeLevel = 0;
        }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
