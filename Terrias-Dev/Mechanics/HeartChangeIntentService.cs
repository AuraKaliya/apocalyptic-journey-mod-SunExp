using System;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class HeartChangeIntentService
{
    private const int StrikePriority = 50;

    public static void InitAction(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        DictionaryUtil.Set(executor.Vars, "CD", "0");
        DictionaryUtil.Set(executor.Vars, "priority", StrikePriority.ToString());
        ExecutorApi.AddDamageDescription(executor, "1", StrikeDamage(executor));
    }

    public static void Target(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        ExecutorApi.SetStatusForTarget(executor, SelectStrikeTarget(executor), "Target");
    }

    public static void UseAction(ScriptExecutor self, string actionId)
    {
        if (ReferenceEquals(self, null))
        {
            return;
        }

        var executor = self!;
        var target = SelectStrikeTarget(executor);
        if (target == null)
        {
            TerriasPerformanceCounters.Record("HeartChange.IntentNoTarget");
            TerriasLog.Info("[HeartChange] proxy strike skipped: status="
                + StatusId(executor.Self)
                + ", reason=NoTarget");
            return;
        }

        var damage = StrikeDamage(executor);
        if (ExecutorApi.DealDamageToTarget(executor, target, damage))
        {
            TerriasLog.Info("[HeartChange] proxy strike: status="
                + StatusId(executor.Self)
                + ", target="
                + StatusId(target)
                + ", damage="
                + damage);
            TerriasPerformanceCounters.Record("HeartChange.IntentStrike");
        }
    }

    public static IStatusManager? SelectStrikeTarget(IStatusManager? self)
    {
        return HeartChangeControlService.ControlledOpponentStatuses(self)
            .Where(IsAlive)
            .OrderBy(target => target.CurHp)
            .ThenBy(target => target.InstanceId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static int StrikeDamage(IStatusManager? self)
    {
        return Math.Max(1, (self?.fatherObject as Enemy)?.Attack ?? 1);
    }

    private static IStatusManager? SelectStrikeTarget(ScriptExecutor executor)
    {
        return SelectStrikeTarget(executor.Self);
    }

    private static int StrikeDamage(ScriptExecutor executor)
    {
        return StrikeDamage(executor.Self);
    }

    private static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }

    private static string StatusId(IStatusManager? status)
    {
        return status?.InstanceId ?? "";
    }
}
