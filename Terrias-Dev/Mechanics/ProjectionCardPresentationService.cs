using System;
using System.Collections.Generic;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.Mechanics;

public static class ProjectionCardPresentationService
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> Presented = new(StringComparer.Ordinal);

    public static void ResetBattle()
    {
        lock (SyncRoot)
        {
            Presented.Clear();
        }
    }

    public static void PublishCommitted(
        ProjectionOtherObj projection,
        DataConfig card,
        IStatusManager? target,
        int sequence,
        string source)
    {
        if (projection?.Status == null || card == null || !CompanionAuthorityService.IsAuthoritative())
        {
            return;
        }
        var actionId = CompanionAuthorityService.BattleEpoch
                       + ":" + projection.InstanceId
                       + ":" + sequence
                       + ":" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var snapshot = new ProjectionCardPresentationSnapshot
        {
            ProtocolVersion = CompanionAuthorityService.ProjectionProtocolVersion,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            ActionId = actionId,
            Sequence = Math.Max(0, sequence),
            ProjectionStatusId = projection.InstanceId,
            OwnerStatusId = projection.OwnerStatusId,
            CardId = DictionaryUtil.Get(card.data, "Id"),
            TargetStatusIds = target == null
                ? new List<string>()
                : new List<string> { target.InstanceId }
        };
        Apply(snapshot, card, source + ".Local");
        if (TerriasNetworkRuntime.IsMultiplayerSession())
        {
            TerriasNetworkRuntime.Send(
                new RpcProjectionCardPresentation(snapshot),
                source,
                excludeOwner: true);
        }
    }

    public static void Apply(
        ProjectionCardPresentationSnapshot? snapshot,
        DataConfig? authoritativeCard,
        string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || snapshot.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || string.IsNullOrWhiteSpace(snapshot.ActionId))
        {
            return;
        }
        lock (SyncRoot)
        {
            if (!Presented.Add(snapshot.ActionId))
            {
                return;
            }
            if (Presented.Count > 512)
            {
                Presented.Clear();
                Presented.Add(snapshot.ActionId);
            }
        }

        var projection = ProjectionStateStore.Find(snapshot.ProjectionStatusId)?.Projection;
        if (projection?.Status == null)
        {
            return;
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

    private static DataConfig? Materialize(string cardId)
    {
        try
        {
            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, cardId);
            if (handle == null)
            {
                return null;
            }
            return AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
            {
                Definition = handle
            }).Instance as DataConfig;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[ProjectionCardPresentation] materialize failed: " + ex.Message);
            return null;
        }
    }
}
