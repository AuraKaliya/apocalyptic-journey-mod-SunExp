using System;
using System.Collections.Generic;
using SanGuoShaExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SanGuoShaExp.Dll.Hooks;

public static class SanGuoShaDodgeRuntime
{
    private const float FullDamageFilter = 100f;

    private static readonly Dictionary<string, PendingDodge> PendingByTarget =
        new Dictionary<string, PendingDodge>(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "StatusManager.Hit", BeforeHit);
        RegisterAfter(modConfig, "StatusManager.Hit", AfterHit);
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, action);
            SanGuoShaExpLog.Info("Hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SanGuoShaExpLog.Info("Hook after registered: " + target);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Hook after failed: " + target + " -> " + ex.Message);
        }
    }

    private static void BeforeHit(ModHookContext context)
    {
        try
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive)
            {
                return;
            }

            if (context.Target is not IStatusManager target || string.IsNullOrEmpty(target.InstanceId))
            {
                return;
            }

            // A previous Hit that threw before its after-hook must not leak a temporary filter.
            RestoreAndRemovePending(target.InstanceId);

            var args = context.Arguments;
            var damage = ReadInt(args, 0);
            var damageType = ReadString(args, 1);
            var sourceId = ReadString(args, 2);
            var fromInstanceId = ReadString(args, 3);
            var dodgeLevel = BuffLevel(target, SanGuoShaExpIds.Dodge);
            if (damage <= 0 || dodgeLevel <= 0)
            {
                return;
            }

            var source = ClassifySource(target, sourceId, fromInstanceId);
            SanGuoShaExpLog.Info(
                "Dodge hit check: target=" + target.InstanceId
                + ", level=" + dodgeLevel
                + ", damage=" + damage
                + ", damageType=" + damageType
                + ", sourceId=" + sourceId
                + ", sourceType=" + source.Description
                + ", from=" + fromInstanceId
                + ", result=" + (source.IsAttack ? "attack" : "ignored"));

            var filterKey = !string.IsNullOrEmpty(sourceId) ? sourceId : damageType;
            if (!source.IsAttack || string.IsNullOrEmpty(filterKey) || target.DamageFilter == null)
            {
                return;
            }

            var hadPreviousFilter = target.DamageFilter.TryGetValue(filterKey, out var previousFilter);
            PendingByTarget[target.InstanceId] = new PendingDodge(
                target,
                sourceId,
                filterKey,
                hadPreviousFilter,
                previousFilter);

            // StatusManager.Hit checks source-specific filters before its early immune return.
            // Direct dictionary mutation is intentional: this value only exists during this Hit.
            target.DamageFilter[filterKey] = FullDamageFilter;
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Dodge before-hit hook failed", ex);
        }
    }

    private static void AfterHit(ModHookContext context)
    {
        try
        {
            if (context.Target is not IStatusManager target || string.IsNullOrEmpty(target.InstanceId))
            {
                return;
            }

            var sourceId = ReadString(context.Arguments, 2);
            if (!PendingByTarget.TryGetValue(target.InstanceId, out var pending)
                || !string.Equals(pending.SourceId, sourceId, StringComparison.Ordinal))
            {
                return;
            }

            RestoreAndRemovePending(target.InstanceId);
            var levelBefore = BuffLevel(target, SanGuoShaExpIds.Dodge);
            ConsumeOneDodge(target);
            var levelAfter = BuffLevel(target, SanGuoShaExpIds.Dodge);
            SanGuoShaExpLog.Info(
                "Dodge consumed: target=" + target.InstanceId
                + ", sourceId=" + sourceId
                + ", level=" + levelBefore + "->" + levelAfter);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Dodge after-hit hook failed", ex);
        }
    }

    public static void ClearPending()
    {
        foreach (var targetId in new List<string>(PendingByTarget.Keys))
        {
            RestoreAndRemovePending(targetId);
        }

        PendingByTarget.Clear();
    }

    private static void RestoreAndRemovePending(string targetId)
    {
        if (!PendingByTarget.TryGetValue(targetId, out var pending))
        {
            return;
        }

        PendingByTarget.Remove(targetId);
        var filters = pending.Target.DamageFilter;
        if (filters == null)
        {
            return;
        }

        if (pending.HadPreviousFilter)
        {
            filters[pending.FilterKey] = pending.PreviousFilter;
        }
        else
        {
            filters.Remove(pending.FilterKey);
        }
    }

    private static void ConsumeOneDodge(IStatusManager target)
    {
        var buff = target.GetBuff(SanGuoShaExpIds.Dodge);
        var level = buff?.buffConfig?.Level ?? 0;
        if (level <= 0)
        {
            return;
        }

        if (level == 1)
        {
            target.RemoveBuff(SanGuoShaExpIds.Dodge);
            return;
        }

        buff!.buffConfig.Level = level - 1;
    }

    private static SourceClassification ClassifySource(
        IStatusManager target,
        string sourceId,
        string fromInstanceId)
    {
        var manager = Singleton<GameConfigManager>.Instance;
        if (manager != null)
        {
            if (HasConfig(manager, DataType.Buff, sourceId))
            {
                return SourceClassification.Ignore("Buff");
            }

            if (HasConfig(manager, DataType.Card, sourceId))
            {
                return SourceClassification.Attack("Card");
            }

            if (HasConfig(manager, DataType.EnemyCard, sourceId))
            {
                return SourceClassification.Attack("EnemyCard");
            }

            if (HasConfig(manager, DataType.PartnerCard, sourceId))
            {
                return SourceClassification.Attack("PartnerCard");
            }

            if (HasConfig(manager, DataType.Relic, sourceId)
                || HasConfig(manager, DataType.Bless, sourceId)
                || HasConfig(manager, DataType.Food, sourceId))
            {
                return SourceClassification.Ignore("known non-attack");
            }
        }

        if (sourceId.StartsWith("buff_", StringComparison.OrdinalIgnoreCase))
        {
            return SourceClassification.Ignore("buff prefix");
        }

        var fight = FightManager.Instance;
        if (!string.IsNullOrEmpty(fromInstanceId)
            && !string.Equals(fromInstanceId, target.InstanceId, StringComparison.Ordinal)
            && fight?.statuses != null
            && fight.statuses.ContainsKey(fromInstanceId))
        {
            return SourceClassification.Attack("combatant fallback");
        }

        return SourceClassification.Ignore("unresolved");
    }

    private static bool HasConfig(GameConfigManager manager, DataType type, string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
        {
            return false;
        }

        try
        {
            return manager.GetOne(type, sourceId) != null;
        }
        catch
        {
            return false;
        }
    }

    private static int BuffLevel(IStatusManager target, string buffId)
    {
        return target.GetBuff(buffId)?.buffConfig?.Level ?? 0;
    }

    private static int ReadInt(object[]? args, int index)
    {
        if (args == null || index < 0 || index >= args.Length)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(args[index]);
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadString(object[]? args, int index)
    {
        return args != null && index >= 0 && index < args.Length
            ? Convert.ToString(args[index]) ?? ""
            : "";
    }

    private sealed class PendingDodge
    {
        public PendingDodge(
            IStatusManager target,
            string sourceId,
            string filterKey,
            bool hadPreviousFilter,
            float previousFilter)
        {
            Target = target;
            SourceId = sourceId;
            FilterKey = filterKey;
            HadPreviousFilter = hadPreviousFilter;
            PreviousFilter = previousFilter;
        }

        public IStatusManager Target { get; }

        public string SourceId { get; }

        public string FilterKey { get; }

        public bool HadPreviousFilter { get; }

        public float PreviousFilter { get; }
    }

    private readonly struct SourceClassification
    {
        private SourceClassification(bool isAttack, string description)
        {
            IsAttack = isAttack;
            Description = description;
        }

        public bool IsAttack { get; }

        public string Description { get; }

        public static SourceClassification Attack(string description)
        {
            return new SourceClassification(true, description);
        }

        public static SourceClassification Ignore(string description)
        {
            return new SourceClassification(false, description);
        }
    }
}
