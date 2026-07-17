using System;
using System.Collections.Generic;
using System.Reflection;
using AuraShared.Core;
using Witch.Core;
using Witch.UI.Window;

namespace AudioArbiter.Shared;

internal sealed class AudioGameStateReader
{
    private readonly Dictionary<string, MemberInfo?> intMemberCache = new(StringComparer.Ordinal);

    public string ReadCareerId(ShowCareer? showCareer)
    {
        return ReadDataId(showCareer?.dataConfig);
    }

    public string ReadBuffId(BuffItemConfig? config)
    {
        return config?.BuffId ?? "";
    }

    public string ReadCurrentCareerId()
    {
        return ReadDataId(RoleTable.Instance?.Career ?? GameEntryUI.career);
    }

    public string ReadStatusRoleId(StatusManager? status, bool fallbackToCurrent = true)
    {
        try
        {
            var id = AuraSharedReflection.ReadString(status?.fatherObject, "Id", "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }
        catch
        {
        }

        return fallbackToCurrent ? ReadCurrentCareerId() : "";
    }

    public string ReadStatusInstanceId(StatusManager? status, bool fallbackToRole = true)
    {
        if (status == null)
        {
            return "";
        }

        if (!string.IsNullOrWhiteSpace(status.InstanceId))
        {
            return status.InstanceId;
        }

        return fallbackToRole ? ReadStatusRoleId(status) : "";
    }

    public bool IsLocalOwnerStatus(StatusManager? status, string statusInstanceId)
    {
        try
        {
            var playerId = PlayerManager.Instance?.PlayerId ?? "";
            return (!string.IsNullOrWhiteSpace(playerId)
                    && string.Equals(playerId, statusInstanceId, StringComparison.Ordinal))
                   || ReferenceEquals(FightPlayer.Instance?.Status, status);
        }
        catch
        {
            return false;
        }
    }

    public bool IsLocalPlayerStatus(StatusManager? status)
    {
        if (status == null)
        {
            return false;
        }

        try
        {
            var playerId = PlayerManager.Instance?.PlayerId;
            if (!string.IsNullOrWhiteSpace(playerId)
                && string.Equals(playerId, status.InstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ReferenceEquals(FightPlayer.Instance?.Status, status);
        }
        catch
        {
            return false;
        }
    }

    public AudioStatusSnapshot? ReadStatusSnapshot(StatusManager? status, string sourceName)
    {
        if (status == null)
        {
            return null;
        }

        var maxHp = ReadIntMember(status, "MaxHp");
        var hp = ReadIntMember(status, "CurHp");
        if (hp <= 0)
        {
            hp = ReadIntMember(status, "Hp");
        }

        if (maxHp <= 0 || hp <= 0)
        {
            return null;
        }

        var statusInstanceId = ReadStatusInstanceId(status);
        if (string.IsNullOrWhiteSpace(statusInstanceId))
        {
            return null;
        }

        var isLocalOwner = IsLocalPlayerStatus(status);
        var roleId = ReadStatusRoleId(status, fallbackToCurrent: false);
        var careerId = isLocalOwner ? ReadCurrentCareerId() : roleId;
        if (string.IsNullOrWhiteSpace(roleId) && isLocalOwner)
        {
            roleId = careerId;
        }

        return new AudioStatusSnapshot
        {
            StatusInstanceId = statusInstanceId,
            RoleId = roleId,
            CareerId = careerId,
            Hp = hp,
            MaxHp = maxHp,
            HpRatio = (float)hp / maxHp,
            IsLocalOwner = isLocalOwner,
            SourceName = sourceName ?? ""
        };
    }

    public IReadOnlyList<AudioStatusSnapshot> ReadExecutorStatusSnapshots(
        IScriptExecutor? executor,
        string selfSourceName,
        string targetSourceName)
    {
        if (executor == null)
        {
            return Array.Empty<AudioStatusSnapshot>();
        }

        var snapshots = new List<AudioStatusSnapshot>();
        AddSnapshot(snapshots, ReadStatusSnapshot(executor.Self as StatusManager, selfSourceName));
        var targets = executor.Object;
        if (targets == null)
        {
            return snapshots;
        }

        foreach (var target in targets)
        {
            AddSnapshot(snapshots, ReadStatusSnapshot(target as StatusManager, targetSourceName));
        }

        return snapshots;
    }

    public IReadOnlyList<AudioStatusSnapshot> ReadFightStatusSnapshots(string sourceName)
    {
        var snapshots = new List<AudioStatusSnapshot>();
        var statuses = FightManager.Instance?.statuses;
        if (statuses == null)
        {
            return snapshots;
        }

        foreach (var status in statuses.Values)
        {
            AddSnapshot(snapshots, ReadStatusSnapshot(status, sourceName));
        }

        return snapshots;
    }

    private static void AddSnapshot(ICollection<AudioStatusSnapshot> snapshots, AudioStatusSnapshot? snapshot)
    {
        if (snapshot != null)
        {
            snapshots.Add(snapshot);
        }
    }

    private static string ReadDataId(IDataConfig? data)
    {
        return ReadDataValue(data, "Id");
    }

    private static string ReadDataValue(IDataConfig? data, string key)
    {
        try
        {
            return data?.data != null && data.data.TryGetValue(key, out var value)
                ? value ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private int ReadIntMember(object source, string memberName)
    {
        try
        {
            var type = source.GetType();
            var key = type.FullName + "|" + memberName;
            if (!intMemberCache.TryGetValue(key, out var member))
            {
                member = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? (MemberInfo?)type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                intMemberCache[key] = member;
            }

            var value = member switch
            {
                PropertyInfo property => property.GetValue(source),
                FieldInfo field => field.GetValue(source),
                _ => null
            };
            if (value is int typed)
            {
                return typed;
            }

            return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
        }
        catch
        {
            return 0;
        }
    }
}
