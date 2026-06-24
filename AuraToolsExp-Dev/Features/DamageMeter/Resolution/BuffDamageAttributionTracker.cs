using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.DamageMeter.Resolution;

internal sealed class BuffDamageAttributionTracker
{
    private readonly Dictionary<string, Dictionary<string, BuffContribution>> contributions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IBuffItemConfig, string> configKeys =
        new(ReferenceComparer<IBuffItemConfig>.Instance);

    private readonly List<PendingBuffApplication> pending = new();
    private long nextPendingId;

    public void Clear()
    {
        contributions.Clear();
        configKeys.Clear();
        pending.Clear();
    }

    public long BeginApplication(IScriptExecutor? executor, string buffId, int frame)
    {
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

        var targets = ResolveTargets(executor)
            .Select(target => new PendingBuffTarget
            {
                Target = target,
                BeforeLevel = BuffLevel(target, buffId)
            })
            .ToList();
        if (targets.Count == 0)
        {
            return 0;
        }

        var id = ++nextPendingId;
        pending.Add(new PendingBuffApplication
        {
            Id = id,
            Frame = frame,
            BuffId = buffId.Trim(),
            SourceId = sourceId,
            SourceName = CombatantTeamResolver.DisplayName(source, sourceId),
            SourceTeam = CombatantTeamResolver.Resolve(source, sourceId),
            Targets = targets
        });
        return id;
    }

    public void CompleteApplication(long pendingId)
    {
        var index = pending.FindLastIndex(item => item.Id == pendingId);
        if (index < 0)
        {
            return;
        }

        var item = pending[index];
        pending.RemoveAt(index);
        foreach (var target in item.Targets)
        {
            var added = Math.Max(0, BuffLevel(target.Target, item.BuffId) - target.BeforeLevel);
            if (added <= 0)
            {
                continue;
            }

            var key = Key(SafeStatusId(target.Target), item.BuffId);
            if (!contributions.TryGetValue(key, out var owners))
            {
                owners = new Dictionary<string, BuffContribution>(StringComparer.OrdinalIgnoreCase);
                contributions[key] = owners;
            }

            if (!owners.TryGetValue(item.SourceId, out var contribution))
            {
                contribution = new BuffContribution
                {
                    SourceId = item.SourceId,
                    SourceName = item.SourceName,
                    SourceTeam = item.SourceTeam
                };
                owners[item.SourceId] = contribution;
            }

            contribution.Weight += added;
            try
            {
                var config = target.Target.GetBuff(item.BuffId)?.buffConfig;
                if (config != null)
                {
                    configKeys[config] = key;
                }
            }
            catch
            {
            }
        }
    }

    public void CancelApplication(long pendingId)
    {
        pending.RemoveAll(item => item.Id == pendingId);
    }

    public void RemoveBuff(IStatusManager? target, string buffId)
    {
        var key = Key(SafeStatusId(target), buffId);
        if (!string.IsNullOrWhiteSpace(key))
        {
            contributions.Remove(key);
            foreach (var config in configKeys.Where(pair => pair.Value == key).Select(pair => pair.Key).ToList())
            {
                configKeys.Remove(config);
            }
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
            contributions.Remove(key);
            configKeys.Remove(config);
            return;
        }

        if (!contributions.TryGetValue(key, out var owners) || owners.Count == 0)
        {
            return;
        }

        var ordered = owners.Values
            .Where(owner => owner.Weight > 0)
            .OrderBy(owner => owner.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var oldTotal = ordered.Sum(owner => owner.Weight);
        if (oldTotal <= 0 || oldTotal == newLevel)
        {
            return;
        }

        if (newLevel > oldTotal)
        {
            if (!owners.TryGetValue("unknown", out var unknown))
            {
                unknown = new BuffContribution
                {
                    SourceId = "unknown",
                    SourceName = "未知来源",
                    SourceTeam = DamageTeam.Unknown
                };
                owners["unknown"] = unknown;
            }

            unknown.Weight += newLevel - oldTotal;
            return;
        }

        var scaled = DamageAllocation.ProportionalSplit(
            newLevel,
            ordered.Select(owner => owner.Weight).ToList());
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Weight = scaled[i];
        }

        foreach (var sourceId in owners.Where(pair => pair.Value.Weight <= 0).Select(pair => pair.Key).ToList())
        {
            owners.Remove(sourceId);
        }
    }

    public IReadOnlyList<AttributedDamagePart> Split(
        IStatusManager? target,
        string buffId,
        int hpDamage,
        int shieldDamage,
        int finalDamage)
    {
        var key = Key(SafeStatusId(target), buffId);
        if (string.IsNullOrWhiteSpace(key)
            || !contributions.TryGetValue(key, out var owners)
            || owners.Count == 0)
        {
            return Array.Empty<AttributedDamagePart>();
        }

        var ordered = owners.Values
            .Where(owner => owner.Weight > 0)
            .OrderBy(owner => owner.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalWeight = ordered.Sum(owner => owner.Weight);
        if (totalWeight <= 0)
        {
            return Array.Empty<AttributedDamagePart>();
        }

        var weights = ordered.Select(owner => owner.Weight).ToList();
        var hpParts = DamageAllocation.ProportionalSplit(hpDamage, weights);
        var shieldParts = DamageAllocation.ProportionalSplit(shieldDamage, weights);
        var finalParts = DamageAllocation.ProportionalSplit(finalDamage, weights);
        var confidence = ordered.Count == 1
            ? DamageAttributionConfidence.Exact
            : DamageAttributionConfidence.Mixed;
        var result = new List<AttributedDamagePart>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            result.Add(new AttributedDamagePart
            {
                SourceId = ordered[i].SourceId,
                SourceName = ordered[i].SourceName,
                SourceTeam = ordered[i].SourceTeam,
                HpDamage = hpParts[i],
                ShieldDamage = shieldParts[i],
                FinalDamage = finalParts[i],
                Confidence = confidence
            });
        }

        return result;
    }

    private void Prune(int frame)
    {
        pending.RemoveAll(item => frame - item.Frame > 4);
        if (pending.Count > 128)
        {
            pending.RemoveRange(0, pending.Count - 128);
        }
    }

    private static IEnumerable<IStatusManager> ResolveTargets(IScriptExecutor executor)
    {
        if (executor.Object != null && executor.Object.Count > 0)
        {
            foreach (var target in executor.Object)
            {
                if (target != null)
                {
                    yield return target;
                }
            }

            yield break;
        }

        if (executor.status != null)
        {
            yield return executor.status;
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

    private sealed class PendingBuffApplication
    {
        public long Id { get; set; }
        public int Frame { get; set; }
        public string BuffId { get; set; } = "";
        public string SourceId { get; set; } = "";
        public string SourceName { get; set; } = "";
        public DamageTeam SourceTeam { get; set; }
        public List<PendingBuffTarget> Targets { get; set; } = new();
    }

    private sealed class PendingBuffTarget
    {
        public IStatusManager Target { get; set; } = null!;
        public int BeforeLevel { get; set; }
    }

    private sealed class BuffContribution
    {
        public string SourceId { get; set; } = "";
        public string SourceName { get; set; } = "";
        public DamageTeam SourceTeam { get; set; }
        public int Weight { get; set; }
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

internal sealed class AttributedDamagePart
{
    public string SourceId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public DamageTeam SourceTeam { get; set; }
    public int HpDamage { get; set; }
    public int ShieldDamage { get; set; }
    public int FinalDamage { get; set; }
    public DamageAttributionConfidence Confidence { get; set; }
}
