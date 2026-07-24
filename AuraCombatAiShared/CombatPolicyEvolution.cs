using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatPolicyEvolutionRequest
{
    public string DecisionProfile { get; set; } = "balanced";

    public int Iterations { get; set; } = 1;

    public int TrainingEpisodesPerIteration { get; set; } = 32;

    public int ArenaEpisodesPerIteration { get; set; } = 16;

    public ulong SeedStart { get; set; } = 1UL;

    public double MaximumWinRateRegression { get; set; }

    public CombatDecisionProfile Profile { get; set; } = new();

    public CombatPolicyValueTrainingOptions Training { get; set; } = new();

    public List<CombatScenarioDefinition> Scenarios { get; set; } = new();

    public Action<int, int, string>? Progress { get; set; }
}

public sealed class CombatPolicyEvolutionIteration
{
    public int Iteration { get; set; }

    public int ReplayEpisodes { get; set; }

    public string CandidateModelId { get; set; } = "";

    public double ChampionArenaScore { get; set; }

    public double CandidateArenaScore { get; set; }

    public double ChampionWinRate { get; set; }

    public double CandidateWinRate { get; set; }

    public int InvalidCandidateBattles { get; set; }

    public bool Promoted { get; set; }
}

public sealed class CombatPolicyEvolutionResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public CombatPolicyValueNetworkDefinition? Champion { get; set; }

    public List<CombatEpisode> Replay { get; set; } = new();

    public List<CombatPolicyEvolutionIteration> Iterations { get; set; } = new();
}

public sealed class CombatPolicyEvolutionRunner
{
    private readonly CombatSimulationEngine engine;

    public CombatPolicyEvolutionRunner(CombatSimulationEngine? engine = null)
    {
        this.engine = engine ?? new CombatSimulationEngine();
    }

    public CombatPolicyEvolutionResult Run(
        CombatPolicyEvolutionRequest request,
        CombatRuleset ruleset,
        CombatPolicyValueNetworkDefinition? initialChampion = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (request.Scenarios == null || request.Scenarios.Count == 0)
        {
            return new CombatPolicyEvolutionResult { Message = "没有可用于进化训练的场景" };
        }
        var iterations = Math.Max(1, Math.Min(100, request.Iterations));
        var trainingCount = Math.Max(2, Math.Min(100000, request.TrainingEpisodesPerIteration));
        var arenaCount = Math.Max(2, Math.Min(10000, request.ArenaEpisodesPerIteration));
        var result = new CombatPolicyEvolutionResult
        {
            Champion = initialChampion
        };
        ICombatPolicyValueModel champion = initialChampion == null
            ? NullCombatPolicyValueModel.Instance
            : new ManagedCombatPolicyValueModel(initialChampion);
        var nextSeed = request.SeedStart;
        var completedBattles = 0;
        var totalBattles = iterations * (trainingCount + arenaCount * 2);

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var episodeIndex = 0; episodeIndex < trainingCount; episodeIndex++)
            {
                var scenario = Scenario(
                    request.Scenarios,
                    episodeIndex + iteration * trainingCount,
                    nextSeed++);
                var policy = new CombatEpisodeRecordingPolicy(
                    new CombatDecisionSimulationPolicy(
                        request.Profile,
                        policyValueModel: champion),
                    request.DecisionProfile);
                var battle = engine.Run(scenario, ruleset, policy, cancellationToken);
                result.Replay.Add(policy.Complete(battle));
                completedBattles++;
                request.Progress?.Invoke(
                    completedBattles,
                    totalBattles,
                    "第 " + (iteration + 1) + " 轮：生成完整战斗轨迹");
            }

            request.Progress?.Invoke(
                completedBattles,
                totalBattles,
                "第 " + (iteration + 1) + " 轮：训练策略价值候选");
            var trained = CombatPolicyValueTrainer.Train(
                result.Replay,
                request.DecisionProfile,
                request.Training);
            if (!trained.Success || trained.Model == null)
            {
                result.Message = "第 "
                                 + (iteration + 1)
                                 + " 轮策略价值训练失败："
                                 + trained.Message;
                return result;
            }
            var candidate = new ManagedCombatPolicyValueModel(trained.Model);
            var championResults = new List<CombatSimulationResult>(arenaCount);
            var candidateResults = new List<CombatSimulationResult>(arenaCount);
            for (var arenaIndex = 0; arenaIndex < arenaCount; arenaIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var seed = nextSeed++;
                var source = request.Scenarios[(arenaIndex + iteration) % request.Scenarios.Count];
                var championScenario = CombatScenarioCloner.Clone(source);
                championScenario.Seed = seed;
                var candidateScenario = CombatScenarioCloner.Clone(source);
                candidateScenario.Seed = seed;
                championResults.Add(engine.Run(
                    championScenario,
                    ruleset,
                    new CombatDecisionSimulationPolicy(
                        request.Profile,
                        policyValueModel: champion),
                    cancellationToken));
                completedBattles++;
                candidateResults.Add(engine.Run(
                    candidateScenario,
                    ruleset,
                    new CombatDecisionSimulationPolicy(
                        request.Profile,
                        policyValueModel: candidate),
                    cancellationToken));
                completedBattles++;
                request.Progress?.Invoke(
                    completedBattles,
                    totalBattles,
                    "第 " + (iteration + 1) + " 轮：同种子竞技场");
            }

            var invalid = candidateResults.Count(item =>
                item.Outcome == CombatSimulationOutcome.Invalid);
            var championScore = championResults.Average(Score);
            var candidateScore = candidateResults.Average(Score);
            var championWinRate = championResults.Count(item =>
                                      item.Outcome == CombatSimulationOutcome.Victory)
                                  / (double)arenaCount;
            var candidateWinRate = candidateResults.Count(item =>
                                     item.Outcome == CombatSimulationOutcome.Victory)
                                 / (double)arenaCount;
            var promoted = invalid == 0
                           && candidateWinRate
                           + Math.Max(0d, request.MaximumWinRateRegression)
                           + 0.0000001d >= championWinRate
                           && candidateScore + 0.0000001d >= championScore;
            result.Iterations.Add(new CombatPolicyEvolutionIteration
            {
                Iteration = iteration + 1,
                ReplayEpisodes = result.Replay.Count,
                CandidateModelId = trained.Model.ModelId,
                ChampionArenaScore = championScore,
                CandidateArenaScore = candidateScore,
                ChampionWinRate = championWinRate,
                CandidateWinRate = candidateWinRate,
                InvalidCandidateBattles = invalid,
                Promoted = promoted
            });
            if (promoted)
            {
                result.Champion = trained.Model;
                champion = candidate;
            }
        }

        result.Success = result.Champion != null;
        result.Message = result.Success
            ? "策略进化完成："
              + result.Iterations.Count(item => item.Promoted)
              + "/"
              + result.Iterations.Count
              + " 个候选晋升"
            : "策略进化完成但没有候选通过竞技场门禁";
        return result;
    }

    private static CombatScenarioDefinition Scenario(
        IReadOnlyList<CombatScenarioDefinition> scenarios,
        int index,
        ulong seed)
    {
        var result = CombatScenarioCloner.Clone(scenarios[index % scenarios.Count]);
        result.Seed = seed;
        return result;
    }

    private static double Score(CombatSimulationResult result)
    {
        var outcome = result.Outcome == CombatSimulationOutcome.Victory
            ? 100d
            : result.Outcome == CombatSimulationOutcome.Defeat
                ? -100d
                : result.Outcome == CombatSimulationOutcome.Invalid
                    ? -200d
                    : -25d;
        return outcome
               + result.FinalPlayerHp * 0.1d
               - result.Turns * 0.05d
               - (result.Metrics?.DamageTaken ?? 0) * 0.02d;
    }
}
