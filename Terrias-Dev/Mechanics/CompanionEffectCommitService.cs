using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

/// <summary>
/// Commits a resolved companion effect to its explicit target. Synthetic
/// Spirit statuses are authoritative Terrias state and never use the native
/// ScriptExecutor ForEachObject network path.
/// </summary>
public static class CompanionEffectCommitService
{
    public static bool Damage(ScriptExecutor executor, IStatusManager target, int amount)
    {
        if (!CanCommit(executor, target) || amount <= 0)
        {
            return false;
        }

        var before = target.CurHp;
        var applied = ExecutorApi.DealDamageToTarget(executor, target, amount);
        Log("damage", executor, target, before, target.CurHp, applied);
        SynchronizeSpiritTarget(target, "damage");
        return applied;
    }

    public static bool Block(ScriptExecutor executor, IStatusManager target, int amount)
    {
        if (!CanCommit(executor, target) || amount <= 0)
        {
            return false;
        }

        if (!TryResolveSpirit(target, out _))
        {
            var before = target.Defend;
            ExecutorApi.SetStatusForTarget(executor, target, "Self");
            executor.ChangeDefence(amount.ToString());
            Log("block.native", executor, target, before, target.Defend, true);
            return true;
        }

        var result = ExplicitStatusEffectApi.AddCompanionShield(target, amount);
        Log("block." + result.Mode, executor, target, result.Before, result.After, result.Applied);
        SynchronizeSpiritTarget(target, "block");
        return result.Applied;
    }

    public static bool ApplyBuff(
        ScriptExecutor executor,
        IStatusManager target,
        string buffId,
        int stacks)
    {
        if (!CanCommit(executor, target) || string.IsNullOrWhiteSpace(buffId) || stacks <= 0)
        {
            return false;
        }

        if (!TryResolveSpirit(target, out _))
        {
            var before = BuffApi.Level(target, buffId);
            var applied = ExecutorApi.AddStatusBuff(executor, target, buffId, stacks);
            Log("buff.native:" + buffId, executor, target, before, BuffApi.Level(target, buffId), applied);
            return applied;
        }

        var result = ExplicitStatusEffectApi.AddCompanionBuff(target, buffId, stacks);
        Log("buff:" + buffId, executor, target, result.Before, result.After, result.Applied);
        SynchronizeSpiritTarget(target, "buff." + buffId);
        return result.Applied;
    }

    public static bool Heal(ScriptExecutor executor, IStatusManager target, int amount)
    {
        if (!CanCommit(executor, target) || amount <= 0)
        {
            return false;
        }

        if (!TryResolveSpirit(target, out _))
        {
            var before = target.CurHp;
            ExecutorApi.SetStatusForTarget(executor, target, "Self");
            executor.ChangeHp(amount.ToString());
            Log("heal.native", executor, target, before, target.CurHp, true);
            return true;
        }

        var result = ExplicitStatusEffectApi.HealCompanion(target, amount);
        Log("heal", executor, target, result.Before, result.After, result.Applied);
        SynchronizeSpiritTarget(target, "heal");
        return result.Applied;
    }

    public static bool IsExplicitSpiritTarget(string statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId) && SpiritStateStore.Find(statusId)?.Spirit != null;
    }

    private static bool CanCommit(ScriptExecutor? executor, IStatusManager? target)
    {
        return executor != null
            && target != null
            && CompanionAuthorityService.IsAuthoritative()
            && CompanionTargetPolicyRegistry.IsAlive(target);
    }

    private static bool TryResolveSpirit(IStatusManager target, out SpiritOtherObj? spirit)
    {
        spirit = SpiritStateStore.Find(target.InstanceId)?.Spirit;
        return spirit != null;
    }

    private static void SynchronizeSpiritTarget(IStatusManager target, string source)
    {
        if (!TryResolveSpirit(target, out var spirit) || spirit == null)
        {
            return;
        }

        SpiritSummonService.BroadcastRuntimeState(spirit, "IntentEffect." + source);
    }

    private static void Log(
        string effect,
        ScriptExecutor executor,
        IStatusManager target,
        int before,
        int after,
        bool applied)
    {
        TerriasLog.InfoAlways("[CompanionEffectCommit] effect="
            + effect
            + ", source="
            + (executor.Self?.InstanceId ?? "")
            + ", target="
            + (target.InstanceId ?? "")
            + ", before="
            + before
            + ", after="
            + after
            + ", delta="
            + (after - before)
            + ", applied="
            + applied
            + ".");
    }
}
