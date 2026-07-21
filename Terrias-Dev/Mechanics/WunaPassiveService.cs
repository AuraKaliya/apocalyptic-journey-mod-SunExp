using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class WunaPassiveService
{
    public static int ResolveEmberConsumed(
        ScriptExecutor? executor,
        IStatusManager? status,
        int consumed,
        string source)
    {
        if (executor?.Self == null || status == null || consumed <= 0)
        {
            return 0;
        }

        if (!ExecutorApi.IsSelf(executor, status)
            || !PolymorphStateStore.IsEffectiveCombatRoleFor(status, "wuna"))
        {
            return consumed;
        }

        var heal = Math.Max(1, StatusApi.MaxHp(status) * consumed / 100);
        ExecutorApi.SetStatusForTarget(executor, status, "Self");
        executor.ChangeHp(heal.ToString());
        executor.ChangeMaxHp(consumed.ToString());
        TerriasLog.Debug("[WunaPassive] resolved Ember consumption from " + source
            + ": consumed=" + consumed
            + ", heal=" + heal
            + ", owner=" + (status.InstanceId ?? "") + ".");
        return consumed;
    }
}
