using System;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class ProjectionStrategyService
{
    public const int StaffTapDamage = 8;
    public const int ShieldBlessingBlock = 10;
    public const int StaffTapBasePriority = 20;
    public const int ShieldBlessingBasePriority = 16;

    public static int ProjectionMaxHp(PolymorphRoleSpec role)
    {
        return 36;
    }

    public static void InitAction(ScriptExecutor self, string actionId)
    {
        var spec = ActionSpec(actionId);
        DictionaryUtil.Set(self.Vars, "CD", spec.Cooldown.ToString());
        DictionaryUtil.Set(self.Vars, "priority", DynamicPriority(self, actionId).ToString());
        self.AddDescription("1", spec.DescriptionType, spec.DescriptionValue.ToString());
    }

    public static void Target(ScriptExecutor self, string actionId)
    {
        if (actionId == SunExpIds.ProjectionActionShieldBlessing)
        {
            var target = SelectShieldTarget(self);
            ExecutorApi.SetStatusForTarget(self, target, "Self");
            return;
        }

        var enemy = SelectAttackTarget(self);
        ExecutorApi.SetStatusForTarget(self, enemy, "Target");
    }

    public static void UseAction(ScriptExecutor self, string actionId)
    {
        switch (actionId)
        {
            case SunExpIds.ProjectionActionShieldBlessing:
                UseShieldBlessing(self);
                break;
            case SunExpIds.ProjectionActionStaffTap:
            default:
                UseStaffTap(self);
                break;
        }
    }

    public static IStatusManager? SelectAttackTarget(ScriptExecutor self)
    {
        return ExecutorApi.EnemyTargets(self)
            .Where(IsAlive)
            .OrderBy(target => target.CurHp)
            .ThenBy(target => target.InstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static IStatusManager? SelectShieldTarget(ScriptExecutor self)
    {
        var projection = self?.Self;
        var state = ProjectionStateStore.Find(projection?.InstanceId ?? "");
        var owner = StatusById(state?.OwnerStatusId);

        if (IsAlive(owner) && HpPercent(owner) <= 45)
        {
            return owner;
        }

        if (projection != null && IsAlive(projection) && (HpPercent(projection) <= 55 || projection.Defend <= 0))
        {
            return projection;
        }

        return owner ?? projection;
    }

    private static void UseStaffTap(ScriptExecutor self)
    {
        var target = SelectAttackTarget(self);
        if (target == null)
        {
            return;
        }

        ExecutorApi.DealDamageToTarget(self, target, StaffTapDamage);
    }

    private static void UseShieldBlessing(ScriptExecutor self)
    {
        var target = SelectShieldTarget(self);
        if (target == null)
        {
            return;
        }

        ExecutorApi.SetStatusForTarget(self, target, "Self");
        self.ChangeDefence(ShieldBlessingBlock.ToString());
    }

    private static int DynamicPriority(ScriptExecutor self, string actionId)
    {
        if (actionId == SunExpIds.ProjectionActionShieldBlessing)
        {
            var target = SelectShieldTarget(self);
            var priority = ShieldBlessingBasePriority;
            if (target != null && HpPercent(target) <= 35)
            {
                priority += 35;
            }

            if (target != null && target.Defend <= 0)
            {
                priority += 10;
            }

            return priority;
        }

        var enemy = SelectAttackTarget(self);
        var attackPriority = StaffTapBasePriority;
        if (enemy != null && enemy.CurHp <= StaffTapDamage)
        {
            attackPriority += 40;
        }
        else if (enemy != null && HpPercent(enemy) <= 30)
        {
            attackPriority += 15;
        }

        return attackPriority;
    }

    private static ProjectionActionSpec ActionSpec(string actionId)
    {
        return actionId switch
        {
            SunExpIds.ProjectionActionShieldBlessing => new ProjectionActionSpec(1, ShieldBlessingBasePriority, "Defence", ShieldBlessingBlock),
            _ => new ProjectionActionSpec(0, StaffTapBasePriority, "Damage", StaffTapDamage)
        };
    }

    private static IStatusManager? StatusById(string? statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return null;
        }

        try
        {
            return FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true ? status : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }

    private static int HpPercent(IStatusManager? status)
    {
        if (status == null || status.MaxHp <= 0)
        {
            return 100;
        }

        return Math.Max(0, Math.Min(100, status.CurHp * 100 / Math.Max(1, status.MaxHp)));
    }

    private readonly struct ProjectionActionSpec
    {
        public ProjectionActionSpec(int cooldown, int priority, string descriptionType, int descriptionValue)
        {
            Cooldown = cooldown;
            Priority = priority;
            DescriptionType = descriptionType;
            DescriptionValue = descriptionValue;
        }

        public int Cooldown { get; }

        public int Priority { get; }

        public string DescriptionType { get; }

        public int DescriptionValue { get; }
    }
}
