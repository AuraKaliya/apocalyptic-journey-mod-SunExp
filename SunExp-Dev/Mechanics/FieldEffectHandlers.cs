using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class FieldEffectHandlers
{
    public static bool ResolveRoundStart(ScriptExecutor? executor, FieldBuffSnapshot snapshot, string source)
    {
        if (snapshot == null
            || !snapshot.IsActive
            || !FieldApi.CanResolveFieldEffects()
            || FieldEffectRegistry.DefinitionFor(snapshot.Field)?.HasRoundStartHandler != true)
        {
            return false;
        }

        return snapshot.Field switch
        {
            SunExpFieldId.ScorchingCanopy => TriggerScorchingCanopyRoundStart(executor, snapshot, source),
            _ => false
        };
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
            SunExpFieldId.ScorchingCanopy => BuffOverflowApi.HandleBurnOverflow(target, buffId, amount),
            _ => false
        };
    }

    private static bool TriggerScorchingCanopyRoundStart(ScriptExecutor? executor, FieldBuffSnapshot snapshot, string source)
    {
        var count = Math.Max(0, snapshot.Stacks);
        if (executor == null || count <= 0)
        {
            return false;
        }

        var applied = 0;
        foreach (var target in ExecutorApi.AllCombatTargets(executor, includeSelf: true))
        {
            target.AddBuff(SunExpIds.Burn, count);
            applied++;
        }

        ClearSelfBurnIfProtected(executor);
        SunExpLog.Debug("[FieldEffect] scorching canopy round start: stacks="
            + count
            + ", targets="
            + applied
            + ", source="
            + (source ?? ""));
        return applied > 0;
    }

    private static void ClearSelfBurnIfProtected(ScriptExecutor executor)
    {
        if (executor.Self == null || !ExecutorApi.IsSelfBurnProtected(executor, includePending: true))
        {
            return;
        }

        executor.Self.RemoveBuff(SunExpIds.Burn);
    }
}
