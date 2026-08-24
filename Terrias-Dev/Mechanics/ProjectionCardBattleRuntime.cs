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
    private readonly List<ProjectionCardInstance> cards;
    private readonly Dictionary<string, int[]> targetSetsByCandidate =
        new(StringComparer.Ordinal);
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

    public void InitializeLifecycle(IStatusManager? actor)
    {
        foreach (var card in cards.Where(card => card.Zone == CombatActorCardZone.Hand))
        {
            card.EnterHand(actor);
        }
    }

    public static ProjectionCardBattleState? CreateFresh(
        ProjectionDeckRecipe? recipe,
        string actorId,
        out string reason)
    {
        reason = "projection role deck is unavailable";
        if (recipe == null || recipe.Cards.Count == 0)
        {
            return null;
        }

        var actorDeck = ProjectionActorDeckProjection.Build(
            recipe,
            ProjectionDeckCapabilityInspector.Inspect,
            new ProjectionDeckCardRecipe(TerriasIds.ProjectionBasicActionCardId));
        if (!actorDeck.Success || actorDeck.EffectiveRecipe == null)
        {
            reason = actorDeck.FailureReason;
            return null;
        }

        var effectiveRecipe = actorDeck.EffectiveRecipe;
        var cards = effectiveRecipe.Cards
            .Select((entry, index) => ProjectionCardInstance.CreateBaseline(
                entry,
                CombatActorCardZone.DrawPile,
                index))
            .ToList();
        var random = new System.Random(effectiveRecipe.ShuffleSeed);
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (cards[index], cards[swap]) = (cards[swap], cards[index]);
        }
        for (var index = 0; index < cards.Count; index++)
        {
            cards[index].Zone = index < ProjectionDeckRecipe.DefaultDrawCount
                ? CombatActorCardZone.Hand
                : CombatActorCardZone.DrawPile;
            cards[index].ZoneIndex = index < ProjectionDeckRecipe.DefaultDrawCount
                ? index
                : index - ProjectionDeckRecipe.DefaultDrawCount;
        }

        reason = "";
        TerriasLog.InfoAlways("[ProjectionCards] actor-safe role deck initialized: actor="
            + actorId
            + ", sourceCards="
            + recipe.Cards.Count
            + ", actorCards="
            + cards.Count
            + ", rejected="
            + actorDeck.RejectedCards.Count
            + ", basicAction="
            + actorDeck.UsesBasicAction
            + ", initialHand="
            + Math.Min(ProjectionDeckRecipe.DefaultDrawCount, cards.Count)
            + ", sourceHash="
            + recipe.Hash.Substring(0, 12)
            + ", actorHash="
            + effectiveRecipe.Hash.Substring(0, 12)
            + (actorDeck.RejectedCards.Count == 0
                ? ""
                : ", rejectedIds=" + string.Join(
                    ",",
                    actorDeck.RejectedCards
                        .Select(card => card.CardId)
                        .Distinct(StringComparer.Ordinal)
                        .Take(12))));
        return new ProjectionCardBattleState(
            cards,
            ProjectionDeckRecipe.DefaultMaxPower,
            ProjectionDeckRecipe.DefaultMaxPower,
            ProjectionDeckRecipe.DefaultDrawCount,
            0,
            0,
            firstTurnPending: true);
    }

    public void PrepareTurn(IStatusManager? actor)
    {
        if (firstTurnPending)
        {
            return;
        }

        CurrentPower = MaxPower;
        Draw(drawCount, actor);
        revision++;
    }

    public void CompleteTurn(IStatusManager? actor)
    {
        foreach (var card in cards.Where(card =>
                     card.Zone == CombatActorCardZone.Hand
                     || card.Zone == CombatActorCardZone.Wait))
        {
            if (card.Retained)
            {
                card.Zone = CombatActorCardZone.Retained;
            }
            else
            {
                card.Zone = CombatActorCardZone.DiscardPile;
                card.EndTurn(actor);
            }
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
        targetSetsByCandidate.Clear();
        QuarantineUnusableHandCards(projection.Status);
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
            if (!card.TryPrepare(projection.Status, out _))
            {
                continue;
            }
            card.Refresh(projection.Status, revision);
            AddCardActions(state, projection.Status, card, enemies, friendlyStatuses);
        }
        var endTurn = new CombatActionObservation
        {
            CandidateId = "projection:end-turn",
            SourceId = "projection:end-turn",
            DisplayName = "End turn",
            Kind = CombatActionKind.EndTurn,
            RuntimeId = -1,
            Legal = false
        };
        var legalNonEndActions = state.Actions.Count(action =>
            action != null
            && action.Legal
            && action.Kind != CombatActionKind.EndTurn);
        endTurn.Legal = ProjectionActorTurnPolicy.CanEndTurn(legalNonEndActions);
        endTurn.RejectionReason = endTurn.Legal
            ? ""
            : "projection must play an available actor-safe card before ending its turn";
        state.Actions.Add(endTurn);
        state.Fingerprint = BuildFingerprint(state);
        return state;
    }

    private void QuarantineUnusableHandCards(IStatusManager? actor)
    {
        var remainingPasses = Math.Max(1, cards.Count + 1);
        while (remainingPasses-- > 0)
        {
            var rejected = cards
                .Where(card => card.Zone == CombatActorCardZone.Hand)
                .Select(card => new
                {
                    Card = card,
                    Prepared = card.TryPrepare(actor, out var reason),
                    Reason = reason
                })
                .Where(result => !result.Prepared)
                .ToArray();
            if (rejected.Length == 0)
            {
                return;
            }

            foreach (var result in rejected)
            {
                result.Card.LeaveHand();
                result.Card.Zone = CombatActorCardZone.ExhaustPile;
                TerriasLog.WarnOnce(
                    "projection-card-runtime-quarantine:"
                    + result.Card.CardId
                    + ":"
                    + result.Card.RuntimeId,
                    "[ProjectionCards] actor-safe deck card quarantined after runtime initialization failure: "
                    + result.Card.CardId
                    + "; "
                    + result.Reason);
                TerriasPerformanceCounters.Record("ProjectionCards.ActorDeckRuntimeQuarantined");
            }

            ReindexZones();
            Draw(rejected.Length, actor);
            revision++;
        }
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
        if (!card.TryPrepare(projection.Status, out var prepareReason))
        {
            return CombatAgentPreflightResult.Reject(
                prepareReason,
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
        if (!TryResolveTargets(action, out var targets))
        {
            return CombatAgentPreflightResult.Reject(
                "projection card target is no longer available",
                CombatAgentFailureScope.Candidate);
        }
        var declaration = ProjectionCardTargetPolicy.ResolveDeclaration(card.Config);
        var enemies = AliveStatuses(status => status.fatherObject is Enemy
                                             && !HeartChangeControlService.IsControlled(status)).ToArray();
        var friendlies = CompanionFriendlyRosterService.Snapshot(true)
            .Where(status => !string.Equals(status.InstanceId, projection.InstanceId, StringComparison.Ordinal))
            .ToArray();
        if (!ProjectionCardTargetPolicy.IsLegalTargetSet(
                declaration,
                projection.Status,
                targets,
                enemies,
                friendlies))
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
        if (!card.TryPrepare(projection.Status, out var prepareReason))
        {
            return CombatAgentExecutionResult.Reject(
                prepareReason,
                CombatAgentFailureScope.CardInstance);
        }
        if (!TryResolveTargets(action, out var targets))
        {
            return CombatAgentExecutionResult.Reject(
                "projection card target disappeared",
                CombatAgentFailureScope.Candidate);
        }
        var target = targets.FirstOrDefault();

        var cost = card.Cost(projection.Status);
        var committed = false;
        var actionFramePublished = false;
        try
        {
            CurrentPower -= cost;
            committed = true;
            ProjectionCardPresentationService.PublishCommitted(
                projection,
                card.Config,
                targets,
                "ProjectionCardBattleState.Execute");
            if (!TryExecuteSpecialCard(projection, card, target))
            {
                card.Execute(projection, targets);
            }
            card.LeaveHand();
            card.MoveAfterUse();
            if (card.Zone == CombatActorCardZone.DiscardPile)
            {
                card.EnterDiscard(projection.Status);
            }
            else if (card.Zone == CombatActorCardZone.Hand)
            {
                card.EnterHand(projection.Status);
            }
            ReindexZones();
            revision++;
            ProjectionCardPresentationService.BroadcastCommitted(
                projection,
                card.Config,
                targets,
                "ActorActionCommitted." + card.CardId);
            actionFramePublished = true;
            return CombatAgentExecutionResult.AwaitSettlement(
                "projection card committed");
        }
        catch (Exception ex)
        {
            if (committed && !actionFramePublished)
            {
                revision++;
                ProjectionCardPresentationService.BroadcastCommitted(
                    projection,
                    card.Config,
                    targets,
                    "ActorActionCommitted.Failed." + card.CardId);
            }
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
                    CombatActorCardZone.DiscardPile,
                    executor.Self);
                executor.SetStatus("Self");
                if (discarded > 0)
                {
                    executor.AddBuff(
                        TerriasIds.SolarRadiance,
                        discarded.ToString(CultureInfo.InvariantCulture));
                }
                Draw(3, executor.Self);
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
                    Draw(1, executor.Self);
                }
                return;
            case "solar_return":
                executor.SetStatus("Self");
                executor.AddBuff(TerriasIds.SolarRadiance, "1");
                Draw(1, executor.Self);
                return;
            case "solar_origin_core":
            {
                var burned = MoveOtherHandCards(
                    card,
                    CombatActorCardZone.ExhaustPile,
                    executor.Self);
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
                    Draw(converted / 5, executor.Self);
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
        CombatActorCardZone destination,
        IStatusManager? actor)
    {
        var moved = 0;
        foreach (var other in cards.Where(candidate =>
                     !ReferenceEquals(candidate, current)
                     && candidate.Zone == CombatActorCardZone.Hand))
        {
            other.LeaveHand();
            other.Zone = destination;
            if (destination == CombatActorCardZone.DiscardPile)
            {
                other.EnterDiscard(actor);
            }
            moved++;
        }
        ReindexZones();
        return moved;
    }

    private void AddCardActions(
        CombatStateObservation state,
        IStatusManager actor,
        ProjectionCardInstance card,
        IReadOnlyList<IStatusManager> enemies,
        IReadOnlyList<IStatusManager> friendlies)
    {
        var cost = card.Cost(actor);
        var declaration = ProjectionCardTargetPolicy.ResolveDeclaration(card.Config);
        var mode = declaration.Mode;
        if (mode is ProjectionCardTargetMode.SingleEnemy or ProjectionCardTargetMode.AnySingleUnit)
        {
            foreach (var enemy in enemies)
            {
                AddAction(state, card, new[] { enemy }, CombatTargetKind.Enemy, cost);
            }
        }
        if (mode is ProjectionCardTargetMode.SingleFriendly or ProjectionCardTargetMode.AnySingleUnit)
        {
            foreach (var friendly in friendlies)
            {
                AddAction(state, card, new[] { friendly }, CombatTargetKind.Friendly, cost);
            }
        }
        if (mode is ProjectionCardTargetMode.Self or ProjectionCardTargetMode.AnySingleUnit)
        {
            AddAction(state, card, new[] { actor }, CombatTargetKind.Self, cost);
        }
        else if (mode == ProjectionCardTargetMode.NoTarget)
        {
            AddAction(state, card, Array.Empty<IStatusManager>(), CombatTargetKind.None, cost);
        }
        else if (mode == ProjectionCardTargetMode.AllEnemies && enemies.Count > 0)
        {
            AddAction(state, card, enemies, CombatTargetKind.Enemy, cost);
        }
        else if (mode == ProjectionCardTargetMode.AllFriendlies)
        {
            var pool = declaration.IncludeSelf
                ? friendlies.Concat(new[] { actor })
                : friendlies;
            var targets = pool
                .OrderBy(status => status.MaxHp <= 0
                    ? 1d
                    : (double)status.CurHp / status.MaxHp)
                .ThenBy(status => status.InstanceId, StringComparer.Ordinal)
                .ToArray();
            if (targets.Length > 0) AddAction(state, card, targets, CombatTargetKind.Friendly, cost);
        }
        else if (mode == ProjectionCardTargetMode.RandomEnemyN && enemies.Count > 0)
        {
            AddAction(state, card, StableSample(
                enemies,
                declaration.Count,
                card.RuntimeId ^ turnIndex * 397 ^ revision), CombatTargetKind.Enemy, cost);
        }
        else if (mode == ProjectionCardTargetMode.RandomFriendlyN)
        {
            var pool = declaration.IncludeSelf
                ? friendlies.Concat(new[] { actor }).ToArray()
                : friendlies.ToArray();
            if (pool.Length > 0) AddAction(state, card, StableSample(
                pool,
                declaration.Count,
                card.RuntimeId ^ turnIndex * 397 ^ revision), CombatTargetKind.Friendly, cost);
        }
        else if (mode == ProjectionCardTargetMode.DeclaredTargetSet)
        {
            var kinds = new HashSet<string>(
                (declaration.SetKinds ?? "").Split(
                    new[] { ',', ';', '|', ' ' },
                    StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
            var pool = (kinds.Contains("Enemies")
                    ? enemies
                    : Array.Empty<IStatusManager>())
                .Concat(kinds.Contains("Friendlies")
                    ? friendlies
                    : Array.Empty<IStatusManager>())
                .Concat(kinds.Contains("Self") || declaration.IncludeSelf
                    ? new[] { actor }
                    : Array.Empty<IStatusManager>())
                .ToArray();
            if (pool.Length > 0) AddAction(
                state,
                card,
                pool.OrderBy(RuntimeId).Take(Math.Max(1, declaration.Count)).ToArray(),
                CombatTargetKind.None,
                cost);
        }
    }

    private void AddAction(
        CombatStateObservation state,
        ProjectionCardInstance card,
        IReadOnlyCollection<IStatusManager> targets,
        CombatTargetKind targetKind,
        int cost)
    {
        var semantics = WitchCombatValueEstimator.Estimate(
            card.Config,
            forceAttack: false,
            targetKind);
        var targetRuntimeIds = (targets ?? Array.Empty<IStatusManager>())
            .Where(target => target != null)
            .Select(RuntimeId)
            .Distinct()
            .ToArray();
        var targetRuntimeId = targetRuntimeIds.FirstOrDefault();
        if (targetKind == CombatTargetKind.Enemy && targetRuntimeIds.Length > 1)
        {
            semantics.AffectedEnemyCount = Math.Max(
                semantics.AffectedEnemyCount,
                targetRuntimeIds.Length);
        }
        var candidateId = "projection:card:"
                          + card.RuntimeId
                          + ":"
                          + string.Join("-", targetRuntimeIds);
        var action = new CombatActionObservation
        {
            CandidateId = candidateId,
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
        targetSetsByCandidate[candidateId] = targetRuntimeIds;
        state.Actions.Add(action);
    }

    private bool TryResolveTargets(
        CombatActionObservation action,
        out IReadOnlyList<IStatusManager> targets)
    {
        var ids = targetSetsByCandidate.TryGetValue(action.CandidateId ?? "", out var declared)
            ? declared
            : action.TargetRuntimeId == 0
                ? Array.Empty<int>()
                : new[] { action.TargetRuntimeId };
        var result = new List<IStatusManager>(ids.Length);
        foreach (var id in ids)
        {
            if (!TryStatus(id, out var target) || target == null)
            {
                targets = Array.Empty<IStatusManager>();
                return false;
            }
            result.Add(target);
        }
        targets = result;
        return true;
    }

    private static IReadOnlyList<IStatusManager> StableSample(
        IReadOnlyCollection<IStatusManager> source,
        int count,
        int salt)
    {
        var values = source.OrderBy(RuntimeId).ToArray();
        if (values.Length == 0) return values;
        var take = Math.Min(values.Length, Math.Max(1, count));
        var start = Math.Abs(salt) % values.Length;
        return Enumerable.Range(0, take)
            .Select(index => values[(start + index) % values.Length])
            .ToArray();
    }

    private void Draw(int count, IStatusManager? actor)
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
            drawPile[0].EnterHand(actor);
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

    private static CombatUnitObservation ObserveUnit(
        IStatusManager? status,
        CombatTargetKind kind)
    {
        var actor = status?.fatherObject as OtherObj;
        var result = new CombatUnitObservation
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
        if (status is StatusManager manager)
        {
            try
            {
                foreach (var buff in (manager.GetBuffs() ?? Array.Empty<IBuffItem>()).Take(64))
                {
                    var config = buff?.buffConfig;
                    if (config == null || string.IsNullOrWhiteSpace(config.BuffId)) continue;
                    var level = Math.Max(0, config.Level);
                    result.Features["status:" + config.BuffId] = level;
                    result.Statuses.Add(new CombatStatusObservation
                    {
                        StatusId = config.BuffId,
                        DisplayName = config.BuffName ?? "",
                        Level = level,
                        UpperBound = config.UpperBound,
                        ReducePerTurn = config.ReducePerTurn,
                        ReducePerUse = config.ReducePerUse,
                        ReducePerAttacked = config.ReducePerAttacked,
                        Type = config.Type ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                TerriasLog.Debug("[ProjectionCards] status observation fallback: " + ex.Message);
            }
        }
        result.Features["missingHp"] = Math.Max(0, result.MaxHp - result.CurrentHp);
        result.Features["effectiveHp"] = Math.Max(0, result.CurrentHp + result.Defend);
        return result;
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

}

internal sealed class ProjectionCardInstance
{
    private static int nextRuntimeId = 100000;
    private readonly List<DataConfig> attachments;
    private readonly string cardId;
    private readonly ProjectionDeckCardRecipe? baselineRecipe;
    private DataConfig? config;
    private bool materializationAttempted;
    private bool initialized;
    private int lastRefreshRevision;
    private bool inHandLifecycle;
    private string materializationReason = "";

    private ProjectionCardInstance(
        ProjectionDeckCardRecipe recipe,
        CombatActorCardZone zone,
        int zoneIndex)
    {
        baselineRecipe = recipe;
        cardId = recipe.CardId;
        attachments = new List<DataConfig>();
        Tags = new HashSet<string>(StringComparer.Ordinal);
        Zone = zone;
        ZoneIndex = Math.Max(0, zoneIndex);
        RuntimeId = System.Threading.Interlocked.Increment(ref nextRuntimeId);
    }

    public DataConfig Config => config
        ?? throw new InvalidOperationException(
            "projection baseline card is not materialized: " + cardId);

    public HashSet<string> Tags { get; }

    public CombatActorCardZone Zone { get; set; }

    public int ZoneIndex { get; set; }

    public int RuntimeId { get; }

    public string CardId => cardId;

    public string DisplayName => config?.data.Localize("Name") ?? cardId;

    public bool Retained => Tags.Contains("Froze")
                            || Tags.Contains("Retain")
                            || Tags.Contains("Retained");

    public static ProjectionCardInstance CreateBaseline(
        ProjectionDeckCardRecipe recipe,
        CombatActorCardZone zone,
        int zoneIndex)
    {
        return new ProjectionCardInstance(recipe, zone, zoneIndex);
    }

    public bool TryPrepare(IStatusManager? actor, out string reason)
    {
        if (!TryMaterialize(out reason))
        {
            return false;
        }
        if (!HeadlessSupported(out reason))
        {
            return false;
        }
        if (initialized)
        {
            return true;
        }
        if (actor == null)
        {
            reason = "projection actor is unavailable while initializing card";
            return false;
        }

        try
        {
            PrepareExecutor(Config.scriptExecutor, actor, actor);
            Config.scriptExecutor.RunScript("InitScript");
            foreach (var attachment in attachments)
            {
                PrepareExecutor(attachment.scriptExecutor, actor, actor);
                attachment.scriptExecutor.RunScript("InitScript");
            }
            RefreshTags();
            initialized = true;
            lastRefreshRevision = 0;
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = "projection baseline card initialization failed: " + ex.Message;
            return false;
        }
    }

    public void Refresh(IStatusManager? actor, int stateRevision)
    {
        if (!initialized || actor == null || stateRevision <= lastRefreshRevision)
        {
            return;
        }
        try
        {
            PrepareExecutor(Config.scriptExecutor, actor, actor);
            Config.scriptExecutor.RunScript("InitScript");
            foreach (var attachment in attachments)
            {
                PrepareExecutor(attachment.scriptExecutor, actor, actor);
                attachment.scriptExecutor.RunScript("InitScript");
            }
            RefreshTags();
            lastRefreshRevision = stateRevision;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ProjectionCards] refresh skipped: " + ex.Message);
        }
    }

    public void EnterHand(IStatusManager? actor)
    {
        if (inHandLifecycle) return;
        inHandLifecycle = true;
        RunLifecycleScript("DrawScript", actor);
    }

    public void LeaveHand()
    {
        inHandLifecycle = false;
    }

    public void EnterDiscard(IStatusManager? actor)
    {
        LeaveHand();
        RunLifecycleScript("DropScript", actor);
        if (string.Equals(
                DictionaryUtil.Get(Config.Vars, "NeedRemove"),
                "True",
                StringComparison.OrdinalIgnoreCase))
        {
            Zone = CombatActorCardZone.ExhaustPile;
        }
    }

    public void EndTurn(IStatusManager? actor)
    {
        EnterDiscard(actor);
    }

    private void RunLifecycleScript(string scriptName, IStatusManager? actor)
    {
        if (actor == null || !TryPrepare(actor, out _))
        {
            return;
        }
        var script = DictionaryUtil.Get(Config.data, scriptName);
        if (string.IsNullOrWhiteSpace(script)
            || !ProjectionWrappedCardPolicy.IsLifecycleSafe(CardId, script))
        {
            return;
        }
        try
        {
            PrepareExecutor(Config.scriptExecutor, actor, null);
            Config.scriptExecutor.RunScript(scriptName);
            foreach (var attachment in attachments)
            {
                var attachmentScript = DictionaryUtil.Get(attachment.data, scriptName);
                if (string.IsNullOrWhiteSpace(attachmentScript)
                    || !ProjectionWrappedCardPolicy.IsLifecycleSafe(CardId, attachmentScript))
                {
                    continue;
                }
                PrepareExecutor(attachment.scriptExecutor, actor, null);
                attachment.scriptExecutor.RunScript(scriptName);
            }
            RefreshTags();
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ProjectionCards] " + scriptName + " skipped: " + ex.Message);
        }
    }

    private bool TryMaterialize(out string reason)
    {
        if (config != null)
        {
            reason = "";
            return true;
        }
        if (materializationAttempted || baselineRecipe == null)
        {
            reason = materializationReason.Length > 0
                ? materializationReason
                : "projection baseline card recipe is unavailable";
            return false;
        }

        materializationAttempted = true;
        config = Materialize(
            baselineRecipe.DefinitionType,
            baselineRecipe.CardId,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            out materializationReason);
        if (config == null)
        {
            reason = materializationReason;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(baselineRecipe.AttachmentId))
        {
            var attachment = Materialize(
                baselineRecipe.AttachmentType,
                baselineRecipe.AttachmentId,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                out _);
            if (attachment != null)
            {
                attachments.Add(attachment);
            }
        }
        TerriasPerformanceCounters.Record("Projection.CardMaterialized");
        RefreshTags();
        reason = "";
        return true;
    }

    private void RefreshTags()
    {
        Tags.Clear();
        AddTags(DictionaryUtil.Get(config?.data, "Tag"));
        AddTags(DictionaryUtil.Get(config?.Vars, "Tag"));
        AddTags(DictionaryUtil.Get(config?.Vars, "SpecialTag"));
        foreach (var attachment in attachments)
        {
            AddTags(DictionaryUtil.Get(attachment.data, "Tag"));
            AddTags(DictionaryUtil.Get(attachment.Vars, "Tag"));
            AddTags(DictionaryUtil.Get(attachment.Vars, "SpecialTag"));
        }
    }

    private void AddTags(string value)
    {
        foreach (var tag in (value ?? "").Split(
                     new[] { ',', ';', '|', ' ', '\t', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            Tags.Add(tag.Trim());
        }
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
        var scriptScan = script;
        var capability = ProjectionCardExecutionPolicy.Resolve(Config, CardId, script);
        if (capability.Mode == ProjectionCardExecutionMode.Unsupported)
        {
            reason = "projection card uses wrapped behavior without an actor-safe declaration";
            return false;
        }
        foreach (var attachment in attachments)
        {
            var attachmentId = DictionaryUtil.Get(attachment.data, "Id");
            var attachmentScript = string.Join(
                "\n",
                attachment.data.Values ?? Enumerable.Empty<string>());
            scriptScan += "\n" + attachmentScript;
            if (ProjectionCardExecutionPolicy.Resolve(
                    attachment,
                    attachmentId,
                    attachmentScript).Mode == ProjectionCardExecutionMode.Unsupported)
            {
                reason = "projection card attachment is not actor-safe: " + attachmentId;
                return false;
            }
        }
        return ProjectionCardExecutionPolicy.IsHeadlessScriptSurfaceSafe(
            scriptScan,
            out reason);
    }

    public void Execute(
        ProjectionOtherObj actor,
        IReadOnlyList<IStatusManager> targets)
    {
        ExecuteCore(
            actor,
            targets,
            executor => executor.RunScript("UseScript"));
    }

    public void ExecuteSpecial(
        ProjectionOtherObj actor,
        IStatusManager? target,
        Action<ScriptExecutor> execute)
    {
        ExecuteCore(
            actor,
            target == null ? Array.Empty<IStatusManager>() : new[] { target },
            execute);
    }

    private void ExecuteCore(
        ProjectionOtherObj actor,
        IReadOnlyList<IStatusManager> targets,
        Action<ScriptExecutor> execute)
    {
        var self = actor.Status;
        if (self == null)
        {
            throw new InvalidOperationException("projection status is unavailable");
        }

        PrepareExecutorTargets(Config.scriptExecutor, self, targets);
        if (Config.scriptExecutor is not ScriptExecutor executor)
        {
            throw new InvalidOperationException(
                "projection card executor is unavailable");
        }
        foreach (var attachment in attachments)
        {
            PrepareExecutorTargets(attachment.scriptExecutor, self, targets);
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
        executor.Target = target;
        executor.Object.Clear();
        if (target != null)
        {
            executor.Object.Add(target);
        }
    }

    private static void PrepareExecutorTargets(
        IScriptExecutor executor,
        IStatusManager self,
        IReadOnlyList<IStatusManager> targets)
    {
        executor.Self = self;
        executor.Target = targets?.FirstOrDefault();
        executor.Object.Clear();
        foreach (var target in targets ?? Array.Empty<IStatusManager>())
        {
            if (target != null) executor.Object.Add(target);
        }
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

    private static int ReadInt(IDictionary<string, string>? source, string key)
    {
        return source != null
               && source.TryGetValue(key, out var raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
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
        state.CompleteTurn(projection.Status);
        TerriasLog.InfoAlways("[ProjectionCards] actor turn completed: status="
            + projection.InstanceId
            + ", reason="
            + result.Reason
            + ", forced="
            + result.Forced
            + ", actions="
            + result.CommittedActions
            + ", failures="
            + result.ConsecutiveFailures
            + ", message="
            + (string.IsNullOrWhiteSpace(result.Message)
                ? "<none>"
                : result.Message.Replace('\r', ' ').Replace('\n', ' ')));
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
            || !ProjectionActionAutomationPolicy.DeclaresHeadlessExecutionRoute(
                action.SourceId))
        {
            return false;
        }
        descriptor = new CombatActionAutomationDescriptor
        {
            HeadlessSupported = true,
            FailureScope = CombatAgentFailureScope.Turn,
            Reason = ""
        };
        return true;
    }
}
