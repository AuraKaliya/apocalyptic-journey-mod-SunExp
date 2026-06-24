using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal sealed class AuraToolsDamageAttribution
{
    private readonly Stack<DamageSourceContext> contexts = new();
    private readonly Dictionary<string, DamageSourceContext> buffOwners = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        contexts.Clear();
        buffOwners.Clear();
    }

    public bool PushScript(IScriptExecutor? executor, string action)
    {
        var context = BuildContext(executor, action);
        if (context != null)
        {
            contexts.Push(context);
            return true;
        }

        return false;
    }

    public bool PushBuff(BuffItem? buffItem, string action)
    {
        if (buffItem == null)
        {
            return false;
        }

        var targetId = SafeStatusId(buffItem.status);
        var buffId = SafeBuffId(buffItem.buffConfig);
        if (!string.IsNullOrWhiteSpace(targetId)
            && !string.IsNullOrWhiteSpace(buffId)
            && buffOwners.TryGetValue(BuffKey(targetId, buffId), out var owner))
        {
            contexts.Push(owner.Copy(action, buffId));
            return true;
        }

        return PushScript(buffItem.scriptExecutor, action);
    }

    public void Pop()
    {
        if (contexts.Count > 0)
        {
            contexts.Pop();
        }
    }

    public DamageSource Resolve(IStatusManager? target, string fromInstanceId, string fromDataId, string damageType)
    {
        var sourceId = fromInstanceId?.Trim() ?? "";
        var dataId = fromDataId?.Trim() ?? "";
        var source = ResolveStatus(sourceId);
        var label = LabelFor(dataId, damageType);

        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            return new DamageSource(source, sourceId, dataId, label);
        }

        if (!string.IsNullOrWhiteSpace(dataId)
            && target != null
            && buffOwners.TryGetValue(BuffKey(target.InstanceId, dataId), out var buffOwner))
        {
            source = ResolveStatus(buffOwner.SourceInstanceId);
            return new DamageSource(source, buffOwner.SourceInstanceId, dataId, LabelFor(dataId, damageType));
        }

        if (contexts.Count > 0)
        {
            var context = contexts.Peek();
            source = ResolveStatus(context.SourceInstanceId) ?? context.Source;
            dataId = string.IsNullOrWhiteSpace(dataId) ? context.SourceDataId : dataId;
            label = LabelFor(dataId, damageType);
            return new DamageSource(source, context.SourceInstanceId, dataId, label);
        }

        var fallbackId = SafeNetworkNowActionRole();
        if (!string.IsNullOrWhiteSpace(fallbackId))
        {
            source = ResolveStatus(fallbackId);
            return new DamageSource(source, fallbackId, dataId, label);
        }

        return new DamageSource(null, "", dataId, label);
    }

    public PendingBuffApplication CaptureBuffApplication(IScriptExecutor? executor, string buffId)
    {
        var context = BuildContext(executor, "AddBuff")
                      ?? (contexts.Count > 0 ? contexts.Peek() : null);
        return new PendingBuffApplication
        {
            BuffId = buffId?.Trim() ?? "",
            Owner = context
        };
    }

    public PendingBuffApplication CaptureCurrentBuffApplication(string buffId)
    {
        return new PendingBuffApplication
        {
            BuffId = buffId?.Trim() ?? "",
            Owner = contexts.Count > 0 ? contexts.Peek() : null
        };
    }

    public void RememberBuffOwner(IStatusManager? target, string buffId, PendingBuffApplication pending)
    {
        var targetId = SafeStatusId(target);
        buffId = string.IsNullOrWhiteSpace(buffId) ? pending.BuffId : buffId.Trim();
        if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(buffId) || pending.Owner == null)
        {
            return;
        }

        buffOwners[BuffKey(targetId, buffId)] = pending.Owner.Copy("BuffOwner", buffId);
    }

    private static DamageSourceContext? BuildContext(IScriptExecutor? executor, string action)
    {
        if (executor == null)
        {
            return null;
        }

        var source = executor.Self;
        var sourceId = SafeStatusId(source);
        var dataId = SafeDataId(executor.dataConfig);
        if (string.IsNullOrWhiteSpace(sourceId) && string.IsNullOrWhiteSpace(dataId))
        {
            return null;
        }

        return new DamageSourceContext(source, sourceId, dataId, action);
    }

    private static string LabelFor(string dataId, string damageType)
    {
        dataId = dataId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(dataId))
        {
            return string.IsNullOrWhiteSpace(damageType) ? "未知来源" : damageType.Trim();
        }

        foreach (var type in DataTypesForLookup(dataId))
        {
            try
            {
                var row = Singleton<GameConfigManager>.Instance?.GetOne(type, dataId);
                if (row == null)
                {
                    continue;
                }

                if (row.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
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

    private static IEnumerable<DataType> DataTypesForLookup(string dataId)
    {
        yield return DataType.Card;
        yield return DataType.EnemyCard;
        yield return DataType.PartnerCard;
        yield return DataType.Buff;
        yield return DataType.Relic;
        yield return DataType.Bless;
        yield return DataType.EnchTag;
        yield return DataType.Career;
        yield return DataType.Enemy;
    }

    private static IStatusManager? ResolveStatus(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        try
        {
            if (FightManager.Instance?.statuses != null
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

    private static string SafeNetworkNowActionRole()
    {
        try
        {
            var value = ReflectionUtil.GetMemberValue(FightManager.Instance, "NetworkNowActionRole");
            return value?.ToString()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static string SafeDataId(IDataConfig? dataConfig)
    {
        try
        {
            if (dataConfig?.data != null && dataConfig.data.TryGetValue("Id", out var id))
            {
                return id?.Trim() ?? "";
            }
        }
        catch
        {
        }

        try
        {
            return dataConfig?.InstanceID?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static string SafeStatusId(IStatusManager? status)
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

    internal static string SafeBuffId(IBuffItemConfig? config)
    {
        try
        {
            if (config == null)
            {
                return "";
            }

            return string.IsNullOrWhiteSpace(config.BuffId)
                ? SafeDataId(config.dataConfig)
                : config.BuffId.Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string BuffKey(string targetId, string buffId)
    {
        return targetId.Trim() + "|" + buffId.Trim();
    }
}

internal sealed class PendingBuffApplication
{
    public string BuffId { get; set; } = "";

    public DamageSourceContext? Owner { get; set; }
}

internal sealed class DamageSource
{
    public DamageSource(IStatusManager? source, string sourceInstanceId, string sourceDataId, string detailLabel)
    {
        Source = source;
        SourceInstanceId = sourceInstanceId;
        SourceDataId = sourceDataId;
        DetailLabel = detailLabel;
    }

    public IStatusManager? Source { get; }

    public string SourceInstanceId { get; }

    public string SourceDataId { get; }

    public string DetailLabel { get; }
}

internal sealed class DamageSourceContext
{
    public DamageSourceContext(IStatusManager? source, string sourceInstanceId, string sourceDataId, string action)
    {
        Source = source;
        SourceInstanceId = sourceInstanceId;
        SourceDataId = sourceDataId;
        Action = action;
    }

    public IStatusManager? Source { get; }

    public string SourceInstanceId { get; }

    public string SourceDataId { get; }

    public string Action { get; }

    public DamageSourceContext Copy(string action, string sourceDataId)
    {
        return new DamageSourceContext(Source, SourceInstanceId, sourceDataId, action);
    }
}
