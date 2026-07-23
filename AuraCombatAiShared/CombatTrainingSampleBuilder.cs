using System;
using System.Collections.Generic;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public static class CombatTrainingSampleBuilder
{
    public static CombatTrainingSample Create(
        CombatStateObservation before,
        CombatStateObservation? after,
        CombatDecision decision,
        long decisionIndex,
        long transactionId,
        string completionState,
        string terminalReason,
        bool terminal,
        string gameBuild,
        string sharedBuild,
        string demonstrator = "policy",
        string recommendedCandidateId = "",
        bool policyVisibleToHuman = false)
    {
        if (before == null)
        {
            throw new ArgumentNullException(nameof(before));
        }

        if (decision?.Action == null)
        {
            throw new ArgumentException("decision must contain an action", nameof(decision));
        }

        var reward = BuildReward(before, after, decision.Action, terminal);
        var executedBy = string.Equals(
            demonstrator,
            "human",
            StringComparison.OrdinalIgnoreCase)
            ? "human"
            : "policy";
        var policyPreselectedCandidateId = string.IsNullOrWhiteSpace(recommendedCandidateId)
            && executedBy == "policy"
            ? decision.Action.CandidateId
            : recommendedCandidateId ?? "";
        var policyWasExecuted = !string.IsNullOrWhiteSpace(policyPreselectedCandidateId)
                                && string.Equals(
                                    decision.Action.CandidateId,
                                    policyPreselectedCandidateId,
                                    StringComparison.Ordinal);
        var policyPreselectedDisplayName = FindCandidateDisplayName(
            decision,
            policyPreselectedCandidateId);
        var sample = new CombatTrainingSample
        {
            GameBuild = gameBuild ?? "",
            SharedBuild = sharedBuild ?? "",
            BattleSessionId = before.BattleSessionId,
            DecisionIndex = decisionIndex,
            Sequence = before.Sequence,
            TransactionId = transactionId,
            StateFingerprint = before.Fingerprint,
            NextStateFingerprint = after?.Fingerprint ?? "",
            DecisionProfile = decision.ProfileId,
            CandidateId = decision.Action.CandidateId,
            SourceId = decision.Action.SourceId,
            Demonstrator = executedBy,
            RecommendedCandidateId = policyPreselectedCandidateId,
            Selection = new CombatTrainingSelectionTrace
            {
                ExecutedBy = executedBy,
                LabelKind = executedBy == "human"
                    ? "human-preference"
                    : "policy-trajectory",
                ExecutedCandidateId = decision.Action.CandidateId,
                ExecutedDisplayName = DisplayNameOrId(decision.Action),
                PolicyPreselectedCandidateId = policyPreselectedCandidateId,
                PolicyPreselectedDisplayName = policyPreselectedDisplayName,
                PolicyWasExecuted = policyWasExecuted,
                HumanPolicyAgreement = executedBy == "human" && policyWasExecuted,
                PolicyVisibleToHuman = executedBy == "human" && policyVisibleToHuman
            },
            PlanSummary = decision.PlanSummary,
            Plan = new List<CombatPlanStep>(decision.Plan),
            StateFeatures = BuildStateFeatures(before),
            Features = SanitizeFeatures(decision.Action.Features),
            PredictedScore = Finite(decision.Score),
            RewardComponents = reward,
            Reward = Finite(
                reward.EffectiveDamage
                + reward.PlayerHpChange * 1.2d
                + reward.UsefulDefend * 0.1d
                - reward.WastedDefend * 0.15d
                + reward.PowerChange * 0.2d
                + reward.HandChange * 0.1d
                + reward.TurnCost
                + reward.TerminalBonus),
            Terminal = terminal,
            BattleOutcome = ResolveOutcome(after, terminal),
            CompletionState = completionState ?? "",
            TerminalReason = terminalReason ?? ""
        };

        for (var i = 0; i < decision.Candidates.Count; i++)
        {
            sample.Candidates.Add(ToTrainingCandidate(
                decision.Candidates[i],
                decision.Action.CandidateId,
                policyPreselectedCandidateId,
                executedBy));
        }

        return sample;
    }

    private static CombatTrainingReward BuildReward(
        CombatStateObservation before,
        CombatStateObservation? after,
        CombatActionObservation action,
        bool terminal)
    {
        var reward = new CombatTrainingReward
        {
            TurnCost = action.Kind == CombatActionKind.EndTurn ? -0.25d : 0d
        };
        if (after == null)
        {
            return reward;
        }

        reward.EffectiveDamage = Math.Max(0, SumEnemyHp(before) - SumEnemyHp(after));
        reward.PlayerHpChange = after.Player.CurrentHp - before.Player.CurrentHp;
        reward.ShieldGain = Math.Max(0, after.Player.Defend - before.Player.Defend);
        var threat = before.Threat ?? new CombatThreatForecast();
        var requiredDefend = Math.Max(
            0d,
            threat.RiskAdjustedBlockableDamage(0.65d) - before.Player.Defend);
        reward.UsefulDefend = Math.Min(reward.ShieldGain, requiredDefend);
        reward.WastedDefend = Math.Max(0d, reward.ShieldGain - reward.UsefulDefend);
        reward.UnblockableThreat = Math.Max(
            0d,
            threat.ExpectedUnblockableDamage + threat.ExpectedDamageOverTime);
        reward.PowerChange = after.CurrentPower - before.CurrentPower;
        reward.HandChange = after.HandCount - before.HandCount;
        if (terminal)
        {
            if (after.Enemies.Count == 0 && after.Player.CurrentHp > 0)
            {
                reward.TerminalBonus = 50d;
            }
            else if (after.Player.CurrentHp <= 0)
            {
                reward.TerminalBonus = -50d;
            }
        }

        return reward;
    }

    private static Dictionary<string, double> BuildStateFeatures(CombatStateObservation state)
    {
        var features = SanitizeFeatures(state.Features);
        features["playerHp"] = state.Player.CurrentHp;
        features["playerMaxHp"] = state.Player.MaxHp;
        features["playerHpRatio"] = state.Player.MaxHp <= 0
            ? 0d
            : Finite((double)state.Player.CurrentHp / state.Player.MaxHp);
        features["playerDefend"] = state.Player.Defend;
        features["power"] = state.CurrentPower;
        features["maxPower"] = state.MaxPower;
        features["handCount"] = state.HandCount;
        features["enemyCount"] = state.Enemies.Count;
        features["enemyHpTotal"] = SumEnemyHp(state);
        features["expectedIncomingDamage"] = Finite(state.ExpectedIncomingDamage);
        features["expectedBlockableDamage"] = Finite(state.Threat?.ExpectedBlockableDamage ?? 0d);
        features["maximumBlockableDamage"] = Finite(state.Threat?.MaximumBlockableDamage ?? 0d);
        features["expectedUnblockableDamage"] = Finite(state.Threat?.ExpectedUnblockableDamage ?? 0d);
        features["expectedDamageOverTime"] = Finite(state.Threat?.ExpectedDamageOverTime ?? 0d);
        features["attackProbability"] = Finite(state.Threat?.AttackProbability ?? 0d);
        features["threatConfidence"] = Finite(state.Threat?.Confidence ?? 0d);
        features["currentIntentKnown"] = state.Threat?.CurrentIntentKnown == true ? 1d : 0d;
        return features;
    }

    private static string FindCandidateDisplayName(
        CombatDecision decision,
        string candidateId)
    {
        for (var i = 0; i < decision.Candidates.Count; i++)
        {
            var action = decision.Candidates[i].Action;
            if (action != null
                && string.Equals(
                    action.CandidateId,
                    candidateId,
                    StringComparison.Ordinal))
            {
                return DisplayNameOrId(action);
            }
        }

        return candidateId ?? "";
    }

    private static string DisplayNameOrId(CombatActionObservation action)
    {
        return string.IsNullOrWhiteSpace(action.DisplayName)
            ? action.CandidateId
            : action.DisplayName;
    }

    private static CombatTrainingCandidate ToTrainingCandidate(
        CombatCandidateEvaluation evaluation,
        string executedCandidateId,
        string policyPreselectedCandidateId,
        string executedBy)
    {
        var action = evaluation.Action ?? new CombatActionObservation();
        var isExecuted = string.Equals(
            action.CandidateId,
            executedCandidateId,
            StringComparison.Ordinal);
        return new CombatTrainingCandidate
        {
            CandidateId = action.CandidateId,
            SourceId = action.SourceId,
            DisplayName = action.DisplayName,
            ActionKind = action.Kind.ToString(),
            TargetKind = action.TargetKind.ToString(),
            Cost = action.Cost,
            Legal = evaluation.Legal,
            RejectionReason = evaluation.RejectionReason,
            IsExecutedAction = isExecuted,
            IsHumanSelection = isExecuted && executedBy == "human",
            IsPolicyPreselection = !string.IsNullOrWhiteSpace(policyPreselectedCandidateId)
                                   && string.Equals(
                                       action.CandidateId,
                                       policyPreselectedCandidateId,
                                       StringComparison.Ordinal),
            Features = SanitizeFeatures(action.Features),
            Semantics = SanitizeSemantics(action.Semantics),
            Utility = ToTrainingUtility(evaluation.Utility),
            BaseRuleScore = Finite(evaluation.BaseRuleScore),
            RawResidualScore = Finite(evaluation.RawResidualScore),
            ResidualApplicability = Finite(evaluation.ResidualApplicability),
            AppliedResidualScore = Finite(evaluation.AppliedResidualScore),
            RuleScore = Finite(evaluation.RuleScore)
        };
    }

    private static CombatActionSemantics SanitizeSemantics(CombatActionSemantics? value)
    {
        value ??= new CombatActionSemantics();
        return new CombatActionSemantics
        {
            Damage = Finite(value.Damage),
            TrueDamage = Finite(value.TrueDamage),
            DamageOverTime = Finite(value.DamageOverTime),
            HitCount = Math.Max(1d, Finite(value.HitCount)),
            Defend = Finite(value.Defend),
            Heal = Finite(value.Heal),
            Draw = Finite(value.Draw),
            EnergyGain = Finite(value.EnergyGain),
            Scaling = Finite(value.Scaling),
            DeckValue = Finite(value.DeckValue),
            Buff = Finite(value.Buff),
            Debuff = Finite(value.Debuff),
            Cleanse = Finite(value.Cleanse),
            CostReduction = Finite(value.CostReduction),
            CardGeneration = Finite(value.CardGeneration),
            PersistentValue = Finite(value.PersistentValue),
            CooldownTurns = Finite(value.CooldownTurns),
            Risk = Finite(value.Risk),
            Uncertainty = Finite(value.Uncertainty),
            OpensInteraction = value.OpensInteraction,
            RandomOutcome = value.RandomOutcome
        };
    }

    private static CombatTrainingUtility ToTrainingUtility(DecisionUtility? value)
    {
        value ??= new DecisionUtility();
        return new CombatTrainingUtility
        {
            Survival = Finite(value.Survival),
            Lethal = Finite(value.Lethal),
            Tempo = Finite(value.Tempo),
            Resource = Finite(value.Resource),
            DeckEconomy = Finite(value.DeckEconomy),
            Scaling = Finite(value.Scaling),
            Synergy = Finite(value.Synergy),
            Continuation = Finite(value.Continuation),
            Risk = Finite(value.Risk),
            Uncertainty = Finite(value.Uncertainty),
            Coordination = Finite(value.Coordination)
        };
    }

    private static Dictionary<string, double> SanitizeFeatures(
        IReadOnlyDictionary<string, double>? features)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (features == null)
        {
            return result;
        }

        foreach (var pair in features)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                result[pair.Key] = Finite(pair.Value);
            }
        }

        return result;
    }

    private static int SumEnemyHp(CombatStateObservation state)
    {
        var total = 0;
        for (var i = 0; i < state.Enemies.Count; i++)
        {
            total += Math.Max(0, state.Enemies[i].CurrentHp);
        }

        return total;
    }

    private static string ResolveOutcome(CombatStateObservation? after, bool terminal)
    {
        if (!terminal || after == null)
        {
            return "unknown";
        }

        if (after.Enemies.Count == 0 && after.Player.CurrentHp > 0)
        {
            return "victory";
        }

        return after.Player.CurrentHp <= 0 ? "defeat" : "ended";
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
