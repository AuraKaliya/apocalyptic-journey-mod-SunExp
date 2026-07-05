using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using UnityEngine;

namespace SunExp.Dll.Mechanics;

public static class HeartChangeControlService
{
    private const int ExistingTargetWeight = 100;
    private const int ControlledTargetWeight = 90;

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, HeartChangeState> Active = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RemovingBuffs = new(StringComparer.Ordinal);

    public static bool TryControlFromCard(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        if (!CanControlFromCard(self, target, out var reason))
        {
            PlayerApi.ShowCaption("Heart Change: " + reason);
            RestoreCardTarget(self, target);
            return false;
        }

        if (!ExecutorApi.AddStatusBuff(self, target, SunExpIds.HeartChangeBuffId, 1, "Target"))
        {
            PlayerApi.ShowCaption("Heart Change: failed to apply control.");
            RestoreCardTarget(self, target);
            return false;
        }

        RestoreCardTarget(self, target);
        return true;
    }

    public static void Apply(ScriptExecutor? executor)
    {
        var status = executor?.Self;
        if (!TryCreateState(status, out var state, out var reason))
        {
            PlayerApi.ShowCaption("Heart Change: " + reason);
            RemoveHeartChangeBuff(status, "ApplyFailed");
            return;
        }

        lock (SyncRoot)
        {
            if (Active.ContainsKey(state.StatusId))
            {
                return;
            }

            Active[state.StatusId] = state;
        }

        try
        {
            CompanionSlotService.PositionStatusInPlayerSlot(state.Status, state.SlotIndex);
            ApplyFriendlyFacing(state);
            state.Status.UpdateStatus(true);
            QueueProxyAction(state, "Apply");
            SunExpPerformanceCounters.Record("HeartChange.Controlled");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] positioning failed: " + ex.Message);
            EndControl(state.Status, "ApplyPositionFailed", removeBuff: true, consumeNativeAction: false);
        }
    }

    public static void Clear(ScriptExecutor? executor, string source)
    {
        EndControl(executor?.Self, source, removeBuff: false, consumeNativeAction: false);
    }

    public static void ClearBattle(string source)
    {
        HeartChangeState[] snapshot;
        lock (SyncRoot)
        {
            snapshot = Active.Values.ToArray();
        }

        foreach (var state in snapshot)
        {
            EndControl(state.Status, source, removeBuff: true, consumeNativeAction: false);
        }
    }

    public static void BeginEnemyAction(Enemy? enemy, int actionIndex, bool isSingle)
    {
        var status = enemy?.Status;
        var state = Snapshot(status);
        if (state == null)
        {
            return;
        }

        if (!IsAlive(status))
        {
            EndControl(status, "BeginEnemyAction.Dead", removeBuff: true, consumeNativeAction: false);
            return;
        }

        state.IsActing = true;
        ResolveProxyBeforeNativeFallback(state, "UnexpectedNativeAction");
        if (!IsAlive(status))
        {
            EndControl(status, "BeginEnemyAction.DeadAfterProxy", removeBuff: true, consumeNativeAction: false);
            return;
        }

        SuppressNativeAction(state, "UnexpectedNativeAction");
        SunExpPerformanceCounters.Record("HeartChange.ActionBegin");
    }

    public static void EndEnemyAction(Enemy? enemy, int actionIndex)
    {
        var status = enemy?.Status;
        var state = Snapshot(status);
        if (state == null)
        {
            return;
        }

        state.IsActing = false;
        RestoreSuppressedNativeState(state, "EndEnemyAction");
        EndControl(status, "EndEnemyAction.NativeFallback", removeBuff: true, consumeNativeAction: true);

        SunExpPerformanceCounters.Record("HeartChange.ActionEnd");
    }

    public static void CompleteProxyAction(IStatusManager? status, string source)
    {
        EndControl(status, source, removeBuff: true, consumeNativeAction: true);
    }

    public static void CleanupIfDead(IStatusManager? status, string source)
    {
        if (IsControlled(status) && !IsAlive(status))
        {
            EndControl(status, source + ".Dead", removeBuff: true, consumeNativeAction: false);
        }
    }

    public static void HandleSetStatus(ScriptExecutor? executor, string filter)
    {
        if (executor?.Self == null)
        {
            return;
        }

        var clean = NormalizeFilter(filter);
        var controlledActor = Snapshot(executor.Self);
        if (controlledActor != null)
        {
            RetargetControlledActor(executor, filter, clean);
            return;
        }

        if (executor.Self.fatherObject is Enemy)
        {
            AddControlledTargetsForEnemy(executor, clean);
            return;
        }

        RemoveControlledTargetsForPlayers(executor, clean);
    }

    public static void HandleRunScript(ScriptExecutor? executor, string scriptName)
    {
        if (!string.Equals(scriptName, "UseScript", StringComparison.Ordinal)
            || executor?.Self == null
            || Snapshot(executor.Self) == null)
        {
            return;
        }

        RetargetControlledUseScript(executor);
    }

    public static bool IsControlled(IStatusManager? status)
    {
        var id = StatusId(status);
        if (id.Length == 0)
        {
            return false;
        }

        lock (SyncRoot)
        {
            return Active.ContainsKey(id);
        }
    }

    public static IEnumerable<int> ActiveSlotIndexes()
    {
        lock (SyncRoot)
        {
            return Active.Values.Select(state => state.SlotIndex).ToArray();
        }
    }

    public static IEnumerable<IStatusManager> ActiveStatuses()
    {
        lock (SyncRoot)
        {
            return Active.Values
                .Select(state => state.Status)
                .Where(status => IsAlive(status))
                .ToArray();
        }
    }

    public static IEnumerable<IStatusManager> ControlledOpponentStatuses(IStatusManager? self)
    {
        return ControlledOpponents(self).ToArray();
    }

    private static bool CanControlFromCard(ScriptExecutor? executor, IStatusManager? target, out string reason)
    {
        reason = "";
        if (executor?.Self == null)
        {
            reason = "no caster.";
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            reason = "can only be used in combat.";
            return false;
        }

        if (target == null || target.fatherObject is not Enemy)
        {
            reason = "choose an enemy.";
            return false;
        }

        if (!IsAlive(target))
        {
            reason = "target is not alive.";
            return false;
        }

        if (IsControlled(target))
        {
            reason = "target is already controlled.";
            return false;
        }

        if (AliveEnemyStatuses().Count(status => !IsControlled(status)) < 2)
        {
            reason = "needs at least two uncontrolled enemies.";
            return false;
        }

        if (CompanionSlotService.FindOpenPlayerSlot() == null)
        {
            reason = "no open friendly slot.";
            return false;
        }

        return true;
    }

    private static bool TryCreateState(IStatusManager? status, out HeartChangeState state, out string reason)
    {
        state = HeartChangeState.Empty;
        reason = "";
        if (status == null || status.fatherObject is not Enemy enemy)
        {
            reason = "target is not an enemy.";
            return false;
        }

        if (!IsAlive(status))
        {
            reason = "target is not alive.";
            return false;
        }

        if (IsControlled(status))
        {
            reason = "target is already controlled.";
            return false;
        }

        if (AliveEnemyStatuses().Count(candidate => !IsControlled(candidate)) < 2)
        {
            reason = "needs at least two uncontrolled enemies.";
            return false;
        }

        var slotIndex = CompanionSlotService.FindOpenPlayerSlot();
        if (slotIndex == null)
        {
            reason = "no open friendly slot.";
            return false;
        }

        var transform = status.transform;
        state = new HeartChangeState(
            StatusId(status),
            status,
            enemy,
            slotIndex.Value,
            transform == null ? Vector3.zero : transform.position,
            transform == null ? Vector3.one : transform.localScale);
        return true;
    }

    private static void EndControl(IStatusManager? status, string source, bool removeBuff, bool consumeNativeAction)
    {
        var state = TakeState(status);
        if (state == null)
        {
            if (removeBuff)
            {
                RemoveHeartChangeBuff(status, source + ".NoState");
            }

            return;
        }

        try
        {
            state.IsActing = false;
            RestoreSuppressedNativeState(state, source);
            RestorePosition(state);
            RestoreNativeQueueNow(state, source, consumeNativeAction);

            state.Status.UpdateStatus(true);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] restore failed from " + source + ": " + ex.Message);
        }

        if (removeBuff)
        {
            RemoveHeartChangeBuff(state.Status, source);
        }

        SunExpPerformanceCounters.Record("HeartChange.Cleared");
    }

    private static void RestorePosition(HeartChangeState state)
    {
        if (state.Status?.transform == null)
        {
            return;
        }

        state.Status.transform.localScale = state.OriginalScale;
        state.Status.SetPosition(state.OriginalPosition);
    }

    private static void ApplyFriendlyFacing(HeartChangeState state)
    {
        try
        {
            var transform = state.Status?.transform;
            if (transform == null)
            {
                return;
            }

            var scale = transform.localScale;
            var originalX = Math.Abs(state.OriginalScale.x) < 0.001f
                ? (Math.Abs(scale.x) < 0.001f ? 1f : scale.x)
                : state.OriginalScale.x;
            scale.x = -originalX;
            transform.localScale = scale;
            SunExpPerformanceCounters.Record("HeartChange.FacingMirrored");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] facing mirror failed: " + ex.Message);
        }
    }

    private static void QueueProxyAction(HeartChangeState state, string source)
    {
        try
        {
            var manager = FightManager.Instance;
            if (manager?.ActionQueue == null || state.Enemy == null)
            {
                return;
            }

            var proxy = state.Enemy.GetComponent<HeartChangeActionProxyObj>()
                ?? state.Enemy.gameObject.AddComponent<HeartChangeActionProxyObj>();
            state.Proxy = proxy;
            proxy.Configure(state.Enemy);

            var removed = manager.ActionQueue.RemoveAll(obj => IsNativeOrProxyAction(state, obj));
            manager.ActionQueue.Add(proxy);
            if (removed > 0)
            {
                SunExpLog.Info("[HeartChange] queued controlled enemy proxy action: status="
                    + state.StatusId
                    + ", intentCount="
                    + proxy.IntentCount);
                SunExpPerformanceCounters.Record("HeartChange.QueueMoved");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] queue move failed from " + source + ": " + ex.Message);
        }
    }

    private static void ResolveProxyBeforeNativeFallback(HeartChangeState state, string source)
    {
        try
        {
            var proxy = state.Proxy;
            if (proxy == null && state.Enemy != null)
            {
                proxy = state.Enemy.GetComponent<HeartChangeActionProxyObj>()
                    ?? state.Enemy.gameObject.AddComponent<HeartChangeActionProxyObj>();
                proxy.Configure(state.Enemy);
                state.Proxy = proxy;
            }

            if (proxy == null)
            {
                SunExpLog.Warn("[HeartChange] native fallback has no proxy: status=" + state.StatusId);
                return;
            }

            if (proxy.ResolveNow("NativeFallback." + source))
            {
                SunExpPerformanceCounters.Record("HeartChange.ProxyNativeFallbackResolved");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] native fallback proxy resolve failed from " + source + ": " + ex.Message);
        }
    }

    private static bool IsNativeOrProxyAction(HeartChangeState state, FightObject? obj)
    {
        return obj == null
            || ReferenceEquals(obj, state.Enemy)
            || ReferenceEquals(obj, state.Proxy)
            || (obj is Enemy && string.Equals(obj.InstanceId, state.StatusId, StringComparison.Ordinal))
            || (obj is HeartChangeActionProxyObj && string.Equals(obj.InstanceId, state.StatusId, StringComparison.Ordinal));
    }

    private static void SuppressNativeAction(HeartChangeState state, string reason)
    {
        try
        {
            state.SuppressedState = state.Status.state;
            state.NativeActionSuppressed = true;
            state.Status.ChangeState(IStatusManager.State.NoAction);
            SunExpLog.Info("[HeartChange] suppressed native enemy action: status="
                + state.StatusId
                + ", reason="
                + reason);
            SunExpPerformanceCounters.Record("HeartChange.ActionSuppressed." + reason);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] suppress failed from " + reason + ": " + ex.Message);
        }
    }

    private static void RestoreSuppressedNativeState(HeartChangeState state, string source)
    {
        try
        {
            if (state.NativeActionSuppressed && IsAlive(state.Status))
            {
                state.Status.ChangeState(state.SuppressedState == IStatusManager.State.NoAction
                    ? IStatusManager.State.Default
                    : state.SuppressedState);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] native action state restore failed from " + source + ": " + ex.Message);
        }
        finally
        {
            state.NativeActionSuppressed = false;
            state.SuppressedState = IStatusManager.State.Default;
        }
    }

    private static void RestoreNativeQueueNow(HeartChangeState state, string source, bool afterConsumedAction)
    {
        try
        {
            var manager = FightManager.Instance;
            if (manager?.ActionQueue == null)
            {
                return;
            }

            RemoveControlledQueueEntries(state, source);

            if (!IsAlive(state.Status) || state.Enemy == null || !CanRestoreQueue(manager))
            {
                return;
            }

            RestoreNativeVisibleState(state, source);

            var alreadyQueued = manager.ActionQueue.Any(obj =>
                ReferenceEquals(obj, state.Enemy)
                || (obj is Enemy && string.Equals(obj.InstanceId, state.StatusId, StringComparison.Ordinal)));
            if (!alreadyQueued)
            {
                manager.ActionQueue.Add(state.Enemy);
                SunExpLog.Info("[HeartChange] restored native enemy to action queue: status="
                    + state.StatusId
                    + ", source="
                    + source
                    + ", afterConsumedAction="
                    + afterConsumedAction);
                SunExpPerformanceCounters.Record("HeartChange.NativeRestoreApplied");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] native queue restore failed from " + source + ": " + ex.Message);
        }
    }

    private static void RestoreNativeVisibleState(HeartChangeState state, string source)
    {
        try
        {
            if (IsAlive(state.Status) && state.Status.state == IStatusManager.State.NoAction)
            {
                state.Status.ChangeState(IStatusManager.State.Default);
                SunExpLog.Info("[HeartChange] restored native visible state from NoAction: status="
                    + state.StatusId
                    + ", source="
                    + source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] native visible state restore failed from " + source + ": " + ex.Message);
        }
    }

    private static void RemoveControlledQueueEntries(HeartChangeState state, string source)
    {
        try
        {
            var manager = FightManager.Instance;
            if (manager?.ActionQueue == null)
            {
                return;
            }

            var removed = manager.ActionQueue.RemoveAll(obj =>
                obj == null
                || ReferenceEquals(obj, state.Proxy)
                || ReferenceEquals(obj, state.Enemy)
                || (obj is HeartChangeActionProxyObj && string.Equals(obj.InstanceId, state.StatusId, StringComparison.Ordinal))
                || (obj is Enemy && string.Equals(obj.InstanceId, state.StatusId, StringComparison.Ordinal)));
            if (removed > 0)
            {
                SunExpLog.Info("[HeartChange] removed controlled queue entries: status="
                    + state.StatusId
                    + ", count="
                    + removed
                    + ", source="
                    + source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] controlled queue removal failed from " + source + ": " + ex.Message);
        }
    }

    private static bool CanRestoreQueue(FightManager manager)
    {
        return manager.fightType != FightType.None
            && manager.fightType != FightType.Win
            && manager.fightType != FightType.Loss
            && manager.fightType != FightType.Escape;
    }

    private static void RetargetControlledActor(ScriptExecutor executor, string rawFilter, string clean)
    {
        if (!IsTargetFilter(clean) && !string.Equals(clean, "All", StringComparison.Ordinal))
        {
            return;
        }

        var opponents = ControlledOpponents(executor.Self).ToList();
        if (opponents.Count == 0)
        {
            ReplaceTargets(executor, Enumerable.Empty<IStatusManager>());
            return;
        }

        if (IsSingleTargetFilter(clean))
        {
            ReplaceTargets(executor, new[] { opponents[UnityEngine.Random.Range(0, opponents.Count)] });
            return;
        }

        if (clean.StartsWith("AllRandom", StringComparison.Ordinal))
        {
            ReplaceTargets(executor, PickRandomTargets(opponents, RandomTargetCount(rawFilter)));
            return;
        }

        ReplaceTargets(executor, opponents);
    }

    private static void RetargetControlledUseScript(ScriptExecutor executor)
    {
        var opponents = ControlledOpponents(executor.Self).ToList();
        if (opponents.Count == 0)
        {
            ReplaceTargets(executor, Enumerable.Empty<IStatusManager>());
            SunExpPerformanceCounters.Record("HeartChange.UseScriptNoTarget");
            return;
        }

        var requestedTargets = RequestedTargetShape(executor);
        var preferredTargets = requestedTargets
            .Where(target => opponents.Any(opponent => SameStatus(opponent, target)))
            .ToList();
        if (preferredTargets.Count > 0)
        {
            ReplaceTargets(executor, requestedTargets.Count <= 1 ? preferredTargets.Take(1) : preferredTargets);
            SunExpPerformanceCounters.Record("HeartChange.UseScriptRetargeted");
            return;
        }

        if (requestedTargets.Count > 1)
        {
            ReplaceTargets(executor, opponents);
        }
        else
        {
            ReplaceTargets(executor, new[] { opponents[UnityEngine.Random.Range(0, opponents.Count)] });
        }

        SunExpPerformanceCounters.Record("HeartChange.UseScriptRetargeted");
    }

    private static void AddControlledTargetsForEnemy(ScriptExecutor executor, string clean)
    {
        if (!IsTargetFilter(clean))
        {
            return;
        }

        if (IsSingleTargetFilter(clean))
        {
            TryRedirectEnemySingleTargetToControlled(executor);
            return;
        }

        if (clean.StartsWith("AllRandom", StringComparison.Ordinal))
        {
            return;
        }

        AddActiveControlledToObject(executor);
    }

    private static void RemoveControlledTargetsForPlayers(ScriptExecutor executor, string clean)
    {
        if (!IsTargetFilter(clean) || executor.Object == null)
        {
            return;
        }

        var filtered = executor.Object
            .Where(target => target != null && !IsControlled(target))
            .ToList();
        if (filtered.Count == executor.Object.Count && !IsControlled(executor.Target))
        {
            return;
        }

        ReplaceTargets(executor, filtered);
    }

    private static void TryRedirectEnemySingleTargetToControlled(ScriptExecutor executor)
    {
        var controlled = ActiveStatuses().ToList();
        if (controlled.Count == 0)
        {
            return;
        }

        var existingTargets = executor.Object?
            .Where(target => target != null && !IsControlled(target))
            .ToList() ?? new List<IStatusManager>();
        var totalWeight = existingTargets.Count * ExistingTargetWeight + controlled.Count * ControlledTargetWeight;
        if (totalWeight <= 0)
        {
            return;
        }

        var roll = UnityEngine.Random.Range(0, totalWeight);
        if (roll < existingTargets.Count * ExistingTargetWeight)
        {
            return;
        }

        var index = (roll - existingTargets.Count * ExistingTargetWeight) / ControlledTargetWeight;
        index = Math.Max(0, Math.Min(controlled.Count - 1, index));
        ReplaceTargets(executor, new[] { controlled[index] });
        SunExpPerformanceCounters.Record("HeartChange.TargetedByEnemy");
    }

    private static void AddActiveControlledToObject(ScriptExecutor executor)
    {
        if (executor.Object == null)
        {
            executor.Object = new List<IStatusManager>();
        }

        var seen = new HashSet<string>(
            executor.Object.Where(target => target != null).Select(target => target.InstanceId),
            StringComparer.Ordinal);
        foreach (var status in ActiveStatuses())
        {
            if (seen.Add(status.InstanceId))
            {
                executor.Object.Add(status);
            }
        }
    }

    private static void ReplaceTargets(ScriptExecutor executor, IEnumerable<IStatusManager> targets)
    {
        if (executor.Object == null)
        {
            executor.Object = new List<IStatusManager>();
        }

        var unique = new List<IStatusManager>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (target?.InstanceId == null || !seen.Add(target.InstanceId))
            {
                continue;
            }

            unique.Add(target);
        }

        executor.Object.Clear();
        executor.Object.AddRange(unique);
        executor.Target = unique.Count == 0 ? null : unique[0];
    }

    private static List<IStatusManager> RequestedTargetShape(ScriptExecutor executor)
    {
        var requested = new List<IStatusManager>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (executor.Object != null)
        {
            foreach (var target in executor.Object)
            {
                AddRequestedTarget(target, requested, seen);
            }
        }

        AddRequestedTarget(executor.Target, requested, seen);
        return requested;
    }

    private static void AddRequestedTarget(
        IStatusManager? target,
        ICollection<IStatusManager> requested,
        ISet<string> seen)
    {
        if (!IsAlive(target)
            || target?.InstanceId == null
            || !seen.Add(target.InstanceId))
        {
            return;
        }

        requested.Add(target);
    }

    private static List<IStatusManager> PickRandomTargets(IReadOnlyList<IStatusManager> source, int count)
    {
        var pool = source.ToList();
        var result = new List<IStatusManager>();
        var take = Math.Max(1, Math.Min(count, pool.Count));
        for (var i = 0; i < take; i++)
        {
            var index = UnityEngine.Random.Range(0, pool.Count);
            result.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return result;
    }

    private static int RandomTargetCount(string filter)
    {
        var digits = new string((filter ?? "").Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var count) ? count : 1;
    }

    private static void RemoveHeartChangeBuff(IStatusManager? status, string source)
    {
        var id = StatusId(status);
        if (status == null || id.Length == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!RemovingBuffs.Add(id))
            {
                return;
            }
        }

        try
        {
            status.RemoveBuff(SunExpIds.HeartChangeBuffId);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[HeartChange] remove buff failed from " + source + ": " + ex.Message);
        }
        finally
        {
            lock (SyncRoot)
            {
                RemovingBuffs.Remove(id);
            }
        }
    }

    private static HeartChangeState? Snapshot(IStatusManager? status)
    {
        var id = StatusId(status);
        if (id.Length == 0)
        {
            return null;
        }

        lock (SyncRoot)
        {
            return Active.TryGetValue(id, out var state) ? state : null;
        }
    }

    private static HeartChangeState? TakeState(IStatusManager? status)
    {
        var id = StatusId(status);
        if (id.Length == 0)
        {
            return null;
        }

        lock (SyncRoot)
        {
            if (!Active.TryGetValue(id, out var state))
            {
                return null;
            }

            Active.Remove(id);
            return state;
        }
    }

    private static IEnumerable<IStatusManager> AliveEnemyStatuses()
    {
        try
        {
            var statuses = FightManager.Instance?.statuses?.Values;
            if (statuses == null)
            {
                return Enumerable.Empty<IStatusManager>();
            }

            return statuses
                .Where(status => status?.fatherObject is Enemy)
                .Where(IsAlive)
                .Cast<IStatusManager>()
                .ToArray();
        }
        catch
        {
            return Enumerable.Empty<IStatusManager>();
        }
    }

    private static IEnumerable<IStatusManager> ControlledOpponents(IStatusManager? self)
    {
        return AliveEnemyStatuses()
            .Where(status => !SameStatus(status, self))
            .Where(status => !IsControlled(status));
    }

    private static bool IsTargetFilter(string clean)
    {
        return clean.Contains("Target");
    }

    private static bool IsSingleTargetFilter(string clean)
    {
        return string.Equals(clean, "Target", StringComparison.Ordinal);
    }

    private static string NormalizeFilter(string filter)
    {
        var clean = (filter ?? "").Replace("ExSelf", "").Trim();
        foreach (var ch in "0123456789")
        {
            clean = clean.Replace(ch.ToString(), "");
        }

        return clean;
    }

    private static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }

    private static bool SameStatus(IStatusManager? left, IStatusManager? right)
    {
        return StatusId(left).Length > 0 && string.Equals(StatusId(left), StatusId(right), StringComparison.Ordinal);
    }

    private static string StatusId(IStatusManager? status)
    {
        return status?.InstanceId ?? "";
    }

    private static void RestoreCardTarget(ScriptExecutor? executor, IStatusManager? target)
    {
        if (executor != null && target != null)
        {
            ExecutorApi.SetStatusForTarget(executor, target, "Target");
        }
    }

    private sealed class HeartChangeState
    {
        public static readonly HeartChangeState Empty = new("", null!, null!, -1, Vector3.zero, Vector3.one);

        public HeartChangeState(
            string statusId,
            IStatusManager status,
            Enemy enemy,
            int slotIndex,
            Vector3 originalPosition,
            Vector3 originalScale)
        {
            StatusId = statusId ?? "";
            Status = status;
            Enemy = enemy;
            SlotIndex = slotIndex;
            OriginalPosition = originalPosition;
            OriginalScale = originalScale;
        }

        public string StatusId { get; }

        public IStatusManager Status { get; }

        public Enemy Enemy { get; }

        public int SlotIndex { get; }

        public Vector3 OriginalPosition { get; }

        public Vector3 OriginalScale { get; }

        public bool IsActing { get; set; }

        public HeartChangeActionProxyObj? Proxy { get; set; }

        public bool NativeActionSuppressed { get; set; }

        public IStatusManager.State SuppressedState { get; set; } = IStatusManager.State.Default;
    }
}
