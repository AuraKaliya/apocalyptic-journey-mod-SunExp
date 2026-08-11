using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public sealed class CombatSearchBudget
{
    public string Tier { get; set; } = "fixed";

    public int SimulationBudget { get; set; }

    public int MinimumSimulations { get; set; }

    public int StabilityWindow { get; set; }

    public int StableChecks { get; set; }

    public int MaxPly { get; set; }

    public int NodeBudget { get; set; }

    public int TimeBudgetMilliseconds { get; set; }

    public int MinimumTimeMilliseconds { get; set; }

    public int MinimumRootVisits { get; set; } = 2;

    public int MinimumChallengerVisits { get; set; } = 4;

    public double EarlyStopConfidence { get; set; } = 0.55d;

    public double DominanceStandardErrors { get; set; } = 1d;

    public string Reason { get; set; } = "";
}

public static class CombatSearchBudgetPolicy
{
    public static CombatDecisionProfile WithContext(
        CombatDecisionProfile source,
        string context)
    {
        var profile = source ?? new CombatDecisionProfile();
        return new CombatDecisionProfile
        {
            Id = profile.Id,
            Weights = profile.Weights,
            Graph = profile.Graph,
            ModelOwnsActionSelection = profile.ModelOwnsActionSelection,
            MinimumActionScore = profile.MinimumActionScore,
            UnknownActionPenalty = profile.UnknownActionPenalty,
            EmergencyHpRatio = profile.EmergencyHpRatio,
            FreeActionTieBreaker = profile.FreeActionTieBreaker,
            SkillCooldownPenalty = profile.SkillCooldownPenalty,
            ThreatRiskTolerance = profile.ThreatRiskTolerance,
            SurplusDefendRetention = profile.SurplusDefendRetention,
            SearchSimulationBudget = profile.SearchSimulationBudget,
            SearchNodeBudget = profile.SearchNodeBudget,
            SearchMaxPly = profile.SearchMaxPly,
            SearchMinimumSimulations = profile.SearchMinimumSimulations,
            SearchStabilityWindow = profile.SearchStabilityWindow,
            SearchStableChecks = profile.SearchStableChecks,
            SearchBudgetMode = profile.SearchBudgetMode,
            SearchQuality = profile.SearchQuality,
            SearchBudgetContext = string.IsNullOrWhiteSpace(context)
                ? "deployment"
                : context,
            SearchTimeBudgetMilliseconds =
                profile.SearchTimeBudgetMilliseconds,
            SearchMinimumTimeMilliseconds =
                profile.SearchMinimumTimeMilliseconds,
            SearchMinimumRootVisits = profile.SearchMinimumRootVisits,
            SearchMinimumChallengerVisits =
                profile.SearchMinimumChallengerVisits,
            SearchEarlyStopConfidence = profile.SearchEarlyStopConfidence,
            SearchDominanceStandardErrors =
                profile.SearchDominanceStandardErrors,
            SearchModelEvaluationBudget =
                profile.SearchModelEvaluationBudget,
            SearchExploration = profile.SearchExploration,
            DeathRiskLimit = profile.DeathRiskLimit,
            LoopMaximumCertifiedCycles = profile.LoopMaximumCertifiedCycles,
            LoopLimitDamageMaximumCycles =
                profile.LoopLimitDamageMaximumCycles,
            LoopMinimumEffectiveProgress =
                profile.LoopMinimumEffectiveProgress,
            LoopMinimumHpReserveRatio = profile.LoopMinimumHpReserveRatio,
            TailRiskPenalty = profile.TailRiskPenalty,
            TailRiskQuantile = profile.TailRiskQuantile,
            RiskPreference = profile.RiskPreference,
            UncertaintyPenalty = profile.UncertaintyPenalty,
            NetworkDeathRiskWeight = profile.NetworkDeathRiskWeight,
            SemanticCoverageRiskWeight =
                profile.SemanticCoverageRiskWeight,
            SetupValueWeight = profile.SetupValueWeight,
            PersistentValueWeight = profile.PersistentValueWeight,
            NextTurnThreatRetention = profile.NextTurnThreatRetention,
            UnknownNextTurnThreatProbabilityFloor =
                profile.UnknownNextTurnThreatProbabilityFloor,
            EndTurnUncertainty = profile.EndTurnUncertainty,
            PreferDominantFreeSetup = profile.PreferDominantFreeSetup,
            UseLowConfidenceFallback = profile.UseLowConfidenceFallback,
            MinimumSearchConfidence = profile.MinimumSearchConfidence,
            EnableActorCandidatePruning = profile.EnableActorCandidatePruning,
            ActorCandidateTopK = profile.ActorCandidateTopK,
            ActorCandidateProbabilityMass =
                profile.ActorCandidateProbabilityMass
        };
    }

    public static CombatSearchBudget Resolve(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatDecisionProfile profile)
    {
        var legal = (candidates ?? Array.Empty<CombatCandidateEvaluation>())
            .Where(candidate => candidate?.Legal == true
                                && candidate.Action != null)
            .ToList();
        if (string.Equals(
                profile.SearchBudgetMode,
                "fixed",
                StringComparison.OrdinalIgnoreCase))
        {
            return Fixed(profile);
        }
        if (legal.Count <= 1)
        {
            return QualityBudget(profile, "forced", 1, 1, 1, 1, 4, 256,
                "single-legal-action");
        }

        var playerHp = Math.Max(0, state?.Player?.CurrentHp ?? 0);
        var playerMaxHp = Math.Max(1, state?.Player?.MaxHp ?? 1);
        var incoming = Math.Max(0d, state?.ExpectedIncomingDamage ?? 0d);
        var enemyCount = state?.Enemies?.Count ?? 0;
        var enemyHp = state?.Enemies?.Sum(enemy =>
            Math.Max(0, enemy.CurrentHp)) ?? 0;
        var boss = HasToken(state, "boss")
                   || (state?.Enemies ?? new List<CombatUnitObservation>())
                   .Any(enemy => ContainsToken(enemy.DefinitionId, "boss")
                                 || ContainsToken(enemy.DefinitionId, "final"));
        var damageCap = HasToken(state, "damagecap")
                        || HasToken(state, "damagelimit")
                        || HasToken(state, "limitdamage")
                        || HasStatusToken(state, "damagecap")
                        || HasStatusToken(state, "limitdamage")
                        || HasStatusToken(state, "限伤");
        var loop = HasToken(state, "loop")
                   || HasStatusToken(state, "loop")
                   || legal.Any(candidate =>
                       PositiveFeature(candidate.Action, "certifiedLoop")
                       || PositiveFeature(candidate.Action, "repeatableLoop")
                       || candidate.Action.Semantics?.EndOfCycleSelfHpLoss > 0d
                          && (candidate.Action.Semantics?.CardGeneration > 0d
                              || candidate.Action.Semantics?.EnergyGain > 0d
                              || candidate.Action.Semantics?.Draw > 0d));
        var highRisk = incoming >= playerHp
                       || playerHp / (double)playerMaxHp <= 0.35d
                       || legal.Any(candidate =>
                           candidate.Action.Semantics?.Risk >= 0.65d);
        var ruleEntropy = NormalizedRuleEntropy(legal);
        var lowRuleEntropy = legal.Count >= 4 && ruleEntropy <= 0.35d;
        var uncertain = legal.Count >= 8 && !lowRuleEntropy
                        || enemyCount >= 3
                        || legal.Any(candidate =>
                            candidate.Action.Semantics?.Uncertainty >= 0.5d);
        var lethal = enemyHp > 0
                     && legal.Any(candidate =>
                         (candidate.Action.Semantics?.Damage ?? 0d)
                         + (candidate.Action.Semantics?.TrueDamage ?? 0d)
                         >= enemyHp
                         && (candidate.Action.Semantics?.SelfHpLoss ?? 0d)
                         < playerHp);
        var teacher = ContainsToken(profile.SearchBudgetContext, "teacher");
        var hardTeacher = ContainsToken(
            profile.SearchBudgetContext,
            "hard");

        if (damageCap || loop)
        {
            return QualityBudget(
                profile,
                "complex",
                512,
                128,
                32,
                2,
                16,
                4096,
                damageCap ? "damage-cap-or-limit" : "loop-or-fake-loop");
        }
        if (boss || highRisk || uncertain || hardTeacher)
        {
            return QualityBudget(
                profile,
                "difficult",
                teacher ? 512 : 384,
                128,
                32,
                2,
                teacher ? 14 : 12,
                4096,
                boss
                    ? "boss"
                    : highRisk
                        ? "high-death-risk"
                        : uncertain
                            ? "branching-or-chance"
                            : "hard-seed-teacher");
        }
        if (lethal || legal.Count <= 3 || lowRuleEntropy)
        {
            return QualityBudget(
                profile,
                "simple",
                teacher ? 128 : 96,
                32,
                16,
                2,
                lethal ? 8 : 6,
                1024,
                lethal
                    ? "visible-lethal"
                    : lowRuleEntropy
                        ? "low-rule-entropy"
                        : "low-branching");
        }
        return QualityBudget(
            profile,
            "normal",
            teacher ? 384 : 224,
            64,
            32,
            2,
            teacher ? 12 : 10,
            teacher ? 4096 : 2048,
            teacher ? "self-play-teacher" : "ordinary-position");
    }

    private static CombatSearchBudget Fixed(CombatDecisionProfile profile)
    {
        return Budget(
            "fixed",
            Math.Max(1, profile.SearchSimulationBudget),
            Math.Max(1, profile.SearchMinimumSimulations),
            Math.Max(1, profile.SearchStabilityWindow),
            Math.Max(1, profile.SearchStableChecks),
            Math.Max(1, profile.SearchMaxPly),
            Math.Max(256, profile.SearchNodeBudget),
            "fixed-test-budget");
    }

    private static CombatSearchBudget QualityBudget(
        CombatDecisionProfile profile,
        string tier,
        int simulations,
        int minimum,
        int stabilityWindow,
        int stableChecks,
        int maxPly,
        int nodeBudget,
        string reason)
    {
        if (string.Equals(tier, "forced", StringComparison.Ordinal))
        {
            return Budget(
                tier,
                simulations,
                minimum,
                stabilityWindow,
                stableChecks,
                maxPly,
                nodeBudget,
                reason + "; quality=" + NormalizeQuality(profile.SearchQuality));
        }

        var quality = NormalizeQuality(profile.SearchQuality);
        var simulationScale = quality == "fast"
            ? 0.65d
            : quality == "deep"
                ? 1.75d
                : 1d;
        var nodeScale = quality == "fast"
            ? 0.75d
            : quality == "deep"
                ? 2d
                : 1d;
        var plyAdjustment = quality == "fast"
            ? -2
            : quality == "deep"
                ? 4
                : 0;
        var resolved = Budget(
            tier,
            Math.Max(1, (int)Math.Ceiling(simulations * simulationScale)),
            Math.Max(1, (int)Math.Ceiling(minimum * simulationScale)),
            stabilityWindow,
            stableChecks,
            Math.Max(4, Math.Min(32, maxPly + plyAdjustment)),
            Math.Max(256, (int)Math.Ceiling(nodeBudget * nodeScale)),
            reason + "; quality=" + quality);
        return ApplyDeploymentLimits(profile, resolved);
    }

    private static CombatSearchBudget ApplyDeploymentLimits(
        CombatDecisionProfile profile,
        CombatSearchBudget budget)
    {
        if (!ContainsToken(profile.SearchBudgetContext, "deployment"))
        {
            return budget;
        }

        var simulationCap = budget.Tier switch
        {
            "complex" => 256,
            "difficult" => 192,
            "normal" => 128,
            "simple" => 96,
            _ => budget.SimulationBudget
        };
        if (budget.SimulationBudget > simulationCap)
        {
            budget.SimulationBudget = simulationCap;
            budget.MinimumSimulations = Math.Min(
                budget.MinimumSimulations,
                simulationCap);
            budget.Reason += "; deployment-simulation-cap=" + simulationCap;
        }
        budget.TimeBudgetMilliseconds = Math.Max(
            0,
            profile.SearchTimeBudgetMilliseconds);
        if (budget.TimeBudgetMilliseconds > 0)
        {
            var automaticRatio = budget.Tier switch
            {
                "complex" => 0.60d,
                "difficult" => 0.50d,
                "normal" => 0.35d,
                "simple" => 0.25d,
                _ => 0d
            };
            var requestedMinimum = profile.SearchMinimumTimeMilliseconds > 0
                ? profile.SearchMinimumTimeMilliseconds
                : (int)Math.Ceiling(
                    budget.TimeBudgetMilliseconds * automaticRatio);
            budget.MinimumTimeMilliseconds = Math.Max(
                0,
                Math.Min(budget.TimeBudgetMilliseconds, requestedMinimum));
            budget.Reason += "; deployment-time-ms="
                             + budget.TimeBudgetMilliseconds
                             + "; minimum-effective-ms="
                             + budget.MinimumTimeMilliseconds;
        }
        budget.MinimumRootVisits = Math.Max(
            1,
            Math.Min(
                8,
                Math.Max(
                    profile.SearchMinimumRootVisits,
                    budget.Tier == "complex" ? 4
                    : budget.Tier == "difficult" ? 3
                    : 2)));
        budget.MinimumChallengerVisits = Math.Max(
            budget.MinimumRootVisits,
            Math.Min(16, profile.SearchMinimumChallengerVisits));
        budget.EarlyStopConfidence = Math.Max(
            0d,
            Math.Min(1d, profile.SearchEarlyStopConfidence));
        budget.DominanceStandardErrors = Math.Max(
            0.25d,
            Math.Min(4d, profile.SearchDominanceStandardErrors));
        return budget;
    }

    private static string NormalizeQuality(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized == "fast" || normalized == "deep"
            ? normalized
            : "balanced";
    }

    private static CombatSearchBudget Budget(
        string tier,
        int simulations,
        int minimum,
        int stabilityWindow,
        int stableChecks,
        int maxPly,
        int nodeBudget,
        string reason)
    {
        return new CombatSearchBudget
        {
            Tier = tier,
            SimulationBudget = simulations,
            MinimumSimulations = Math.Min(simulations, minimum),
            StabilityWindow = stabilityWindow,
            StableChecks = stableChecks,
            MaxPly = maxPly,
            NodeBudget = nodeBudget,
            Reason = reason
        };
    }

    private static bool HasToken(CombatStateObservation? state, string token)
    {
        return (state?.Features ?? new Dictionary<string, double>())
            .Any(item => Math.Abs(item.Value) > 0.0000001d
                         && ContainsToken(item.Key, token));
    }

    internal static double NormalizedRuleEntropy(
        IReadOnlyList<CombatCandidateEvaluation> candidates)
    {
        var legal = (candidates ?? Array.Empty<CombatCandidateEvaluation>())
            .Where(candidate => candidate?.Legal == true
                                && candidate.Action != null)
            .ToList();
        if (legal.Count <= 1)
        {
            return 0d;
        }
        var mean = legal.Average(candidate => Finite(candidate.RuleScore));
        var variance = legal.Average(candidate =>
        {
            var delta = Finite(candidate.RuleScore) - mean;
            return delta * delta;
        });
        var deviation = Math.Sqrt(Math.Max(0.0000001d, variance));
        var maximum = double.NegativeInfinity;
        for (var index = 0; index < legal.Count; index++)
        {
            maximum = Math.Max(
                maximum,
                Math.Max(
                    -30d,
                    Math.Min(
                        30d,
                        (Finite(legal[index].RuleScore) - mean) / deviation)));
        }
        var total = 0d;
        for (var index = 0; index < legal.Count; index++)
        {
            var normalized = Math.Max(
                -30d,
                Math.Min(
                    30d,
                    (Finite(legal[index].RuleScore) - mean) / deviation));
            total += Math.Exp(normalized - maximum);
        }
        var entropy = 0d;
        for (var index = 0; index < legal.Count; index++)
        {
            var normalized = Math.Max(
                -30d,
                Math.Min(
                    30d,
                    (Finite(legal[index].RuleScore) - mean) / deviation));
            var probability = Math.Exp(normalized - maximum)
                              / Math.Max(0.0000001d, total);
            if (probability > 0d)
            {
                entropy -= probability * Math.Log(probability);
            }
        }
        return Math.Max(0d, Math.Min(1d, entropy / Math.Log(legal.Count)));
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }

    private static bool HasStatusToken(
        CombatStateObservation? state,
        string token)
    {
        if ((state?.Player?.Statuses ?? new List<CombatStatusObservation>())
            .Any(status => ContainsToken(status.StatusId, token)))
        {
            return true;
        }
        return (state?.Enemies ?? new List<CombatUnitObservation>())
            .SelectMany(enemy =>
                enemy.Statuses ?? new List<CombatStatusObservation>())
            .Any(status => ContainsToken(status.StatusId, token));
    }

    private static bool PositiveFeature(
        CombatActionObservation action,
        string key)
    {
        return action?.Features != null
               && action.Features.TryGetValue(key, out var value)
               && value > 0.0000001d;
    }

    private static bool ContainsToken(string? value, string token)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value!.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
