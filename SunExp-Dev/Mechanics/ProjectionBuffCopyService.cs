using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public enum ProjectionBuffPolicy
{
    Direct,
    Adapter,
    Reject
}

[Serializable]
public sealed class ProjectionBuffSnapshot
{
    public ProjectionBuffSnapshot()
    {
    }

    public ProjectionBuffSnapshot(string buffId, int level)
    {
        BuffId = buffId?.Trim() ?? "";
        Level = Math.Max(0, level);
    }

    public string BuffId { get; set; } = "";

    public int Level { get; set; }
}

/// <summary>
/// Adapts a player-resource buff to projection semantics. Implementations must
/// apply an exact level and be safe to invoke again for a newer snapshot.
/// </summary>
public interface IProjectionBuffAdapter
{
    void ApplyExact(IStatusManager projection, ProjectionBuffSnapshot snapshot);
}

public readonly struct ProjectionBuffPolicyDecision
{
    public ProjectionBuffPolicyDecision(ProjectionBuffPolicy policy, IProjectionBuffAdapter? adapter = null, string reason = "")
    {
        Policy = policy;
        Adapter = adapter;
        Reason = reason ?? "";
    }

    public ProjectionBuffPolicy Policy { get; }

    public IProjectionBuffAdapter? Adapter { get; }

    public string Reason { get; }
}

/// <summary>
/// Registry for buffs which cannot safely use the default Status.AddBuff path.
/// Unknown buffs intentionally default to Direct and fail independently.
/// </summary>
public static class ProjectionBuffPolicyRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, ProjectionBuffPolicyDecision> Decisions = new(StringComparer.Ordinal);

    public static void RegisterAdapter(string buffId, IProjectionBuffAdapter adapter)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            throw new ArgumentException("Buff id is required.", nameof(buffId));
        }

        if (adapter == null)
        {
            throw new ArgumentNullException(nameof(adapter));
        }

        lock (SyncRoot)
        {
            Decisions[buffId.Trim()] = new ProjectionBuffPolicyDecision(ProjectionBuffPolicy.Adapter, adapter);
        }
    }

    public static void RegisterReject(string buffId, string reason)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            throw new ArgumentException("Buff id is required.", nameof(buffId));
        }

        lock (SyncRoot)
        {
            Decisions[buffId.Trim()] = new ProjectionBuffPolicyDecision(ProjectionBuffPolicy.Reject, null, reason);
        }
    }

    public static ProjectionBuffPolicyDecision Resolve(string? buffId)
    {
        var id = buffId?.Trim() ?? "";
        lock (SyncRoot)
        {
            return Decisions.TryGetValue(id, out var decision)
                ? decision
                : new ProjectionBuffPolicyDecision(ProjectionBuffPolicy.Direct);
        }
    }

    public static void ClearOverrides()
    {
        lock (SyncRoot)
        {
            Decisions.Clear();
        }
    }
}

/// <summary>
/// Captures Cocoa-style positive/negative buffs and restores them on a
/// projection. Each buff is isolated so an incompatible mod buff never aborts
/// the remaining copy or the projection summon.
/// </summary>
public static class ProjectionBuffCopyService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, long> AppliedRevisions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> HydratedBuffIds = new(StringComparer.Ordinal);

    public static IReadOnlyList<ProjectionBuffSnapshot> Capture(IStatusManager? owner)
    {
        if (owner == null)
        {
            return Array.Empty<ProjectionBuffSnapshot>();
        }

        IBuffItem[] buffs;
        try
        {
            buffs = (owner.GetBuffs() ?? Array.Empty<IBuffItem>()).Where(buff => buff != null).ToArray();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[ProjectionBuff] owner buff capture failed: " + ex.Message);
            return Array.Empty<ProjectionBuffSnapshot>();
        }

        var snapshots = new List<ProjectionBuffSnapshot>(buffs.Length);
        foreach (var buff in buffs)
        {
            try
            {
                var config = buff.buffConfig;
                if (config == null || config.Level <= 0 || string.IsNullOrWhiteSpace(config.BuffId) || !IsCopyableType(config.Type))
                {
                    continue;
                }

                snapshots.Add(new ProjectionBuffSnapshot(config.BuffId, config.Level));
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("[ProjectionBuff] skipped malformed owner buff: " + ex.Message);
            }
        }

        return snapshots;
    }

    /// <summary>
    /// Applies the authoritative summon snapshot. Missing direct buffs are
    /// created with Status.AddBuff; existing buffs are set to the exact level.
    /// </summary>
    public static IReadOnlyList<ProjectionBuffSnapshot> ApplyInitial(
        IStatusManager? projection,
        IEnumerable<ProjectionBuffSnapshot>? snapshots)
    {
        return ApplyExactCore(projection, snapshots, null);
    }

    /// <summary>
    /// Idempotent client/reconciliation entry point. A revision no newer than
    /// the last applied revision is ignored.
    /// </summary>
    public static IReadOnlyList<ProjectionBuffSnapshot> HydrateExact(
        IStatusManager? projection,
        IEnumerable<ProjectionBuffSnapshot>? snapshots,
        long revision,
        bool removeMissing = true)
    {
        if (projection == null || string.IsNullOrWhiteSpace(projection.InstanceId))
        {
            return Array.Empty<ProjectionBuffSnapshot>();
        }

        var source = snapshots?.Where(snapshot => snapshot != null).ToArray()
            ?? Array.Empty<ProjectionBuffSnapshot>();
        HashSet<string> previousIds;
        lock (SyncRoot)
        {
            if (AppliedRevisions.TryGetValue(projection.InstanceId, out var applied) && revision <= applied)
            {
                return ReadActual(projection);
            }

            previousIds = HydratedBuffIds.TryGetValue(projection.InstanceId, out var hydrated)
                ? new HashSet<string>(hydrated, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            AppliedRevisions[projection.InstanceId] = revision;
            var nextIds = new HashSet<string>(
                source.Where(item => item.Level > 0 && !string.IsNullOrWhiteSpace(item.BuffId))
                    .Select(item => item.BuffId.Trim()),
                StringComparer.Ordinal);
            if (!removeMissing)
            {
                nextIds.UnionWith(previousIds);
            }

            HydratedBuffIds[projection.InstanceId] = nextIds;
        }

        if (removeMissing)
        {
            var currentIds = new HashSet<string>(source.Select(item => item.BuffId?.Trim() ?? ""), StringComparer.Ordinal);
            foreach (var staleId in previousIds.Where(id => !currentIds.Contains(id)))
            {
                ApplyOneExact(projection, new ProjectionBuffSnapshot(staleId, 0), revision);
            }
        }

        return ApplyExactCore(projection, source, revision);
    }

    public static IReadOnlyList<ProjectionBuffSnapshot> ReadActual(IStatusManager? projection)
    {
        return Capture(projection);
    }

    public static void Forget(string? statusId)
    {
        var id = statusId?.Trim() ?? "";
        if (id.Length == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            AppliedRevisions.Remove(id);
            HydratedBuffIds.Remove(id);
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            AppliedRevisions.Clear();
            HydratedBuffIds.Clear();
        }
    }

    public static bool IsCopyableType(string? type)
    {
        var value = type?.Trim() ?? "";
        return value.IndexOf("正面", StringComparison.Ordinal) >= 0
            || value.IndexOf("负面", StringComparison.Ordinal) >= 0
            || string.Equals(value, "Positive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Negative", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ProjectionBuffSnapshot> ApplyExactCore(
        IStatusManager? projection,
        IEnumerable<ProjectionBuffSnapshot>? snapshots,
        long? revision)
    {
        if (projection == null)
        {
            return Array.Empty<ProjectionBuffSnapshot>();
        }

        var source = snapshots?.Where(snapshot => snapshot != null).ToArray()
            ?? Array.Empty<ProjectionBuffSnapshot>();
        foreach (var snapshot in source)
        {
            var id = snapshot.BuffId?.Trim() ?? "";
            if (id.Length == 0)
            {
                continue;
            }

            ApplyOneExact(projection, new ProjectionBuffSnapshot(id, snapshot.Level), revision);
        }

        return ReadActual(projection);
    }

    private static void UpsertDirect(IStatusManager projection, string buffId, int level)
    {
        var existing = projection.GetBuff(buffId);
        if (level <= 0)
        {
            if (existing != null)
            {
                projection.RemoveBuff(buffId);
            }

            return;
        }

        if (existing?.buffConfig == null)
        {
            projection.AddBuff(buffId, level);
            return;
        }

        existing.buffConfig.Level = level;
    }

    private static void ApplyOneExact(IStatusManager projection, ProjectionBuffSnapshot snapshot, long? revision)
    {
        var id = snapshot.BuffId;
        try
        {
            var decision = ProjectionBuffPolicyRegistry.Resolve(id);
            switch (decision.Policy)
            {
                case ProjectionBuffPolicy.Reject:
                    SunExpLog.Warn("[ProjectionBuff] rejected buff=" + id + ", reason=" + decision.Reason);
                    break;
                case ProjectionBuffPolicy.Adapter:
                    if (decision.Adapter == null)
                    {
                        throw new InvalidOperationException("Adapter policy has no adapter.");
                    }

                    decision.Adapter.ApplyExact(projection, snapshot);
                    break;
                default:
                    UpsertDirect(projection, id, snapshot.Level);
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[ProjectionBuff] skipped buff=" + id
                + ", level=" + snapshot.Level
                + (revision.HasValue ? ", revision=" + revision.Value : "")
                + ": " + ex.Message);
        }
    }
}
