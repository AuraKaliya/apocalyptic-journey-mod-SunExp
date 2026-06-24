using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using UnityEngine;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal sealed class AuraToolsDamageMeterState
{
    private readonly Dictionary<string, CombatantStat> stats = new(StringComparer.OrdinalIgnoreCase);

    public long FightIndex { get; private set; }

    public int RoundIndex { get; private set; }

    public bool InFight { get; private set; }

    public IReadOnlyCollection<CombatantStat> Stats => stats.Values;

    public void ResetFight()
    {
        FightIndex++;
        RoundIndex = 0;
        InFight = true;
        stats.Clear();
    }

    public void EndFight()
    {
        InFight = false;
    }

    public void ResetRound()
    {
        RoundIndex++;
        foreach (var stat in stats.Values)
        {
            stat.RoundDamage = 0;
        }
    }

    public bool AddDamage(
        IStatusManager? source,
        IStatusManager? target,
        string sourceInstanceId,
        string sourceDataId,
        string damageType,
        string detailLabel,
        int hpLoss,
        int shieldLoss,
        bool countShieldLoss)
    {
        var total = hpLoss + (countShieldLoss ? shieldLoss : 0);
        if (total <= 0)
        {
            return false;
        }

        var targetId = SafeStatusId(target, "target");
        sourceInstanceId = string.IsNullOrWhiteSpace(sourceInstanceId)
            ? SafeStatusId(source, "source")
            : sourceInstanceId.Trim();
        sourceDataId = sourceDataId?.Trim() ?? "";
        damageType = string.IsNullOrWhiteSpace(damageType) ? "Unknown" : damageType.Trim();

        var stat = GetOrCreate(source, sourceInstanceId);
        stat.RoundDamage += total;
        stat.FightDamage += total;
        if (stat.IsFriendly)
        {
            stat.RunFriendlyDamage += total;
        }

        stat.LastDamageAt = Time.unscaledTime;
        stat.DisplayName = ResolveDisplayName(source, sourceInstanceId, stat.DisplayName);
        stat.IsDead = source?.state == IStatusManager.State.Dead;
        stat.AddDetail(string.IsNullOrWhiteSpace(detailLabel) ? BuildFallbackLabel(sourceDataId, damageType) : detailLabel.Trim(), total);
        return true;
    }

    public IReadOnlyList<CombatantStat> VisibleRows(DamageMeterSettings settings)
    {
        var maxRows = Math.Max(1, settings.MaxRows);
        var query = stats.Values
            .Where(stat => stat.FightDamage > 0 || stat.RoundDamage > 0 || stat.RunFriendlyDamage > 0);
        if (settings.FriendlyOnly)
        {
            query = query.Where(stat => stat.IsFriendly);
        }

        return query
            .OrderByDescending(stat => stat.FightDamage)
            .ThenByDescending(stat => stat.RoundDamage)
            .ThenBy(stat => stat.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(maxRows)
            .ToList();
    }

    public int MaxFightDamage(DamageMeterSettings settings)
    {
        var rows = VisibleRows(settings);
        return rows.Count == 0 ? 0 : rows.Max(stat => stat.FightDamage);
    }

    public void RememberStatus(IStatusManager? status)
    {
        if (status == null || string.IsNullOrWhiteSpace(status.InstanceId))
        {
            return;
        }

        var stat = GetOrCreate(status, status.InstanceId);
        stat.DisplayName = ResolveDisplayName(status, status.InstanceId, stat.DisplayName);
        stat.IsFriendly = IsFriendly(status, status.InstanceId);
        stat.IsDead = status.state == IStatusManager.State.Dead;
    }

    private CombatantStat GetOrCreate(IStatusManager? status, string instanceId)
    {
        instanceId = string.IsNullOrWhiteSpace(instanceId) ? "unknown" : instanceId.Trim();
        if (stats.TryGetValue(instanceId, out var stat))
        {
            return stat;
        }

        stat = new CombatantStat
        {
            InstanceId = instanceId,
            DisplayName = ResolveDisplayName(status, instanceId, instanceId),
            IsFriendly = IsFriendly(status, instanceId),
            IsDead = status?.state == IStatusManager.State.Dead
        };
        stats[instanceId] = stat;
        return stat;
    }

    private static string BuildFallbackLabel(string sourceDataId, string damageType)
    {
        if (!string.IsNullOrWhiteSpace(sourceDataId))
        {
            return sourceDataId;
        }

        return string.IsNullOrWhiteSpace(damageType) ? "未知来源" : damageType;
    }

    private static string SafeStatusId(IStatusManager? status, string fallback)
    {
        try
        {
            if (status == null || string.IsNullOrWhiteSpace(status.InstanceId))
            {
                return fallback;
            }

            return status.InstanceId;
        }
        catch
        {
            return fallback;
        }
    }

    private static string ResolveDisplayName(IStatusManager? status, string instanceId, string fallback)
    {
        try
        {
            var name = status?.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name!.Trim();
            }
        }
        catch
        {
        }

        return string.IsNullOrWhiteSpace(fallback) ? instanceId : fallback;
    }

    private static bool IsFriendly(IStatusManager? status, string instanceId)
    {
        try
        {
            var typeName = status?.fatherObject?.GetType().Name ?? "";
            if (typeName.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (typeName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Partner", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Role", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        catch
        {
        }

        return !instanceId.StartsWith("e", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class CombatantStat
{
    private readonly Dictionary<string, int> details = new(StringComparer.OrdinalIgnoreCase);

    public string InstanceId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool IsFriendly { get; set; }

    public bool IsDead { get; set; }

    public int RoundDamage { get; set; }

    public int FightDamage { get; set; }

    public int RunFriendlyDamage { get; set; }

    public float LastDamageAt { get; set; }

    public IReadOnlyDictionary<string, int> Details => details;

    public void AddDetail(string label, int amount)
    {
        label = string.IsNullOrWhiteSpace(label) ? "未知来源" : label.Trim();
        details.TryGetValue(label, out var existing);
        details[label] = existing + Math.Max(0, amount);
    }
}
