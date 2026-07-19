using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.GameApi;

public static class TargetApi
{
    public static List<IStatusManager> EnemyTargets(ScriptExecutor? executor)
    {
        if (executor == null)
        {
            return new List<IStatusManager>();
        }

        executor.SetStatus("AllTarget");
        var selfId = executor.Self?.InstanceId;
        return executor.Object?
            .Where(target => target != null
                && target.InstanceId != selfId
                && !IsUnavailableControlledEnemyTarget(executor, target))
            .ToList() ?? new List<IStatusManager>();
    }

    public static List<IStatusManager> FriendlyTargets(ScriptExecutor? executor, bool includeSelf)
    {
        if (executor == null)
        {
            return new List<IStatusManager>();
        }

        var enemyIds = new HashSet<string>(EnemyTargets(executor).Select(target => target.InstanceId), StringComparer.Ordinal);
        executor.SetStatus("All");
        var result = new List<IStatusManager>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in executor.Object ?? new List<IStatusManager>())
        {
            if (target == null || target.InstanceId == null || enemyIds.Contains(target.InstanceId))
            {
                continue;
            }

            if (!includeSelf && IsSelf(executor, target))
            {
                continue;
            }

            if (seen.Add(target.InstanceId))
            {
                result.Add(target);
            }
        }

        if (includeSelf && executor.Self != null && seen.Add(executor.Self.InstanceId))
        {
            result.Add(executor.Self);
        }

        return result;
    }

    public static List<IStatusManager> AllCombatTargets(ScriptExecutor? executor, bool includeSelf)
    {
        if (executor == null)
        {
            return new List<IStatusManager>();
        }

        executor.SetStatus("All");
        var result = new List<IStatusManager>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in executor.Object ?? new List<IStatusManager>())
        {
            if (target == null)
            {
                continue;
            }

            if (!includeSelf && IsSelf(executor, target))
            {
                continue;
            }

            if (TryAddDistinct(seen, target))
            {
                result.Add(target);
            }
        }

        if (includeSelf && executor.Self != null && TryAddDistinct(seen, executor.Self))
        {
            result.Add(executor.Self);
        }

        return result;
    }

    public static List<IStatusManager> SameSideTargets(ScriptExecutor? executor, IStatusManager? anchor, bool aliveOnly = true)
    {
        if (executor == null || anchor == null)
        {
            return new List<IStatusManager>();
        }

        var opponents = EnemyTargets(executor);
        var sameSide = opponents.Any(target => SameStatus(target, anchor))
            ? opponents
            : FriendlyTargets(executor, includeSelf: true);
        if (!sameSide.Any(target => SameStatus(target, anchor)))
        {
            sameSide.Add(anchor);
        }

        return sameSide
            .Where(target => target != null && (!aliveOnly || StatusApi.IsAlive(target)))
            .GroupBy(StatusKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(StatusKey, StringComparer.Ordinal)
            .ToList();
    }

    public static List<IStatusManager> OpposingSideTargets(ScriptExecutor? executor, IStatusManager? source, bool aliveOnly = true)
    {
        if (executor == null || source == null)
        {
            return new List<IStatusManager>();
        }

        var sourceSide = new HashSet<string>(SameSideTargets(executor, source, aliveOnly: false).Select(StatusKey), StringComparer.Ordinal);
        return AllCombatTargets(executor, includeSelf: true)
            .Where(target => !sourceSide.Contains(StatusKey(target)) && (!aliveOnly || StatusApi.IsAlive(target)))
            .GroupBy(StatusKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(StatusKey, StringComparer.Ordinal)
            .ToList();
    }

    public static IStatusManager? RandomEnemyTarget(ScriptExecutor? executor, bool requireBurn)
    {
        var candidates = EnemyTargets(executor)
            .Where(target => !requireBurn || BuffApi.Level(target, SunExpIds.Burn) > 0)
            .ToList();
        return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    public static IStatusManager? RandomFriendlyTarget(ScriptExecutor? executor, bool includeSelf)
    {
        var candidates = FriendlyTargets(executor, includeSelf);
        if (candidates.Count == 0)
        {
            return includeSelf ? executor?.Self : null;
        }

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    public static IStatusManager? PrimaryTarget(ScriptExecutor? executor)
    {
        if (executor == null)
        {
            return null;
        }

        if (executor.Target != null
            && !IsSelf(executor, executor.Target)
            && !IsUnavailableControlledEnemyTarget(executor, executor.Target))
        {
            return executor.Target;
        }

        if (executor.Self == null)
        {
            return null;
        }

        try
        {
            executor.SetStatus("Target");
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Primary target unavailable while resolving script display: " + ex.Message);
            return null;
        }

        return executor.Object?.FirstOrDefault(target => target != null && !IsSelf(executor, target));
    }

    public static IStatusManager? PrimaryTargetIncludingSelf(ScriptExecutor? executor)
    {
        if (executor == null)
        {
            return null;
        }

        if (executor.Target != null && !IsUnavailableControlledEnemyTarget(executor, executor.Target))
        {
            return executor.Target;
        }

        if (executor.Self == null)
        {
            return null;
        }

        try
        {
            executor.SetStatus("Target");
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Primary target unavailable while resolving script display: " + ex.Message);
        }

        return executor.Object?.FirstOrDefault(target => target != null) ?? executor.Self;
    }

    public static bool IsSelf(ScriptExecutor? executor, IStatusManager? target)
    {
        return executor?.Self != null && target?.InstanceId != null && executor.Self.InstanceId == target.InstanceId;
    }

    public static bool SetStatusForTarget(ScriptExecutor? executor, IStatusManager? target, string fallbackStatus = "Self")
    {
        if (executor == null)
        {
            return false;
        }

        if (target == null)
        {
            executor.SetStatus(fallbackStatus);
            return true;
        }

        if (IsSelf(executor, target))
        {
            executor.SetStatus("Self");
            return true;
        }

        executor.SetStatusById(target.InstanceId);
        return true;
    }

    private static bool IsUnavailableControlledEnemyTarget(ScriptExecutor? executor, IStatusManager? target)
    {
        if (!HeartChangeControlService.IsControlled(target))
        {
            return false;
        }

        return executor?.Self?.fatherObject is not Enemy;
    }

    private static bool TryAddDistinct(ISet<string> seen, IStatusManager target)
    {
        var key = string.IsNullOrWhiteSpace(target.InstanceId)
            ? target.GetHashCode().ToString()
            : target.InstanceId;
        return seen.Add(key);
    }

    private static bool SameStatus(IStatusManager? left, IStatusManager? right)
    {
        return left != null
            && right != null
            && (ReferenceEquals(left, right)
                || !string.IsNullOrWhiteSpace(left.InstanceId)
                && string.Equals(left.InstanceId, right.InstanceId, StringComparison.Ordinal));
    }

    private static string StatusKey(IStatusManager target)
    {
        return string.IsNullOrWhiteSpace(target.InstanceId)
            ? target.GetHashCode().ToString()
            : target.InstanceId;
    }
}
