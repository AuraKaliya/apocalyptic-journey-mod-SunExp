using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class FieldEffectHandlers
{
    public static bool ResolveRoundStart(ScriptExecutor? executor, FieldBuffSnapshot snapshot, string source)
    {
        if (snapshot == null || !snapshot.IsActive)
        {
            return false;
        }

        return snapshot.Field switch
        {
            SunExpFieldId.ScorchingCanopy => TriggerScorchingCanopyRoundStart(executor, snapshot, source),
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
        foreach (var target in CombatTargets(executor))
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

    private static IEnumerable<IStatusManager> CombatTargets(ScriptExecutor executor)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in ExecutorApi.FriendlyTargets(executor, includeSelf: true))
        {
            if (TryAddTarget(seen, target))
            {
                yield return target;
            }
        }

        foreach (var target in ExecutorApi.EnemyTargets(executor))
        {
            if (TryAddTarget(seen, target))
            {
                yield return target;
            }
        }
    }

    private static bool TryAddTarget(ISet<string> seen, IStatusManager? target)
    {
        if (target == null)
        {
            return false;
        }

        var key = string.IsNullOrWhiteSpace(target.InstanceId)
            ? target.GetHashCode().ToString()
            : target.InstanceId;
        return seen.Add(key);
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
