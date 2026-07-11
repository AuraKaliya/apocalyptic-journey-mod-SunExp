using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.DamageMeter.Resolution;

internal static class DamageMeterFightIndex
{
    private static readonly Dictionary<string, IndexedCombatant> Combatants =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Labels =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, bool> BuffFlags =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> FriendlyDisplayNames =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> FriendlyIdentityIds =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool combatantsBuilt;

    public static void BeginFight()
    {
        ClearCombatants();
        BuildCombatants();
    }

    public static void SetFriendlyIdentitySnapshots(IEnumerable<OutOfRunTeamMemberSnapshot>? members)
    {
        FriendlyDisplayNames.Clear();
        FriendlyIdentityIds.Clear();
        foreach (var member in members ?? Array.Empty<OutOfRunTeamMemberSnapshot>())
        {
            if (member == null)
            {
                continue;
            }

            RegisterFriendlyIdentity(member.InstanceId);
            RegisterFriendlyIdentity(member.PlayerId);
            var displayName = FirstNonEmpty(member.RoleDisplayName, member.PlayerDisplayName, member.DisplayName);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            RegisterFriendlyDisplayName(member.InstanceId, displayName);
            RegisterFriendlyDisplayName(member.PlayerId, displayName);
        }
    }

    public static void Clear()
    {
        ClearCombatants();
        Labels.Clear();
        BuffFlags.Clear();
    }

    public static IStatusManager? ResolveStatus(string instanceId)
    {
        instanceId = instanceId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        EnsureCombatants();
        if (Combatants.TryGetValue(instanceId, out var indexed) && indexed.Status != null)
        {
            return indexed.Status;
        }

        try
        {
            if (FightManager.Instance?.statuses != null
                && FightManager.Instance.statuses.TryGetValue(instanceId, out var status)
                && status != null)
            {
                IndexStatus(status, null);
                return status;
            }
        }
        catch
        {
        }

        return null;
    }

    public static DamageTeam ResolveTeam(IStatusManager? status, string instanceId)
    {
        var id = SafeStatusId(status);
        if (string.IsNullOrWhiteSpace(id))
        {
            id = instanceId?.Trim() ?? "";
        }

        if (status == null && !string.IsNullOrWhiteSpace(id))
        {
            status = ResolveStatus(id);
        }

        EnsureCombatants();
        if (!string.IsNullOrWhiteSpace(id)
            && Combatants.TryGetValue(id, out var indexed)
            && indexed.Team != DamageTeam.Unknown)
        {
            return indexed.Team;
        }

        var resolved = ResolveTeamUncached(status, id);
        if (!string.IsNullOrWhiteSpace(id))
        {
            IndexAlias(id, status, resolved);
        }

        return resolved;
    }

    public static string DisplayName(IStatusManager? status, string fallback)
    {
        var id = SafeStatusId(status);
        if (string.IsNullOrWhiteSpace(id))
        {
            id = fallback?.Trim() ?? "";
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            EnsureCombatants();
            var knownFriendlyName = KnownFriendlyDisplayName(id);
            if (!string.IsNullOrWhiteSpace(knownFriendlyName))
            {
                return knownFriendlyName;
            }

            if (Combatants.TryGetValue(id, out var indexed)
                && !string.IsNullOrWhiteSpace(indexed.DisplayName))
            {
                return indexed.DisplayName;
            }
        }

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

        var safeFallback = fallback ?? "";
        return string.IsNullOrWhiteSpace(safeFallback) ? "未知单位" : safeFallback.Trim();
    }

    public static bool IsBuff(string dataId)
    {
        dataId = dataId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(dataId))
        {
            return false;
        }

        if (BuffFlags.TryGetValue(dataId, out var cached))
        {
            return cached;
        }

        var result = false;
        try
        {
            result = Singleton<GameConfigManager>.Instance?.GetOne(DataType.Buff, dataId) != null;
        }
        catch
        {
            result = dataId.StartsWith("buff_", StringComparison.OrdinalIgnoreCase);
        }

        BuffFlags[dataId] = result;
        return result;
    }

    public static string ResolveLabel(string dataId, string damageType)
    {
        dataId = dataId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(dataId))
        {
            return string.IsNullOrWhiteSpace(damageType) ? "未知来源" : damageType.Trim();
        }

        if (Labels.TryGetValue(dataId, out var cached))
        {
            return cached;
        }

        var label = dataId;
        for (var i = 0; i < DataTypes.Length; i++)
        {
            try
            {
                var row = Singleton<GameConfigManager>.Instance?.GetOne(DataTypes[i], dataId);
                if (row != null
                    && row.TryGetValue("Name", out var name)
                    && !string.IsNullOrWhiteSpace(name))
                {
                    label = name.Trim();
                    break;
                }
            }
            catch
            {
            }
        }

        Labels[dataId] = label;
        return label;
    }

    private static void EnsureCombatants()
    {
        if (!combatantsBuilt)
        {
            BuildCombatants();
        }
    }

    private static void BuildCombatants()
    {
        combatantsBuilt = true;
        try
        {
            if (FightManager.Instance?.statuses != null)
            {
                foreach (var pair in FightManager.Instance.statuses)
                {
                    IndexStatus(pair.Value, null);
                }
            }
        }
        catch
        {
        }

        try
        {
            if (EnemyManager.Instance?.enemyList != null)
            {
                foreach (var enemy in EnemyManager.Instance.enemyList)
                {
                    if (enemy == null)
                    {
                        continue;
                    }

                    IndexAlias(enemy.InstanceId, enemy.Status, DamageTeam.Enemy);
                    IndexStatus(enemy.Status, DamageTeam.Enemy);
                }
            }
        }
        catch
        {
        }

        try
        {
            var roleStatusMap = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            if (roleStatusMap != null)
            {
                foreach (var pair in roleStatusMap)
                {
                    var values = pair.Value;
                    if (values == null)
                    {
                        continue;
                    }

                    var playerDisplayName = KnownFriendlyDisplayName(pair.Key);

                    foreach (var id in values)
                    {
                        RegisterFriendlyIdentity(id);
                        RegisterFriendlyDisplayName(id, playerDisplayName);
                        MarkFriendly(id);
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static void ClearCombatants()
    {
        Combatants.Clear();
        combatantsBuilt = false;
    }

    private static void IndexStatus(IStatusManager? status, DamageTeam? preferredTeam)
    {
        var id = SafeStatusId(status);
        if (!string.IsNullOrWhiteSpace(id))
        {
            IndexAlias(id, status, preferredTeam ?? DamageTeam.Unknown);
        }
    }

    private static void IndexAlias(string instanceId, IStatusManager? status, DamageTeam preferredTeam)
    {
        instanceId = instanceId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        var team = IsKnownFriendlyIdentity(instanceId)
            ? DamageTeam.Friendly
            : preferredTeam == DamageTeam.Unknown
            ? ResolveTeamUncached(status, instanceId)
            : preferredTeam;
        var displayName = team == DamageTeam.Friendly
            ? FirstNonEmpty(KnownFriendlyDisplayName(instanceId), SafeDisplayName(status, instanceId))
            : SafeDisplayName(status, instanceId);
        if (Combatants.TryGetValue(instanceId, out var existing))
        {
            existing.Status ??= status;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                existing.DisplayName = displayName;
            }

            if (team != DamageTeam.Unknown || existing.Team == DamageTeam.Unknown)
            {
                existing.Team = team;
            }

            return;
        }

        Combatants[instanceId] = new IndexedCombatant
        {
            Status = status,
            DisplayName = displayName,
            Team = team
        };
    }

    private static void MarkFriendly(string instanceId)
    {
        instanceId = instanceId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        if (Combatants.TryGetValue(instanceId, out var existing))
        {
            existing.Team = DamageTeam.Friendly;
            existing.DisplayName = FirstNonEmpty(KnownFriendlyDisplayName(instanceId), existing.DisplayName);

            return;
        }

        Combatants[instanceId] = new IndexedCombatant
        {
            Status = ResolveStatus(instanceId),
            DisplayName = FirstNonEmpty(KnownFriendlyDisplayName(instanceId), "未命名友方单位"),
            Team = DamageTeam.Friendly
        };
    }

    private static void RegisterFriendlyDisplayName(string? id, string? displayName)
    {
        id = id?.Trim() ?? "";
        displayName = displayName?.Trim() ?? "";
        if (id.Length > 0 && displayName.Length > 0)
        {
            FriendlyDisplayNames[id] = displayName;
        }
    }

    private static void RegisterFriendlyIdentity(string? id)
    {
        id = id?.Trim() ?? "";
        if (id.Length > 0)
        {
            FriendlyIdentityIds.Add(id);
        }
    }

    private static bool IsKnownFriendlyIdentity(string? id)
    {
        id = id?.Trim() ?? "";
        return id.Length > 0 && FriendlyIdentityIds.Contains(id);
    }

    private static string KnownFriendlyDisplayName(string? id)
    {
        id = id?.Trim() ?? "";
        return id.Length > 0 && FriendlyDisplayNames.TryGetValue(id, out var displayName)
            ? displayName
            : "";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return "";
    }

    private static DamageTeam ResolveTeamUncached(IStatusManager? status, string instanceId)
    {
        try
        {
            var id = SafeStatusId(status);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = instanceId?.Trim() ?? "";
            }

            if (IsKnownFriendlyIdentity(id))
            {
                return DamageTeam.Friendly;
            }

            var typeName = status?.fatherObject?.GetType().Name ?? "";
            if (typeName.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DamageTeam.Enemy;
            }

            if (EnemyManager.Instance?.enemyList != null)
            {
                foreach (var enemy in EnemyManager.Instance.enemyList)
                {
                    if (enemy == null)
                    {
                        continue;
                    }

                    if (string.Equals(enemy.InstanceId, id, StringComparison.Ordinal)
                        || string.Equals(enemy.Status?.InstanceId, id, StringComparison.Ordinal))
                    {
                        return DamageTeam.Enemy;
                    }
                }
            }

            if (typeName.IndexOf("FightPlayer", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Partner", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Role", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DamageTeam.Friendly;
            }

            var roleStatusMap = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            if (roleStatusMap != null)
            {
                foreach (var values in roleStatusMap.Values)
                {
                    if (values == null)
                    {
                        continue;
                    }

                    foreach (var value in values)
                    {
                        if (string.Equals(value, id, StringComparison.Ordinal))
                        {
                            return DamageTeam.Friendly;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return DamageTeam.Unknown;
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

    private static string SafeDisplayName(IStatusManager? status, string fallback)
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

    private sealed class IndexedCombatant
    {
        public IStatusManager? Status { get; set; }
        public string DisplayName { get; set; } = "";
        public DamageTeam Team { get; set; }
    }
}
