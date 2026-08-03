using System;
using System.Collections.Generic;
using System.Linq;
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

        before = CombatPlayerObservationBoundary.Normalize(before);
        after = after == null
            ? null
            : CombatPlayerObservationBoundary.Normalize(after);
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
            SearchAlgorithm = decision.SearchAlgorithm,
            SearchSimulations = decision.SearchSimulations,
            SearchNodes = decision.SearchNodes,
            SearchTranspositionHits = decision.SearchTranspositionHits,
            SearchBudgetTier = decision.SearchBudgetTier,
            StateFeatures = BuildStateFeatures(before),
            Features = CombatPublicFeaturePolicy.SanitizeAction(
                decision.Action.Features),
            PredictedScore = Finite(decision.Score),
            RewardComponents = reward,
            Reward = Finite(
                reward.EffectiveDamage
                + reward.PlayerHpChange * 1.2d
                + reward.UsefulDefend * 0.1d
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
        // Witch's Apocalyptic Journey keeps shield between turns.  Shield above the
        // current telegraphed hit is therefore stored survivability, not waste.
        reward.UsefulDefend = reward.ShieldGain;
        reward.WastedDefend = 0d;
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
        var features = CombatPublicFeaturePolicy.SanitizeState(state.Features);
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
            Features = CombatPublicFeaturePolicy.SanitizeAction(action.Features),
            Semantics = SanitizeSemantics(action.Semantics),
            Utility = ToTrainingUtility(evaluation.Utility),
            BaseRuleScore = Finite(evaluation.BaseRuleScore),
            RawResidualScore = Finite(evaluation.RawResidualScore),
            ResidualApplicability = Finite(evaluation.ResidualApplicability),
            AppliedResidualScore = Finite(evaluation.AppliedResidualScore),
            RuleScore = Finite(evaluation.RuleScore),
            PlanScore = Finite(evaluation.PlanScore),
            SearchPrior = Finite(evaluation.SearchPrior),
            SearchVisits = Math.Max(0, evaluation.SearchVisits),
            SearchDeathRisk = Finite(evaluation.SearchDeathRisk),
            SearchMeanReturn = Finite(evaluation.SearchMeanReturn),
            SearchReturnStandardError =
                Finite(evaluation.SearchReturnStandardError),
            SearchLowerTailMean = Finite(evaluation.SearchLowerTailMean),
            SearchReturnQuantiles = evaluation.SearchReturnQuantiles
                .Select(Finite)
                .Take(16)
                .ToList()
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
            EnergySetAmount = value.EnergySetAmount.HasValue
                ? Finite(value.EnergySetAmount.Value)
                : null,
            EnergyMinimum = value.EnergyMinimum.HasValue
                ? Finite(value.EnergyMinimum.Value)
                : null,
            RestoreEnergyToMaximum = value.RestoreEnergyToMaximum,
            CardRetrievals = value.CardRetrievals.Select(item =>
                new CombatCardRetrievalSemantic
                {
                    SourceZone = item.SourceZone,
                    DestinationZone = item.DestinationZone,
                    Amount = Math.Max(0, item.Amount),
                    RequiredCardTag = item.RequiredCardTag ?? "",
                    CandidateBranchCount = Math.Max(
                        1,
                        Math.Min(3, item.CandidateBranchCount))
                }).ToList(),
            Scaling = Finite(value.Scaling),
            DeckValue = Finite(value.DeckValue),
            Buff = Finite(value.Buff),
            Debuff = Finite(value.Debuff),
            Cleanse = Finite(value.Cleanse),
            CostReduction = Finite(value.CostReduction),
            CardGeneration = Finite(value.CardGeneration),
            PersistentValue = Finite(value.PersistentValue),
            DamageMultiplierGain = Finite(value.DamageMultiplierGain),
            ImmediateHpDamage = Finite(value.ImmediateHpDamage),
            ImmediateDurabilityDamage =
                Finite(value.ImmediateDurabilityDamage),
            DeferredHpDamage = Finite(value.DeferredHpDamage),
            AffectedEnemyCount = Math.Max(0, value.AffectedEnemyCount),
            TargetEffects = value.TargetEffects.Select(item =>
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
                value.StateChanges),
            CooldownTurns = Finite(value.CooldownTurns),
            Risk = Finite(value.Risk),
            Uncertainty = Finite(value.Uncertainty),
            OpensInteraction = value.OpensInteraction,
            RandomOutcome = value.RandomOutcome,
            EndsTurn = value.EndsTurn,
            DamageToBlockSetup = value.DamageToBlockSetup,
            HandTransform = value.HandTransform == null
                ? null
                : new CombatHandTransformSemantic
                {
                    TargetCardId = value.HandTransform.TargetCardId,
                    TargetCardSemantics = SanitizeSemantics(
                        value.HandTransform.TargetCardSemantics),
                    TransformAllHandCards =
                        value.HandTransform.TransformAllHandCards,
                    PreserveInstances = value.HandTransform.PreserveInstances,
                    ClearsEnhancements =
                        value.HandTransform.ClearsEnhancements,
                    ClearsVariables = value.HandTransform.ClearsVariables,
                    TargetRetained = value.HandTransform.TargetRetained,
                    TargetExhaustsOnUse =
                        value.HandTransform.TargetExhaustsOnUse,
                    GrowthStateKey = value.HandTransform.GrowthStateKey,
                    GrowthPerExhaust = Finite(
                        value.HandTransform.GrowthPerExhaust),
                    CurrentGrowthValue = Finite(
                        value.HandTransform.CurrentGrowthValue),
                    TargetTier = Math.Max(0, value.HandTransform.TargetTier),
                    NextTierThreshold = Math.Max(
                        0,
                        value.HandTransform.NextTierThreshold),
                    CooldownProgressRequired = Math.Max(
                        0d,
                        Finite(value.HandTransform.CooldownProgressRequired)),
                    CooldownProgressEvent =
                        value.HandTransform.CooldownProgressEvent
                }
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
