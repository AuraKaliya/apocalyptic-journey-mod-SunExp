using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class FieldRoundStartContext
{
    public FieldRoundStartContext(
        ScriptExecutor executor,
        FieldBuffSnapshot snapshot,
        IReadOnlyList<IStatusManager> targets,
        string source)
    {
        Executor = executor;
        Snapshot = snapshot;
        Targets = targets ?? Array.Empty<IStatusManager>();
        Source = source ?? "";
    }

    public ScriptExecutor Executor { get; }

    public FieldBuffSnapshot Snapshot { get; }

    public IReadOnlyList<IStatusManager> Targets { get; }

    public string Source { get; }
}

public static class FieldEffectHandlers
{
    private static readonly IReadOnlyDictionary<TerriasFieldId, Func<FieldRoundStartContext, bool>> RoundStartHandlers =
        new Dictionary<TerriasFieldId, Func<FieldRoundStartContext, bool>>
        {
            [TerriasFieldId.ScorchingCanopy] = TriggerScorchingCanopyRoundStart,
            [TerriasFieldId.SamsaraGarden] = TriggerSamsaraGardenRoundStart
        };

    public static bool ResolveRoundStart(ScriptExecutor? executor, FieldBuffSnapshot snapshot, string source)
    {
        if (executor == null
            || snapshot == null
            || !snapshot.IsActive
            || !FieldApi.CanResolveFieldEffects()
            || FieldEffectRegistry.DefinitionFor(snapshot.Field)?.HasRoundStartHandler != true
            || !RoundStartHandlers.TryGetValue(snapshot.Field, out var handler))
        {
            return false;
        }

        var targets = ExecutorApi.AllCombatTargets(executor, includeSelf: true);
        return handler(new FieldRoundStartContext(executor, snapshot, targets, source));
    }

    public static int ApplyToAllCombatants(
        FieldRoundStartContext context,
        Func<IStatusManager, bool> effect,
        string effectId)
    {
        if (context == null || effect == null)
        {
            return 0;
        }

        var applied = 0;
        foreach (var target in context.Targets)
        {
            if (target == null)
            {
                continue;
            }

            try
            {
                if (effect(target))
                {
                    applied++;
                }
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[FieldEffect] target failed: field="
                    + context.Snapshot.Slug
                    + ", effect="
                    + (effectId ?? "")
                    + ", target="
                    + (target.InstanceId ?? "")
                    + ", error="
                    + ex.Message);
            }
        }

        return applied;
    }

    public static bool HandleBuffAdded(IStatusManager? target, string buffId, int amount, string source)
    {
        if (target == null
            || amount <= 0
            || !FieldApi.CanResolveFieldEffects()
            || !FieldApi.HasActiveBuffAddedPolicy())
        {
            return false;
        }

        if (!FieldApi.TryGetActiveField(out var field, out _, out _)
            || !FieldApi.HasActivePolicy(FieldEffectPolicyFlags.BurnOverflow))
        {
            return false;
        }

        return field switch
        {
            TerriasFieldId.ScorchingCanopy => BuffOverflowApi.HandleBurnOverflow(target, buffId, amount),
            _ => false
        };
    }

    private static bool TriggerScorchingCanopyRoundStart(FieldRoundStartContext context)
    {
        var count = Math.Max(0, context.Snapshot.Stacks);
        if (count <= 0)
        {
            return false;
        }

        var applied = ApplyToAllCombatants(
            context,
            target =>
            {
                target.AddBuff(TerriasIds.Burn, count);
                return true;
            },
            "burn");

        ClearSelfBurnIfProtected(context.Executor);
        TerriasLog.Debug("[FieldEffect] scorching canopy round start: stacks="
            + count
            + ", targets="
            + applied
            + ", source="
            + context.Source);
        return applied > 0;
    }

    private static bool TriggerSamsaraGardenRoundStart(FieldRoundStartContext context)
    {
        var stacks = Math.Max(0, context.Snapshot.Stacks);
        if (stacks <= 0)
        {
            return false;
        }

        var healPercent = stacks * 5;
        var atUpperBound = context.Snapshot.MaxStacks > 0 && stacks >= context.Snapshot.MaxStacks;
        var applied = ApplyToAllCombatants(
            context,
            target =>
            {
                if (!StatusApi.IsAlive(target))
                {
                    return false;
                }

                var maxHp = Math.Max(1, StatusApi.MaxHp(target));
                var heal = (int)Math.Max(1L, (long)maxHp * healPercent / 100L);
                var resolved = StatusApi.TryHeal(target, heal);
                if (atUpperBound)
                {
                    target.AddBuff(TerriasIds.Rebirth, 30);
                    resolved = true;
                }

                return resolved;
            },
            "heal-and-rebirth");

        TerriasLog.Debug("[FieldEffect] samsara garden round start: stacks="
            + stacks
            + ", healPercent="
            + healPercent
            + ", rebirth="
            + (atUpperBound ? 30 : 0)
            + ", targets="
            + applied
            + ", source="
            + context.Source);
        return applied > 0;
    }

    private static void ClearSelfBurnIfProtected(ScriptExecutor executor)
    {
        if (executor.Self == null || !ExecutorApi.IsSelfBurnProtected(executor, includePending: true))
        {
            return;
        }

        executor.Self.RemoveBuff(TerriasIds.Burn);
    }
}
