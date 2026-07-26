using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AuraCombatSimulation.Shared;

public sealed class CombatBatchRequest
{
    public CombatScenarioDefinition Scenario { get; set; } = new();

    public ulong SeedStart { get; set; } = 1UL;

    public int SimulationCount { get; set; } = 100;

    public int MaximumDegreeOfParallelism { get; set; } = 1;

    public bool KeepInvalidResults { get; set; } = true;
}

public sealed class CombatBatchStatistics
{
    public int RequestedSimulations { get; set; }

    public int CompletedSimulations { get; set; }

    public int AuthoritativeSimulations { get; set; }

    public int Victories { get; set; }

    public int Defeats { get; set; }

    public int Draws { get; set; }

    public int Invalid { get; set; }

    public double WinRate { get; set; }

    public double WinRateLower95 { get; set; }

    public double WinRateUpper95 { get; set; }

    public double MeanTurns { get; set; }

    public double MedianTurns { get; set; }

    public double TurnsP10 { get; set; }

    public double TurnsP90 { get; set; }

    public double MeanFinalPlayerHp { get; set; }

    public Dictionary<CombatTerminationReason, int> TerminationCounts { get; set; } = new();
}

public sealed class CombatBatchResult
{
    public string ScenarioId { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string PolicyId { get; set; } = "";

    public CombatBatchStatistics Statistics { get; set; } = new();

    public List<CombatSimulationResult> Results { get; set; } = new();
}

public sealed class CombatBatchRunner
{
    private readonly CombatSimulationEngine engine;

    public CombatBatchRunner(CombatSimulationEngine? engine = null)
    {
        this.engine = engine ?? new CombatSimulationEngine();
    }

    public CombatBatchResult Run(
        CombatBatchRequest request,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory policyFactory,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (policyFactory == null) throw new ArgumentNullException(nameof(policyFactory));
        if (request.Scenario == null) throw new ArgumentException("Batch scenario is required.", nameof(request));

        var count = Math.Max(1, Math.Min(1000000, request.SimulationCount));
        var results = new CombatSimulationResult?[count];
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(
                1,
                Math.Min(Environment.ProcessorCount, request.MaximumDegreeOfParallelism))
        };
        try
        {
            Parallel.For(0, count, options, index =>
            {
                var scenario = CombatScenarioCloner.Clone(request.Scenario);
                scenario.Seed = request.SeedStart + (ulong)index;
                results[index] = engine.Run(
                    scenario,
                    ruleset,
                    policyFactory.Create(),
                    options.CancellationToken);
            });
        }
        catch (OperationCanceledException)
        {
            // Completed indices remain available and are merged in seed order.
        }

        var ordered = results.Where(result => result != null).Select(result => result!).ToList();
        var kept = request.KeepInvalidResults
            ? ordered
            : ordered.Where(result => result.Outcome != CombatSimulationOutcome.Invalid).ToList();
        return new CombatBatchResult
        {
            ScenarioId = request.Scenario.ScenarioId,
            RulesetHash = ruleset.RulesetHash,
            PolicyId = policyFactory.PolicyId,
            Results = kept,
            Statistics = BuildStatistics(count, ordered)
        };
    }

    private static CombatBatchStatistics BuildStatistics(
        int requested,
        IReadOnlyList<CombatSimulationResult> results)
    {
        var authoritative = results
            .Where(result => result.SemanticCoverage >= 0.999999d
                             && result.Outcome != CombatSimulationOutcome.Invalid)
            .ToList();
        var victories = authoritative.Count(result => result.Outcome == CombatSimulationOutcome.Victory);
        var sampleSize = authoritative.Count;
        var interval = Wilson(victories, sampleSize);
        var turns = authoritative.Select(result => (double)result.Turns).OrderBy(value => value).ToList();
        var hp = authoritative.Select(result => (double)result.FinalPlayerHp).ToList();
        return new CombatBatchStatistics
        {
            RequestedSimulations = requested,
            CompletedSimulations = results.Count,
            AuthoritativeSimulations = sampleSize,
            Victories = victories,
            Defeats = authoritative.Count(result => result.Outcome == CombatSimulationOutcome.Defeat),
            Draws = authoritative.Count(result => result.Outcome == CombatSimulationOutcome.Draw),
            Invalid = results.Count(result => result.Outcome == CombatSimulationOutcome.Invalid),
            WinRate = sampleSize == 0 ? 0d : (double)victories / sampleSize,
            WinRateLower95 = interval.Item1,
            WinRateUpper95 = interval.Item2,
            MeanTurns = turns.Count == 0 ? 0d : turns.Average(),
            MedianTurns = Quantile(turns, 0.5d),
            TurnsP10 = Quantile(turns, 0.1d),
            TurnsP90 = Quantile(turns, 0.9d),
            MeanFinalPlayerHp = hp.Count == 0 ? 0d : hp.Average(),
            TerminationCounts = results
                .GroupBy(result => result.TerminationReason)
                .ToDictionary(group => group.Key, group => group.Count())
        };
    }

    private static Tuple<double, double> Wilson(int successes, int total)
    {
        if (total <= 0)
        {
            return Tuple.Create(0d, 0d);
        }
        const double z = 1.959963984540054d;
        var n = (double)total;
        var p = successes / n;
        var denominator = 1d + z * z / n;
        var center = (p + z * z / (2d * n)) / denominator;
        var margin = z * Math.Sqrt((p * (1d - p) + z * z / (4d * n)) / n) / denominator;
        return Tuple.Create(Math.Max(0d, center - margin), Math.Min(1d, center + margin));
    }

    private static double Quantile(IReadOnlyList<double> sorted, double probability)
    {
        if (sorted.Count == 0)
        {
            return 0d;
        }
        var position = Math.Max(0d, Math.Min(1d, probability)) * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }
        var fraction = position - lower;
        return sorted[lower] * (1d - fraction) + sorted[upper] * fraction;
    }
}

public static class CombatScenarioCloner
{
    public static CombatScenarioDefinition Clone(CombatScenarioDefinition source)
    {
        return new CombatScenarioDefinition
        {
            ScenarioId = source.ScenarioId,
            RulesetVersion = source.RulesetVersion,
            Seed = source.Seed,
            Player = new CombatPlayerSetup
            {
                RoleId = source.Player.RoleId,
                MaxHp = source.Player.MaxHp,
                CurrentHp = source.Player.CurrentHp,
                BaseEnergy = source.Player.BaseEnergy,
                Deck = new List<string>(source.Player.Deck),
                InitialStatuses = source.Player.InitialStatuses
                    .Select(CloneStatus)
                    .ToList(),
                Variables = new Dictionary<string, double>(
                    source.Player.Variables,
                    StringComparer.OrdinalIgnoreCase)
            },
            Enemies = source.Enemies.Select(enemy => new CombatEnemySetup
            {
                EnemyId = enemy.EnemyId,
                InstanceKey = enemy.InstanceKey,
                HpScale = enemy.HpScale,
                AttackScale = enemy.AttackScale,
                InitialBlockBonus = enemy.InitialBlockBonus,
                InitialStatuses = enemy.InitialStatuses.Select(CloneStatus).ToList(),
                Variables = new Dictionary<string, double>(
                    enemy.Variables,
                    StringComparer.OrdinalIgnoreCase)
            }).ToList(),
            InitialDraw = source.InitialDraw,
            DrawPerTurn = source.DrawPerTurn,
            HandLimit = source.HandLimit,
            RetainBlockBetweenTurns = source.RetainBlockBetweenTurns,
            MovePlayedCardAfterResolution = source.MovePlayedCardAfterResolution,
            InitialDiscardCards = new List<string>(source.InitialDiscardCards),
            DirectHpLossAfterPlayerCard = source.DirectHpLossAfterPlayerCard,
            RewardRules = source.RewardRules
                .Select(item => item.Clone())
                .ToList(),
            RewardCatalog = source.RewardCatalog
                .Select(item => item.Clone())
                .ToList(),
            CampaignVariables = new Dictionary<string, string>(
                source.CampaignVariables,
                StringComparer.OrdinalIgnoreCase),
            RequireAuthoritativeRules = source.RequireAuthoritativeRules,
            TraceLevel = source.TraceLevel,
            Limits = (source.Limits ?? new CombatSimulationLimits()).Normalize()
        };
    }

    private static CombatInitialStatus CloneStatus(CombatInitialStatus source)
    {
        return source.Clone();
    }
}
