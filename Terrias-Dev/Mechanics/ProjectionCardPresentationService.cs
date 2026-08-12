using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.Mechanics;

public static class ProjectionCardPresentationService
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> Presented = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DataConfig> MaterializedCards =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<ProjectionActionFrameSnapshot>> Pending =
        new(StringComparer.Ordinal);

    public static void ResetBattle()
    {
        lock (SyncRoot)
        {
            Presented.Clear();
            MaterializedCards.Clear();
            Pending.Clear();
        }
    }

    public static void PublishCommitted(
        ProjectionOtherObj projection,
        DataConfig card,
        IReadOnlyCollection<IStatusManager> targets,
        string source)
    {
        if (projection?.Status == null || card == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }
        var state = ProjectionStateStore.Find(projection.InstanceId);
        var actionSequence = (state?.Replication.ActionSequence ?? 0L) + 1L;
        var snapshot = new ProjectionActionFrameSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            Generation = state?.Replication.Generation ?? "",
            StateRevision = (state?.Replication.StateRevision ?? 0L) + 1L,
            ActionSequence = actionSequence,
            CompletedTurnSequence = state?.Replication.CompletedTurnSequence ?? 0L,
            ProjectionStatusId = projection.InstanceId,
            CardId = DictionaryUtil.Get(card.data, "Id"),
            TargetStatusIds = (targets ?? Array.Empty<IStatusManager>())
                .Where(target => target != null)
                .Select(target => target.InstanceId)
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
        Apply(snapshot, card, source + ".Local");
    }

    public static void BroadcastCommitted(
        ProjectionOtherObj projection,
        DataConfig card,
        IReadOnlyCollection<IStatusManager> targets,
        string source)
    {
        var state = ProjectionStateStore.Find(projection.InstanceId);
        if (state == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }
        state.Replication.CommitAction();
        var battle = CompanionBattleStateStore.Find(projection.InstanceId);
        var snapshot = new ProjectionActionFrameSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            Generation = state.Replication.Generation,
            StateRevision = state.Replication.StateRevision,
            ActionSequence = state.Replication.ActionSequence,
            CompletedTurnSequence = state.Replication.CompletedTurnSequence,
            ProjectionStatusId = projection.InstanceId,
            CardId = DictionaryUtil.Get(card.data, "Id"),
            TargetStatusIds = (targets ?? Array.Empty<IStatusManager>())
                .Where(target => target != null)
                .Select(target => target.InstanceId)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            MaxHp = projection.MaxHp,
            CurrentHp = projection.CurHp,
            Attack = projection.Attack,
            Armor = projection.Defend,
            CurrentMagic = battle?.Stats.CurrentMagic ?? 0
        };
        if (TerriasNetworkRuntime.IsMultiplayerSession())
        {
            TerriasNetworkRuntime.Send(
                new RpcProjectionActionFrame(snapshot),
                source);
        }
    }

    public static void Apply(
        ProjectionActionFrameSnapshot? snapshot,
        DataConfig? authoritativeCard,
        string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || snapshot.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || string.IsNullOrWhiteSpace(snapshot.Generation)
            || snapshot.ActionSequence <= 0L)
        {
            return;
        }
        var projection = ProjectionStateStore.Find(snapshot.ProjectionStatusId)?.Projection;
        if (projection?.Status == null)
        {
            if (authoritativeCard == null)
            {
                QueuePending(snapshot);
            }
            return;
        }
        lock (SyncRoot)
        {
            var actionId = ActionId(snapshot.Generation, snapshot.ActionSequence);
            if (!Presented.Add(actionId))
            {
                return;
            }
            if (Presented.Count > 512)
            {
                Presented.Clear();
                Presented.Add(actionId);
            }
        }
        if (authoritativeCard == null)
        {
            var state = ProjectionStateStore.Find(snapshot.ProjectionStatusId);
            if (state == null
                || !state.Replication.MatchesActiveGeneration(snapshot.Generation))
            {
                return;
            }
            var publicStateAdvanced = state.Replication.TryApplyRemote(
                snapshot.Generation,
                snapshot.StateRevision,
                snapshot.ActionSequence,
                snapshot.CompletedTurnSequence,
                true);
            state.RemoteTurnGate.Observe(
                snapshot.CompletedTurnSequence,
                snapshot.ActionSequence,
                snapshot.StateRevision,
                UnityEngine.Time.unscaledTimeAsDouble);
            if (publicStateAdvanced)
            {
                projection.MaxHp = Math.Max(1, snapshot.MaxHp);
                projection.CurHp = Math.Max(0, Math.Min(projection.MaxHp, snapshot.CurrentHp));
                projection.Attack = snapshot.Attack;
                projection.Defend = Math.Max(0, snapshot.Armor);
                var battle = CompanionBattleStateStore.Find(snapshot.ProjectionStatusId);
                battle?.Stats.SetCurrentMagic(snapshot.CurrentMagic);
                projection.Status.UpdateStatus(true);
            }
        }
        var card = authoritativeCard ?? Materialize(snapshot.CardId);
        if (card?.scriptExecutor is not ScriptExecutor executor)
        {
            return;
        }
        var targets = new List<IStatusManager>();
        foreach (var targetId in snapshot.TargetStatusIds ?? new List<string>())
        {
            if (FightManager.Instance?.statuses?.TryGetValue(targetId, out var target) == true)
            {
                targets.Add(target);
            }
        }
        FightActionPresentationApi.PresentCommittedAction(
            executor,
            projection.Status,
            targets,
            source);
        ProjectionStateStore.NotifyActionPresented(projection.InstanceId);
    }

    public static void FlushPending(string projectionStatusId, string source)
    {
        ProjectionActionFrameSnapshot[] pending;
        lock (SyncRoot)
        {
            if (!Pending.TryGetValue(projectionStatusId ?? "", out var values))
            {
                return;
            }
            Pending.Remove(projectionStatusId ?? "");
            pending = values
                .OrderBy(value => value.ActionSequence)
                .ToArray();
        }
        foreach (var snapshot in pending)
        {
            Apply(snapshot, null, source + ".Pending");
        }
    }

    private static void QueuePending(ProjectionActionFrameSnapshot snapshot)
    {
        lock (SyncRoot)
        {
            var actionId = ActionId(snapshot.Generation, snapshot.ActionSequence);
            if (Presented.Contains(actionId))
            {
                return;
            }
            if (!Pending.TryGetValue(snapshot.ProjectionStatusId, out var values))
            {
                values = new List<ProjectionActionFrameSnapshot>();
                Pending[snapshot.ProjectionStatusId] = values;
            }
            if (values.Any(value => string.Equals(
                    ActionId(value.Generation, value.ActionSequence),
                    actionId,
                    StringComparison.Ordinal)))
            {
                return;
            }
            if (Pending.Values.Sum(value => value.Count) >= 64)
            {
                Pending.Clear();
                values = new List<ProjectionActionFrameSnapshot>();
                Pending[snapshot.ProjectionStatusId] = values;
            }
            values.Add(snapshot);
        }
    }

    private static DataConfig? Materialize(string cardId)
    {
        try
        {
            lock (SyncRoot)
            {
                if (MaterializedCards.TryGetValue(cardId ?? "", out var cached))
                {
                    return cached;
                }
            }
            var normalizedCardId = cardId ?? "";
            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, normalizedCardId);
            if (handle == null)
            {
                return null;
            }
            var materialized = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
            {
                Definition = handle
            }).Instance as DataConfig;
            if (materialized != null)
            {
                lock (SyncRoot)
                {
                    if (MaterializedCards.Count >= 128) MaterializedCards.Clear();
                    MaterializedCards[cardId ?? ""] = materialized;
                }
            }
            return materialized;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[ProjectionCardPresentation] materialize failed: " + ex.Message);
            return null;
        }
    }

    private static string ActionId(string? generation, long actionSequence)
    {
        return CompanionAuthorityService.BattleEpoch
               + ":" + (generation ?? "")
               + ":" + Math.Max(0L, actionSequence);
    }
}
