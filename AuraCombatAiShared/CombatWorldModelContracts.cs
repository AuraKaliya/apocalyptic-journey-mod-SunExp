using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public static class CombatWorldModelProtocol
{
    public const string ObservationProtocol =
        "aura.combat-world-model.observation.v1";

    public const string ActionProtocol =
        "aura.combat-world-model.action.v1";

    public const string TransitionProtocol =
        "aura.combat-world-model.transition.v1";

    public const int TokenSchemaVersion = 2;
}

public enum CombatInformationVisibility
{
    PublicExact,
    PublicDerived,
    Belief,
    Unknown
}

public enum CombatObjectTokenKind
{
    Global,
    Role,
    Familiar,
    Friendly,
    Enemy,
    EnemyIntent,
    Status,
    HandCard,
    DrawTop,
    DrawBottom,
    DrawBelief,
    DiscardCard,
    ExhaustCard,
    Relic,
    Blessing,
    Difficulty,
    Resource,
    DeferredEffect,
    HistoryEvent,
    ActionCandidate,
    CampaignGlobal,
    CampaignDeckCard,
    CampaignReserveCard,
    CampaignRelic,
    CampaignBlessing,
    CampaignAttribute,
    BuildGoal
}

public enum CombatCoverageStage
{
    Missing,
    Present,
    Observable,
    Encoded,
    Trained,
    Validated,
    Active
}

public sealed class CombatObjectToken
{
    public string TokenId { get; set; } = "";

    public CombatObjectTokenKind Kind { get; set; }

    public string DefinitionId { get; set; } = "";

    public int RuntimeId { get; set; }

    public int OwnerRuntimeId { get; set; }

    public string Zone { get; set; } = "";

    public int Position { get; set; } = -1;

    public int Count { get; set; } = 1;

    public CombatInformationVisibility Visibility { get; set; } =
        CombatInformationVisibility.PublicExact;

    public Dictionary<string, double> Values { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Relations { get; set; } = new();
}

public sealed class CombatWorldModelCoverageEntry
{
    public string Domain { get; set; } = "";

    public CombatCoverageStage Stage { get; set; }

    public int PresentCount { get; set; }

    public int EncodedCount { get; set; }

    public string Reason { get; set; } = "";
}

public sealed class CombatWorldModelCoverageManifest
{
    public int TokenSchemaVersion { get; set; } =
        CombatWorldModelProtocol.TokenSchemaVersion;

    public List<CombatWorldModelCoverageEntry> Entries { get; set; } = new();

    public CombatCoverageStage Stage(string domain)
    {
        return Entries.FirstOrDefault(item => string.Equals(
                   item.Domain,
                   domain,
                   StringComparison.OrdinalIgnoreCase))?.Stage
               ?? CombatCoverageStage.Missing;
    }
}

public sealed class CombatTypedActionEnvelope
{
    public string Protocol { get; set; } =
        CombatWorldModelProtocol.ActionProtocol;

    public string CandidateId { get; set; } = "";

    public CombatActionKind ActionType { get; set; }

    public string SourceDefinitionId { get; set; } = "";

    public int SourceRuntimeId { get; set; }

    public CombatTargetKind TargetKind { get; set; }

    public List<int> TargetRuntimeIds { get; set; } = new();

    public int ResourceCost { get; set; }

    public string SourceZone { get; set; } = "";

    public bool CardInstanceBound { get; set; }

    public bool SkillLifecycleBound { get; set; }

    public bool Legal { get; set; }

    public string RejectionReason { get; set; } = "";

    public CombatTransitionEnvelope Transition { get; set; } = new();
}

public sealed class CombatTransitionEnvelope
{
    public string Protocol { get; set; } =
        CombatWorldModelProtocol.TransitionProtocol;

    public bool Legal { get; set; }

    public string RejectionReason { get; set; } = "";

    public bool Deterministic { get; set; }

    public bool HasUnknownResidual { get; set; }

    public string SemanticSource { get; set; } = "";

    public CombatKnowledgeFidelity SemanticFidelity { get; set; } =
        CombatKnowledgeFidelity.Unsupported;

    public CombatActionSemantics DeterministicAfterstate { get; set; } = new();

    public List<CombatActionOutcome> ExactChanceOutcomes { get; set; } = new();
}

public sealed class CombatObservationEnvelope
{
    public string Protocol { get; set; } =
        CombatWorldModelProtocol.ObservationProtocol;

    public int TokenSchemaVersion { get; set; } =
        CombatWorldModelProtocol.TokenSchemaVersion;

    public string ObservationId { get; set; } = "";

    public long BattleSessionId { get; set; }

    public long Sequence { get; set; }

    public string PublicFingerprint { get; set; } = "";

    public List<CombatObjectToken> Tokens { get; set; } = new();

    public List<CombatTypedActionEnvelope> LegalActions { get; set; } = new();

    public CombatWorldModelCoverageManifest Coverage { get; set; } = new();
}

public sealed class CombatCampaignObservationEnvelope
{
    public string Protocol { get; set; } =
        "aura.combat-world-model.campaign-observation.v1";

    public int TokenSchemaVersion { get; set; } =
        CombatWorldModelProtocol.TokenSchemaVersion;

    public ulong WorldSeed { get; set; }

    public int CurrentLayer { get; set; }

    public List<CombatObjectToken> Tokens { get; set; } = new();
}

public static class CombatWorldModelTokenizer
{
    public static CombatObservationEnvelope Build(CombatStateObservation state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        return BuildNormalizedOwned(
            CombatPlayerObservationBoundary.Normalize(state));
    }

    internal static CombatObservationEnvelope BuildNormalizedOwned(
        CombatStateObservation normalized)
    {
        if (normalized == null)
        {
            throw new ArgumentNullException(nameof(normalized));
        }
        var result = new CombatObservationEnvelope
        {
            ObservationId = normalized.ObservationId,
            BattleSessionId = normalized.BattleSessionId,
            Sequence = normalized.Sequence,
            PublicFingerprint = normalized.Fingerprint
        };

        AddGlobal(result.Tokens, normalized);
        AddUnit(
            result.Tokens,
            normalized.Player,
            CombatObjectTokenKind.Role,
            "role");
        for (var index = 0; index < normalized.Friendlies.Count; index++)
        {
            AddUnit(
                result.Tokens,
                normalized.Friendlies[index],
                index == 0
                    ? CombatObjectTokenKind.Familiar
                    : CombatObjectTokenKind.Friendly,
                "friendly:" + index.ToString(CultureInfo.InvariantCulture));
        }
        for (var index = 0; index < normalized.Enemies.Count; index++)
        {
            AddUnit(
                result.Tokens,
                normalized.Enemies[index],
                CombatObjectTokenKind.Enemy,
                "enemy:" + index.ToString(CultureInfo.InvariantCulture));
        }
        AddEnemyIntents(result.Tokens, normalized);

        AddHand(result.Tokens, normalized);
        AddDeckKnowledge(result.Tokens, normalized);
        AddVisiblePile(
            result.Tokens,
            normalized.DiscardPileCardIds,
            CombatObjectTokenKind.DiscardCard,
            "discard");
        AddVisiblePile(
            result.Tokens,
            normalized.ExhaustPileCardIds,
            CombatObjectTokenKind.ExhaustCard,
            "exhaust");
        AddDeferredEffects(result.Tokens, normalized);
        AddPrefixedStateTokens(result.Tokens, normalized.Features);

        foreach (var action in normalized.Actions.Where(item => item != null))
        {
            var envelope = ActionEnvelope(action);
            if (envelope.Legal)
            {
                result.LegalActions.Add(envelope);
            }
            result.Tokens.Add(ActionToken(action));
        }
        result.Coverage = Coverage(result, normalized);
        return result;
    }

    public static CombatTypedActionEnvelope ActionEnvelope(
        CombatActionObservation action)
    {
        return ActionEnvelope(action, null);
    }

    public static CombatTypedActionEnvelope ActionEnvelope(
        CombatActionObservation action,
        CombatActionModel? authoritativeModel)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        var outcomes = (authoritativeModel?.Outcomes
                        ?? new List<CombatActionOutcome>())
            .Where(item => item != null)
            .ToList();
        var positiveOutcomes = outcomes.Count(item => item.Probability > 0d);
        return new CombatTypedActionEnvelope
        {
            CandidateId = action.CandidateId,
            ActionType = action.Kind,
            SourceDefinitionId = action.SourceId,
            SourceRuntimeId = action.RuntimeId,
            TargetKind = action.TargetKind,
            TargetRuntimeIds = action.TargetRuntimeId == 0
                ? new List<int>()
                : new List<int> { action.TargetRuntimeId },
            ResourceCost = action.Cost,
            SourceZone = action.Kind == CombatActionKind.PlayCard
                ? "hand"
                : action.Kind == CombatActionKind.UseSkill
                    ? "skill"
                    : "global",
            CardInstanceBound = action.Kind == CombatActionKind.PlayCard
                                && action.RuntimeId != 0,
            SkillLifecycleBound = action.Kind == CombatActionKind.UseSkill,
            Legal = action.Legal,
            RejectionReason = action.RejectionReason,
            Transition = new CombatTransitionEnvelope
            {
                Legal = action.Legal,
                RejectionReason = action.RejectionReason,
                Deterministic = positiveOutcomes <= 1
                                && action.Semantics?.RandomOutcome != true,
                HasUnknownResidual = (action.Semantics?.RandomOutcome == true
                                      && positiveOutcomes == 0)
                                     || action.SemanticFidelity
                                        != CombatKnowledgeFidelity.Authoritative,
                SemanticSource = action.SemanticSource,
                SemanticFidelity = action.SemanticFidelity,
                DeterministicAfterstate = action.Semantics
                                          ?? new CombatActionSemantics(),
                ExactChanceOutcomes = outcomes
                    .Where(item => item != null)
                    .ToList()
            }
        };
    }

    private static void AddGlobal(
        ICollection<CombatObjectToken> tokens,
        CombatStateObservation state)
    {
        var token = new CombatObjectToken
        {
            TokenId = "global",
            Kind = CombatObjectTokenKind.Global,
            DefinitionId = "combat",
            Visibility = CombatInformationVisibility.PublicExact
        };
        token.Values["turn"] = Value(state.Features, "turn");
        token.Values["sequence"] = state.Sequence;
        token.Values["playerActionWindow"] = state.IsPlayerActionWindow ? 1d : 0d;
        token.Values["uiBusy"] = state.UiBusy ? 1d : 0d;
        token.Values["expectedIncomingDamage"] = state.ExpectedIncomingDamage;
        tokens.Add(token);

        tokens.Add(new CombatObjectToken
        {
            TokenId = "resource:power",
            Kind = CombatObjectTokenKind.Resource,
            DefinitionId = "power",
            OwnerRuntimeId = state.Player?.RuntimeId ?? 0,
            Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["current"] = state.CurrentPower,
                ["maximum"] = state.MaxPower
            }
        });
    }

    private static void AddUnit(
        ICollection<CombatObjectToken> tokens,
        CombatUnitObservation unit,
        CombatObjectTokenKind kind,
        string fallbackId)
    {
        if (unit == null)
        {
            return;
        }
        var tokenId = string.IsNullOrWhiteSpace(unit.DefinitionId)
            ? fallbackId
            : kind.ToString().ToLowerInvariant() + ":" + unit.DefinitionId
              + ":" + unit.RuntimeId.ToString(CultureInfo.InvariantCulture);
        tokens.Add(new CombatObjectToken
        {
            TokenId = tokenId,
            Kind = kind,
            DefinitionId = unit.DefinitionId,
            RuntimeId = unit.RuntimeId,
            Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["hp"] = unit.CurrentHp,
                ["maxHp"] = unit.MaxHp,
                ["defend"] = unit.Defend,
                ["attack"] = unit.Attack,
                ["alive"] = unit.Alive ? 1d : 0d
            }
        });
        for (var index = 0; index < unit.Statuses.Count; index++)
        {
            var status = unit.Statuses[index];
            if (status == null)
            {
                continue;
            }
            tokens.Add(new CombatObjectToken
            {
                TokenId = "status:" + unit.RuntimeId.ToString(CultureInfo.InvariantCulture)
                          + ":" + status.StatusId + ":"
                          + index.ToString(CultureInfo.InvariantCulture),
                Kind = CombatObjectTokenKind.Status,
                DefinitionId = status.StatusId,
                OwnerRuntimeId = unit.RuntimeId,
                Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["level"] = status.Level,
                    ["rarity"] = status.Rarity,
                    ["upperBound"] = status.UpperBound,
                    ["reducePerTurn"] = status.ReducePerTurn,
                    ["reducePerUse"] = status.ReducePerUse,
                    ["reducePerAttacked"] = status.ReducePerAttacked
                },
                Relations = new List<string> { "owner:" + tokenId }
            });
        }
    }

    private static void AddHand(
        ICollection<CombatObjectToken> tokens,
        CombatStateObservation state)
    {
        if (state.HandCards.Count > 0)
        {
            for (var index = 0; index < state.HandCards.Count; index++)
            {
                var card = state.HandCards[index];
                if (card == null)
                {
                    continue;
                }
                tokens.Add(new CombatObjectToken
                {
                    TokenId = "hand:" + card.RuntimeId.ToString(CultureInfo.InvariantCulture)
                              + ":" + index.ToString(CultureInfo.InvariantCulture),
                    Kind = CombatObjectTokenKind.HandCard,
                    DefinitionId = card.CardId,
                    RuntimeId = card.RuntimeId,
                    OwnerRuntimeId = state.Player?.RuntimeId ?? 0,
                    Zone = "hand",
                    Position = index,
                    Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cost"] = card.EffectiveCost,
                        ["retained"] = card.Retained ? 1d : 0d,
                        ["exhaustsOnUse"] = card.ExhaustsOnUse ? 1d : 0d,
                        ["createdThisBattle"] = card.CreatedThisBattle ? 1d : 0d,
                        ["enhancementCount"] = card.EnhancementCount
                    }
                });
            }
            return;
        }
        for (var index = 0; index < state.HandCardIds.Count; index++)
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = "hand:id:" + index.ToString(CultureInfo.InvariantCulture),
                Kind = CombatObjectTokenKind.HandCard,
                DefinitionId = state.HandCardIds[index],
                OwnerRuntimeId = state.Player?.RuntimeId ?? 0,
                Zone = "hand",
                Position = index
            });
        }
    }

    private static void AddEnemyIntents(
        ICollection<CombatObjectToken> tokens,
        CombatStateObservation state)
    {
        var intents = state.Threat?.Intents
                      ?? new List<CombatIntentObservation>();
        for (var index = 0; index < intents.Count; index++)
        {
            var intent = intents[index];
            if (intent == null)
            {
                continue;
            }
            tokens.Add(new CombatObjectToken
            {
                TokenId = "intent:"
                          + intent.SourceRuntimeId.ToString(CultureInfo.InvariantCulture)
                          + ":" + index.ToString(CultureInfo.InvariantCulture),
                Kind = CombatObjectTokenKind.EnemyIntent,
                DefinitionId = intent.SourceId,
                OwnerRuntimeId = intent.SourceRuntimeId,
                Visibility = intent.Current
                    ? CombatInformationVisibility.PublicExact
                    : CombatInformationVisibility.Belief,
                Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = (int)intent.Kind,
                    ["probability"] = intent.Probability,
                    ["blockableDamage"] = intent.BlockableDamage,
                    ["unblockableDamage"] = intent.UnblockableDamage,
                    ["damageOverTime"] = intent.DamageOverTime,
                    ["confidence"] = intent.Confidence,
                    ["current"] = intent.Current ? 1d : 0d
                }
            });
        }
    }

    private static void AddDeckKnowledge(
        ICollection<CombatObjectToken> tokens,
        CombatStateObservation state)
    {
        var knowledge = state.DeckKnowledge ?? new CombatDeckKnowledge();
        for (var index = 0; index < knowledge.KnownTopCardIds.Count; index++)
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = "draw-top:" + index.ToString(CultureInfo.InvariantCulture),
                Kind = CombatObjectTokenKind.DrawTop,
                DefinitionId = knowledge.KnownTopCardIds[index],
                Zone = "draw",
                Position = index,
                Visibility = CombatInformationVisibility.PublicExact
            });
        }
        for (var index = 0; index < knowledge.KnownBottomCardIds.Count; index++)
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = "draw-bottom:" + index.ToString(CultureInfo.InvariantCulture),
                Kind = CombatObjectTokenKind.DrawBottom,
                DefinitionId = knowledge.KnownBottomCardIds[index],
                Zone = "draw",
                Position = index,
                Visibility = CombatInformationVisibility.PublicExact
            });
        }
        var knownBoundary = knowledge.KnownTopCardIds.Count
                            + knowledge.KnownBottomCardIds.Count;
        var unknown = Math.Max(0, knowledge.DrawPileCount - knownBoundary);
        tokens.Add(new CombatObjectToken
        {
            TokenId = "draw-belief",
            Kind = CombatObjectTokenKind.DrawBelief,
            DefinitionId = "unknown-draw-multiset",
            Zone = "draw",
            Count = unknown,
            Visibility = CombatInformationVisibility.Belief,
            Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["drawPileCount"] = knowledge.DrawPileCount,
                ["unknownSlots"] = unknown,
                ["shuffleEpoch"] = knowledge.ShuffleEpoch
            }
        });
    }

    private static void AddVisiblePile(
        ICollection<CombatObjectToken> tokens,
        IEnumerable<string> cardIds,
        CombatObjectTokenKind kind,
        string zone)
    {
        foreach (var group in (cardIds ?? Array.Empty<string>())
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = zone + ":" + group.Key,
                Kind = kind,
                DefinitionId = group.Key,
                Zone = zone,
                Count = group.Count()
            });
        }
    }

    private static void AddDeferredEffects(
        ICollection<CombatObjectToken> tokens,
        CombatStateObservation state)
    {
        foreach (var effect in state.DeferredEffects.OrderBy(item => item.Sequence))
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = "deferred:" + effect.Sequence.ToString(CultureInfo.InvariantCulture),
                Kind = CombatObjectTokenKind.DeferredEffect,
                DefinitionId = effect.StatusId,
                OwnerRuntimeId = effect.TargetRuntimeId,
                Position = effect.Sequence,
                Relations = new List<string> { "source:" + effect.SourceId }
            });
        }
    }

    private static void AddPrefixedStateTokens(
        ICollection<CombatObjectToken> tokens,
        IReadOnlyDictionary<string, double> features)
    {
        AddFeatureTokens(tokens, features, "relic:", CombatObjectTokenKind.Relic);
        AddFeatureTokens(tokens, features, "blessing:", CombatObjectTokenKind.Blessing);
        AddFeatureTokens(tokens, features, "difficulty:", CombatObjectTokenKind.Difficulty);
    }

    private static void AddFeatureTokens(
        ICollection<CombatObjectToken> tokens,
        IReadOnlyDictionary<string, double> features,
        string prefix,
        CombatObjectTokenKind kind)
    {
        foreach (var pair in (features ?? new Dictionary<string, double>())
                     .Where(pair => pair.Key.StartsWith(
                         prefix,
                         StringComparison.OrdinalIgnoreCase)
                                    && Finite(pair.Value)
                                    && Math.Abs(pair.Value) > 0.0000001d)
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = pair.Key,
                Kind = kind,
                DefinitionId = pair.Key.Substring(prefix.Length),
                Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["value"] = pair.Value
                },
                Visibility = CombatInformationVisibility.PublicDerived
            });
        }
    }

    private static CombatObjectToken ActionToken(CombatActionObservation action)
    {
        return new CombatObjectToken
        {
            TokenId = "action:" + action.CandidateId,
            Kind = CombatObjectTokenKind.ActionCandidate,
            DefinitionId = action.SourceId,
            RuntimeId = action.RuntimeId,
            OwnerRuntimeId = action.TargetRuntimeId,
            Zone = action.Kind == CombatActionKind.PlayCard
                ? "hand"
                : action.Kind == CombatActionKind.UseSkill
                    ? "skill"
                    : "global",
            Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["kind"] = (int)action.Kind,
                ["targetKind"] = (int)action.TargetKind,
                ["cost"] = action.Cost,
                ["legal"] = action.Legal ? 1d : 0d,
                ["damage"] = action.Semantics?.Damage ?? 0d,
                ["defend"] = action.Semantics?.Defend ?? 0d,
                ["heal"] = action.Semantics?.Heal ?? 0d,
                ["draw"] = action.Semantics?.Draw ?? 0d,
                ["energyGain"] = action.Semantics?.EnergyGain ?? 0d,
                ["uncertainty"] = action.Semantics?.Uncertainty ?? 0d
            }
        };
    }

    private static CombatWorldModelCoverageManifest Coverage(
        CombatObservationEnvelope envelope,
        CombatStateObservation state)
    {
        var manifest = new CombatWorldModelCoverageManifest();
        AddCoverage(manifest, "role", 1, envelope.Tokens.Count(item =>
            item.Kind == CombatObjectTokenKind.Role));
        AddCoverage(manifest, "familiar", state.Friendlies.Count, envelope.Tokens.Count(item =>
            item.Kind is CombatObjectTokenKind.Familiar or CombatObjectTokenKind.Friendly));
        AddCoverage(manifest, "enemy", state.Enemies.Count, envelope.Tokens.Count(item =>
            item.Kind == CombatObjectTokenKind.Enemy));
        var statuses = state.Player.Statuses.Count
                       + state.Friendlies.Sum(item => item.Statuses.Count)
                       + state.Enemies.Sum(item => item.Statuses.Count);
        AddCoverage(manifest, "status", statuses, envelope.Tokens.Count(item =>
            item.Kind == CombatObjectTokenKind.Status));
        AddCoverage(manifest, "hand", state.HandCount, envelope.Tokens.Count(item =>
            item.Kind == CombatObjectTokenKind.HandCard));
        AddCoverage(manifest, "draw-belief", 1, envelope.Tokens.Count(item =>
            item.Kind == CombatObjectTokenKind.DrawBelief));
        AddCoverage(manifest, "discard", state.DiscardPileCardIds.Count, envelope.Tokens
            .Where(item => item.Kind == CombatObjectTokenKind.DiscardCard)
            .Sum(item => item.Count));
        AddCoverage(manifest, "exhaust", state.ExhaustPileCardIds.Count, envelope.Tokens
            .Where(item => item.Kind == CombatObjectTokenKind.ExhaustCard)
            .Sum(item => item.Count));
        AddCoverage(manifest, "actions", state.Actions.Count, envelope.Tokens.Count(item =>
            item.Kind == CombatObjectTokenKind.ActionCandidate));
        AddCoverage(manifest, "resource", 1, envelope.Tokens.Count(item =>
            item.Kind == CombatObjectTokenKind.Resource));
        return manifest;
    }

    private static void AddCoverage(
        CombatWorldModelCoverageManifest manifest,
        string domain,
        int present,
        int encoded)
    {
        manifest.Entries.Add(new CombatWorldModelCoverageEntry
        {
            Domain = domain,
            PresentCount = Math.Max(0, present),
            EncodedCount = Math.Max(0, encoded),
            Stage = present <= 0
                ? CombatCoverageStage.Present
                : encoded >= present
                    ? CombatCoverageStage.Encoded
                    : CombatCoverageStage.Observable,
            Reason = encoded >= present
                ? "all currently observed objects encoded"
                : "observed objects exceed encoded objects"
        });
    }

    private static double Value(
        IReadOnlyDictionary<string, double> values,
        string key)
    {
        return values != null
               && values.TryGetValue(key, out var value)
               && Finite(value)
            ? value
            : 0d;
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public static class CombatWorldModelTokenEncoding
{
    public const int MaximumBattleTokens = 192;

    public static double[][] Encode(
        CombatObservationEnvelope? observation,
        int dimensions,
        int maximumTokens = MaximumBattleTokens,
        bool includeActionCandidates = true)
    {
        if (observation?.Tokens == null || observation.Tokens.Count == 0)
        {
            return Array.Empty<double[]>();
        }
        var safeDimensions = Math.Max(1, dimensions);
        var safeMaximum = Math.Max(1, maximumTokens);
        var ordered = observation.Tokens
            .Where(token => token != null)
            .Where(token => includeActionCandidates
                            || token.Kind
                            != CombatObjectTokenKind.ActionCandidate)
            .OrderByDescending(IsRequiredToken)
            .ThenBy(TokenPriority)
            .ThenBy(token => token.TokenId, StringComparer.Ordinal)
            .ToList();
        var requiredCount = ordered.Count(IsRequiredToken);
        return ordered
            .Take(Math.Max(safeMaximum, requiredCount))
            .Select(token => Encode(token, safeDimensions))
            .ToArray();
    }

    public static double[] Encode(CombatObjectToken token, int dimensions)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));
        var features = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["kind:" + token.Kind] = 1d,
            ["visibility:" + token.Visibility] = 1d,
            ["definition:" + (token.DefinitionId ?? "")] = 1d,
            ["zone:" + (token.Zone ?? "")] = 1d,
            ["position"] = token.Position,
            ["count"] = token.Count
        };
        if (token.RuntimeId != 0)
        {
            features["runtime:" + token.RuntimeId.ToString(
                CultureInfo.InvariantCulture)] = 1d;
        }
        if (token.OwnerRuntimeId != 0)
        {
            features["owner-runtime:" + token.OwnerRuntimeId.ToString(
                CultureInfo.InvariantCulture)] = 1d;
        }
        foreach (var pair in token.Values
                 ?? new Dictionary<string, double>())
        {
            features["value:" + pair.Key] = pair.Value;
        }
        foreach (var relation in token.Relations
                 ?? new List<string>())
        {
            features["relation:" + relation] = 1d;
        }
        return CombatPolicyValueEncoding.Encode(
            features,
            dimensions,
            "world-object-v1");
    }

    private static int TokenPriority(CombatObjectToken token)
    {
        return token.Kind switch
        {
            CombatObjectTokenKind.Global => 0,
            CombatObjectTokenKind.Role => 1,
            CombatObjectTokenKind.Familiar => 2,
            CombatObjectTokenKind.Friendly => 3,
            CombatObjectTokenKind.Enemy => 4,
            CombatObjectTokenKind.EnemyIntent => 5,
            CombatObjectTokenKind.HandCard => 6,
            CombatObjectTokenKind.Resource => 7,
            CombatObjectTokenKind.Status => 8,
            CombatObjectTokenKind.DeferredEffect => 9,
            CombatObjectTokenKind.ActionCandidate => 10,
            _ => 20
        };
    }

    private static bool IsRequiredToken(CombatObjectToken token)
    {
        return token.Kind is CombatObjectTokenKind.Global
            or CombatObjectTokenKind.Role
            or CombatObjectTokenKind.Familiar
            or CombatObjectTokenKind.Friendly
            or CombatObjectTokenKind.Enemy
            or CombatObjectTokenKind.EnemyIntent
            or CombatObjectTokenKind.HandCard
            or CombatObjectTokenKind.Resource
            or CombatObjectTokenKind.DeferredEffect
            or CombatObjectTokenKind.ActionCandidate;
    }
}

public static class CombatCampaignWorldModelTokenizer
{
    public static CombatCampaignObservationEnvelope Build(
        CombatCampaignState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var result = new CombatCampaignObservationEnvelope
        {
            WorldSeed = state.WorldSeed,
            CurrentLayer = state.CurrentLayer
        };
        result.Tokens.Add(new CombatObjectToken
        {
            TokenId = "campaign:global",
            Kind = CombatObjectTokenKind.CampaignGlobal,
            DefinitionId = "campaign",
            Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["layer"] = state.CurrentLayer,
                ["gameLevel"] = state.CurrentGameLevel,
                ["hp"] = state.CurrentHp,
                ["maxHp"] = state.MaxHp,
                ["money"] = state.Money
            }
        });
        AddValues(result.Tokens, state.Attributes, "attribute", CombatObjectTokenKind.CampaignAttribute);
        AddValues(
            result.Tokens,
            state.PermanentAttributeBonuses,
            "permanent-attribute",
            CombatObjectTokenKind.CampaignAttribute);
        AddCards(result.Tokens, state.Deck, "campaign-deck", CombatObjectTokenKind.CampaignDeckCard);
        AddCards(
            result.Tokens,
            state.ReserveCards,
            "campaign-reserve",
            CombatObjectTokenKind.CampaignReserveCard);
        AddIds(result.Tokens, state.Relics, "campaign-relic", CombatObjectTokenKind.CampaignRelic);
        AddIds(
            result.Tokens,
            state.Blessings.Concat(state.InnateBlessings),
            "campaign-blessing",
            CombatObjectTokenKind.CampaignBlessing);
        AddBuildGoal(result.Tokens, state.BuildPlan);
        return result;
    }

    private static void AddValues(
        ICollection<CombatObjectToken> tokens,
        IReadOnlyDictionary<string, int> values,
        string prefix,
        CombatObjectTokenKind kind)
    {
        foreach (var pair in (values ?? new Dictionary<string, int>())
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = prefix + ":" + pair.Key,
                Kind = kind,
                DefinitionId = pair.Key,
                Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["value"] = pair.Value
                }
            });
        }
    }

    private static void AddCards(
        ICollection<CombatObjectToken> tokens,
        IEnumerable<string> values,
        string prefix,
        CombatObjectTokenKind kind)
    {
        foreach (var group in (values ?? Array.Empty<string>())
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = prefix + ":" + group.Key,
                Kind = kind,
                DefinitionId = group.Key,
                Count = group.Count()
            });
        }
    }

    private static void AddIds(
        ICollection<CombatObjectToken> tokens,
        IEnumerable<string> values,
        string prefix,
        CombatObjectTokenKind kind)
    {
        var index = 0;
        foreach (var value in (values ?? Array.Empty<string>())
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            tokens.Add(new CombatObjectToken
            {
                TokenId = prefix + ":" + index.ToString(CultureInfo.InvariantCulture),
                Kind = kind,
                DefinitionId = value
            });
            index++;
        }
    }

    private static void AddBuildGoal(
        ICollection<CombatObjectToken> tokens,
        CombatCampaignBuildPlan plan)
    {
        if (plan == null)
        {
            return;
        }
        var token = new CombatObjectToken
        {
            TokenId = "build-goal",
            Kind = CombatObjectTokenKind.BuildGoal,
            DefinitionId = plan.FocusStrategyId,
            Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["layer"] = plan.LayerNumber,
                ["targetDeckMinimum"] = plan.TargetDeckSizeMinimum,
                ["targetDeckMaximum"] = plan.TargetDeckSizeMaximum,
                ["deckSizeAlert"] = plan.DeckSizeAlert ? 1d : 0d,
                ["revision"] = plan.Revision
            }
        };
        foreach (var pair in plan.FeatureWeights)
        {
            token.Values["feature:" + pair.Key] = pair.Value;
        }
        tokens.Add(token);
    }
}
