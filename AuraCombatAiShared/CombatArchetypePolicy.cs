using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public enum CombatArchetypeCommitment
{
    None,
    Candidate,
    Committed
}

public enum CombatRebirthPhase
{
    Insurance,
    Charging,
    Ready,
    Convertible,
    Activated
}

public enum CombatTimeCagePhase
{
    Inactive,
    Primed,
    Loaded,
    Amplifying
}

public sealed class CombatArchetypeAssessment
{
    public CombatArchetypeCommitment RebirthCommitment { get; set; }

    public CombatArchetypeCommitment TimeCageCommitment { get; set; }

    public CombatRebirthPhase RebirthPhase { get; set; }

    public CombatTimeCagePhase TimeCagePhase { get; set; }

    public double RebirthScore { get; set; }

    public double TimeCageScore { get; set; }

    public int RebirthStacks { get; set; }

    public int ResurrectionCount { get; set; }

    public int TimeCageCount { get; set; }
}

public static class CombatArchetypePolicy
{
    public const string RebirthCommittedFeature = "archetype:rebirth.committed";
    public const string TimeCageCommittedFeature = "archetype:time-cage.committed";
    public const string RebirthStacksFeature = "mechanic:rebirth.stacks";
    public const string ResurrectionCountFeature = "mechanic:rebirth.count";
    public const string KeenEdgeFeature = "mechanic:rebirth.keen-edge";
    public const string TimeCageCountFeature = "mechanic:time-cage.count";
    public const string TerminalLethalCertifiedFeature =
        "mechanic:terminal-lethal-certified";

    private static readonly HashSet<string> RebirthStarters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Crowdfundingcard_6",
            "Crowdfundingcard_47"
        };

    private static readonly HashSet<string> RebirthSustain =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Crowdfundingcard_8"
        };

    private static readonly HashSet<string> RebirthConverters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Crowdfundingcard_10"
        };

    private static readonly HashSet<string> RebirthPayoffs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Crowdfundingcard_11"
        };

    private static readonly HashSet<string> RebirthSupport =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Crowdfundingcard_7",
            "Crowdfundingcard_9",
            "Crowdfundingcard_49",
            "Crowdfundingcard_25",
            "SpellCard_17",
            "universalcard_10",
            "universalcard_15"
        };

    private static readonly HashSet<string> TimeCagePayloads =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "timekeeper_3",
            "timekeeper_9",
            "timekeeper_10",
            "timekeeper_14",
            "timekeeper_17",
            "timekeeper_18"
        };

    private static readonly HashSet<string> TimeCageOperators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "timekeeper_4",
            "timekeeper_6",
            "timekeeper_7",
            "timekeeper_8",
            "timekeeper_13"
        };

    private static readonly HashSet<string> TimeCageSupport =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "timekeeper_2",
            "timekeeper_5",
            "timekeeper_15",
            "timekeeper_16"
        };

    private static readonly HashSet<string> FrozenCards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "timekeeper_2",
            "timekeeper_3",
            "timekeeper_4",
            "timekeeper_6",
            "timekeeper_7",
            "timekeeper_8",
            "timekeeper_10",
            "timekeeper_12",
            "timekeeper_13",
            "timekeeper_15",
            "timekeeper_16"
        };

    private static readonly HashSet<string> HighRiskRebirthCards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Crowdfundingcard_25",
            "Crowdfundingcard_49",
            "SpellCard_17",
            "universalcard_10",
            "universalcard_15"
        };

    private static readonly HashSet<string> HighRiskEngineStarters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Crowdfundingcard_43",
            "card_7"
        };

    private static readonly HashSet<string> HardBanCards =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "luckycard_4"
        };

    public static CombatArchetypeAssessment Enrich(CombatStateObservation state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        state.Features ??= new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        var assessment = Assess(state);
        WriteFeatures(state, assessment);
        foreach (var action in state.Actions ?? new List<CombatActionObservation>())
        {
            if (action != null)
            {
                EnrichAction(state, action, assessment);
            }
        }
        foreach (var effect in state.DeferredEffects
                     ?? new List<CombatDeferredEffectObservation>())
        {
            var deferredAction = new CombatActionObservation
            {
                SourceId = effect.SourceId,
                TargetRuntimeId = effect.TargetRuntimeId,
                Semantics = effect.Semantics ?? new CombatActionSemantics()
            };
            EnrichAction(state, deferredAction, assessment);
            effect.Semantics = deferredAction.Semantics;
        }
        return assessment;
    }

    public static CombatArchetypeAssessment Assess(CombatStateObservation state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var deck = state.DeckCardIds ?? new List<string>();
        var starters = Count(deck, RebirthStarters);
        var sustain = Count(deck, RebirthSustain);
        var converters = Count(deck, RebirthConverters);
        var payoffs = Count(deck, RebirthPayoffs);
        var rebirthSupport = Count(deck, RebirthSupport);
        var rebirthComponents =
            starters + sustain + converters + payoffs + rebirthSupport;
        var rebirthRoles =
            (starters > 0 ? 1 : 0)
            + (sustain > 0 ? 1 : 0)
            + (converters > 0 ? 1 : 0)
            + (payoffs > 0 ? 1 : 0);
        var rebirthScore =
            starters * 2.5d
            + sustain * 1.5d
            + converters * 1.5d
            + payoffs * 2d
            + rebirthSupport * 0.35d;
        var rebirthCommitted =
            starters > 0
            && rebirthRoles >= 3
            && rebirthComponents >= 3;

        var payloads = Count(deck, TimeCagePayloads);
        var operators = Count(deck, TimeCageOperators);
        var cageSupport = Count(deck, TimeCageSupport);
        var packages = deck.Count(id => IdEquals(id, "timekeeper_12"));
        var cageComponents = payloads + operators + cageSupport + packages;
        var timeCageScore =
            payloads * 1.5d
            + operators * 1.25d
            + packages * 2d
            + cageSupport * 0.45d;
        var cageCommitted = payloads >= 2
                            && operators >= 1
                            && cageComponents >= 4
                            || packages >= 1
                            && payloads >= 2;

        var rebirthStacks = StatusLevel(state.Player, "buff_rebirth");
        var resurrectionCount = Math.Max(
            0,
            (int)Math.Round(Value(state.Features, ResurrectionCountFeature)));
        var timeCageCount = Math.Max(
            StatusLevel(state.Player, "buff_timelock"),
            state.DeferredEffects?.Count ?? 0);

        return new CombatArchetypeAssessment
        {
            RebirthCommitment = rebirthCommitted
                ? CombatArchetypeCommitment.Committed
                : rebirthComponents >= 2
                    ? CombatArchetypeCommitment.Candidate
                    : CombatArchetypeCommitment.None,
            TimeCageCommitment = cageCommitted
                ? CombatArchetypeCommitment.Committed
                : cageComponents >= 2
                    ? CombatArchetypeCommitment.Candidate
                    : CombatArchetypeCommitment.None,
            RebirthPhase = resurrectionCount > 0
                ? CombatRebirthPhase.Activated
                : rebirthStacks >= 59
                    ? CombatRebirthPhase.Convertible
                    : rebirthStacks >= 30
                        ? CombatRebirthPhase.Ready
                        : rebirthStacks > 0
                            ? CombatRebirthPhase.Charging
                            : CombatRebirthPhase.Insurance,
            TimeCagePhase = timeCageCount <= 0
                ? CombatTimeCagePhase.Inactive
                : operators > 0
                    ? CombatTimeCagePhase.Amplifying
                    : timeCageCount >= 2
                        ? CombatTimeCagePhase.Loaded
                        : CombatTimeCagePhase.Primed,
            RebirthScore = rebirthScore,
            TimeCageScore = timeCageScore,
            RebirthStacks = rebirthStacks,
            ResurrectionCount = resurrectionCount,
            TimeCageCount = timeCageCount
        };
    }

    public static bool IsLegal(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason)
    {
        var assessment = Assess(state);
        var id = action.SourceId ?? "";
        if (HardBanCards.Contains(id))
        {
            reason = "hard-banned card has unacceptable combat downside";
            return false;
        }
        if (StatusLevel(state.Player, "buff_rotten") > 0
            && IsPureBlockAction(action))
        {
            reason = "post-action corrosion nullifies this block-only action";
            return false;
        }
        if (HighRiskEngineStarters.Contains(id)
            && !HighRiskEngineStarterIsSafe(
                state,
                action,
                assessment,
                out reason))
        {
            return false;
        }
        if (IdEquals(id, "Crowdfundingcard_10")
            && assessment.RebirthStacks < 59)
        {
            reason = "rebirth conversion would remove the 30-stack insurance";
            return false;
        }
        if (HighRiskRebirthCards.Contains(id)
            && assessment.RebirthCommitment != CombatArchetypeCommitment.Committed)
        {
            reason = "high-risk life conversion requires a committed rebirth build";
            return false;
        }
        if (HighRiskRebirthCards.Contains(id))
        {
            var hpLoss = EstimatedSelfHpLoss(action);
            var postActionHp = Math.Max(0d, state.Player.CurrentHp - hpLoss);
            if (postActionHp <= RiskAdjustedIncoming(state)
                && assessment.RebirthStacks < 30)
            {
                reason = "high-risk life conversion is not covered by health or rebirth";
                return false;
            }
        }
        if (!HighRiskEngineStarters.Contains(id)
            && WouldCauseLethalSelfLoss(state, action))
        {
            if (assessment.RebirthCommitment != CombatArchetypeCommitment.Committed)
            {
                reason = "intentional lethal damage is forbidden outside a committed rebirth build";
                return false;
            }
            if (assessment.RebirthStacks < 30)
            {
                reason = "intentional lethal damage requires at least 30 rebirth stacks";
                return false;
            }
            var postRebirthHp = Math.Min(
                Math.Max(1, state.Player.MaxHp),
                assessment.RebirthStacks);
            if (postRebirthHp <= RiskAdjustedIncoming(state))
            {
                reason = "post-rebirth health does not cover the current incoming threat";
                return false;
            }
        }
        if (IsEmptyCageOperator(id)
            && assessment.TimeCageCount <= 0)
        {
            reason = "time-cage operator has no queued effect";
            return false;
        }
        if (IdEquals(id, "timekeeper_12")
            && !PackageIsSafe(state, assessment, out reason))
        {
            return false;
        }
        reason = "";
        return true;
    }

    public static bool IsFrozenCard(string cardId)
    {
        return FrozenCards.Contains(cardId ?? "");
    }

    public static bool IsAutomaticTimeCagePayload(string cardId)
    {
        return TimeCagePayloads.Contains(cardId ?? "");
    }

    public static bool IsHardBannedCard(string cardId)
    {
        return HardBanCards.Contains(cardId ?? "");
    }

    public static bool IsHighRiskRebirthCard(string cardId)
    {
        return HighRiskRebirthCards.Contains(cardId ?? "");
    }

    public static bool IsHighRiskEngineStarter(string cardId)
    {
        return HighRiskEngineStarters.Contains(cardId ?? "");
    }

    private static void WriteFeatures(
        CombatStateObservation state,
        CombatArchetypeAssessment value)
    {
        state.Features["archetype:rebirth.score"] = value.RebirthScore;
        state.Features[RebirthCommittedFeature] =
            value.RebirthCommitment == CombatArchetypeCommitment.Committed ? 1d : 0d;
        state.Features["archetype:rebirth.commitment"] =
            (int)value.RebirthCommitment;
        state.Features["archetype:time-cage.score"] = value.TimeCageScore;
        state.Features[TimeCageCommittedFeature] =
            value.TimeCageCommitment == CombatArchetypeCommitment.Committed ? 1d : 0d;
        state.Features["archetype:time-cage.commitment"] =
            (int)value.TimeCageCommitment;
        state.Features[RebirthStacksFeature] = value.RebirthStacks;
        state.Features[ResurrectionCountFeature] = value.ResurrectionCount;
        state.Features[KeenEdgeFeature] =
            StatusLevel(state.Player, "buff_keenedge");
        state.Features["mechanic:rebirth.phase"] = (int)value.RebirthPhase;
        state.Features[TimeCageCountFeature] = value.TimeCageCount;
        state.Features["mechanic:time-cage.phase"] = (int)value.TimeCagePhase;
    }

    private static void EnrichAction(
        CombatStateObservation state,
        CombatActionObservation action,
        CombatArchetypeAssessment assessment)
    {
        action.Features ??= new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        action.Semantics ??= new CombatActionSemantics();
        var id = action.SourceId ?? "";
        var semantics = action.Semantics;
        var derived = false;

        if (semantics.RestoreEnergyToMaximum)
        {
            semantics.EnergyGain = Math.Max(
                0d,
                state.MaxPower - state.CurrentPower);
            derived = true;
        }
        else if (semantics.EnergyMinimum.HasValue)
        {
            semantics.EnergyGain = Math.Max(
                0d,
                semantics.EnergyMinimum.Value - state.CurrentPower);
            derived = true;
        }
        else if (semantics.EnergySetAmount.HasValue)
        {
            semantics.EnergyGain = Math.Max(
                0d,
                semantics.EnergySetAmount.Value - state.CurrentPower);
            derived = true;
        }
        if (semantics.CardRetrievals.Count > 0)
        {
            semantics.CardGeneration = 0d;
            semantics.Draw = 0d;
            semantics.DeckValue = Math.Max(
                semantics.DeckValue,
                RetrievalValue(state, semantics.CardRetrievals));
            semantics.OpensInteraction = true;
            derived = true;
        }

        switch (id)
        {
            case "Crowdfundingcard_43":
            {
                var protectedByChrysalis =
                    StatusLevel(state.Player, "buff_chrysalis") > 0
                    || Value(state.Features, "blessing:blessing_34") > 0d;
                var certifiedTerminalFollowUp =
                    Value(state.Features, TerminalLethalCertifiedFeature) > 0d
                    || HasImmediateTerminalFollowUp(
                        state,
                        action,
                        state.CurrentPower - Math.Max(0, action.Cost));
                var toxinSettlement = certifiedTerminalFollowUp
                    ? 0d
                    : protectedByChrysalis
                    ? Math.Min(
                        999d,
                        Math.Max(1d, Math.Floor(state.Player.MaxHp * 0.5d)))
                    : 999d;
                semantics.EndOfCycleSelfHpLoss = toxinSettlement;
                semantics.Buff = Math.Max(semantics.Buff, 3d);
                semantics.Risk = Math.Max(
                    semantics.Risk,
                    Math.Max(toxinSettlement, state.Player.CurrentHp * 0.5d));
                derived = true;
                break;
            }
            case "card_7":
            {
                semantics.PersistentValue = Math.Max(
                    semantics.PersistentValue,
                    4d);
                semantics.CostReduction = Math.Max(
                    semantics.CostReduction,
                    2d);
                semantics.Risk = Math.Max(
                    semantics.Risk,
                    Math.Max(20d, state.Player.CurrentHp));
                derived = true;
                break;
            }
            case "Crowdfundingcard_25":
            {
                ResetTactical(semantics);
                var targetHp = Math.Max(1, state.Player.MaxHp / 2);
                semantics.SelfHpLoss = Math.Max(
                    0,
                    state.Player.CurrentHp - targetHp);
                semantics.Heal = Math.Max(
                    0,
                    targetHp - state.Player.CurrentHp);
                semantics.Buff = 1d;
                semantics.Risk = semantics.SelfHpLoss;
                derived = true;
                break;
            }
            case "Crowdfundingcard_6":
            {
                var uses = Math.Max(
                    0,
                    (int)Math.Round(Value(action.Features, "mechanic:card-use-count")));
                ResetTactical(semantics);
                if (uses <= 0)
                {
                    semantics.Defend = 10d;
                }
                else if (uses == 1)
                {
                    semantics.Heal = 8d;
                    semantics.Draw = 1d;
                }
                else
                {
                    semantics.StateChanges["status:buff_rebirth"] = 100d;
                    semantics.PersistentValue = 8d;
                }
                derived = true;
                break;
            }
            case "Crowdfundingcard_7":
                ResetTactical(semantics);
                semantics.CardGeneration = 1d;
                semantics.DeckValue = 1d;
                semantics.OpensInteraction = true;
                derived = true;
                break;
            case "Crowdfundingcard_8":
                ResetTactical(semantics);
                semantics.Draw = 1d;
                if (assessment.ResurrectionCount > 0)
                {
                    semantics.StateChanges["status:buff_rebirth"] = 9d;
                    semantics.PersistentValue = 1.5d;
                    action.Features["recycle"] = 1d;
                }
                derived = true;
                break;
            case "Crowdfundingcard_9":
                ResetTactical(semantics);
                semantics.Buff = 4d;
                derived = true;
                break;
            case "Crowdfundingcard_10":
            {
                ResetTactical(semantics);
                var spent = assessment.RebirthStacks / 2;
                var keenEdge = spent / 2;
                semantics.StateChanges["status:buff_rebirth"] = -spent;
                semantics.StateChanges["status:buff_keenedge"] = keenEdge;
                semantics.Scaling = keenEdge;
                derived = true;
                break;
            }
            case "Crowdfundingcard_11":
            {
                ResetTactical(semantics);
                var keenEdge = StatusLevel(state.Player, "buff_keenedge");
                semantics.Damage =
                    (1d + keenEdge) * Math.Pow(2d, assessment.ResurrectionCount);
                semantics.HitCount = 5d;
                derived = true;
                break;
            }
            case "Crowdfundingcard_49":
            {
                ResetTactical(semantics);
                var hpLoss = Math.Max(0, state.Player.MaxHp / 5);
                var gain = hpLoss / 4;
                semantics.SelfHpLoss = hpLoss;
                semantics.Risk = hpLoss * 1.5d;
                semantics.StateChanges["status:buff_rebirth"] = gain;
                semantics.StateChanges["status:buff_keenedge"] = gain;
                derived = true;
                break;
            }
            case "SpellCard_17":
            {
                ResetTactical(semantics);
                var hpLoss = Math.Max(
                    1,
                    (int)Math.Ceiling(state.Player.CurrentHp * 0.6d));
                semantics.SelfHpLoss = hpLoss;
                semantics.Damage = hpLoss * 2d;
                semantics.CardGeneration = 1d;
                semantics.Risk = hpLoss * 1.25d;
                derived = true;
                break;
            }
            case "universalcard_10":
            {
                ResetTactical(semantics);
                semantics.SelfHpLoss = Math.Max(
                    0,
                    state.Player.CurrentHp - 10);
                semantics.Heal = Math.Max(
                    0,
                    10 - state.Player.CurrentHp);
                semantics.Damage =
                    Math.Max(0, state.Player.MaxHp - 10) * 0.3d;
                semantics.Risk = semantics.SelfHpLoss * 1.25d;
                derived = true;
                break;
            }
            case "universalcard_15":
            {
                ResetTactical(semantics);
                semantics.SelfHpLoss = Math.Max(
                    0,
                    state.Player.CurrentHp - 10);
                semantics.Heal = Math.Max(
                    0,
                    10 - state.Player.CurrentHp);
                semantics.Draw = 4d;
                semantics.EnergyGain = 3d;
                semantics.Risk = semantics.SelfHpLoss * 1.25d;
                derived = true;
                break;
            }
            case "timekeeper_1":
                ResetTactical(semantics);
                semantics.Damage = 9d;
                semantics.Debuff = 2d;
                derived = true;
                break;
            case "timekeeper_3":
                ResetTactical(semantics);
                semantics.Defend = Math.Max(
                    0d,
                    Value(action.Features, "mechanic:card-use-count"));
                action.Features["mechanic:time-cage.payload"] = 1d;
                derived = true;
                break;
            case "timekeeper_4":
                ResetTactical(semantics);
                action.Features["mechanic:time-cage.operator"] = 2d;
                derived = true;
                break;
            case "timekeeper_5":
                ResetTactical(semantics);
                semantics.Defend = 8d;
                semantics.EnergyGain = assessment.TimeCageCount > 0 ? 1d : 0d;
                action.Features["mechanic:time-cage.reverse"] = 1d;
                derived = true;
                break;
            case "timekeeper_6":
                ResetTactical(semantics);
                semantics.RandomOutcome = true;
                semantics.Uncertainty = 0.5d;
                semantics.Risk = 0.35d;
                action.Features["mechanic:time-cage.operator"] = 0.5d;
                derived = true;
                break;
            case "timekeeper_7":
                ResetTactical(semantics);
                action.Features["mechanic:time-cage.operator"] = 1d;
                derived = true;
                break;
            case "timekeeper_8":
                ResetTactical(semantics);
                action.Features["mechanic:time-cage.first-repeats"] = 2d;
                derived = true;
                break;
            case "timekeeper_9":
                ResetTactical(semantics);
                semantics.Damage = 27d / Math.Max(1, state.Enemies.Count);
                action.Features["mechanic:time-cage.payload"] = 1d;
                derived = true;
                break;
            case "timekeeper_11":
                ResetTactical(semantics);
                semantics.EnergyGain = 2d;
                derived = true;
                break;
            case "timekeeper_12":
                ResetTactical(semantics);
                semantics.Draw = PackageCardCount(state) + 1d;
                semantics.DeckValue = PackageCardCount(state) * 0.75d;
                semantics.Risk = PackageRisk(state);
                action.Features["mechanic:time-cage.package"] = 1d;
                derived = true;
                break;
            case "timekeeper_13":
                ResetTactical(semantics);
                semantics.Defend = 5d;
                action.Features["mechanic:time-cage.last-repeats"] = 2d;
                derived = true;
                break;
            case "timekeeper_14":
                ResetTactical(semantics);
                semantics.Defend = assessment.TimeCageCount;
                action.Features["mechanic:time-cage.payload"] = 1d;
                derived = true;
                break;
            case "timekeeper_15":
            case "timekeeper_16":
                break;
            case "timekeeper_17":
                ResetTactical(semantics);
                semantics.Damage = assessment.TimeCageCount;
                action.Features["mechanic:time-cage.payload"] = 1d;
                derived = true;
                break;
            case "timekeeper_18":
                action.Features["mechanic:time-cage.payload"] = 1d;
                break;
        }

        if (RebirthStarters.Contains(id)
            || RebirthSustain.Contains(id)
            || RebirthConverters.Contains(id)
            || RebirthPayoffs.Contains(id))
        {
            action.Features["synergy"] =
                assessment.RebirthCommitment == CombatArchetypeCommitment.Committed
                    ? 2d
                    : assessment.RebirthCommitment == CombatArchetypeCommitment.Candidate
                        ? 0.5d
                        : -1d;
            action.Features["mechanic:rebirth.phase"] =
                (int)assessment.RebirthPhase;
        }
        if (TimeCagePayloads.Contains(id)
            || TimeCageOperators.Contains(id)
            || TimeCageSupport.Contains(id)
            || IdEquals(id, "timekeeper_12"))
        {
            action.Features["synergy"] =
                assessment.TimeCageCommitment == CombatArchetypeCommitment.Committed
                    ? 2d
                    : assessment.TimeCageCommitment == CombatArchetypeCommitment.Candidate
                        ? 0.5d
                        : 0d;
            action.Features["mechanic:time-cage.count"] =
                assessment.TimeCageCount;
        }
        if (derived)
        {
            action.SemanticSource = "archetype-policy-v1";
            action.SemanticFidelity = CombatKnowledgeFidelity.Derived;
        }
    }

    private static bool PackageIsSafe(
        CombatStateObservation state,
        CombatArchetypeAssessment assessment,
        out string reason)
    {
        var cards = (state.HandCardIds ?? new List<string>())
            .Where(id => !IdEquals(id, "timekeeper_12"))
            .Where(id => !FrozenCards.Contains(id))
            .ToList();
        if (cards.Count == 0)
        {
            reason = "package has no eligible card";
            return false;
        }
        if (cards.Any(HardBanCards.Contains))
        {
            reason = "package would execute a hard-banned card";
            return false;
        }
        var highRiskCount = cards.Count(HighRiskRebirthCards.Contains);
        if (highRiskCount > 1)
        {
            reason = "package would compound multiple life-conversion effects";
            return false;
        }
        if (highRiskCount > 0
            && (assessment.RebirthCommitment
                != CombatArchetypeCommitment.Committed
                || assessment.RebirthStacks < 30))
        {
            reason = "package would execute an unsafe life-conversion card";
            return false;
        }
        reason = "";
        return true;
    }

    private static bool HighRiskEngineStarterIsSafe(
        CombatStateObservation state,
        CombatActionObservation action,
        CombatArchetypeAssessment assessment,
        out string reason)
    {
        var id = action.SourceId ?? "";
        var remainingPower = state.CurrentPower - Math.Max(0, action.Cost);
        var continuationCards = (state.HandCardIds ?? new List<string>())
            .Where(cardId => !IdEquals(cardId, id))
            .Where(cardId => !IsDeadHandCard(cardId))
            .ToList();
        var distinctContinuationCards = continuationCards
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var nonBasicDeckCards = (state.DeckCardIds ?? new List<string>())
            .Count(cardId => !IsBasicOrDeadCard(cardId));
        var committedEngine =
            assessment.RebirthCommitment == CombatArchetypeCommitment.Committed
            || assessment.TimeCageCommitment == CombatArchetypeCommitment.Committed
            || distinctContinuationCards >= 3
            && nonBasicDeckCards >= 6;
        if (!committedEngine)
        {
            reason = "high-risk engine starter requires a coherent deck engine";
            return false;
        }
        if (remainingPower < 2)
        {
            reason = "high-risk engine starter leaves insufficient follow-up energy";
            return false;
        }
        if (continuationCards.Count < 3)
        {
            reason = "high-risk engine starter requires at least three actionable follow-up cards";
            return false;
        }
        if (IdEquals(id, "Crowdfundingcard_43"))
        {
            if (StatusLevel(state.Player, "buff_toxin") > 0)
            {
                reason = "finale cannot safely stack another lethal toxin cycle";
                return false;
            }
            var terminalLethalCertified =
                Value(state.Features, TerminalLethalCertifiedFeature) > 0d
                || HasImmediateTerminalFollowUp(
                    state,
                    action,
                    remainingPower);
            if (!terminalLethalCertified
                && StatusLevel(state.Player, "buff_chrysalis") <= 0
                && Value(state.Features, "blessing:blessing_34") <= 0d)
            {
                reason = "finale requires Solar/chrysalis protection unless a terminal line is already certified";
                return false;
            }
            if (!terminalLethalCertified
                && EstimatedSelfHpLoss(action)
                >= Math.Max(1, state.Player.CurrentHp))
            {
                reason = "finale toxin settlement is lethal at the current health";
                return false;
            }
        }
        if (IdEquals(id, "card_7")
            && StatusLevel(state.Player, "buff_eclipse") > 0)
        {
            reason = "magic heart cannot safely restart an active eclipse contract";
            return false;
        }
        reason = "";
        return true;
    }

    private static bool IsPureBlockAction(CombatActionObservation action)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        if (semantics.Defend <= 0d)
        {
            return false;
        }
        return semantics.Damage <= 0d
               && semantics.TrueDamage <= 0d
               && semantics.DamageOverTime <= 0d
               && semantics.Heal <= 0d
               && semantics.Draw <= 0d
               && semantics.EnergyGain <= 0d
               && semantics.Cleanse <= 0d
               && semantics.CardGeneration <= 0d
               && semantics.PersistentValue <= 0d
               && semantics.Buff <= 0d
               && semantics.Debuff <= 0d;
    }

    private static bool HasImmediateTerminalFollowUp(
        CombatStateObservation state,
        CombatActionObservation starter,
        int remainingPower)
    {
        return (state.Actions ?? new List<CombatActionObservation>())
            .Where(action => action != null
                             && !ReferenceEquals(action, starter)
                             && !IdEquals(action.SourceId, starter.SourceId)
                             && action.Cost <= remainingPower)
            .Any(action =>
            {
                var target = state.Enemies.FirstOrDefault(enemy =>
                    enemy.RuntimeId == action.TargetRuntimeId);
                return target != null
                       && target.CurrentHp > 0
                       && Value(
                           action.Features,
                           "terminalLethalEligible") > 0.5d
                       && Value(
                           action.Features,
                           "effectiveHpDamage")
                       >= target.CurrentHp;
            });
    }

    private static bool IsDeadHandCard(string cardId)
    {
        return string.IsNullOrWhiteSpace(cardId)
               || cardId.StartsWith("cursecard_", StringComparison.OrdinalIgnoreCase)
               || cardId.StartsWith("nocard_", StringComparison.OrdinalIgnoreCase)
               || HardBanCards.Contains(cardId);
    }

    private static bool IsBasicOrDeadCard(string cardId)
    {
        return IsDeadHandCard(cardId)
               || IdEquals(cardId, "card_1")
               || IdEquals(cardId, "card_2");
    }

    private static bool IsEmptyCageOperator(string id)
    {
        return IdEquals(id, "timekeeper_4")
               || IdEquals(id, "timekeeper_6")
               || IdEquals(id, "timekeeper_7")
               || IdEquals(id, "timekeeper_8");
    }

    private static bool WouldCauseLethalSelfLoss(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        var loss = EstimatedSelfHpLoss(action);
        if (semantics.StateChanges.TryGetValue("player.hp", out var hpDelta)
            && hpDelta < 0d)
        {
            loss = Math.Max(loss, -hpDelta);
        }
        return loss >= Math.Max(1, state.Player.CurrentHp);
    }

    private static double EstimatedSelfHpLoss(CombatActionObservation action)
    {
        var semantics = action.Semantics ?? new CombatActionSemantics();
        return Math.Max(0d, semantics.SelfHpLoss)
               + Math.Max(0d, semantics.EndOfCycleSelfHpLoss);
    }

    private static double RiskAdjustedIncoming(CombatStateObservation state)
    {
        var threat = state.Threat ?? new CombatThreatForecast();
        return Math.Max(
            state.ExpectedIncomingDamage,
            threat.ExpectedUnblockableDamage
            + threat.ExpectedDamageOverTime
            + threat.RiskAdjustedBlockableDamage(0.65d));
    }

    private static int PackageCardCount(CombatStateObservation state)
    {
        return (state.HandCardIds ?? new List<string>())
            .Count(id => !IdEquals(id, "timekeeper_12")
                         && !FrozenCards.Contains(id));
    }

    private static double PackageRisk(CombatStateObservation state)
    {
        return (state.HandCardIds ?? new List<string>())
            .Where(id => !FrozenCards.Contains(id))
            .Sum(id => HardBanCards.Contains(id)
                ? 100d
                : HighRiskRebirthCards.Contains(id)
                    ? 12d
                    : 0d);
    }

    private static double RetrievalValue(
        CombatStateObservation state,
        IReadOnlyList<CombatCardRetrievalSemantic> retrievals)
    {
        var handSlots = Math.Max(
            0,
            CombatActionExecutionPolicy.ResolveAvailableHandSlots(state));
        var total = 0d;
        foreach (var retrieval in retrievals)
        {
            IEnumerable<string> source = retrieval.SourceZone switch
            {
                CombatCardZoneKind.Hand => state.HandCardIds,
                CombatCardZoneKind.DiscardPile => state.DiscardPileCardIds,
                CombatCardZoneKind.ExhaustPile => state.ExhaustPileCardIds,
                _ => state.DeckKnowledge?.KnownDeckCardIds
                     ?? state.DeckCardIds
            };
            var amount = retrieval.DestinationZone == CombatCardZoneKind.Hand
                ? Math.Min(Math.Max(0, retrieval.Amount), handSlots)
                : Math.Max(0, retrieval.Amount);
            total += source
                .Where(cardId => HasKnownCardTag(
                    state,
                    cardId,
                    retrieval.RequiredCardTag))
                .Select(ContextualCardValue)
                .OrderByDescending(value => value)
                .Take(amount)
                .Sum();
        }
        return Math.Max(0d, total * 0.35d);
    }

    private static bool HasKnownCardTag(
        CombatStateObservation state,
        string cardId,
        string requiredTag)
    {
        return string.IsNullOrWhiteSpace(requiredTag)
               || state.CardTagsById.TryGetValue(cardId ?? "", out var tags)
               && tags.Contains(
                   requiredTag,
                   StringComparer.OrdinalIgnoreCase);
    }

    private static double ContextualCardValue(string cardId)
    {
        if (IsDeadHandCard(cardId))
        {
            return -5d;
        }
        if (!CombatKnowledgeRegistry.TryDescribeAction(
                new CombatActionObservation
                {
                    SourceId = cardId,
                    Kind = CombatActionKind.PlayCard
                },
                out var semantics,
                out var fidelity,
                out _)
            || fidelity == CombatKnowledgeFidelity.Unsupported)
        {
            return 0.25d;
        }
        var confidence = fidelity == CombatKnowledgeFidelity.Authoritative
            ? 1d
            : fidelity == CombatKnowledgeFidelity.Derived
                ? 0.7d
                : 0.4d;
        return confidence * (semantics.Damage * 0.45d
                             + semantics.TrueDamage * 0.6d
                             + semantics.Defend * 0.3d
                             + semantics.Heal * 0.35d
                             + semantics.Draw * 0.7d
                             + semantics.EnergyGain
                             + semantics.Buff * 0.5d
                             + semantics.Debuff * 0.45d
                             + semantics.Scaling
                             + semantics.PersistentValue
                             + semantics.DamageMultiplierGain * 100d);
    }

    private static void ResetTactical(CombatActionSemantics value)
    {
        value.Damage = 0d;
        value.TrueDamage = 0d;
        value.DamageOverTime = 0d;
        value.SelfHpLoss = 0d;
        value.EndOfCycleSelfHpLoss = 0d;
        value.HitCount = 1d;
        value.Defend = 0d;
        value.Heal = 0d;
        value.Draw = 0d;
        value.EnergyGain = 0d;
        value.EnergySetAmount = null;
        value.EnergyMinimum = null;
        value.RestoreEnergyToMaximum = false;
        value.CardRetrievals.Clear();
        value.Scaling = 0d;
        value.DeckValue = 0d;
        value.Buff = 0d;
        value.Debuff = 0d;
        value.Cleanse = 0d;
        value.CostReduction = 0d;
        value.CardGeneration = 0d;
        value.PersistentValue = 0d;
        value.DamageMultiplierGain = 0d;
        value.StateChanges.Clear();
        value.Risk = 0d;
        value.Uncertainty = 0d;
        value.OpensInteraction = false;
        value.RandomOutcome = false;
    }

    private static int Count(
        IEnumerable<string> deck,
        ISet<string> ids)
    {
        return deck.Count(id => ids.Contains(id ?? ""));
    }

    private static int StatusLevel(CombatUnitObservation? unit, string id)
    {
        return unit?.Statuses?
                   .FirstOrDefault(status => IdEquals(status.StatusId, id))?
                   .Level
               ?? 0;
    }

    private static double Value(
        IReadOnlyDictionary<string, double>? values,
        string key)
    {
        return values != null
               && values.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private static bool IdEquals(string? left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
