using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class DuskAfterheatRecoveryService
{
    private static readonly HashSet<IBuffItem> ObservedBurnBuffs = new(BuffReferenceComparer.Instance);
    private static ScriptExecutor? activeOwner;
    private static IBuffItem? activeTraitBuff;
    private static ScriptEventScope? activeScope;
    private static bool initialized;
    private static bool familiarAshAvailable;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        BurnTriggerApi.Triggered += OnBurnActuallyTriggered;
        BuffApi.EmberConsumed += OnEmberConsumed;
    }

    public static bool ActivateFamiliar(ScriptExecutor owner, string source)
    {
        var scope = ScriptEventApi.BeginFightScope(owner, "Familiar.DuskAfterheat");
        if (scope == null)
        {
            return false;
        }

        activeTraitBuff = null;
        Activate(owner, scope);
        scope.Commit();
        familiarAshAvailable = true;
        TerriasLog.Debug("Dusk familiar effects bound from " + source + ".");
        return true;
    }

    public static void BeginPlayerRound()
    {
        familiarAshAvailable = true;
    }

    public static bool EnsureActive(IStatusManager? status, string source)
    {
        var trait = status?.GetBuff(TerriasIds.DuskAfterheatRecoveryTrait);
        var executor = trait?.scriptExecutor as ScriptExecutor;
        if (status == null || trait == null || executor == null)
        {
            return false;
        }

        if (ReferenceEquals(activeOwner, executor)
            && ReferenceEquals(activeTraitBuff, trait)
            && activeScope?.IsActive == true)
        {
            foreach (var target in ExecutorApi.EnemyTargets(executor))
            {
                AttachBurnObserver(target, source + ".ExistingBinding");
            }
            return true;
        }

        return ActivateTrait(executor, trait, source);
    }

    public static bool ActivateTrait(ScriptExecutor owner, IBuffItem? trait = null, string source = "TraitApply")
    {
        var scope = ScriptEventApi.BeginFightScope(owner, "Buff.DuskAfterheatTrait");
        if (scope == null)
        {
            return false;
        }

        activeTraitBuff = trait ?? owner.Self?.GetBuff(TerriasIds.DuskAfterheatRecoveryTrait);
        Activate(owner, scope);
        scope.Commit();
        TerriasLog.Debug("Dusk afterheat recovery bound from " + source + ".");
        return true;
    }

    public static void Activate(ScriptExecutor owner, ScriptEventScope scope)
    {
        if (owner == null || scope == null)
        {
            return;
        }

        if (!ReferenceEquals(activeOwner, owner) || !ReferenceEquals(activeScope, scope))
        {
            activeScope?.Invalidate();
            ObservedBurnBuffs.Clear();
        }

        activeOwner = owner;
        activeScope = scope;
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

        activeScope?.Invalidate();
        activeOwner = null;
        activeTraitBuff = null;
        activeScope = null;
        ObservedBurnBuffs.Clear();
        familiarAshAvailable = false;
        TerriasLog.Debug("Dusk afterheat recovery deactivated from " + source + ".");
    }

    public static void ObserveBurnAdded(IStatusManager? target, string buffId, string source)
    {
        if (!string.Equals(buffId, TerriasIds.Burn, StringComparison.Ordinal))
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
        var scope = activeScope;
        if (owner == null
            || scope?.IsActive != true
            || target == null
            || target.fatherObject is not Enemy
            || HeartChangeControlService.IsControlled(target))
        {
            return false;
        }

        var burn = target.GetBuff(TerriasIds.Burn);
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
            new Action(() => NotifyNativeBurnIfBindingActive(owner, target, scope)),
            burnExecutor,
            EventDispose.OnFightEnd,
            "dusk_afterheat:" + source);
        if (!attached)
        {
            ObservedBurnBuffs.Remove(burn);
            return false;
        }

        TerriasPerformanceCounters.Record("DuskAfterheat.ObserverAttached");
        return true;
    }

    private static void NotifyNativeBurnIfBindingActive(
        ScriptExecutor owner,
        IStatusManager target,
        ScriptEventScope scope)
    {
        if (!ReferenceEquals(activeOwner, owner)
            || !ReferenceEquals(activeScope, scope)
            || !scope.IsActive)
        {
            return;
        }

        BurnTriggerApi.NotifyActual(
            target,
            ExecutorApi.StatusBuffLevel(target, TerriasIds.Burn),
            "NativeBurnStartRound");
    }

    private static void OnBurnActuallyTriggered(BurnTriggerSnapshot snapshot)
    {
        var owner = activeOwner;
        var scope = activeScope;
        var target = snapshot.Target;
        if (owner == null)
        {
            return;
        }

        if (!ReferenceEquals(activeOwner, owner)
            || scope?.IsActive != true)
        {
            return;
        }

        if (target.fatherObject is not Enemy || HeartChangeControlService.IsControlled(target))
        {
            return;
        }

        var traitGain = activeTraitBuff == null ? 0 : snapshot.StacksAtTrigger / 3;
        var duskMultiplier = FamiliarFinalBlessingService.EffectAmountFor("DuskAfterheatMultiplierAndCap");
        var emberGain = traitGain * Math.Max(1, duskMultiplier);
        var ash = FamiliarBlessingEffectRuntime.EffectAmount("BurnTriggeredEmber");
        if (ash > 0 && familiarAshAvailable)
        {
            emberGain += ash;
            familiarAshAvailable = false;
        }

        var store = FamiliarBlessingEffectRuntime.EffectAmount("BurnStackToEmber");
        if (store > 0)
        {
            emberGain += Math.Max(1, snapshot.StacksAtTrigger / 2) * store;
        }
        if (emberGain <= 0 && traitGain <= 0)
        {
            return;
        }

        owner.SetStatus("Self");
        if (emberGain > 0)
        {
            var emberBefore = BuffApi.Level(owner.Self, TerriasIds.Ember);
            owner.AddBuff(TerriasIds.Ember, emberGain.ToString());
            if (duskMultiplier > 0)
            {
                var cap = FamiliarFinalBlessingService.EffectParameterInt("DuskAfterheatMultiplierAndCap", "cap", 150);
                BuffApi.SetExactLevel(owner.Self, TerriasIds.Ember, Math.Min(cap, emberBefore + emberGain));
            }
            BuffApi.SyncEmberDamageBonus(owner, owner.Self);
        }
        if (traitGain > 0)
        {
            owner.AddBuff(TerriasIds.GatheredFlame, traitGain.ToString());
        }
        TerriasPerformanceCounters.Record("DuskAfterheat.Triggered");
        TerriasLog.Debug("Dusk afterheat recovery triggered: target=" + target.InstanceId
            + ", burn=" + snapshot.StacksAtTrigger
            + ", ember=" + emberGain
            + ", gatheredFlame=" + traitGain
            + ", source=" + snapshot.Source);
    }

    private static void OnEmberConsumed(ScriptExecutor executor, IStatusManager status, int consumed)
    {
        var transfer = Math.Min(consumed, FamiliarBlessingEffectRuntime.EffectAmount("EmberOffsetBurnTransfer"));
        if (transfer <= 0)
        {
            return;
        }

        var target = TargetApi.RandomEnemyTarget(executor, requireBurn: false);
        target?.AddBuff(TerriasIds.Burn, transfer);
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
