using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class CompanionThreatSnapshot
{
    public string StatusId { get; set; } = "";

    public int BaseThreat { get; set; }

    public int PreviewThreat { get; set; }

    public int RecentThreat { get; set; }

    public int DecayPerRound { get; set; }

    public int FinalThreat { get; set; }
}

public static class CompanionThreatService
{
    private const int RealPlayerTargetWeight = 100;
    private const int SpiritBaseTargetWeight = 20;
    public const int MinCompanionTargetWeight = 0;
    public const int MaxCompanionTargetWeight = 160;
    public const int MaxBaseThreat = 0;
    public const int MaxPreviewThreat = 40;
    public const int MaxOnUseThreat = 60;
    public const int MaxRecentThreat = 120;
    public const int MinDecay = 1;
    public const int MaxDecay = 30;

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, CompanionThreatState> Threats = new(StringComparer.Ordinal);

    public static void Register(CompanionBattleState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.StatusId))
        {
            return;
        }

        lock (SyncRoot)
        {
            Threats[state.StatusId] = new CompanionThreatState(
                state.StatusId,
                string.Equals(state.EntityKind, "SpiritAttachment", StringComparison.Ordinal)
                    ? SpiritBaseTargetWeight
                    : 0);
        }

        TerriasPerformanceCounters.Record("Companion.Threat.Registered");
    }

    /// <summary>Compatibility overload. Projections no longer derive threat from HP, armor, attack, or magic.</summary>
    public static void Register(
        CompanionBattleState state,
        int maxHp,
        int armor,
        int attack,
        int maxMagic)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.StatusId))
        {
            return;
        }

        lock (SyncRoot)
        {
            Threats[state.StatusId] = new CompanionThreatState(
                state.StatusId,
                0);
        }

        TerriasPerformanceCounters.Record("Companion.Threat.Registered");
    }

    public static void Remove(string? statusId)
    {
        var id = statusId ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (SyncRoot)
        {
            Threats.Remove(id);
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            Threats.Clear();
        }
    }

    public static void SetPreview(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        int resolvedValue = 0,
        int repeatCount = 1)
    {
        var threat = FindMutable(state?.StatusId);
        if (threat == null || intent == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            threat.PreviewThreat = CalculatePreview(intent, resolvedValue, repeatCount);
            threat.DecayPerRound = Clamp(intent.Threat?.Decay ?? 4, MinDecay, MaxDecay);
        }
    }

    public static void ClearPreview(CompanionBattleState? state)
    {
        var threat = FindMutable(state?.StatusId);
        if (threat == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            threat.PreviewThreat = 0;
        }
    }

    public static void MarkIntentUsed(
        CompanionBattleState state,
        CompanionIntentDefinition intent,
        int resolvedValue = 0,
        int repeatCount = 1)
    {
        var threat = FindMutable(state?.StatusId);
        if (threat == null || intent == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            var outputBonus = Math.Max(0, resolvedValue) * Math.Max(1, repeatCount) / 10;
            var onUse = Clamp((intent.Threat?.OnUse ?? 0) + outputBonus, 0, MaxOnUseThreat);
            threat.RecentThreat = Clamp(threat.RecentThreat + onUse, 0, MaxRecentThreat);
            threat.DecayPerRound = Clamp(intent.Threat?.Decay ?? threat.DecayPerRound, MinDecay, MaxDecay);
        }
    }

    public static void DecayForTurn(CompanionBattleState? state)
    {
        var threat = FindMutable(state?.StatusId);
        if (threat == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            threat.RecentThreat = Math.Max(0, threat.RecentThreat - threat.DecayPerRound);
        }
    }

    public static int ThreatPressurePercent(CompanionBattleState? state)
    {
        var threat = Snapshot(state?.StatusId);
        if (threat == null)
        {
            return 0;
        }

        return Clamp((int)Math.Round(
            (threat.Value.Value - MinCompanionTargetWeight) * 100d
            / (MaxCompanionTargetWeight - MinCompanionTargetWeight),
            MidpointRounding.AwayFromZero), 0, 100);
    }

    public static int ThreatPercent(CompanionBattleState? state) => ThreatPressurePercent(state);

    public static int CurrentWeight(CompanionBattleState? state)
    {
        return Snapshot(state?.StatusId)?.Value ?? MinCompanionTargetWeight;
    }

    public static int CalculatePreview(CompanionIntentDefinition? intent, int resolvedValue, int repeatCount)
    {
        if (intent == null)
        {
            return 0;
        }

        var outputBonus = Math.Max(0, resolvedValue) * Math.Max(1, repeatCount) / 20;
        return Clamp((intent.Threat?.Preview ?? 0) + outputBonus, 0, MaxPreviewThreat);
    }

    public static CompanionThreatSnapshot? Export(string? statusId)
    {
        var threat = FindMutable(statusId);
        if (threat == null)
        {
            return null;
        }

        lock (SyncRoot)
        {
            return new CompanionThreatSnapshot
            {
                StatusId = threat.StatusId,
                BaseThreat = threat.BaseThreat,
                PreviewThreat = threat.PreviewThreat,
                RecentThreat = threat.RecentThreat,
                DecayPerRound = threat.DecayPerRound,
                FinalThreat = threat.Value
            };
        }
    }

    /// <summary>Restores a host-authored threat snapshot on an observing client.</summary>
    public static void ApplyAuthoritative(CompanionThreatSnapshot? snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.StatusId))
        {
            return;
        }

        lock (SyncRoot)
        {
            Threats[snapshot.StatusId] = new CompanionThreatState(snapshot.StatusId, snapshot.BaseThreat)
            {
                PreviewThreat = Clamp(snapshot.PreviewThreat, 0, MaxPreviewThreat),
                RecentThreat = Clamp(snapshot.RecentThreat, 0, MaxRecentThreat),
                DecayPerRound = Clamp(snapshot.DecayPerRound, MinDecay, MaxDecay)
            };
        }
    }

    public static int CalculateBaseThreat(CompanionStats? stats) => 0;

    public static int CalculateBaseThreat(int maxHp, int armor, int attack, int maxMagic)
    {
        return 0;
    }

    public static void AddActiveCompanionsToAllTargets(ScriptExecutor executor)
    {
        if (executor?.Object == null)
        {
            return;
        }

        var known = new HashSet<string>(
            executor.Object.Where(status => status != null && IsAlive(status)).Select(status => status!.InstanceId),
            StringComparer.Ordinal);
        foreach (var state in SpiritStateStore.Active())
        {
            var status = state.Spirit?.Status;
            if (status != null && IsAlive(status) && known.Add(status.InstanceId))
            {
                executor.Object.Add(status);
            }
        }
    }

    public static bool TryRedirectEnemySingleTarget(ScriptExecutor executor)
    {
        if (executor?.Object == null)
        {
            return false;
        }

        var realTargets = executor.Object
            .Where(target => IsAlive(target)
                             && !ProjectionStateStore.IsProjection(target)
                             && !SpiritStateStore.IsSpirit(target))
            .GroupBy(target => target.InstanceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var spiritTargets = SpiritStateStore.Active()
            .Select(state => state.Spirit?.Status)
            .Where(IsAlive)
            .Cast<IStatusManager>()
            .GroupBy(target => target.InstanceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (realTargets.Count == 0 && spiritTargets.Count == 0)
        {
            return false;
        }

        var ownerBonuses = realTargets.ToDictionary(
            target => target.InstanceId,
            target => OwnerThreat(target.InstanceId),
            StringComparer.Ordinal);
        if (spiritTargets.Count == 0 && ownerBonuses.Values.All(value => value <= 0))
        {
            return false;
        }

        var weighted = realTargets
            .Select(target => new CompanionTargetCandidate(target, RealPlayerTargetWeight + ownerBonuses[target.InstanceId]))
            .ToList();
        weighted.AddRange(spiritTargets.Select(target => new CompanionTargetCandidate(
            target,
            Math.Max(SpiritBaseTargetWeight, Snapshot(target.InstanceId)?.Value ?? SpiritBaseTargetWeight))));
        var totalWeight = weighted.Sum(candidate => candidate.Weight);
        if (totalWeight <= 0)
        {
            return false;
        }

        var roll = UnityEngine.Random.Range(0, totalWeight);
        var cursor = 0;
        foreach (var candidate in weighted)
        {
            cursor += candidate.Weight;
            if (roll >= cursor)
            {
                continue;
            }

            var changed = !ReferenceEquals(executor.Target, candidate.Status);
            executor.Target = candidate.Status;
            executor.Object.Clear();
            executor.Object.Add(candidate.Status);
            if (SpiritStateStore.IsSpirit(candidate.Status))
            {
                MarkSpiritTargeted(candidate.Status.InstanceId);
            }
            else
            {
                MarkOwnerTargeted(candidate.Status.InstanceId);
            }
            if (changed)
            {
                TerriasPerformanceCounters.Record(SpiritStateStore.IsSpirit(candidate.Status)
                    ? "Companion.Threat.RedirectedToSpirit"
                    : "Companion.Threat.RedirectedToOwner");
            }
            return changed;
        }

        return false;
    }

    private static int OwnerThreat(string ownerStatusId)
    {
        var total = 0;
        foreach (var projection in ProjectionStateStore.Active())
        {
            if (!string.Equals(projection.OwnerStatusId, ownerStatusId, StringComparison.Ordinal))
            {
                continue;
            }

            total += Snapshot(projection.StatusId)?.Value ?? 0;
        }

        return Clamp(total, 0, MaxCompanionTargetWeight);
    }

    private static void MarkOwnerTargeted(string ownerStatusId)
    {
        foreach (var projection in ProjectionStateStore.Active()
                     .Where(state => string.Equals(state.OwnerStatusId, ownerStatusId, StringComparison.Ordinal)))
        {
            var threat = FindMutable(projection.StatusId);
            if (threat == null)
            {
                continue;
            }

            lock (SyncRoot)
            {
                threat.RecentThreat = Math.Max(0, threat.RecentThreat - threat.DecayPerRound);
            }
        }
    }

    private static void MarkSpiritTargeted(string statusId)
    {
        var threat = FindMutable(statusId);
        if (threat != null)
        {
            lock (SyncRoot)
            {
                threat.RecentThreat = Math.Max(0, threat.RecentThreat - threat.DecayPerRound);
            }
        }

        var spirit = SpiritStateStore.Find(statusId)?.Spirit;
        if (spirit != null)
        {
            SpiritSummonService.BroadcastRuntimeState(spirit, "ThreatTargeted");
        }
    }

    private static CompanionThreatState? FindMutable(string? statusId)
    {
        var id = statusId ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        lock (SyncRoot)
        {
            return Threats.TryGetValue(id, out var threat) ? threat : null;
        }
    }

    private static CompanionThreatValueSnapshot? Snapshot(string? statusId)
    {
        var threat = FindMutable(statusId);
        if (threat == null)
        {
            return null;
        }

        lock (SyncRoot)
        {
            return new CompanionThreatValueSnapshot(threat.Value);
        }
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }

    private sealed class CompanionThreatState
    {
        public CompanionThreatState(string statusId, int baseThreat)
        {
            StatusId = statusId;
            BaseThreat = Clamp(baseThreat, 0, MaxCompanionTargetWeight);
        }

        public string StatusId { get; }

        public int BaseThreat { get; }

        public int PreviewThreat { get; set; }

        public int RecentThreat { get; set; }

        public int DecayPerRound { get; set; } = 4;

        public int Value => Clamp(BaseThreat + PreviewThreat + RecentThreat, 0, MaxCompanionTargetWeight);
    }

    private readonly struct CompanionThreatValueSnapshot
    {
        public CompanionThreatValueSnapshot(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    private readonly struct CompanionTargetCandidate
    {
        public CompanionTargetCandidate(IStatusManager status, int weight)
        {
            Status = status;
            Weight = Math.Max(MinCompanionTargetWeight, weight);
        }

        public IStatusManager Status { get; }

        public int Weight { get; }
    }
}
