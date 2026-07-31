using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AuraCombatAi.Shared;

public sealed class PlayerCombatObservation
{
    public string Protocol { get; set; } = "aura.combat-ai.player-observation.v2";

    public string ObservationId { get; set; } = "";

    public CombatStateObservation State { get; set; } = new();
}

public sealed class CombatDeckKnowledge
{
    public int DrawPileCount { get; set; }

    public int DiscardPileCount { get; set; }

    public int ExhaustPileCount { get; set; }

    public int ShuffleEpoch { get; set; }

    public bool DiscardContentsVisible { get; set; }

    public bool ExhaustContentsVisible { get; set; }

    public List<string> KnownDeckCardIds { get; set; } = new();

    public List<string> KnownTopCardIds { get; set; } = new();

    public List<string> KnownBottomCardIds { get; set; } = new();
}

public enum CombatPublicEventKind
{
    CardRevealed,
    CardDrawn,
    CardDiscarded,
    CardExhausted,
    CardGenerated,
    DeckShuffled
}

public sealed class CombatPublicEvent
{
    public long Sequence { get; set; }

    public CombatPublicEventKind Kind { get; set; }

    public string CardId { get; set; } = "";
}

public sealed class PublicCombatHistory
{
    private readonly List<CombatPublicEvent> events = new();

    public IReadOnlyList<CombatPublicEvent> Events => events;

    public void Record(CombatPublicEvent item)
    {
        if (item != null)
        {
            events.Add(item);
        }
    }

    public void Clear()
    {
        events.Clear();
    }
}

public sealed class CombatBeliefState
{
    public string ObservationId { get; set; } = "";

    public int DrawPileCount { get; set; }

    public int ShuffleEpoch { get; set; }

    public List<string> UnknownDrawCardIds { get; set; } = new();

    public List<string> KnownTopCardIds { get; set; } = new();

    public List<string> KnownBottomCardIds { get; set; } = new();

    public int UnknownSlotCount =>
        Math.Max(
            0,
            DrawPileCount - KnownTopCardIds.Count - KnownBottomCardIds.Count);
}

public static class CombatBeliefTracker
{
    public static CombatBeliefState FromObservation(
        CombatStateObservation state,
        PublicCombatHistory? history = null)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var knowledge = state.DeckKnowledge ?? new CombatDeckKnowledge();
        var remaining = new List<string>(
            knowledge.KnownDeckCardIds.Count > 0
                ? knowledge.KnownDeckCardIds
                : state.DeckCardIds);
        RemoveKnown(remaining, state.HandCardIds);
        if (knowledge.DiscardContentsVisible)
        {
            RemoveKnown(remaining, state.DiscardPileCardIds);
        }
        if (knowledge.ExhaustContentsVisible)
        {
            RemoveKnown(remaining, state.ExhaustPileCardIds);
        }
        RemoveKnown(remaining, knowledge.KnownTopCardIds);
        RemoveKnown(remaining, knowledge.KnownBottomCardIds);

        var unknownSlots = Math.Max(
            0,
            knowledge.DrawPileCount
            - knowledge.KnownTopCardIds.Count
            - knowledge.KnownBottomCardIds.Count);
        if (remaining.Count > unknownSlots)
        {
            remaining.RemoveRange(unknownSlots, remaining.Count - unknownSlots);
        }
        while (remaining.Count < unknownSlots)
        {
            remaining.Add("");
        }

        return new CombatBeliefState
        {
            ObservationId = state.ObservationId,
            DrawPileCount = Math.Max(0, knowledge.DrawPileCount),
            ShuffleEpoch = Math.Max(0, knowledge.ShuffleEpoch),
            UnknownDrawCardIds = remaining,
            KnownTopCardIds = new List<string>(knowledge.KnownTopCardIds),
            KnownBottomCardIds = new List<string>(knowledge.KnownBottomCardIds)
        };
    }

    private static void RemoveKnown(List<string> source, IEnumerable<string>? known)
    {
        foreach (var id in known ?? Array.Empty<string>())
        {
            var index = source.FindIndex(value =>
                string.Equals(value, id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                source.RemoveAt(index);
            }
        }
    }
}

public static class CombatRootDeterminizer
{
    public static List<string> SampleDrawPile(CombatBeliefState belief, int seed)
    {
        if (belief == null) throw new ArgumentNullException(nameof(belief));
        var random = new Random(seed);
        var unknown = new List<string>(belief.UnknownDrawCardIds);
        for (var index = unknown.Count - 1; index > 0; index--)
        {
            var selected = random.Next(index + 1);
            (unknown[index], unknown[selected]) = (unknown[selected], unknown[index]);
        }

        var result = new List<string>(Math.Max(0, belief.DrawPileCount));
        result.AddRange(belief.KnownBottomCardIds);
        result.AddRange(unknown);
        for (var index = belief.KnownTopCardIds.Count - 1; index >= 0; index--)
        {
            result.Add(belief.KnownTopCardIds[index]);
        }
        if (result.Count > belief.DrawPileCount)
        {
            result.RemoveRange(belief.DrawPileCount, result.Count - belief.DrawPileCount);
        }
        while (result.Count < belief.DrawPileCount)
        {
            result.Add("");
        }
        return result;
    }
}

public sealed class CombatRuntimeActionContext
{
    public object? SourceHandle { get; set; }

    public object? TargetHandle { get; set; }
}

public sealed class CombatExecutionBinding
{
    public string ObservationId { get; set; } = "";

    public string ActionToken { get; set; } = "";

    public object? SourceHandle { get; set; }

    public object? TargetHandle { get; set; }
}

public sealed class CombatExecutionContext
{
    private readonly Dictionary<string, CombatExecutionBinding> bindings =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, object> actorBindings = new();

    public string ObservationId { get; set; } = "";

    public void BindActor(int publicActorId, object actorHandle)
    {
        if (publicActorId != 0 && actorHandle != null)
        {
            actorBindings[publicActorId] = actorHandle;
        }
    }

    public bool TryResolveActor<T>(int publicActorId, out T? actor)
        where T : class
    {
        actor = actorBindings.TryGetValue(publicActorId, out var value)
            ? value as T
            : null;
        return actor != null;
    }

    public void Bind(
        CombatActionObservation action,
        object? sourceHandle,
        object? targetHandle)
    {
        if (action == null || string.IsNullOrWhiteSpace(action.ActionToken))
        {
            return;
        }
        bindings[action.ActionToken] = new CombatExecutionBinding
        {
            ObservationId = ObservationId,
            ActionToken = action.ActionToken,
            SourceHandle = sourceHandle,
            TargetHandle = targetHandle
        };
    }

    public bool TryResolve(
        CombatActionObservation action,
        out CombatExecutionBinding binding)
    {
        binding = new CombatExecutionBinding();
        return action != null
               && !string.IsNullOrWhiteSpace(action.ActionToken)
               && string.Equals(
                   action.ObservationId,
                   ObservationId,
                   StringComparison.Ordinal)
               && bindings.TryGetValue(action.ActionToken, out binding!);
    }

    public CombatActionObservation? FindBySourceHandle(
        CombatStateObservation state,
        object sourceHandle,
        object? targetHandle = null)
    {
        if (state == null || sourceHandle == null)
        {
            return null;
        }
        foreach (var action in state.Actions)
        {
            if (!bindings.TryGetValue(action.ActionToken, out var binding)
                || !ReferenceEquals(binding.SourceHandle, sourceHandle))
            {
                continue;
            }
            if (targetHandle == null || ReferenceEquals(binding.TargetHandle, targetHandle))
            {
                return action;
            }
        }
        return null;
    }
}

public static class CombatDecisionExecutionBindingProtocol
{
    public static bool TryBindToObservation(
        CombatDecision template,
        CombatStateObservation observation,
        out CombatDecision bound,
        out string reason)
    {
        bound = CloneDecision(template);
        if (template == null)
        {
            reason = "decision is null";
            return false;
        }
        if (!template.HasAction || template.Action == null)
        {
            reason = "";
            return true;
        }
        if (observation == null
            || string.IsNullOrWhiteSpace(observation.ObservationId))
        {
            reason = "current observation is unavailable";
            return false;
        }

        var actions = (observation.Actions
                       ?? new List<CombatActionObservation>())
            .Where(action => action != null
                             && string.Equals(
                                 action.ObservationId,
                                 observation.ObservationId,
                                 StringComparison.Ordinal))
            .ToList();
        var selected = actions.FirstOrDefault(action => string.Equals(
            action.CandidateId,
            template.Action.CandidateId,
            StringComparison.Ordinal));
        if (selected == null)
        {
            reason = "selected candidate is no longer present in the current observation";
            return false;
        }
        if (!selected.Legal)
        {
            reason = string.IsNullOrWhiteSpace(selected.RejectionReason)
                ? "selected candidate is no longer legal"
                : selected.RejectionReason;
            return false;
        }
        if (string.IsNullOrWhiteSpace(selected.ActionToken))
        {
            reason = "selected candidate has no current execution token";
            return false;
        }

        bound.Action = selected;
        bound.Candidates = template.Candidates
            .Select(candidate => CloneCandidate(
                candidate,
                actions.FirstOrDefault(action => string.Equals(
                    action.CandidateId,
                    candidate.Action?.CandidateId,
                    StringComparison.Ordinal))
                ?? candidate.Action))
            .ToList();
        reason = "";
        return true;
    }

    private static CombatDecision CloneDecision(CombatDecision? source)
    {
        if (source == null)
        {
            return new CombatDecision();
        }
        return new CombatDecision
        {
            HasAction = source.HasAction,
            Action = source.Action,
            Score = source.Score,
            Reason = source.Reason,
            ProfileId = source.ProfileId,
            Candidates = source.Candidates.ToList(),
            Plan = source.Plan.ToList(),
            PlanSummary = source.PlanSummary,
            EndTurnTrace = source.EndTurnTrace,
            SearchSimulations = source.SearchSimulations,
            SearchNodes = source.SearchNodes,
            SearchTranspositionHits = source.SearchTranspositionHits,
            SearchStoppedEarly = source.SearchStoppedEarly,
            SearchBudgetTier = source.SearchBudgetTier,
            CertifiedLoops = source.CertifiedLoops,
            SustainableControlLoops = source.SustainableControlLoops,
            FakeLoops = source.FakeLoops,
            BlockedLoops = source.BlockedLoops,
            SearchAlgorithm = source.SearchAlgorithm
        };
    }

    private static CombatCandidateEvaluation CloneCandidate(
        CombatCandidateEvaluation source,
        CombatActionObservation action)
    {
        return new CombatCandidateEvaluation
        {
            Action = action,
            Legal = source.Legal,
            RejectionReason = source.RejectionReason,
            Utility = source.Utility,
            BaseRuleScore = source.BaseRuleScore,
            RawResidualScore = source.RawResidualScore,
            ResidualApplicability = source.ResidualApplicability,
            AppliedResidualScore = source.AppliedResidualScore,
            RuleScore = source.RuleScore,
            PlanScore = source.PlanScore,
            SearchPrior = source.SearchPrior,
            SearchVisits = source.SearchVisits,
            SearchDeathRisk = source.SearchDeathRisk
        };
    }
}

public interface IPlayerCombatObservationProvider
{
    bool TryCapturePlayerObservation(
        out PlayerCombatObservation observation,
        out string reason);
}

public interface ICombatRuntimePreflightRule
{
    bool IsLegal(
        CombatStateObservation state,
        CombatActionObservation action,
        CombatRuntimeActionContext runtime,
        out string reason);
}

public enum CombatPublicFeatureScope
{
    State,
    Unit,
    Action,
    StateChange
}

public static class CombatPublicFeatureRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Registration> Registrations =
        new(StringComparer.OrdinalIgnoreCase);

    public static IDisposable Register(
        string ownerId,
        CombatPublicFeatureScope scope,
        string featureKey)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
        {
            throw new ArgumentException("feature key is required", nameof(featureKey));
        }
        var key = (ownerId ?? "") + "|" + scope + "|" + featureKey.Trim();
        lock (Gate)
        {
            Registrations[key] = new Registration(scope, featureKey.Trim());
        }
        return new RegistrationLease(key);
    }

    public static bool IsRegistered(
        CombatPublicFeatureScope scope,
        string featureKey)
    {
        lock (Gate)
        {
            return Registrations.Values.Any(item =>
                item.Scope == scope
                && string.Equals(
                    item.FeatureKey,
                    featureKey,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class Registration
    {
        public Registration(CombatPublicFeatureScope scope, string featureKey)
        {
            Scope = scope;
            FeatureKey = featureKey;
        }

        public CombatPublicFeatureScope Scope { get; }

        public string FeatureKey { get; }
    }

    private sealed class RegistrationLease : IDisposable
    {
        private readonly string key;
        private bool disposed;

        public RegistrationLease(string key)
        {
            this.key = key;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            lock (Gate)
            {
                Registrations.Remove(key);
            }
        }
    }
}

public static class CombatPublicFeaturePolicy
{
    private static readonly HashSet<string> StateKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "turn",
            "handLimit",
            "drawPile",
            "drawPileCount",
            "discardPile",
            "discardPileCount",
            "exhaustPile",
            "exhaustPileCount",
            "deckCount",
            "drawPerTurn",
            "expectedBlockableDamage",
            "maximumBlockableDamage",
            "expectedUnblockableDamage",
            "expectedDamageOverTime",
            "expectedIncomingDamage",
            "attackProbability",
            "threatConfidence",
            "currentIntentKnown",
            "recyclableCardCount",
            "turnsToReshuffle",
            "cycleAccessRate",
            "playerHp",
            "playerMaxHp",
            "playerHpRatio",
            "playerDefend",
            "power",
            "maxPower",
            "nextTurnPowerOnEnd",
            "bankedSurplusPower",
            "expiringPower",
            "handCount",
            "retainedHandCount",
            "enemyCount",
            "enemyHpTotal",
            "blockableThreat",
            "stepCount",
            "setupValue",
            "persistentValue",
            "damageMultiplier",
            "uncertainty",
            "turnActionsTaken",
            "turnEnergySpent",
            "enemyHpAtTurnStart",
            "consecutiveNoProgressTurns",
            "turnSequence",
            "endTurnPurposeValue",
            "endTurnPurposeCount",
            "endTurnLifecycleHpLoss",
            "endTurnLifecycleHeal",
            "endTurnLifecycleDefend",
            "endTurnLifecyclePowerGain",
            "endTurnLifecyclePowerLoss",
            "endTurnLifecycleDraw",
            "startTurnLifecycleHpLoss",
            "startTurnLifecycleHeal",
            "startTurnLifecycleDefend",
            "startTurnLifecyclePowerGain",
            "startTurnLifecyclePowerLoss",
            "startTurnLifecycleDraw",
            "unknownLifecycleEffectCount",
            "archetype:rebirth.score",
            "archetype:rebirth.committed",
            "archetype:rebirth.commitment",
            "archetype:time-cage.score",
            "archetype:time-cage.committed",
            "archetype:time-cage.commitment",
            "mechanic:rebirth.stacks",
            "mechanic:rebirth.count",
            "mechanic:rebirth.keen-edge",
            "mechanic:rebirth.phase",
            "mechanic:time-cage.count",
            "mechanic:time-cage.phase",
            "mechanic:time-cage.payload-value"
        };

    private static readonly HashSet<string> UnitKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "damageLimitActive",
            "damageLimitLevel",
            "escalationPressure"
        };

    private static readonly HashSet<string> ActionKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "handIndex",
            "isCard",
            "isSkill",
            "strategyCompletion",
            "strategyInfinite",
            "strategyExecutable",
            "strategyDeterministic",
            "synergy",
            "visibleFake",
            "curse",
            "unplayable",
            "semanticUnavailable",
            "requiresAvailableDeckCard",
            "hasVisibleWarning",
            "retain",
            "inherent",
            "recycle",
            "ouroboros",
            "exhaustOnUse",
            "cost",
            "ruleScore",
            "baseRuleScore",
            "planScore",
            "damage",
            "trueDamage",
            "damageOverTime",
            "selfHpLoss",
            "endOfCycleSelfHpLoss",
            "hitCount",
            "defend",
            "heal",
            "draw",
            "energyGain",
            "scaling",
            "deckValue",
            "buff",
            "debuff",
            "cleanse",
            "costReduction",
            "cardGeneration",
            "persistentValue",
            "damageMultiplierGain",
            "cooldownTurns",
            "risk",
            "uncertainty",
            "endTurnSevereMistake",
            "endTurnSafeAlternativeCount",
            "endTurnPlayableCardCount",
            "endTurnUnusedEnergy",
            "endTurnAvoidableUnusedEnergy",
            "endTurnExpiringEnergy",
            "endTurnBankedSurplusEnergy",
            "endTurnDominated",
            "endTurnAvoidableLethal",
            "endTurnCertifiedCycleCount",
            "endTurnReachableCycleCount",
            "endTurnDominanceMargin",
            "endTurnUnknownLifecycleCount",
            "endTurnPurposeValue",
            "opensInteraction",
            "randomOutcome",
            "targetHp",
            "targetMaxHp",
            "targetDefend",
            "targetHpRatio",
            "targetIsEnemy",
            "targetIsSelf",
            "powerAfterCost",
            "power",
            "handCount",
            "playerHp",
            "playerHpRatio",
            "expectedIncomingDamage",
            "expectedBlockableDamage",
            "maximumBlockableDamage",
            "expectedUnblockableDamage",
            "expectedDamageOverTime",
            "attackProbability",
            "threatConfidence",
            "currentIntentKnown",
            "isFreeAction",
            "requiredDefend",
            "immediateDefend",
            "shieldCarryGain",
            "usefulDefend",
            "wastedDefend",
            "effectiveHeal",
            "overheal",
            "effectiveDraw",
            "overdraw",
            "effectiveDamage",
            "effectiveHpDamage",
            "effectiveDurabilityDamage",
            "damagePreventedByLimit",
            "damageLimitActive",
            "marginalSetupValue",
            "overkill",
            "lethal",
            "energyScarcity",
            "freeKnownValue",
            "semanticConfidence",
            "utilitySurvival",
            "utilityLethal",
            "utilityTempo",
            "utilityResource",
            "utilityDeckEconomy",
            "utilityScaling",
            "utilitySynergy",
            "utilityContinuation",
            "utilityRisk",
            "utilityUncertainty",
            "utilityCoordination",
            "categoryAttack",
            "categoryDefend",
            "categorySupport",
            "categorySkill",
            "categoryOther",
            "mechanic:card-use-count",
            "mechanic:rebirth.phase",
            "mechanic:time-cage.count",
            "mechanic:time-cage.payload",
            "mechanic:time-cage.operator",
            "mechanic:time-cage.reverse",
            "mechanic:time-cage.first-repeats",
            "mechanic:time-cage.package",
            "mechanic:time-cage.last-repeats"
        };

    public static Dictionary<string, double> SanitizeState(
        IReadOnlyDictionary<string, double>? values)
    {
        return Sanitize(values, key =>
            StateKeys.Contains(key)
            || key.StartsWith("deck:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("hand:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("retainedHand:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("discard:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("exhaust:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("playerStatus:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("enemyStatus:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("enemy:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("enemyHp:", StringComparison.OrdinalIgnoreCase)
            || CombatPublicFeatureRegistry.IsRegistered(
                CombatPublicFeatureScope.State,
                key));
    }

    public static Dictionary<string, double> SanitizeUnit(
        IReadOnlyDictionary<string, double>? values)
    {
        return Sanitize(values, key =>
            UnitKeys.Contains(key)
            || key.StartsWith("status:", StringComparison.OrdinalIgnoreCase)
            || CombatPublicFeatureRegistry.IsRegistered(
                CombatPublicFeatureScope.Unit,
                key));
    }

    public static Dictionary<string, double> SanitizeAction(
        IReadOnlyDictionary<string, double>? values)
    {
        return Sanitize(values, key =>
            ActionKeys.Contains(key)
            || key.StartsWith("stateChange:", StringComparison.OrdinalIgnoreCase)
            || CombatPublicFeatureRegistry.IsRegistered(
                CombatPublicFeatureScope.Action,
                key));
    }

    public static Dictionary<string, double> SanitizeStateChanges(
        IReadOnlyDictionary<string, double>? values)
    {
        return Sanitize(values, key =>
            string.Equals(key, "player.hp", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("status:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("playerStatus:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("enemyStatus:", StringComparison.OrdinalIgnoreCase)
            || CombatPublicFeatureRegistry.IsRegistered(
                CombatPublicFeatureScope.StateChange,
                key));
    }

    private static Dictionary<string, double> Sanitize(
        IReadOnlyDictionary<string, double>? values,
        Func<string, bool> permitted)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values ?? new Dictionary<string, double>())
        {
            if (!string.IsNullOrWhiteSpace(pair.Key)
                && permitted(pair.Key)
                && !double.IsNaN(pair.Value)
                && !double.IsInfinity(pair.Value))
            {
                result[pair.Key] = pair.Value;
            }
        }
        return result;
    }
}

public static class CombatPlayerObservationBoundary
{
    public static CombatStateObservation Normalize(CombatStateObservation source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var result = new CombatStateObservation
        {
            InformationBoundaryVersion = 2,
            ObservationId = string.IsNullOrWhiteSpace(source.ObservationId)
                ? BuildObservationId(source.BattleSessionId, source.Sequence)
                : source.ObservationId,
            BattleSessionId = source.BattleSessionId,
            Sequence = source.Sequence,
            Player = CloneUnit(source.Player),
            Friendlies = source.Friendlies.Select(CloneUnit).ToList(),
            Enemies = source.Enemies.Select(CloneUnit).ToList(),
            CurrentPower = source.CurrentPower,
            MaxPower = source.MaxPower,
            HandCount = source.HandCount,
            HandCardIds = CleanIds(source.HandCardIds),
            RetainedHandCardIds = CleanIds(source.RetainedHandCardIds),
            DeckCardIds = CleanMultiset(source.DeckCardIds),
            DiscardPileCardIds = source.DeckKnowledge?.DiscardContentsVisible == true
                ? CleanIds(source.DiscardPileCardIds)
                : new List<string>(),
            ExhaustPileCardIds = source.DeckKnowledge?.ExhaustContentsVisible == true
                ? CleanIds(source.ExhaustPileCardIds)
                : new List<string>(),
            DeferredEffects = (source.DeferredEffects
                               ?? new List<CombatDeferredEffectObservation>())
                .Where(item => item != null
                               && !string.IsNullOrWhiteSpace(item.SourceId))
                .OrderBy(item => item.Sequence)
                .Select(item => new CombatDeferredEffectObservation
                {
                    Sequence = Math.Max(0, item.Sequence),
                    StatusId = item.StatusId ?? "",
                    SourceId = item.SourceId ?? "",
                    TargetRuntimeId = item.TargetRuntimeId,
                    Semantics = NormalizeSemantics(item.Semantics)
                })
                .ToList(),
            DeckKnowledge = CloneDeckKnowledge(source),
            ExpectedIncomingDamage = Finite(source.ExpectedIncomingDamage),
            Threat = CloneThreat(source.Threat),
            Features = CombatPublicFeaturePolicy.SanitizeState(source.Features),
            IsPlayerActionWindow = source.IsPlayerActionWindow,
            UiBusy = source.UiBusy
        };
        foreach (var action in source.Actions ?? new List<CombatActionObservation>())
        {
            result.Actions.Add(CloneAction(action, result.ObservationId));
        }
        CombatArchetypePolicy.Enrich(result);
        result.Fingerprint = CombatPublicObservationHasher.Hash(result);
        return result;
    }

    public static PlayerCombatObservation Wrap(CombatStateObservation state)
    {
        var normalized = Normalize(state);
        return new PlayerCombatObservation
        {
            ObservationId = normalized.ObservationId,
            State = normalized
        };
    }

    public static string BuildObservationId(long battleSessionId, long sequence)
    {
        return battleSessionId.ToString(CultureInfo.InvariantCulture)
               + ":"
               + sequence.ToString(CultureInfo.InvariantCulture);
    }

    private static CombatDeckKnowledge CloneDeckKnowledge(CombatStateObservation source)
    {
        var value = source.DeckKnowledge ?? new CombatDeckKnowledge();
        return new CombatDeckKnowledge
        {
            DrawPileCount = Math.Max(
                0,
                value.DrawPileCount > 0
                    ? value.DrawPileCount
                    : FeatureCount(source.Features, "drawPileCount")),
            DiscardPileCount = Math.Max(
                0,
                value.DiscardPileCount > 0
                    ? value.DiscardPileCount
                    : FeatureCount(source.Features, "discardPileCount")),
            ExhaustPileCount = Math.Max(
                0,
                value.ExhaustPileCount > 0
                    ? value.ExhaustPileCount
                    : FeatureCount(source.Features, "exhaustPileCount")),
            ShuffleEpoch = Math.Max(0, value.ShuffleEpoch),
            DiscardContentsVisible = value.DiscardContentsVisible,
            ExhaustContentsVisible = value.ExhaustContentsVisible,
            KnownDeckCardIds = CleanMultiset(
                value.KnownDeckCardIds.Count > 0
                    ? value.KnownDeckCardIds
                    : source.DeckCardIds),
            KnownTopCardIds = CleanIds(value.KnownTopCardIds),
            KnownBottomCardIds = CleanIds(value.KnownBottomCardIds)
        };
    }

    private static CombatUnitObservation CloneUnit(CombatUnitObservation? source)
    {
        source ??= new CombatUnitObservation();
        return new CombatUnitObservation
        {
            RuntimeId = source.RuntimeId,
            DefinitionId = source.DefinitionId ?? "",
            Name = source.Name ?? "",
            Kind = source.Kind,
            CurrentHp = source.CurrentHp,
            MaxHp = source.MaxHp,
            Defend = source.Defend,
            Attack = Finite(source.Attack),
            Statuses = (source.Statuses ?? new List<CombatStatusObservation>())
                .Where(status => status != null)
                .GroupBy(status => status.StatusId ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(group => CloneStatus(group.First()))
                .ToList(),
            Features = CombatPublicFeaturePolicy.SanitizeUnit(source.Features)
        };
    }

    private static CombatStatusObservation CloneStatus(CombatStatusObservation source)
    {
        return new CombatStatusObservation
        {
            StatusId = source.StatusId ?? "",
            DisplayName = source.DisplayName ?? "",
            Level = source.Level,
            UpperBound = source.UpperBound,
            ReducePerTurn = source.ReducePerTurn,
            ReducePerUse = source.ReducePerUse,
            ReducePerAttacked = source.ReducePerAttacked,
            Type = source.Type ?? ""
        };
    }

    private static CombatActionObservation CloneAction(
        CombatActionObservation source,
        string observationId)
    {
        return new CombatActionObservation
        {
            ObservationId = observationId,
            ActionToken = string.IsNullOrWhiteSpace(source.ActionToken)
                ? source.CandidateId ?? ""
                : source.ActionToken,
            CandidateId = source.CandidateId ?? "",
            SourceId = source.SourceId ?? "",
            DisplayName = source.DisplayName ?? "",
            Kind = source.Kind,
            RuntimeId = source.RuntimeId,
            TargetRuntimeId = source.TargetRuntimeId,
            TargetKind = source.TargetKind,
            Cost = source.Cost,
            Legal = source.Legal,
            RejectionReason = source.RejectionReason ?? "",
            Semantics = NormalizeSemantics(source.Semantics),
            SemanticSource = source.SemanticSource ?? "",
            SemanticFidelity = source.SemanticFidelity,
            Features = CombatPublicFeaturePolicy.SanitizeAction(source.Features)
        };
    }

    public static CombatActionSemantics NormalizeSemantics(
        CombatActionSemantics? source)
    {
        source ??= new CombatActionSemantics();
        return new CombatActionSemantics
        {
            Damage = Finite(source.Damage),
            TrueDamage = Finite(source.TrueDamage),
            DamageOverTime = Finite(source.DamageOverTime),
            SelfHpLoss = Finite(source.SelfHpLoss),
            EndOfCycleSelfHpLoss = Finite(source.EndOfCycleSelfHpLoss),
            HitCount = Finite(source.HitCount),
            Defend = Finite(source.Defend),
            Heal = Finite(source.Heal),
            Draw = Finite(source.Draw),
            EnergyGain = Finite(source.EnergyGain),
            Scaling = Finite(source.Scaling),
            DeckValue = Finite(source.DeckValue),
            Buff = Finite(source.Buff),
            Debuff = Finite(source.Debuff),
            Cleanse = Finite(source.Cleanse),
            CostReduction = Finite(source.CostReduction),
            CardGeneration = Finite(source.CardGeneration),
            PersistentValue = Finite(source.PersistentValue),
            DamageMultiplierGain = Finite(source.DamageMultiplierGain),
            ImmediateHpDamage = Finite(source.ImmediateHpDamage),
            ImmediateDurabilityDamage =
                Finite(source.ImmediateDurabilityDamage),
            DeferredHpDamage = Finite(source.DeferredHpDamage),
            AffectedEnemyCount = Math.Max(0, source.AffectedEnemyCount),
            TargetEffects = source.TargetEffects.Select(item =>
                new CombatTargetedSemanticEffect
                {
                    Phase = item.Phase,
                    Kind = item.Kind,
                    TargetRuntimeId = item.TargetRuntimeId,
                    DefinitionId = item.DefinitionId ?? "",
                    Trigger = item.Trigger ?? "",
                    RawAmount = Finite(item.RawAmount),
                    EffectiveAmount = Finite(item.EffectiveAmount),
                    EffectiveDurabilityAmount =
                        Finite(item.EffectiveDurabilityAmount),
                    Probability = Math.Max(
                        0d,
                        Math.Min(1d, Finite(item.Probability))),
                    BypassesBlock = item.BypassesBlock,
                    Contextual = item.Contextual
                }).ToList(),
            StateChanges = CombatPublicFeaturePolicy.SanitizeStateChanges(
                source.StateChanges),
            CooldownTurns = Finite(source.CooldownTurns),
            Risk = Finite(source.Risk),
            Uncertainty = Finite(source.Uncertainty),
            OpensInteraction = source.OpensInteraction,
            RandomOutcome = source.RandomOutcome
        };
    }

    private static CombatThreatForecast CloneThreat(CombatThreatForecast? source)
    {
        source ??= new CombatThreatForecast();
        return new CombatThreatForecast
        {
            CurrentIntentKnown = source.CurrentIntentKnown,
            IntentPoolSize = Math.Max(0, source.IntentPoolSize),
            AttackProbability = Probability(source.AttackProbability),
            ExpectedBlockableDamage = Finite(source.ExpectedBlockableDamage),
            MaximumBlockableDamage = Finite(source.MaximumBlockableDamage),
            ExpectedUnblockableDamage = Finite(source.ExpectedUnblockableDamage),
            ExpectedDamageOverTime = Finite(source.ExpectedDamageOverTime),
            LethalProbability = Probability(source.LethalProbability),
            Confidence = Probability(source.Confidence),
            Summary = source.Summary ?? "",
            Intents = (source.Intents ?? new List<CombatIntentObservation>())
                .Select(intent => new CombatIntentObservation
                {
                    SourceId = intent.SourceId ?? "",
                    DisplayName = intent.DisplayName ?? "",
                    Kind = intent.Kind,
                    SourceRuntimeId = intent.SourceRuntimeId,
                    Probability = Probability(intent.Probability),
                    BlockableDamage = Finite(intent.BlockableDamage),
                    UnblockableDamage = Finite(intent.UnblockableDamage),
                    DamageOverTime = Finite(intent.DamageOverTime),
                    Confidence = Probability(intent.Confidence),
                    Current = intent.Current
                })
                .ToList()
        };
    }

    private static List<string> CleanIds(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
    }

    private static List<string> CleanMultiset(IEnumerable<string>? values)
    {
        return CleanIds(values)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int FeatureCount(
        IReadOnlyDictionary<string, double>? features,
        string key)
    {
        return features != null
               && features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? Math.Max(0, (int)Math.Round(value))
            : 0;
    }

    private static double Probability(double value)
    {
        return Math.Max(0d, Math.Min(1d, Finite(value)));
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}

public static class CombatPublicObservationHasher
{
    public static string Hash(CombatStateObservation state)
    {
        if (state == null)
        {
            return "";
        }
        var builder = new StringBuilder(1024);
        Append(builder, state.BattleSessionId);
        Append(builder, state.Player?.CurrentHp ?? 0);
        Append(builder, state.Player?.MaxHp ?? 0);
        Append(builder, state.Player?.Defend ?? 0);
        Append(builder, state.CurrentPower);
        Append(builder, state.MaxPower);
        Append(builder, state.HandCount);
        AppendUnit(builder, state.Player);
        foreach (var unit in state.Friendlies ?? new List<CombatUnitObservation>())
        {
            AppendUnit(builder, unit);
        }
        foreach (var unit in state.Enemies ?? new List<CombatUnitObservation>())
        {
            AppendUnit(builder, unit);
        }
        foreach (var id in state.HandCardIds ?? new List<string>())
        {
            Append(builder, id);
        }
        foreach (var id in state.DiscardPileCardIds ?? new List<string>())
        {
            Append(builder, id);
        }
        foreach (var id in state.ExhaustPileCardIds ?? new List<string>())
        {
            Append(builder, id);
        }
        var deck = state.DeckKnowledge ?? new CombatDeckKnowledge();
        Append(builder, deck.DrawPileCount);
        Append(builder, deck.DiscardPileCount);
        Append(builder, deck.ExhaustPileCount);
        Append(builder, deck.ShuffleEpoch);
        foreach (var id in deck.KnownDeckCardIds)
        {
            Append(builder, id);
        }
        foreach (var id in deck.KnownTopCardIds)
        {
            Append(builder, id);
        }
        foreach (var effect in state.DeferredEffects
                     ?? new List<CombatDeferredEffectObservation>())
        {
            Append(builder, effect.Sequence);
            Append(builder, effect.StatusId);
            Append(builder, effect.SourceId);
            Append(builder, effect.TargetRuntimeId);
        }
        foreach (var intent in state.Threat?.Intents
                     ?? new List<CombatIntentObservation>())
        {
            Append(builder, intent.SourceRuntimeId);
            Append(builder, intent.SourceId);
            Append(builder, intent.Kind);
            Append(builder, intent.Probability);
            Append(builder, intent.BlockableDamage);
            Append(builder, intent.UnblockableDamage);
            Append(builder, intent.DamageOverTime);
        }
        foreach (var pair in CombatPublicFeaturePolicy.SanitizeState(state.Features)
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(builder, pair.Key);
            Append(builder, pair.Value);
        }
        foreach (var action in state.Actions ?? new List<CombatActionObservation>())
        {
            Append(builder, action.CandidateId);
            Append(builder, action.SourceId);
            Append(builder, action.TargetRuntimeId);
            Append(builder, action.Cost);
            Append(builder, action.Legal ? 1 : 0);
        }
        return Fnv1A(builder.ToString()).ToString("x16", CultureInfo.InvariantCulture);
    }

    public static int Seed(CombatStateObservation state, int sampleIndex)
    {
        unchecked
        {
            var hash = Fnv1A(Hash(state));
            hash ^= (ulong)(uint)sampleIndex;
            hash *= 1099511628211UL;
            return (int)(hash ^ (hash >> 32));
        }
    }

    private static void AppendUnit(StringBuilder builder, CombatUnitObservation? unit)
    {
        if (unit == null)
        {
            Append(builder, "null");
            return;
        }
        Append(builder, unit.RuntimeId);
        Append(builder, unit.DefinitionId);
        Append(builder, unit.CurrentHp);
        Append(builder, unit.MaxHp);
        Append(builder, unit.Defend);
        foreach (var status in unit.Statuses
                     .OrderBy(value => value.StatusId, StringComparer.OrdinalIgnoreCase))
        {
            Append(builder, status.StatusId);
            Append(builder, status.Level);
        }
    }

    private static void Append(StringBuilder builder, object? value)
    {
        if (value is IFormattable formattable)
        {
            builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append(value);
        }
        builder.Append('|');
    }

    private static ulong Fnv1A(string value)
    {
        unchecked
        {
            var hash = 1469598103934665603UL;
            foreach (var character in value ?? "")
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
