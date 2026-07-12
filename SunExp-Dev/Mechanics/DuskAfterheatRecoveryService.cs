using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class DuskAfterheatRecoveryService
{
    private const string TokenKey = "SunExpDuskAfterheatToken";
    private static readonly HashSet<IBuffItem> ObservedBurnBuffs = new(BuffReferenceComparer.Instance);
    private static ScriptExecutor? activeOwner;
    private static string activeToken = "";

    public static void Activate(ScriptExecutor owner, string token)
    {
        if (owner == null || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (!ReferenceEquals(activeOwner, owner) || !string.Equals(activeToken, token, StringComparison.Ordinal))
        {
            ObservedBurnBuffs.Clear();
        }

        activeOwner = owner;
        activeToken = token;
        foreach (var target in ExecutorApi.EnemyTargets(owner))
        {
            AttachBurnObserver(target, "TraitActivated");
        }
    }

    public static void Deactivate(ScriptExecutor? owner, string source)
    {
        if (owner != null && activeOwner != null && !ReferenceEquals(activeOwner, owner))
        {
            return;
        }

        activeOwner = null;
        activeToken = "";
        ObservedBurnBuffs.Clear();
        SunExpLog.Debug("Dusk afterheat recovery deactivated from " + source + ".");
    }

    public static void ObserveBurnAdded(IStatusManager? target, string buffId, string source)
    {
        if (!string.Equals(buffId, SunExpIds.Burn, StringComparison.Ordinal))
        {
            return;
        }

        AttachBurnObserver(target, source);
    }

    public static void ObserveEnemyInitialized(IStatusManager? target, string source)
    {
        AttachBurnObserver(target, source);
    }

    private static bool AttachBurnObserver(IStatusManager? target, string source)
    {
        var owner = activeOwner;
        var token = activeToken;
        if (owner == null
            || string.IsNullOrWhiteSpace(token)
            || target == null
            || target.fatherObject is not Enemy
            || HeartChangeControlService.IsControlled(target))
        {
            return false;
        }

        var burn = target.GetBuff(SunExpIds.Burn);
        var burnExecutor = burn?.scriptExecutor;
        var targetId = target.InstanceId;
        if (burn == null || burnExecutor == null || string.IsNullOrWhiteSpace(targetId))
        {
            return false;
        }

        if (!ObservedBurnBuffs.Add(burn))
        {
            return true;
        }

        var attached = ScriptEventApi.TryAddOwnedEventListener(
            "StartRound" + targetId,
            new Action(() => GrantEmberFromBurn(owner, target, token)),
            burnExecutor,
            EventDispose.OnFightEnd,
            "dusk_afterheat:" + source);
        if (!attached)
        {
            ObservedBurnBuffs.Remove(burn);
            return false;
        }

        SunExpPerformanceCounters.Record("DuskAfterheat.ObserverAttached");
        return true;
    }

    private static void GrantEmberFromBurn(ScriptExecutor owner, IStatusManager target, string token)
    {
        if (!ReferenceEquals(activeOwner, owner)
            || !string.Equals(activeToken, token, StringComparison.Ordinal)
            || !ExecutorApi.IsHookTokenActive(owner, TokenKey, token))
        {
            return;
        }

        var gain = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn) / 2;
        if (gain <= 0)
        {
            return;
        }

        owner.SetStatus("Self");
        owner.AddBuff(SunExpIds.Ember, gain.ToString());
        BuffApi.SyncEmberDamageBonus(owner, owner.Self);
        SunExpPerformanceCounters.Record("DuskAfterheat.Triggered");
    }

    private sealed class BuffReferenceComparer : IEqualityComparer<IBuffItem>
    {
        public static readonly BuffReferenceComparer Instance = new();

        public bool Equals(IBuffItem? left, IBuffItem? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(IBuffItem value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
