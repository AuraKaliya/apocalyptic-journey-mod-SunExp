using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.DamageMeter.Resolution;

internal static class CombatantTeamResolver
{
    public static DamageTeam Resolve(IStatusManager? status, string instanceId)
    {
        if (status == null && !string.IsNullOrWhiteSpace(instanceId))
        {
            status = ResolveStatus(instanceId);
        }

        try
        {
            var id = status?.InstanceId ?? instanceId ?? "";
            var typeName = status?.fatherObject?.GetType().Name ?? "";
            if (typeName.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DamageTeam.Enemy;
            }

            if (EnemyManager.Instance?.enemyList != null
                && EnemyManager.Instance.enemyList.Any(enemy => enemy != null
                    && (string.Equals(enemy.InstanceId, id, StringComparison.Ordinal)
                        || string.Equals(enemy.Status?.InstanceId, id, StringComparison.Ordinal))))
            {
                return DamageTeam.Enemy;
            }

            if (typeName.IndexOf("FightPlayer", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Partner", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Role", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DamageTeam.Friendly;
            }

            var roleStatusMap = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            // RoleStatusMap is not friendly-only: single-player enemies are also registered under the role id.
            if (roleStatusMap != null
                && roleStatusMap.Values.Any(values => values != null && values.Contains(id)))
            {
                return DamageTeam.Friendly;
            }
        }
        catch
        {
        }

        return DamageTeam.Unknown;
    }

    public static IStatusManager? ResolveStatus(string instanceId)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(instanceId)
                && FightManager.Instance?.statuses != null
                && FightManager.Instance.statuses.TryGetValue(instanceId, out var status))
            {
                return status;
            }
        }
        catch
        {
        }

        return null;
    }

    public static string DisplayName(IStatusManager? status, string fallback)
    {
        try
        {
            var name = status?.Name;
            return string.IsNullOrWhiteSpace(name) ? fallback : name!.Trim();
        }
        catch
        {
            return fallback;
        }
    }
}

internal static class DamageDetailResolver
{
    public static bool IsBuff(string dataId)
    {
        if (string.IsNullOrWhiteSpace(dataId))
        {
            return false;
        }

        try
        {
            return Singleton<GameConfigManager>.Instance?.GetOne(DataType.Buff, dataId.Trim()) != null;
        }
        catch
        {
            return dataId.StartsWith("buff_", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string ResolveLabel(string dataId, string damageType)
    {
        dataId = dataId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(dataId))
        {
            return string.IsNullOrWhiteSpace(damageType) ? "未知来源" : damageType.Trim();
        }

        foreach (var type in DataTypes)
        {
            try
            {
                var row = Singleton<GameConfigManager>.Instance?.GetOne(type, dataId);
                if (row != null
                    && row.TryGetValue("Name", out var name)
                    && !string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }
            catch
            {
            }
        }

        return dataId;
    }

    private static readonly DataType[] DataTypes =
    {
        DataType.Card,
        DataType.EnemyCard,
        DataType.PartnerCard,
        DataType.Buff,
        DataType.Relic,
        DataType.Bless,
        DataType.EnchTag,
        DataType.Career,
        DataType.Enemy
    };
}
