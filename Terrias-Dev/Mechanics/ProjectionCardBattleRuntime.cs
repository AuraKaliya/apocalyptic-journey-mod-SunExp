using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraCombatAi.Shared;
using AuraCombatAi.Shared.GameApi;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

internal sealed class ProjectionCardBattleState
{
    public const string ProtocolIdentity = "projection-card-state-v1";

    private readonly List<ProjectionCardInstance> cards;
    private int turnIndex;
    private int drawCount;
    private int revision;
    private bool firstTurnPending;

    private ProjectionCardBattleState(
        IEnumerable<ProjectionCardInstance> cards,
        int currentPower,
        int maxPower,
        int drawCount,
        int turnIndex,
        int revision,
        bool firstTurnPending)
    {
        this.cards = cards.Where(card => card != null).ToList();
        CurrentPower = Math.Max(0, currentPower);
        MaxPower = Math.Max(CurrentPower, maxPower);
        this.drawCount = Math.Max(1, drawCount);
        this.turnIndex = Math.Max(0, turnIndex);
        this.revision = Math.Max(0, revision);
        this.firstTurnPending = firstTurnPending;
    }

    public int CurrentPower { get; private set; }

    public int MaxPower { get; private set; }

    public int Revision => revision;

    public static ProjectionCardBattleState? CaptureFromPlayer(
        string actorId,
        out string reason)
    {
        reason = "";
        var player = FightPlayer.Instance;
        var manager = FightCardManager.Instance;
        if (player?.Status == null || manager == null)
        {
            reason = "player combat card state is unavailable";
            return null;
        }

        var result = new List<ProjectionCardInstance>();
        var captured = new HashSet<DataConfig>(ReferenceEqualityComparer<DataConfig>.Instance);
        AddCardItems(result, captured, FightUI.cardItemList, CombatActorCardZone.Hand);
        AddCardItems(result, captured, FightUI.WaitCard, CombatActorCardZone.Wait);
        AddConfigs(result, captured, manager.cardList, CombatActorCardZone.DrawPile);
        AddConfigs(result, captured, manager.usedCardList, CombatActorCardZone.DiscardPile);
        AddExhaustedConfigs(result, captured, manager);

        if (result.Count == 0)
        {
            reason = "player combat deck did not expose any card instances";
            return null;
        }

        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        var state = new ProjectionCardBattleState(
            result,
            player.CurPowerCount,
            player.MaxPowerCount,
            Math.Max(1, fightUi?.ShouldCard ?? 5),
            0,
            0,
            firstTurnPending: true);
        TerriasLog.Info("[ProjectionCards] captured player battle state: actor="
            + actorId
            + ", cards="
            + result.Count
            + ", hand="
            + result.Count(card => card.Zone == CombatActorCardZone.Hand)
            + ", power="
            + state.CurrentPower
            + "/"
            + state.MaxPower);
        return state;
    }

    public static ProjectionCardBattleState? Hydrate(
        CombatActorCardStateSnapshot? snapshot,
        out string reason)
    {
        reason = "projection card snapshot is unavailable";
        if (snapshot == null || !snapshot.Validate(out reason))
        {
            return null;
        }

        var cards = new List<ProjectionCardInstance>();
        foreach (var cardSnapshot in snapshot.Cards)
        {
            var card = ProjectionCardInstance.Hydrate(cardSnapshot, out var cardReason);
            if (card == null)
            {
                reason = "card hydrate failed: " + cardReason;
                return null;
            }
            cards.Add(card);
        }

        var turnIndex = ReadRuntimeInt(snapshot, "turnIndex", 0);
        var drawCount = ReadRuntimeInt(snapshot, "drawCount", 5);
        var revision = ReadRuntimeInt(snapshot, "revision", 0);
        var firstTurnPending = ReadRuntimeInt(
            snapshot,
            "firstTurnPending",
            snapshot.DrawAtNextTurnStart ? 0 : 1) > 0;
        reason = "";
        return new ProjectionCardBattleState(
            cards,
            snapshot.CurrentPower,
            snapshot.MaxPower,
            drawCount,
            turnIndex,
            revision,
            firstTurnPending);
    }

    public CombatActorCardStateSnapshot Export(
        string ownerModId,
        string actorId,
        long battleSessionId)
    {
        return new CombatActorCardStateSnapshot
        {
            BattleSessionId = battleSessionId,
            OwnerModId = ownerModId,
            ActorId = actorId,
            CurrentPower = CurrentPower,
            MaxPower = MaxPower,
            DrawAtNextTurnStart = !firstTurnPending,
            Cards = cards
                .OrderBy(card => card.Zone)
                .ThenBy(card => card.ZoneIndex)
                .Select(card => card.Export())
                .ToList(),
            RuntimeVariables =
            {
                ["turnIndex"] = turnIndex,
                ["drawCount"] = drawCount,
                ["revision"] = revision,
                ["firstTurnPending"] = firstTurnPending ? 1d : 0d
            }
        };
    }

    public void PrepareTurn()
    {
        if (firstTurnPending)
        {
            return;
        }

        CurrentPower = MaxPower;
        Draw(drawCount);
        revision++;
    }

    public void CompleteTurn()
    {
        foreach (var card in cards.Where(card =>
                     card.Zone == CombatActorCardZone.Hand
                     || card.Zone == CombatActorCardZone.Wait))
        {
            card.Zone = card.Retained
                ? CombatActorCardZone.Retained
                : CombatActorCardZone.DiscardPile;
        }
        foreach (var card in cards.Where(card =>
                     card.Zone == CombatActorCardZone.Retained))
        {
            card.Zone = CombatActorCardZone.Hand;
        }
        ReindexZones();
        firstTurnPending = false;
        turnIndex++;
        revision++;
    }

    public CombatStateObservation Observe(ProjectionOtherObj projection)
    {
        var state = new CombatStateObservation
        {
            BattleSessionId = AuraShared.Core.AuraBattleLifecycleRouter.CurrentBattleSessionId,
            ObservationId = "projection:" + projection.InstanceId + ":" + turnIndex,
            Player = ObserveUnit(projection.Status, CombatTargetKind.Self),
            CurrentPower = CurrentPower,
            MaxPower = MaxPower,
            HandCount = cards.Count(card => card.Zone == CombatActorCardZone.Hand),
            HandCardIds = cards
                .Where(card => card.Zone == CombatActorCardZone.Hand)
                .Select(card => card.CardId)
                .ToList(),
            DeckCardIds = cards
                .Where(card => card.Zone == CombatActorCardZone.DrawPile)
                .Select(card => card.CardId)
                .ToList(),
            DiscardPileCardIds = cards
                .Where(card => card.Zone == CombatActorCardZone.DiscardPile)
                .Select(card => card.CardId)
                .ToList(),
            ExhaustPileCardIds = cards
                .Where(card => card.Zone == CombatActorCardZone.ExhaustPile)
                .Select(card => card.CardId)
                .ToList(),
            IsPlayerActionWindow = true,
            UiBusy = false
        };

        var enemies = AliveStatuses(status =>
                status.fatherObject is Enemy
                && !HeartChangeControlService.IsControlled(status))
            .OrderBy(status => status.InstanceId, StringComparer.Ordinal)
            .ToArray();
        state.Enemies = enemies
            .Select(status => ObserveUnit(status, CombatTargetKind.Enemy))
            .ToList();
        state.Friendlies = CompanionFriendlyRosterService.Snapshot(
                includeCompanions: true)
            .Where(status => status != null
                             && !string.Equals(
                                 status.InstanceId,
                                 projection.InstanceId,
                                 StringComparison.Ordinal))
            .Select(status => ObserveUnit(status, CombatTargetKind.Friendly))
            .ToList();
        var friendlyStatuses = state.Friendlies
            .Select(unit => StatusByRuntimeId(unit.RuntimeId))
            .Where(status => status != null)
            .Cast<IStatusManager>()
            .ToArray();

        foreach (var card in cards.Where(card => card.Zone == CombatActorCardZone.Hand))
        {
            AddCardActions(state, projection.Status, card, enemies, friendlyStatuses);
        }
        state.Actions.Add(new CombatActionObservation
        {
            CandidateId = "projection:end-turn",
            SourceId = "projection:end-turn",
            DisplayName = "End turn",
            Kind = CombatActionKind.EndTurn,
            RuntimeId = -1,
            Legal = true
        });
        state.Fingerprint = BuildFingerprint(state);
        return state;
    }

    public CombatAgentPreflightResult Preflight(
        ProjectionOtherObj projection,
        CombatActionObservation action)
    {
        var card = FindCard(action.RuntimeId);
        if (card == null || card.Zone != CombatActorCardZone.Hand)
        {
            return CombatAgentPreflightResult.Reject(
                "projection card instance is no longer in hand",
                CombatAgentFailureScope.CardInstance);
        }
        if (card.Cost(projection.Status) > CurrentPower)
        {
            return CombatAgentPreflightResult.Reject(
                "projection has insufficient power",
                CombatAgentFailureScope.CardInstance);
        }
        if (!card.HeadlessSupported(out var reason))
        {
            return CombatAgentPreflightResult.Reject(
                reason,
                CombatAgentFailureScope.Turn);
        }
        IStatusManager? target = null;
        if (action.TargetRuntimeId != 0
            && !TryStatus(action.TargetRuntimeId, out target))
        {
            return CombatAgentPreflightResult.Reject(
                "projection card target is no longer available",
                CombatAgentFailureScope.Candidate);
        }
        var mode = ProjectionCardTargetPolicy.Resolve(card.Config);
        var enemies = AliveStatuses(status => status.fatherObject is Enemy
                                             && !HeartChangeControlService.IsControlled(status)).ToArray();
        var friendlies = CompanionFriendlyRosterService.Snapshot(true)
            .Where(status => !string.Equals(status.InstanceId, projection.InstanceId, StringComparison.Ordinal))
            .ToArray();
        if (!ProjectionCardTargetPolicy.IsLegalTarget(mode, projection.Status, target, enemies, friendlies))
        {
            return CombatAgentPreflightResult.Reject(
                "projection card target no longer satisfies content policy",
                CombatAgentFailureScope.Candidate);
        }
        return CombatAgentPreflightResult.Allow();
    }

    public CombatAgentExecutionResult Execute(
        ProjectionOtherObj projection,
        CombatActionObservation action)
    {
        var card = FindCard(action.RuntimeId);
        if (card == null)
        {
            return CombatAgentExecutionResult.Reject(
                "projection card instance disappeared",
                CombatAgentFailureScope.CardInstance);
        }
        IStatusManager? target = null;
        if (action.TargetRuntimeId != 0
            && !TryStatus(action.TargetRuntimeId, out target))
        {
            return CombatAgentExecutionResult.Reject(
                "projection card target disappeared",
                CombatAgentFailureScope.Candidate);
        }

        var cost = card.Cost(projection.Status);
        var committed = false;
        try
        {
            CurrentPower -= cost;
            committed = true;
            ProjectionCardPresentationService.PublishCommitted(
                projection,
                card.Config,
                target,
                revision + 1,
                "ProjectionCardBattleState.Execute");
            if (!TryExecuteSpecialCard(projection, card, target))
            {
                card.Execute(projection, target);
            }
            card.MoveAfterUse();
            ReindexZones();
            revision++;
            ProjectionSummonService.BroadcastRuntimeState(
                projection,
                "ActorActionCommitted." + card.CardId);
            return CombatAgentExecutionResult.AwaitSettlement(
                "projection card committed");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[ProjectionCards] card execution failed: actor="
                + projection.InstanceId
                + ", card="
                + card.CardId,
                ex);
            return CombatAgentExecutionResult.Reject(
                "projection card failed: " + ex.Message,
                committed
                    ? CombatAgentFailureScope.Committed
                    : CombatAgentFailureScope.CardInstance);
        }
    }

    private bool TryExecuteSpecialCard(
        ProjectionOtherObj projection,
        ProjectionCardInstance card,
        IStatusManager? target)
    {
        var id = TerriasContentIdCompatibility.LocalId(card.CardId);
        if (!ProjectionWrappedCardPolicy.IsProjectionStateCard(id))
        {
            return false;
        }

        card.ExecuteSpecial(
            projection,
            target,
            executor => ExecuteProjectionStateCard(id, card, executor));
        return true;
    }

    private void ExecuteProjectionStateCard(
        string id,
        ProjectionCardInstance card,
        ScriptExecutor executor)
    {
        switch (id)
        {
            case "solar_phase_tuning":
            {
                var discarded = MoveOtherHandCards(
                    card,
                    CombatActorCardZone.DiscardPile);
                executor.SetStatus("Self");
                if (discarded > 0)
                {
                    executor.AddBuff(
                        TerriasIds.SolarRadiance,
                        discarded.ToString(CultureInfo.InvariantCulture));
                }
                Draw(3);
                return;
            }
            case "radiant_oath":
                executor.SetStatus("Self");
                executor.AddBuff(TerriasIds.SolarRadiance, "3");
                if (!ExecutorApi.IsActiveField(executor, "scorching_canopy"))
                {
                    ExecutorApi.ApplyFieldBuff(
                        executor,
                        "scorching_canopy",
                        1,
                        "projection-card.radiant_oath");
                }
                else
                {
                    Draw(1);
                }
                return;
            case "solar_return":
                executor.SetStatus("Self");
                executor.AddBuff(TerriasIds.SolarRadiance, "1");
                Draw(1);
                return;
            case "solar_origin_core":
            {
                var burned = MoveOtherHandCards(
                    card,
                    CombatActorCardZone.ExhaustPile);
                CurrentPower = Math.Min(MaxPower, CurrentPower + burned);
                return;
            }
            case "ember_tower":
            {
                var converted = ExecutorApi.SelfBuffLevel(
                                    executor,
                                    TerriasIds.Ember)
                                + ExecutorApi.SelfBuffLevel(
                                    executor,
                                    TerriasIds.Burn);
                if (converted > 0)
                {
                    executor.SetStatus("Self");
                    executor.RemoveBuff(TerriasIds.Ember);
                    executor.RemoveBuff(TerriasIds.Burn);
                    executor.AddBuff(
                        TerriasIds.GatheredFlame,
                        converted.ToString(CultureInfo.InvariantCulture));
                    Draw(converted / 5);
                }
                return;
            }
            default:
                throw new InvalidOperationException(
                    "unsupported projection state card: " + id);
        }
    }

    private int MoveOtherHandCards(
        ProjectionCardInstance current,
        CombatActorCardZone destination)
    {
        var moved = 0;
        foreach (var other in cards.Where(candidate =>
                     !ReferenceEquals(candidate, current)
                     && candidate.Zone == CombatActorCardZone.Hand))
        {
            other.Zone = destination;
            moved++;
        }
        ReindexZones();
        return moved;
    }

    private static void AddCardActions(
        CombatStateObservation state,
        IStatusManager actor,
        ProjectionCardInstance card,
        IReadOnlyList<IStatusManager> enemies,
        IReadOnlyList<IStatusManager> friendlies)
    {
        var cost = card.Cost(actor);
        var mode = ProjectionCardTargetPolicy.Resolve(card.Config);
        if (mode is ProjectionCardTargetMode.SingleEnemy or ProjectionCardTargetMode.AnySingleUnit)
        {
            foreach (var enemy in enemies)
            {
                AddAction(state, card, enemy, CombatTargetKind.Enemy, cost);
            }
        }
        if (mode is ProjectionCardTargetMode.SingleFriendly or ProjectionCardTargetMode.AnySingleUnit)
        {
            foreach (var friendly in friendlies)
            {
                AddAction(state, card, friendly, CombatTargetKind.Friendly, cost);
            }
        }
        if (mode is ProjectionCardTargetMode.Self or ProjectionCardTargetMode.AnySingleUnit)
        {
            AddAction(state, card, null, CombatTargetKind.Self, cost);
        }
        else if (mode == ProjectionCardTargetMode.NoTarget)
        {
            AddAction(state, card, null, CombatTargetKind.None, cost);
        }
    }

    private static void AddAction(
        CombatStateObservation state,
        ProjectionCardInstance card,
        IStatusManager? target,
        CombatTargetKind targetKind,
        int cost)
    {
        var semantics = WitchCombatValueEstimator.Estimate(
            card.Config,
            forceAttack: false,
            targetKind);
        var targetRuntimeId = RuntimeId(target);
        var action = new CombatActionObservation
        {
            CandidateId = "projection:card:"
                          + card.RuntimeId
                          + ":"
                          + targetRuntimeId,
            SourceId = "projection-card:" + card.CardId,
            DisplayName = card.DisplayName,
            Kind = CombatActionKind.PlayCard,
            RuntimeId = card.RuntimeId,
            TargetRuntimeId = targetRuntimeId,
            TargetKind = targetKind,
            Cost = cost,
            Legal = cost <= state.CurrentPower,
            RejectionReason = cost <= state.CurrentPower
                ? ""
                : "insufficient projection power",
            Semantics = semantics,
            SemanticSource = "projection-card-runtime"
        };
        action.Features["projectionActor"] = 1d;
        action.Features["headlessSupported"] = card.HeadlessSupported(out _) ? 1d : 0d;
        state.Actions.Add(action);
    }

    private void Draw(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var drawPile = cards
                .Where(card => card.Zone == CombatActorCardZone.DrawPile)
                .OrderBy(card => card.ZoneIndex)
                .ToList();
            if (drawPile.Count == 0)
            {
                ShuffleDiscardIntoDraw();
                drawPile = cards
                    .Where(card => card.Zone == CombatActorCardZone.DrawPile)
                    .OrderBy(card => card.ZoneIndex)
                    .ToList();
            }
            if (drawPile.Count == 0)
            {
                break;
            }
            drawPile[0].Zone = CombatActorCardZone.Hand;
            ReindexZones();
        }
    }

    private void ShuffleDiscardIntoDraw()
    {
        var discard = cards
            .Where(card => card.Zone == CombatActorCardZone.DiscardPile)
            .OrderBy(card => card.ZoneIndex)
            .ToList();
        var random = new System.Random(unchecked(
            turnIndex * 397
            ^ cards.Count * 17
            ^ CurrentPower));
        for (var index = discard.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (discard[index], discard[swap]) = (discard[swap], discard[index]);
        }
        for (var index = 0; index < discard.Count; index++)
        {
            discard[index].Zone = CombatActorCardZone.DrawPile;
            discard[index].ZoneIndex = index;
        }
    }

    private ProjectionCardInstance? FindCard(int runtimeId)
    {
        return cards.FirstOrDefault(card => card.RuntimeId == runtimeId);
    }

    private void ReindexZones()
    {
        foreach (var zone in Enum.GetValues(typeof(CombatActorCardZone))
                     .Cast<CombatActorCardZone>())
        {
            var index = 0;
            foreach (var card in cards
                         .Where(card => card.Zone == zone)
                         .OrderBy(card => card.ZoneIndex)
                         .ThenBy(card => card.RuntimeId))
            {
                card.ZoneIndex = index++;
            }
        }
    }

    private static void AddCardItems(
        ICollection<ProjectionCardInstance> result,
        ISet<DataConfig> captured,
        IEnumerable<CardItem>? source,
        CombatActorCardZone zone)
    {
        var index = 0;
        foreach (var item in source ?? Enumerable.Empty<CardItem>())
        {
            if (item?.dataConfig == null || !captured.Add(item.dataConfig))
            {
                continue;
            }
            var card = ProjectionCardInstance.Capture(
                item.dataConfig,
                zone,
                index++,
                item.Tags,
                item.enchScriptExecutor?.dataConfig,
                out var reason);
            if (card != null)
            {
                result.Add(card);
            }
            else
            {
                TerriasLog.Warn("[ProjectionCards] skipped card capture: " + reason);
            }
        }
    }

    private static void AddConfigs(
        ICollection<ProjectionCardInstance> result,
        ISet<DataConfig> captured,
        IEnumerable<DataConfig>? source,
        CombatActorCardZone zone)
    {
        var index = 0;
        foreach (var config in source ?? Enumerable.Empty<DataConfig>())
        {
            if (config == null || !captured.Add(config))
            {
                continue;
            }
            var tags = FightCardManager.Instance?.CardTags?.TryGetValue(
                config,
                out var currentTags) == true
                ? currentTags
                : new HashSet<string>();
            var card = ProjectionCardInstance.Capture(
                config,
                zone,
                index++,
                tags,
                AttachmentFor(config),
                out var reason);
            if (card != null)
            {
                result.Add(card);
            }
            else
            {
                TerriasLog.Warn("[ProjectionCards] skipped config capture: " + reason);
            }
        }
    }

    private static void AddExhaustedConfigs(
        ICollection<ProjectionCardInstance> result,
        ISet<DataConfig> captured,
        FightCardManager manager)
    {
        var activeIds = new HashSet<string>(
            captured.Select(config => config.InstanceID ?? ""),
            StringComparer.Ordinal);
        var index = 0;
        foreach (var config in RoleTable.Instance?.cardList
                     ?? Enumerable.Empty<DataConfig>())
        {
            var instanceId = config?.InstanceID ?? "";
            if (config == null
                || instanceId.Length == 0
                || activeIds.Contains(instanceId)
                || manager.FightcardList.Contains(config))
            {
                continue;
            }
            var card = ProjectionCardInstance.Capture(
                config,
                CombatActorCardZone.ExhaustPile,
                index++,
                new HashSet<string>(),
                AttachmentFor(config),
                out _);
            if (card != null)
            {
                result.Add(card);
            }
        }
    }

    private static IEnumerable<IStatusManager> AliveStatuses(
        Func<IStatusManager, bool> predicate)
    {
        try
        {
            return FightManager.Instance?.statuses?.Values
                .Where(status => status != null
                                 && status.CurHp > 0
                                 && status.state != IStatusManager.State.Dead)
                .Where(predicate)
                .ToArray()
                ?? Array.Empty<IStatusManager>();
        }
        catch
        {
            return Array.Empty<IStatusManager>();
        }
    }

    private static IDataConfig? AttachmentFor(DataConfig config)
    {
        try
        {
            return config != null
                   && RoleTable.Instance?.enchasedDict?.TryGetValue(
                       config.InstanceID,
                       out var attachment) == true
                ? attachment
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static CombatUnitObservation ObserveUnit(
        IStatusManager? status,
        CombatTargetKind kind)
    {
        var actor = status?.fatherObject as OtherObj;
        return new CombatUnitObservation
        {
            RuntimeId = RuntimeId(status),
            DefinitionId = actor?.data?.TryGetValue("Id", out var id) == true
                ? id
                : "",
            Name = actor?.data?.Localize("Name") ?? "",
            Kind = kind,
            CurrentHp = status?.CurHp ?? 0,
            MaxHp = status?.MaxHp ?? 0,
            Defend = status?.Defend ?? 0,
            Attack = actor?.Attack ?? 0
        };
    }

    private static bool TryStatus(int runtimeId, out IStatusManager? status)
    {
        status = AliveStatuses(_ => true)
            .FirstOrDefault(candidate => RuntimeId(candidate) == runtimeId);
        return status != null;
    }

    private static IStatusManager? StatusByRuntimeId(int runtimeId)
    {
        return TryStatus(runtimeId, out var status) ? status : null;
    }

    internal static int RuntimeId(IStatusManager? status)
    {
        var value = status?.InstanceId ?? "";
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= 16777619u;
            }
            return (int)(hash & 0x7fffffff);
        }
    }

    private static string BuildFingerprint(CombatStateObservation state)
    {
        return state.CurrentPower
               + ":"
               + string.Join(",", state.HandCardIds)
               + ":"
               + string.Join(",", state.Enemies.Select(enemy =>
                   enemy.RuntimeId + "=" + enemy.CurrentHp));
    }

    private static int ReadRuntimeInt(
        CombatActorCardStateSnapshot snapshot,
        string key,
        int fallback)
    {
        return snapshot.RuntimeVariables.TryGetValue(key, out var value)
            ? (int)Math.Round(value, MidpointRounding.AwayFromZero)
            : fallback;
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T? left, T? right) => ReferenceEquals(left, right);

        public int GetHashCode(T value) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}

internal sealed class ProjectionCardInstance
{
    private static int nextRuntimeId = 100000;
    private readonly List<DataConfig> attachments;
    private readonly string sourceInstanceId;

    private ProjectionCardInstance(
        DataConfig config,
        IEnumerable<DataConfig> attachments,
        IEnumerable<string> tags,
        CombatActorCardZone zone,
        int zoneIndex,
        string sourceInstanceId,
        string snapshotInstanceId)
    {
        Config = config;
        this.attachments = attachments.Where(item => item != null).ToList();
        Tags = new HashSet<string>(tags ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        Zone = zone;
        ZoneIndex = Math.Max(0, zoneIndex);
        this.sourceInstanceId = sourceInstanceId ?? "";
        SnapshotInstanceId = string.IsNullOrWhiteSpace(snapshotInstanceId)
            ? Guid.NewGuid().ToString("N")
            : snapshotInstanceId;
        RuntimeId = System.Threading.Interlocked.Increment(ref nextRuntimeId);
    }

    public DataConfig Config { get; }

    public HashSet<string> Tags { get; }

    public CombatActorCardZone Zone { get; set; }

    public int ZoneIndex { get; set; }

    public int RuntimeId { get; }

    public string SnapshotInstanceId { get; }

    public string CardId => DictionaryUtil.Get(Config.data, "Id");

    public string DisplayName => Config.data.Localize("Name");

    public bool Retained => Tags.Contains("Froze")
                            || Tags.Contains("Retain")
                            || Tags.Contains("Retained");

    public static ProjectionCardInstance? Capture(
        DataConfig source,
        CombatActorCardZone zone,
        int zoneIndex,
        IEnumerable<string> tags,
        IDataConfig? attachment,
        out string reason)
    {
        var config = CloneConfig(source, out reason);
        if (config == null)
        {
            return null;
        }
        var attachments = new List<DataConfig>();
        if (attachment != null
            && CloneConfig(attachment, out _) is { } clonedAttachment)
        {
            attachments.Add(clonedAttachment);
        }
        return new ProjectionCardInstance(
            config,
            attachments,
            tags,
            zone,
            zoneIndex,
            source.InstanceID ?? "",
            Guid.NewGuid().ToString("N"));
    }

    public static ProjectionCardInstance? Hydrate(
        CombatCardInstanceSnapshot snapshot,
        out string reason)
    {
        var config = Materialize(
            snapshot.DefinitionType,
            snapshot.CardId,
            snapshot.RuntimeData,
            snapshot.RuntimeVariables,
            out reason);
        if (config == null)
        {
            return null;
        }
        var attachments = new List<DataConfig>();
        foreach (var attachment in snapshot.AttachmentStates
                     ?? new List<CombatCardAttachmentSnapshot>())
        {
            var hydrated = Materialize(
                attachment.DefinitionType,
                attachment.AttachmentId,
                attachment.RuntimeData,
                attachment.Variables,
                out _);
            if (hydrated != null)
            {
                attachments.Add(hydrated);
            }
        }
        return new ProjectionCardInstance(
            config,
            attachments,
            snapshot.Tags,
            snapshot.Zone,
            snapshot.ZoneIndex,
            snapshot.SourceInstanceId,
            snapshot.InstanceId);
    }

    public CombatCardInstanceSnapshot Export()
    {
        var runtimeVariables = CopyRuntimeVariables(Config.Vars);
        return new CombatCardInstanceSnapshot
        {
            InstanceId = SnapshotInstanceId,
            SourceInstanceId = sourceInstanceId,
            CardId = CardId,
            DefinitionType = Config.Type.ToString(),
            Zone = Zone,
            ZoneIndex = ZoneIndex,
            EffectiveCost = Cost(null),
            Retained = Retained,
            ExhaustsOnUse = ExhaustsOnUse(),
            RuntimeData = CopyOverrides(Config),
            RuntimeVariables = runtimeVariables,
            Tags = Tags.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            Attachments = attachments
                .Select(config => DictionaryUtil.Get(config.data, "Id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList(),
            AttachmentStates = attachments.Select(config =>
                    new CombatCardAttachmentSnapshot
                    {
                        AttachmentId = DictionaryUtil.Get(config.data, "Id"),
                        DefinitionType = config.Type.ToString(),
                        RuntimeData = CopyOverrides(config),
                        Variables = CopyRuntimeVariables(config.Vars)
                    })
                .ToList()
        };
    }

    public int Cost(IStatusManager? actor)
    {
        var baseCost = ReadInt(Config.data, "Expend");
        var multiplier = 1f;
        if (actor?.dynamicVariables != null
            && actor.dynamicVariables.TryGetValue("CardCost", out var currentMultiplier))
        {
            multiplier = currentMultiplier;
        }
        var extra = ReadInt(Config.Vars, "TotalExCost")
                    + ReadInt(Config.Vars, "ExCost")
                    + ReadInt(Config.Vars, "OnceExCost");
        return Math.Max(0, Math.Min(4, (int)(baseCost * multiplier)) + extra);
    }

    public bool HeadlessSupported(out string reason)
    {
        var script = string.Join("\n", Config.data.Values ?? Enumerable.Empty<string>());
        if (script.IndexOf("CS.", StringComparison.Ordinal) >= 0
            && !ProjectionWrappedCardPolicy.IsHeadlessSafe(CardId, script))
        {
            reason = "projection card uses wrapped behavior without an actor-safe declaration";
            return false;
        }
        var unsupported = new[]
        {
            "ChooseCard", "SelectCard", "DeckUI", "FightUI.",
            "FightCardManager", "FightPlayer", "GetCard(", "CreateCard(",
            "BurnCard(", "ThrowCard(", "ChangeCard", "TransformCard",
            "CurPowerCount", "ChangePower(", "GainPower("
        };
        var token = unsupported.FirstOrDefault(value =>
            script.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
        if (token != null)
        {
            reason = "projection card requires player-only behavior: " + token;
            return false;
        }
        reason = "";
        return true;
    }

    public void Execute(ProjectionOtherObj actor, IStatusManager? target)
    {
        ExecuteCore(
            actor,
            target,
            executor => executor.RunScript("UseScript"));
    }

    public void ExecuteSpecial(
        ProjectionOtherObj actor,
        IStatusManager? target,
        Action<ScriptExecutor> execute)
    {
        ExecuteCore(actor, target, execute);
    }

    private void ExecuteCore(
        ProjectionOtherObj actor,
        IStatusManager? target,
        Action<ScriptExecutor> execute)
    {
        var self = actor.Status;
        if (self == null)
        {
            throw new InvalidOperationException("projection status is unavailable");
        }

        PrepareExecutor(Config.scriptExecutor, self, target);
        if (Config.scriptExecutor is not ScriptExecutor executor)
        {
            throw new InvalidOperationException(
                "projection card executor is unavailable");
        }
        foreach (var attachment in attachments)
        {
            PrepareExecutor(attachment.scriptExecutor, self, target);
        }
        EventCenter.Instance.EventTrigger(
            "Action" + self.InstanceId,
            new ActionData(Config, actor.RoleId));
        foreach (var attachment in attachments)
        {
            attachment.scriptExecutor.RunScript("PreUseScript");
        }

        var useCountValue = self.dynamicVariables.TryGetValue("UseCount", out var currentUseCount)
            ? currentUseCount
            : 1f;
        var useCount = Math.Max(
            1,
            (int)useCountValue + ReadInt(Config.Vars, "ExUseCount"));
        self.dynamicVariables["UseCount"] = 1f;
        for (var index = 0; index < useCount; index++)
        {
            foreach (var attachment in attachments)
            {
                attachment.scriptExecutor.RunScript("UseScript");
            }
            execute(executor);
        }
        EventCenter.Instance.EventTrigger(
            "ActionAfter" + self.InstanceId,
            new ActionData(Config, actor.RoleId));
        self.CheckAllBuff("ReducePerUse");
        DictionaryUtil.Set(Config.Vars, "OnceExCost", "0");
        if (Tags.Contains("Combo"))
        {
            executor.ComboSc();
        }
    }

    public void MoveAfterUse()
    {
        if (Tags.Contains("Recycle"))
        {
            Zone = CombatActorCardZone.Hand;
        }
        else if (Tags.Contains("Ouroboros") && !ExhaustsOnUse())
        {
            Zone = CombatActorCardZone.DrawPile;
        }
        else
        {
            Zone = ExhaustsOnUse()
                ? CombatActorCardZone.ExhaustPile
                : CombatActorCardZone.DiscardPile;
        }
    }

    private bool ExhaustsOnUse()
    {
        return Tags.Contains("Burnout")
               || Tags.Contains("Fragmented")
               || string.Equals(
                   DictionaryUtil.Get(Config.Vars, "NeedRemove"),
                   "True",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void PrepareExecutor(
        IScriptExecutor executor,
        IStatusManager self,
        IStatusManager? target)
    {
        executor.Self = self;
        executor.Target = target ?? self;
        executor.Object.Clear();
        executor.Object.Add(target ?? self);
    }

    private static DataConfig? CloneConfig(IDataConfig source, out string reason)
    {
        return Materialize(
            source.Type.ToString(),
            DictionaryUtil.Get(source.data, "Id"),
            Copy(source.data),
            Copy(source.Vars),
            out reason);
    }

    private static DataConfig? Materialize(
        string typeName,
        string id,
        IDictionary<string, string>? data,
        IDictionary<string, string>? vars,
        out string reason)
    {
        reason = "";
        if (!Enum.TryParse(typeName, true, out DataType type))
        {
            type = DataType.Card;
        }
        var handle = AuraGameDataHostApi.ResolveHandle(type, id)
                     ?? (type == DataType.Card
                         ? null
                         : AuraGameDataHostApi.ResolveHandle(DataType.Card, id));
        if (handle == null)
        {
            reason = "definition is not registered: " + type + "/" + id;
            return null;
        }
        var materialized = AuraGameDataHostApi.Materialize(
            new AuraGameDataMaterializeRequest
            {
                Definition = handle,
                DataOverrides = new Dictionary<string, string>(
                    data ?? new Dictionary<string, string>(),
                    StringComparer.Ordinal),
                Vars = new Dictionary<string, string>(
                    vars ?? new Dictionary<string, string>(),
                    StringComparer.Ordinal)
            });
        if (materialized.Instance is DataConfig config)
        {
            return config;
        }
        reason = materialized.Message;
        return null;
    }

    private static Dictionary<string, string> Copy(
        IDictionary<string, string>? source)
    {
        return source == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : source.ToDictionary(
                entry => entry.Key,
                entry => entry.Value ?? "",
                StringComparer.Ordinal);
    }

    private static Dictionary<string, string> CopyRuntimeVariables(
        IDictionary<string, string>? source)
    {
        var result = Copy(source);
        result.Remove("RawData");
        return result;
    }

    private static Dictionary<string, string> CopyOverrides(DataConfig config)
    {
        var current = Copy(config?.data);
        if (config == null)
        {
            return current;
        }
        try
        {
            var baseline = AuraGameDataHostApi.CopyRow(
                config.Type,
                DictionaryUtil.Get(config.data, "Id"));
            if (baseline == null)
            {
                return current;
            }
            return current
                .Where(entry => !baseline.TryGetValue(entry.Key, out var baseValue)
                                || !string.Equals(
                                    entry.Value,
                                    baseValue,
                                    StringComparison.Ordinal))
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value,
                    StringComparer.Ordinal);
        }
        catch
        {
            return current;
        }
    }

    private static int ReadInt(IDictionary<string, string>? source, string key)
    {
        return source != null
               && source.TryGetValue(key, out var raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }
}

internal static class ProjectionWrappedCardPolicy
{
    private static readonly HashSet<string> SafeTerriasCards = new(
        new[]
        {
            "spark",
            "scorching_canopy_card",
            "radiant_flame_slash",
            "ember_cloak_card",
            "draw_flame",
            "solar_prayer",
            "burning_star_hex",
            "crown_radiance",
            "canopy_return",
            "solar_coronation",
            "blazing_crown_collapse",
            "solar_ignition",
            "scorching_flow_reclaim",
            "impurity_purge",
            "eclipse_hex",
            "solar_scorching_light",
            "burning_calamity",
            "burning_crown_oath",
            "morning_light_bulwark",
            "gathered_flame_shield",
            "gathered_flame_cycle",
            "solar_eclipse",
            "smoke_erosion",
            "afterglow_omen_card",
            "solar_phase_tuning",
            "radiant_oath",
            "solar_return",
            "solar_origin_core",
            "ember_tower"
        },
        StringComparer.OrdinalIgnoreCase);

    public static bool IsHeadlessSafe(string cardId, string script)
    {
        const string safePrefix = "CS.Terrias.Dll.Scripting.CardScripts.";
        if (string.IsNullOrWhiteSpace(script)
            || script.IndexOf(
                safePrefix,
                StringComparison.Ordinal) < 0)
        {
            return false;
        }

        for (var index = script.IndexOf("CS.", StringComparison.Ordinal);
             index >= 0;
             index = script.IndexOf("CS.", index + 3, StringComparison.Ordinal))
        {
            if (script.IndexOf(safePrefix, index, StringComparison.Ordinal) != index)
            {
                return false;
            }
        }

        var localId = TerriasContentIdCompatibility.LocalId(cardId);
        return SafeTerriasCards.Contains(localId);
    }

    public static bool IsProjectionStateCard(string cardId)
    {
        return string.Equals(cardId, "solar_phase_tuning", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "radiant_oath", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "solar_return", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "solar_origin_core", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cardId, "ember_tower", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ProjectionCombatAgentPort : ICombatAgentRuntimePort
{
    private readonly ProjectionOtherObj projection;
    private readonly ProjectionCardBattleState state;

    public ProjectionCombatAgentPort(
        ProjectionOtherObj projection,
        ProjectionCardBattleState state)
    {
        this.projection = projection;
        this.state = state;
    }

    public bool TryObserve(
        out CombatAgentObservation observation,
        out string reason)
    {
        observation = new CombatAgentObservation
        {
            State = state.Observe(projection),
            ActorAlive = projection.Status != null
                         && projection.Status.CurHp > 0
                         && projection.Status.state != IStatusManager.State.Dead,
            BattleActive = FightManager.Instance != null
                           && FightManager.Instance.fightType != FightType.None
                           && FightManager.Instance.fightType != FightType.Win
                           && FightManager.Instance.fightType != FightType.Loss
                           && FightManager.Instance.fightType != FightType.Escape,
            ActionWindowOpen = true
        };
        reason = "";
        return true;
    }

    public CombatAgentPreflightResult Preflight(
        CombatAgentObservation observation,
        CombatActionObservation action)
    {
        return state.Preflight(projection, action);
    }

    public CombatAgentExecutionResult Execute(
        CombatAgentObservation observation,
        CombatActionObservation action)
    {
        return state.Execute(projection, action);
    }

    public CombatAgentSettlementResult PollSettlement(
        CombatActionObservation action)
    {
        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi != null
            && (WitchCombatRuntime.IsUiBusy(fightUi)
                || (FightUI.WaitCard?.Count ?? 0) > 0))
        {
            return CombatAgentSettlementResult.Pending(
                "projection card presentation is still resolving");
        }
        return CombatAgentSettlementResult.Complete(
            meaningfulProgress: true,
            message: "projection card settled");
    }

    public void CompleteTurn(CombatAutoTurnResult result)
    {
        state.CompleteTurn();
        ProjectionSummonService.BroadcastRuntimeState(
            projection,
            "ActorTurnCompleted." + result.Reason);
        TerriasLog.Info("[ProjectionCards] actor turn completed: status="
            + projection.InstanceId
            + ", reason="
            + result.Reason
            + ", forced="
            + result.Forced
            + ", actions="
            + result.CommittedActions);
    }
}

internal sealed class ProjectionCardAutomationProvider : ICombatActionAutomationProvider
{
    public bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionAutomationDescriptor descriptor)
    {
        descriptor = new CombatActionAutomationDescriptor();
        if (action == null
            || !action.SourceId.StartsWith("projection-card:", StringComparison.Ordinal))
        {
            return false;
        }
        var supported = action.Features.TryGetValue("headlessSupported", out var value)
                        && value > 0.5d;
        descriptor = new CombatActionAutomationDescriptor
        {
            HeadlessSupported = supported,
            FailureScope = CombatAgentFailureScope.Turn,
            Reason = supported
                ? ""
                : "projection card requires unsupported player interaction"
        };
        return true;
    }
}
