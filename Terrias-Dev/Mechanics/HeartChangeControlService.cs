using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;
using AuraCombatAi.Shared;
using AuraCombatAi.Shared.GameApi;
using UnityEngine;

namespace Terrias.Dll.Mechanics;

public static class HeartChangeControlService
{
    private const string ReasonNoCaster = "NoCaster";
    private const string ReasonCombatOnly = "CombatOnly";
    private const string ReasonChooseEnemy = "ChooseEnemy";
    private const string ReasonTargetDead = "TargetDead";
    private const string ReasonAlreadyControlled = "AlreadyControlled";
    private const string ReasonNeedTwoEnemies = "NeedTwoEnemies";
    private const string ReasonTargetMissing = "TargetMissing";
    private const string ReasonMissingSender = "MissingSender";
    private const string ReasonSenderOutsideLobby = "SenderOutsideLobby";
    private const string ReasonOwnerMismatch = "OwnerMismatch";

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, HeartChangeState> Active = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ResolvedNetworkTokens = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RemovingBuffs = new(StringComparer.Ordinal);
    private static bool publishedActive;

    public static event Action<bool>? ActiveStateChanged;

    public static bool TryControlFromCard(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        if (!CanControlFromCard(self, target, out var reason))
        {
            ShowFailureCaption(reason);
            RestoreCardTarget(self, target);
            return false;
        }

        if (TerriasNetworkRuntime.IsMultiplayerSession() && !TerriasNetworkRuntime.IsServer())
        {
            var token = Guid.NewGuid().ToString("N");
            TerriasNetworkRuntime.Send(
                new RpcHeartChangeControlRequest(StatusId(target), StatusId(self.Self), token),
                "HeartChangeControlService.TryControlFromCard");
            PlayerApi.ShowCaption("心变：正在同步控制结果。");
            RestoreCardTarget(self, target);
            return true;
        }

        if (!ExecutorApi.AddStatusBuff(self, target, TerriasIds.HeartChangeBuffId, 1, "Target"))
        {
            PlayerApi.ShowCaption("心变：控制效果施加失败。");
            RestoreCardTarget(self, target);
            return false;
        }

        RestoreCardTarget(self, target);
        return true;
    }

    public static void Apply(ScriptExecutor? executor)
    {
        var status = executor?.Self;
        if (IsControlled(status))
        {
            return;
        }
        if (TerriasNetworkRuntime.IsMultiplayerSession() && !TerriasNetworkRuntime.IsServer())
        {
            // The authoritative control snapshot carries the server-selected
            // action count. A client-side buff callback must not create a
            // competing proxy before that snapshot arrives.
            return;
        }
        if (!TryCreateState(status, out var state, out var reason))
        {
            ShowFailureCaption(reason);
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
        PublishActiveState();

        try
        {
            state.Status.UpdateStatus(true);
            BroadcastState(state, active: true, accepted: true, token: "");
            TerriasPerformanceCounters.Record("HeartChange.Controlled");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[HeartChange] control state activation failed: " + ex.Message);
            EndControl(state.Status, "ApplyStateFailed", removeBuff: true);
        }
    }

    public static void Clear(ScriptExecutor? executor, string source)
    {
        EndControl(executor?.Self, source, removeBuff: false);
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
            EndControl(state.Status, source, removeBuff: true);
        }
    }

    public static void ResolveNetworkControl(string targetStatusId, string ownerStatusId, string token, TerriasRpcSender sender)
    {
        if (!ClaimNetworkToken(token))
        {
            return;
        }

        var target = FindStatus(targetStatusId);
        var rejection = ValidateNetworkSender(sender, ownerStatusId);
        if (target == null)
        {
            rejection = ReasonTargetMissing;
        }
        else if (!CanControlNetworkTarget(target, out var reason))
        {
            rejection = reason;
        }

        if (!string.IsNullOrWhiteSpace(rejection))
        {
            TerriasNetworkRuntime.Send(
                new RpcHeartChangeControlState(targetStatusId, token, -1, active: false, accepted: false, rejection),
                "HeartChangeControlService.ResolveNetworkControl.Reject");
            return;
        }

        try
        {
            target!.AddBuff(TerriasIds.HeartChangeBuffId, 1);
            if (!IsControlled(target))
            {
                ApplyNetworkState(
                    new RpcHeartChangeControlState(
                        targetStatusId,
                        token,
                        -1,
                        active: true,
                        accepted: true,
                        intentCount: 0),
                    "HeartChangeControlService.ResolveNetworkControl.AfterBuff");
                var state = Snapshot(target);
                if (state != null)
                {
                    BroadcastState(state, active: true, accepted: true, token: token);
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[HeartChange] network add buff failed: " + ex.Message);
            ApplyNetworkState(
                new RpcHeartChangeControlState(
                    targetStatusId,
                    token,
                    -1,
                    active: true,
                    accepted: true,
                    intentCount: 0),
                "HeartChangeControlService.ResolveNetworkControl.Fallback");
        }
    }

    public static void ApplyNetworkState(RpcHeartChangeControlState? command, string source)
    {
        if (command == null)
        {
            return;
        }
        if (command.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion)
        {
            TerriasLog.Warn("[HeartChange] network state protocol mismatch from "
                + source
                + ": remote="
                + command.ProtocolVersion
                + ", local="
                + CompanionAuthorityService.ProjectionProtocolVersion);
            return;
        }

        var status = FindStatus(command.TargetStatusId);
        if (!command.Accepted)
        {
            if (string.Equals(command.RejectionReason, ReasonAlreadyControlled, StringComparison.Ordinal)
                && IsControlled(status))
            {
                TerriasPerformanceCounters.Record("HeartChange.NetworkDuplicateAcceptedAsNoOp");
                return;
            }

            ShowFailureCaption(command.RejectionReason);
            return;
        }

        if (status == null)
        {
            return;
        }

        if (!command.Active)
        {
            EndControl(status, source + ".RemoteClear", removeBuff: false, broadcast: false);
            return;
        }

        if (IsControlled(status))
        {
            TerriasPerformanceCounters.Record("HeartChange.NetworkDuplicateAcceptedAsNoOp");
            return;
        }

        ApplyRemoteState(status, source);
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
            EndControl(status, "BeginEnemyAction.Dead", removeBuff: true);
            return;
        }

        TerriasPerformanceCounters.Record("HeartChange.ActionBegin");
    }

    public static void EndEnemyAction(Enemy? enemy, int actionIndex)
    {
        var status = enemy?.Status;
        var state = Snapshot(status);
        if (state == null)
        {
            return;
        }

        EndControl(status, "EndEnemyAction", removeBuff: true);

        TerriasPerformanceCounters.Record("HeartChange.ActionEnd");
    }

    public static void CleanupIfDead(IStatusManager? status, string source)
    {
        if (IsControlled(status) && !IsAlive(status))
        {
            EndControl(status, source + ".Dead", removeBuff: true);
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

        // Non-controlled actors retain the game's native faction targeting.
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

    public static void RewritePreparedIntent(Enemy? enemy)
    {
        if (enemy?.Status == null || !IsControlled(enemy.Status))
        {
            return;
        }

        foreach (var card in enemy.ActionCards ?? new List<ObjectCard>())
        {
            if (card?.dataConfig?.scriptExecutor is not ScriptExecutor executor)
            {
                continue;
            }
            RetargetControlledUseScript(executor);
        }
        TerriasPerformanceCounters.Record("HeartChange.IntentTargetRewritten");
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
        return Array.Empty<int>();
    }

    public static IEnumerable<KeyValuePair<int, IStatusManager>> ActiveSlotStatuses()
    {
        return Array.Empty<KeyValuePair<int, IStatusManager>>();
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
            reason = ReasonNoCaster;
            return false;
        }

        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            reason = ReasonCombatOnly;
            return false;
        }

        if (target == null || target.fatherObject is not Enemy)
        {
            reason = ReasonChooseEnemy;
            return false;
        }

        if (!IsAlive(target))
        {
            reason = ReasonTargetDead;
            return false;
        }

        if (IsControlled(target))
        {
            reason = ReasonAlreadyControlled;
            return false;
        }

        if (AliveEnemyStatuses().Count(status => !IsControlled(status)) < 2)
        {
            reason = ReasonNeedTwoEnemies;
            return false;
        }

        return true;
    }

    private static bool CanControlNetworkTarget(IStatusManager? target, out string reason)
    {
        reason = "";
        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            reason = ReasonCombatOnly;
            return false;
        }

        if (target == null || target.fatherObject is not Enemy)
        {
            reason = ReasonChooseEnemy;
            return false;
        }

        if (!IsAlive(target))
        {
            reason = ReasonTargetDead;
            return false;
        }

        if (IsControlled(target))
        {
            reason = ReasonAlreadyControlled;
            return false;
        }

        if (AliveEnemyStatuses().Count(status => !IsControlled(status)) < 2)
        {
            reason = ReasonNeedTwoEnemies;
            return false;
        }

        return true;
    }

    private static void ApplyRemoteState(IStatusManager status, string source)
    {
        if (IsControlled(status))
        {
            TerriasPerformanceCounters.Record("HeartChange.NetworkDuplicateAcceptedAsNoOp");
            return;
        }

        if (!TryCreateState(status, out var state, out var reason))
        {
            TerriasLog.Warn("[HeartChange] network state rejected from " + source + ": " + reason);
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
        PublishActiveState();

        try
        {
            state.Status.UpdateStatus(true);
            TerriasPerformanceCounters.Record("HeartChange.Controlled.Network");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[HeartChange] remote state activation failed from " + source + ": " + ex.Message);
            EndControl(state.Status, source + ".ActivationFailed", removeBuff: false, broadcast: false);
        }
    }

    private static bool TryCreateState(IStatusManager? status, out HeartChangeState state, out string reason)
    {
        state = HeartChangeState.Empty;
        reason = "";
        if (status == null || status.fatherObject is not Enemy)
        {
            reason = ReasonChooseEnemy;
            return false;
        }

        if (!IsAlive(status))
        {
            reason = ReasonTargetDead;
            return false;
        }

        if (IsControlled(status))
        {
            reason = ReasonAlreadyControlled;
            return false;
        }

        if (AliveEnemyStatuses().Count(candidate => !IsControlled(candidate)) < 2)
        {
            reason = ReasonNeedTwoEnemies;
            return false;
        }

        state = new HeartChangeState(StatusId(status), status);
        return true;
    }

    private static void EndControl(
        IStatusManager? status,
        string source,
        bool removeBuff,
        bool broadcast = true)
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
            state.Status.UpdateStatus(true);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[HeartChange] restore failed from " + source + ": " + ex.Message);
        }

        if (removeBuff)
        {
            RemoveHeartChangeBuff(state.Status, source);
        }

        if (broadcast)
        {
            BroadcastState(state, active: false, accepted: true, token: "");
        }

        TerriasPerformanceCounters.Record("HeartChange.Cleared");
    }

    private static void RetargetControlledActor(ScriptExecutor executor, string rawFilter, string clean)
    {
        if (!IsTargetFilter(clean) && !string.Equals(clean, "All", StringComparison.Ordinal))
        {
            return;
        }

        var targets = ControlledActionTargets(executor).ToList();
        if (targets.Count == 0)
        {
            ReplaceTargets(executor, Enumerable.Empty<IStatusManager>());
            return;
        }

        if (IsSingleTargetFilter(clean))
        {
            ReplaceTargets(executor, new[] { targets[UnityEngine.Random.Range(0, targets.Count)] });
            return;
        }

        if (clean.StartsWith("AllRandom", StringComparison.Ordinal))
        {
            ReplaceTargets(executor, PickRandomTargets(targets, RandomTargetCount(rawFilter)));
            return;
        }

        ReplaceTargets(executor, targets);
    }

    private static void RetargetControlledUseScript(ScriptExecutor executor)
    {
        var requestedTargets = RequestedTargetShape(executor);
        if (requestedTargets.Count > 0
            && requestedTargets.All(target => SameStatus(target, executor.Self)))
        {
            return;
        }

        var targets = ControlledActionTargets(executor).ToList();
        if (targets.Count == 0)
        {
            ReplaceTargets(executor, Enumerable.Empty<IStatusManager>());
            TerriasPerformanceCounters.Record("HeartChange.UseScriptNoTarget");
            return;
        }

        var preferredTargets = requestedTargets
            .Where(target => targets.Any(candidate => SameStatus(candidate, target)))
            .ToList();
        if (preferredTargets.Count > 0)
        {
            ReplaceTargets(executor, requestedTargets.Count <= 1 ? preferredTargets.Take(1) : preferredTargets);
            TerriasPerformanceCounters.Record("HeartChange.UseScriptRetargeted");
            return;
        }

        if (requestedTargets.Count > 1)
        {
            ReplaceTargets(executor, targets);
        }
        else
        {
            ReplaceTargets(executor, new[] { targets[UnityEngine.Random.Range(0, targets.Count)] });
        }

        TerriasPerformanceCounters.Record("HeartChange.UseScriptRetargeted");
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
            status.RemoveBuff(TerriasIds.HeartChangeBuffId);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[HeartChange] remove buff failed from " + source + ": " + ex.Message);
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

        HeartChangeState? result;
        lock (SyncRoot)
        {
            if (!Active.TryGetValue(id, out var state))
            {
                return null;
            }

            Active.Remove(id);
            result = state;
        }

        PublishActiveState();
        return result;
    }

    private static void PublishActiveState()
    {
        bool active;
        lock (SyncRoot)
        {
            active = Active.Count > 0;
            if (active == publishedActive)
            {
                return;
            }

            publishedActive = active;
        }

        var handlers = ActiveStateChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<bool> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(active);
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[HeartChange] active-state subscriber failed: " + ex.Message);
            }
        }
    }

    private static bool ClaimNetworkToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        lock (SyncRoot)
        {
            return ResolvedNetworkTokens.Add(token);
        }
    }

    private static IStatusManager? FindStatus(string statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return null;
        }

        try
        {
            return FightManager.Instance?.statuses != null
                && FightManager.Instance.statuses.TryGetValue(statusId, out var status)
                ? status
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void BroadcastState(HeartChangeState state, bool active, bool accepted, string token)
    {
        if (state == null || !TerriasNetworkRuntime.IsMultiplayerSession())
        {
            return;
        }

        TerriasNetworkRuntime.Send(
            new RpcHeartChangeControlState(
                state.StatusId,
                token,
                -1,
                active,
                accepted,
                intentCount: 0),
            "HeartChangeControlService.BroadcastState");
    }

    private static string ValidateNetworkSender(TerriasRpcSender sender, string ownerStatusId)
    {
        if (!TerriasNetworkRuntime.IsMultiplayerSession())
        {
            return "";
        }

        if (!sender.IsAvailable)
        {
            return ReasonMissingSender;
        }

        if (!sender.IsLobbyMember)
        {
            return ReasonSenderOutsideLobby;
        }

        return SenderOwnsStatus(sender.PlayerId, ownerStatusId) ? "" : ReasonOwnerMismatch;
    }

    private static void ShowFailureCaption(string reason)
    {
        var message = reason switch
        {
            ReasonNoCaster => "施放者状态无效。",
            ReasonCombatOnly => "只能在战斗中使用。",
            ReasonChooseEnemy => "请选择一名敌人。",
            ReasonTargetDead => "目标已无法行动。",
            ReasonAlreadyControlled => "该目标已处于心变控制中。",
            ReasonNeedTwoEnemies => "敌方至少需要两名未被控制的存活敌人。",
            ReasonTargetMissing => "目标已离开战场。",
            ReasonMissingSender => "无法确认操作玩家。",
            ReasonSenderOutsideLobby => "操作玩家不在当前房间中。",
            ReasonOwnerMismatch => "该目标不属于当前玩家。",
            _ => "控制失败，请稍后重试。"
        };
        PlayerApi.ShowCaption("心变：" + message);
    }

    private static bool SenderOwnsStatus(string playerId, string ownerStatusId)
    {
        if (string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return false;
        }

        if (string.Equals(playerId, ownerStatusId, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            return map != null
                && map.TryGetValue(playerId, out var statuses)
                && statuses != null
                && statuses.Contains(ownerStatusId);
        }
        catch
        {
            return false;
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

    private static IEnumerable<IStatusManager> ControlledActionTargets(
        ScriptExecutor executor)
    {
        var semantics = WitchCombatValueEstimator.Estimate(
            executor.dataConfig,
            forceAttack: false,
            CombatTargetKind.Enemy);
        var harmful = semantics.Damage
                      + semantics.TrueDamage
                      + semantics.DamageOverTime
                      + semantics.Debuff;
        var beneficial = semantics.Heal
                         + semantics.Defend
                         + semantics.Buff
                         + semantics.Cleanse;
        if (beneficial > harmful)
        {
            return CompanionFriendlyRosterService.Snapshot(
                    includeCompanions: true)
                .Where(IsAlive)
                .Where(status => !SameStatus(status, executor.Self))
                .ToArray();
        }

        return ControlledOpponents(executor.Self).ToArray();
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
        public static readonly HeartChangeState Empty = new("", null!);

        public HeartChangeState(
            string statusId,
            IStatusManager status)
        {
            StatusId = statusId ?? "";
            Status = status;
        }

        public string StatusId { get; }

        public IStatusManager Status { get; }

    }
}
