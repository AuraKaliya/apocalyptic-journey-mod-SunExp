using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatTurnFeatureNames
{
    public const string ActionsTakenThisTurn = "turnActionsTaken";
    public const string EnergySpentThisTurn = "turnEnergySpent";
    public const string EnemyHpAtTurnStart = "enemyHpAtTurnStart";
    public const string ConsecutiveNoProgressTurns = "consecutiveNoProgressTurns";
    public const string TurnSequence = "turnSequence";
    public const string EndTurnPurposeValue = "endTurnPurposeValue";
    public const string EndTurnPurposeCount = "endTurnPurposeCount";
    public const string EndTurnSevereMistake = "endTurnSevereMistake";
    public const string EndTurnSafeAlternativeCount = "endTurnSafeAlternativeCount";
    public const string EndTurnPlayableCardCount = "endTurnPlayableCardCount";
    public const string EndTurnUnusedEnergy = "endTurnUnusedEnergy";
    public const string EndTurnAvoidableUnusedEnergy =
        "endTurnAvoidableUnusedEnergy";
}

public sealed class CombatEndTurnAssessment
{
    public bool Prohibited { get; set; }

    public bool SevereMistake { get; set; }

    public bool HasDeliberatePurpose { get; set; }

    public int SafeAlternativeCount { get; set; }

    public int PlayableCardCount { get; set; }

    public int ActionsTakenThisTurn { get; set; }

    public int UnusedEnergy { get; set; }

    public int AvoidableUnusedEnergy { get; set; }

    public int ConsecutiveNoProgressTurns { get; set; }

    public double PurposeValue { get; set; }

    public double OpportunityCost { get; set; }

    public string Reason { get; set; } = "";
}

public static class CombatEndTurnSafety
{
    public static CombatEndTurnAssessment Assess(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatDecisionProfile profile)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (candidates == null) throw new ArgumentNullException(nameof(candidates));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var purposeValue = Feature(
            state.Features,
            CombatTurnFeatureNames.EndTurnPurposeValue);
        var actionsTaken = Math.Max(
            0,
            (int)Math.Round(Feature(
                state.Features,
                CombatTurnFeatureNames.ActionsTakenThisTurn)));
        var noProgressTurns = Math.Max(
            0,
            (int)Math.Round(Feature(
                state.Features,
                CombatTurnFeatureNames.ConsecutiveNoProgressTurns)));
        var safe = candidates
            .Where(candidate => IsSafeAlternative(state, candidate, profile))
            .ToList();
        var playableCards = safe.Count(candidate =>
            candidate.Action.Kind == CombatActionKind.PlayCard);
        var hasPurpose = purposeValue > 0.000001d;
        var unusedEnergy = Math.Max(0, state.CurrentPower);
        var avoidableUnusedEnergy = safe.Count > 0
            ? unusedEnergy
            : 0;
        var severe = safe.Count > 0;
        var reason = severe
            ? "end turn blocked: safe action remains"
              + ", actions=" + actionsTaken
              + ", playableCards=" + playableCards
              + ", unusedEnergy=" + unusedEnergy
              + ", noProgressTurns=" + noProgressTurns
            : hasPurpose
                ? "end turn lifecycle purpose is admissible because no productive action remains"
                : safe.Count == 0
                    ? "no safe positive action remains"
                    : "end turn allowed";
        return new CombatEndTurnAssessment
        {
            Prohibited = severe,
            SevereMistake = severe,
            HasDeliberatePurpose = hasPurpose,
            SafeAlternativeCount = safe.Count,
            PlayableCardCount = playableCards,
            ActionsTakenThisTurn = actionsTaken,
            UnusedEnergy = unusedEnergy,
            AvoidableUnusedEnergy = avoidableUnusedEnergy,
            ConsecutiveNoProgressTurns = noProgressTurns,
            PurposeValue = purposeValue,
            OpportunityCost = severe
                ? 100d
                  + safe.Count * 8d
                  + playableCards * 10d
                  + avoidableUnusedEnergy * 12d
                  + (actionsTaken == 0 ? 24d : 0d)
                  + noProgressTurns * 16d
                : 0d,
            Reason = reason
        };
    }

    public static bool HasDeliberatePurpose(
        IReadOnlyDictionary<string, double>? features)
    {
        return Feature(features, CombatTurnFeatureNames.EndTurnPurposeValue)
               > 0.000001d;
    }

    public static bool IsSafeAlternative(
        CombatStateObservation state,
        CombatCandidateEvaluation candidate,
        CombatDecisionProfile profile)
    {
        if (candidate?.Action == null
            || !candidate.Legal
            || candidate.Action.Kind == CombatActionKind.EndTurn
            || IsVisibleFake(candidate.Action)
            || candidate.Action.Cost > state.CurrentPower)
        {
            return false;
        }

        return CombatActionProductivity.Assess(state, candidate).Productive;
    }

    public static void Annotate(
        CombatActionObservation endTurn,
        CombatCandidateEvaluation evaluation,
        CombatEndTurnAssessment assessment)
    {
        if (endTurn == null) throw new ArgumentNullException(nameof(endTurn));
        if (evaluation == null) throw new ArgumentNullException(nameof(evaluation));
        if (assessment == null) throw new ArgumentNullException(nameof(assessment));

        endTurn.Features[CombatTurnFeatureNames.EndTurnSevereMistake] =
            assessment.SevereMistake ? 1d : 0d;
        endTurn.Features[CombatTurnFeatureNames.EndTurnSafeAlternativeCount] =
            assessment.SafeAlternativeCount;
        endTurn.Features[CombatTurnFeatureNames.EndTurnPlayableCardCount] =
            assessment.PlayableCardCount;
        endTurn.Features[CombatTurnFeatureNames.EndTurnUnusedEnergy] =
            assessment.UnusedEnergy;
        endTurn.Features[
                CombatTurnFeatureNames.EndTurnAvoidableUnusedEnergy] =
            assessment.AvoidableUnusedEnergy;
        endTurn.Features[CombatTurnFeatureNames.EndTurnPurposeValue] =
            assessment.PurposeValue;
        if (assessment.Prohibited)
        {
            evaluation.Legal = false;
            evaluation.BaseRuleScore = -assessment.OpportunityCost;
            evaluation.RuleScore = -assessment.OpportunityCost;
            evaluation.RejectionReason = assessment.Reason;
        }
    }

    public static double ScoreNativeEndTurnPurpose(string? script)
    {
        if (string.IsNullOrWhiteSpace(script)
            || !ContainsAny(
                script!,
                "AddEvent(\"EndRound\"",
                "AddEvent (\"EndRound\"",
                "TurnEnded"))
        {
            return 0d;
        }

        var source = script!;
        var score = 0d;
        if (ContainsAny(source, "GiveWin", "WinTheFight"))
        {
            score += 100d;
        }
        if (ContainsAny(source, "ChangeDefence", "GainBlock"))
        {
            score += 8d;
        }
        if (ContainsAny(source, "UseCard(", "RunImmediately("))
        {
            score += 8d;
        }
        if (ContainsAny(source, "CreateCard(", "AddCard", "DrawCount("))
        {
            score += 5d;
        }
        if (ContainsAny(source, "ChangePower(", "GainEnergy"))
        {
            score += 4d;
        }
        if (ContainsAny(source, "AddBuff(", "RemoveAllBadBuff", "Cleanse"))
        {
            score += 3d;
        }
        if (ContainsAny(source, "ChangeHp((")
            && !ContainsAny(source, "ChangeHp((-", "ChangeHp(\"-"))
        {
            score += 2d;
        }
        return score;
    }

    private static bool IsVisibleFake(CombatActionObservation action)
    {
        return action.Features.TryGetValue("visibleFake", out var value)
               && value > 0.5d;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token =>
            value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static double Feature(
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
}
