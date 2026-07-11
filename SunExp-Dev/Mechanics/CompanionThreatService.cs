using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

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
    public const int MinCompanionTargetWeight = 80;
    public const int MaxCompanionTargetWeight = 200;
    public const int MaxBaseThreat = 120;
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
            Threats[state.StatusId] = new CompanionThreatState(state.StatusId, BaseThreat(state.Stats));
        }

        SunExpPerformanceCounters.Record("Companion.Threat.Registered");
    }

    /// <summary>
    /// Registers from the projection's post-buff values instead of its original
    /// immutable CompanionStats. Intended for the authoritative spawn pipeline.
    /// </summary>
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
                CalculateBaseThreat(maxHp, armor, attack, maxMagic));
        }

        SunExpPerformanceCounters.Record("Companion.Threat.Registered");
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

    public static void SetPreview(CompanionBattleState state, CompanionIntentDefinition intent)
    {
        var threat = FindMutable(state?.StatusId);
        if (threat == null || intent == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            threat.PreviewThreat = Clamp(intent.Threat?.Preview ?? 0, 0, MaxPreviewThreat);
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

    public static void MarkIntentUsed(CompanionBattleState state, CompanionIntentDefinition intent)
    {
        var threat = FindMutable(state?.StatusId);
        if (threat == null || intent == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            var onUse = Clamp(intent.Threat?.OnUse ?? 0, 0, MaxOnUseThreat);
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

    public static int CalculateBaseThreat(CompanionStats? stats) => BaseThreat(stats);

    public static int CalculateBaseThreat(int maxHp, int armor, int attack, int maxMagic)
    {
        var raw = Math.Max(0, maxHp) * 0.15f
            + Math.Max(0, armor) * 0.8f
            + Math.Max(0, attack) * 1.2f
            + Math.Max(0, maxMagic) * 1.5f;
        var contribution = Clamp(
            (int)Math.Round(raw * 0.5f, MidpointRounding.AwayFromZero),
            0,
            MaxBaseThreat - MinCompanionTargetWeight);
        return Clamp(MinCompanionTargetWeight + contribution, MinCompanionTargetWeight, MaxBaseThreat);
    }

    public static void AddActiveCompanionsToAllTargets(ScriptExecutor executor)
    {
        if (executor?.Object == null)
        {
            return;
        }

        var seen = new HashSet<string>(executor.Object.Where(target => target != null).Select(target => target.InstanceId), StringComparer.Ordinal);
        foreach (var candidate in ActiveCompanionTargets())
        {
            if (candidate.Status != null && seen.Add(candidate.Status.InstanceId))
            {
                executor.Object.Add(candidate.Status);
            }
        }
    }

    public static bool TryRedirectEnemySingleTarget(ScriptExecutor executor)
    {
        if (executor?.Object == null)
        {
            return false;
        }

        var companions = ActiveCompanionTargets().ToList();
        if (companions.Count == 0)
        {
            return false;
        }

        var realTargets = executor.Object
            .Where(target => target != null && !ProjectionStateStore.IsProjection(target))
            .ToList();
        var totalWeight = realTargets.Count * RealPlayerTargetWeight + companions.Sum(candidate => candidate.Weight);
        if (totalWeight <= 0)
        {
            return false;
        }

        var roll = UnityEngine.Random.Range(0, totalWeight);
        var cursor = realTargets.Count * RealPlayerTargetWeight;
        if (roll < cursor)
        {
            return false;
        }

        foreach (var candidate in companions)
        {
            cursor += candidate.Weight;
            if (roll >= cursor)
            {
                continue;
            }

            executor.Target = candidate.Status;
            executor.Object.Clear();
            executor.Object.Add(candidate.Status);
            MarkTargeted(candidate.Status.InstanceId);
            SunExpPerformanceCounters.Record("Companion.Threat.Redirected");
            return true;
        }

        return false;
    }

    private static IEnumerable<CompanionTargetCandidate> ActiveCompanionTargets()
    {
        foreach (var projection in ProjectionStateStore.Active())
        {
            var status = projection.Projection?.Status;
            if (!IsAlive(status))
            {
                continue;
            }

            var threat = Snapshot(status!.InstanceId);
            if (threat == null)
            {
                continue;
            }

            yield return new CompanionTargetCandidate(
                status,
                Math.Max(MinCompanionTargetWeight, Math.Min(MaxCompanionTargetWeight, threat.Value.Value)));
        }
    }

    private static void MarkTargeted(string statusId)
    {
        var threat = FindMutable(statusId);
        if (threat == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            threat.RecentThreat = Math.Max(0, threat.RecentThreat - threat.DecayPerRound);
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

    private static int BaseThreat(CompanionStats? stats)
    {
        if (stats == null)
        {
            return MinCompanionTargetWeight;
        }

        return CalculateBaseThreat(stats.MaxHp, stats.Armor, stats.Attack, stats.MaxMagic);
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
            BaseThreat = Clamp(baseThreat, MinCompanionTargetWeight, MaxBaseThreat);
        }

        public string StatusId { get; }

        public int BaseThreat { get; }

        public int PreviewThreat { get; set; }

        public int RecentThreat { get; set; }

        public int DecayPerRound { get; set; } = 4;

        public int Value => Clamp(BaseThreat + PreviewThreat + RecentThreat, MinCompanionTargetWeight, MaxCompanionTargetWeight);
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
