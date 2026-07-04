using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class CompanionThreatService
{
    private const int RealPlayerTargetWeight = 100;
    private const int MinCompanionTargetWeight = 10;
    private const int MaxCompanionTargetWeight = 160;

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
            threat.PreviewThreat = Math.Max(0, intent.Threat?.Preview ?? 0);
            threat.DecayPerRound = Math.Max(1, intent.Threat?.Decay ?? 4);
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
            threat.RecentThreat = Math.Min(200, threat.RecentThreat + Math.Max(0, intent.Threat?.OnUse ?? 0));
            threat.DecayPerRound = Math.Max(1, intent.Threat?.Decay ?? threat.DecayPerRound);
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

    public static int ThreatPercent(CompanionBattleState? state)
    {
        var threat = Snapshot(state?.StatusId);
        if (threat == null)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(100, threat.Value.Value * 100 / MaxCompanionTargetWeight));
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

    private static CompanionThreatSnapshot? Snapshot(string? statusId)
    {
        var threat = FindMutable(statusId);
        if (threat == null)
        {
            return null;
        }

        lock (SyncRoot)
        {
            return new CompanionThreatSnapshot(threat.Value);
        }
    }

    private static int BaseThreat(CompanionStats stats)
    {
        if (stats == null)
        {
            return MinCompanionTargetWeight;
        }

        var value = stats.MaxHp * 0.15f
            + stats.Armor * 0.8f
            + stats.Attack * 1.2f
            + stats.MaxMagic * 1.5f;
        return Math.Max(MinCompanionTargetWeight, (int)Math.Round(value, MidpointRounding.AwayFromZero));
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
            BaseThreat = Math.Max(MinCompanionTargetWeight, baseThreat);
        }

        public string StatusId { get; }

        public int BaseThreat { get; }

        public int PreviewThreat { get; set; }

        public int RecentThreat { get; set; }

        public int DecayPerRound { get; set; } = 4;

        public int Value => Math.Max(MinCompanionTargetWeight, BaseThreat + PreviewThreat + RecentThreat);
    }

    private readonly struct CompanionThreatSnapshot
    {
        public CompanionThreatSnapshot(int value)
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
