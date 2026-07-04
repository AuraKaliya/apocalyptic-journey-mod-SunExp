using System;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class CompanionIntentExecutor
{
    public static void InitAction(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        var intent = CompanionIntentRegistry.Find(actionId) ?? CompanionIntentRegistry.Find(SunExpIds.ProjectionActionStaffTap);
        if (intent == null)
        {
            return;
        }

        var state = CompanionBattleStateStore.Find(executor.Self?.InstanceId);
        var value = state == null ? intent.FlatValue : ResolveValue(state, intent);
        DictionaryUtil.Set(executor.Vars, "CD", intent.Cooldown.ToString());
        DictionaryUtil.Set(executor.Vars, "priority", Math.Max(1, intent.BasePriority).ToString());
        executor.AddDescription("1", DescriptionType(intent), value.ToString());
    }

    public static void Target(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        var intent = CompanionIntentRegistry.Find(actionId) ?? CompanionIntentRegistry.Find(SunExpIds.ProjectionActionStaffTap);
        if (intent == null)
        {
            return;
        }

        var state = CompanionBattleStateStore.Find(executor.Self?.InstanceId);
        var target = SelectTarget(executor, state, intent);
        ExecutorApi.SetStatusForTarget(executor, target, CompanionIntentRegistry.IntentType(intent) == CompanionIntentType.Attack ? "Target" : "Self");
    }

    public static void UseAction(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        var intent = CompanionIntentRegistry.Find(actionId) ?? CompanionIntentRegistry.Find(SunExpIds.ProjectionActionStaffTap);
        if (intent == null)
        {
            return;
        }

        var state = CompanionBattleStateStore.Find(executor.Self?.InstanceId);
        var target = SelectTarget(executor, state, intent);
        var value = state == null ? Math.Max(1, intent.FlatValue) : ResolveValue(state, intent);
        switch ((intent.Effect ?? "").Trim())
        {
            case "Block":
                ExecutorApi.SetStatusForTarget(executor, target, "Self");
                executor.ChangeDefence(value.ToString());
                break;
            case "Damage":
            default:
                ExecutorApi.DealDamageToTarget(executor, target, value);
                break;
        }
    }

    public static IStatusManager? SelectTarget(ScriptExecutor self, CompanionBattleState? state, CompanionIntentDefinition intent)
    {
        return CompanionIntentRegistry.IntentType(intent) switch
        {
            CompanionIntentType.Defense => SelectDefenseTarget(self, state),
            CompanionIntentType.Support => SelectDefenseTarget(self, state),
            CompanionIntentType.Recovery => SelectMostWoundedFriendly(self, state),
            _ => SelectAttackTarget(self)
        };
    }

    public static int ResolveValue(CompanionBattleState state, CompanionIntentDefinition intent)
    {
        if (state == null || intent == null)
        {
            return 1;
        }

        var stats = state.Stats;
        var value = intent.FlatValue
            + stats.Attack * intent.AttackScale
            + stats.Armor * intent.ArmorScale
            + stats.MaxMagic * intent.MagicScale;
        return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static IStatusManager? SelectAttackTarget(ScriptExecutor self)
    {
        return ExecutorApi.EnemyTargets(self)
            .Where(IsAlive)
            .OrderBy(target => target.CurHp)
            .ThenBy(target => target.InstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IStatusManager? SelectDefenseTarget(ScriptExecutor self, CompanionBattleState? state)
    {
        var projection = self?.Self;
        var owner = StatusById(state?.OwnerStatusId);
        if (IsAlive(owner) && HpPercent(owner) <= 45)
        {
            return owner;
        }

        if (IsAlive(projection) && (HpPercent(projection) <= 55 || projection!.Defend <= 0))
        {
            return projection;
        }

        return owner ?? projection;
    }

    private static IStatusManager? SelectMostWoundedFriendly(ScriptExecutor self, CompanionBattleState? state)
    {
        var owner = StatusById(state?.OwnerStatusId);
        var projection = self?.Self;
        return new[] { owner, projection }
            .Where(IsAlive)
            .OrderBy(HpPercent)
            .ThenBy(target => target!.InstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string DescriptionType(CompanionIntentDefinition intent)
    {
        return (intent.Effect ?? "").Trim() switch
        {
            "Block" => "Defence",
            "Damage" => "Damage",
            _ => "Value"
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
}
