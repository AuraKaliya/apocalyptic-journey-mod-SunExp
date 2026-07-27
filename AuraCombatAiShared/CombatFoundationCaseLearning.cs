using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationCampaignObservation
{
    public int SchemaVersion { get; set; } = 1;

    public string CaseId { get; set; } = "";

    public string CompatibilityKey { get; set; } = "";

    public string SourceStage { get; set; } = "";

    public int Iteration { get; set; }

    public string Competitor { get; set; } = "";

    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string DecisionProfile { get; set; } = "";

    public string ModelId { get; set; } = "";

    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public string PlanHash { get; set; } = "";

    public bool FinalBossVictory { get; set; }

    public bool IntegrityValid { get; set; }

    public bool ArchiveEligible { get; set; }

    public int CompletedBattles { get; set; }

    public int TotalBattles { get; set; }

    public int FinalHp { get; set; }

    public int FinalMaxHp { get; set; }

    public int FinalDeckSize { get; set; }

    public int TotalTurns { get; set; }

    public int CardsPlayed { get; set; }

    public int DamageDealt { get; set; }

    public int DamageTaken { get; set; }

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }

    public double BattleSemanticCoverage { get; set; }

    public double ProgressionSemanticCoverage { get; set; }

    public double RobustnessScore { get; set; }

    public string StrategyFingerprint { get; set; } = "";

    public string PrimaryArchetype { get; set; } = "";

    public string SecondaryArchetype { get; set; } = "";

    public List<string> FinalDeck { get; set; } = new();

    public List<string> Relics { get; set; } = new();

    public List<string> Blessings { get; set; } = new();

    public List<string> SelectedCards { get; set; } = new();
}

public sealed class CombatFoundationSuccessCase
{
    public int SchemaVersion { get; set; } = 1;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public CombatFoundationCampaignObservation Observation { get; set; } = new();

    public CombatCampaignResult Campaign { get; set; } = new();

    public List<CombatEpisode> Episodes { get; set; } = new();
}

public sealed class CombatFoundationMatchedCase
{
    public string SuccessCaseId { get; set; } = "";

    public string FailureCaseId { get; set; } = "";

    public string DifficultyId { get; set; } = "";

    public ulong SuccessSeed { get; set; }

    public ulong FailureSeed { get; set; }

    public double Distance { get; set; }

    public int SuccessDeckSize { get; set; }

    public int FailureDeckSize { get; set; }

    public int SuccessCompletedBattles { get; set; }

    public int FailureCompletedBattles { get; set; }

    public string SuccessArchetype { get; set; } = "";

    public string FailureArchetype { get; set; } = "";
}

public sealed class CombatFoundationCaseSignal
{
    public string Kind { get; set; } = "";

    public string Id { get; set; } = "";

    public int SuccessCases { get; set; }

    public int FailureCases { get; set; }

    public double SuccessPresenceRate { get; set; }

    public double FailurePresenceRate { get; set; }

    public double Uplift { get; set; }

    public string Note { get; set; } = "";
}

public sealed class CombatFoundationCaseAnalysis
{
    public int SchemaVersion { get; set; } = 1;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int ObservedCases { get; set; }

    public int IntegrityValidCases { get; set; }

    public int SuccessfulCases { get; set; }

    public int FailedCases { get; set; }

    public int ArchiveEligibleCases { get; set; }

    public int DistinctSuccessStrategies { get; set; }

    public int MatchedPairs { get; set; }

    public double SuccessfulAverageDeckSize { get; set; }

    public double FailedAverageDeckSize { get; set; }

    public double SuccessfulAverageHpRatio { get; set; }

    public double SuccessfulAverageRobustness { get; set; }

    public List<CombatFoundationMatchedCase> Pairs { get; set; } = new();

    public List<CombatFoundationCaseSignal> Signals { get; set; } = new();

    public List<string> Recommendations { get; set; } = new();

    public string StatisticalCaveat { get; set; } =
        "Signals are associations from simulated cases, not causal proof.";
}

public static class CombatFoundationCaseLearning
{
    public const int ArchiveSchemaVersion = 1;

    public static CombatFoundationCampaignObservation Observe(
        CombatCampaignResult campaign,
        string sourceStage,
        int iteration,
        string competitor,
        string rulesetHash,
        string decisionProfile,
        string modelId,
        IReadOnlyList<CombatEpisode>? episodes = null)
    {
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        var episodeList = episodes ?? Array.Empty<CombatEpisode>();
        var battleCoverage = campaign.Battles.Count == 0
            ? 0d
            : campaign.Battles.Average(item => item.SemanticCoverage);
        var integrityValid = !campaign.Invalid
                             && campaign.Battles.Count > 0
                             && campaign.Battles.All(item =>
                                 item.TerminalConsistencyValid
                                 && item.Outcome
                                 != CombatSimulationOutcome.Invalid)
                             && campaign.UnsupportedDefinitions.Count == 0
                             && campaign.Battles.All(item =>
                                 item.UnsupportedDefinitions.Count == 0);
        var authoritativeEpisodes = episodeList.Count == 0
                                    || episodeList.All(item =>
                                        item.Authoritative
                                        && item.SemanticCoverage >= 0.999999d
                                        && item.Campaign?.IntegrityValid == true);
        var fullCoverage = campaign.BattleSemanticCoverage >= 0.999999d
                           && campaign.ProgressionSemanticCoverage >= 0.999999d
                           && battleCoverage >= 0.999999d;
        var selectedCards = campaign.Rewards
            .SelectMany(item => item.Cards)
            .Where(item => !item.Skipped
                           && !string.IsNullOrWhiteSpace(item.SelectedId))
            .Select(item => item.SelectedId.Trim())
            .ToList();
        var finalDeck = new List<string>(campaign.FinalState.Deck);
        var rulesHash = string.IsNullOrWhiteSpace(rulesetHash)
            ? campaign.Battles.FirstOrDefault()?.RulesetHash ?? ""
            : rulesetHash.Trim();
        var compatibilityKey = CompatibilityKey(
            campaign.CampaignId,
            campaign.CampaignVersion,
            rulesHash);
        var strategyFingerprint = Hash(
            "strategy-v1|"
            + campaign.DifficultyId
            + "|"
            + campaign.FinalState.BuildPlan.PrimaryArchetype
            + "|"
            + campaign.FinalState.BuildPlan.SecondaryArchetype
            + "|deck="
            + StableMultiset(finalDeck)
            + "|relic="
            + StableMultiset(campaign.FinalState.Relics)
            + "|blessing="
            + StableMultiset(campaign.FinalState.Blessings));
        var caseId = Hash(
            "case-v1|"
            + sourceStage
            + "|"
            + iteration
            + "|"
            + competitor
            + "|"
            + campaign.CampaignId
            + "|"
            + campaign.CampaignVersion
            + "|"
            + campaign.DifficultyId
            + "|"
            + campaign.WorldSeed
            + "|"
            + campaign.PlanHash
            + "|"
            + campaign.PolicyId
            + "|"
            + modelId
            + "|"
            + strategyFingerprint);
        var observation = new CombatFoundationCampaignObservation
        {
            CaseId = caseId,
            CompatibilityKey = compatibilityKey,
            SourceStage = sourceStage ?? "",
            Iteration = Math.Max(0, iteration),
            Competitor = competitor ?? "",
            CampaignId = campaign.CampaignId,
            CampaignVersion = campaign.CampaignVersion,
            RulesetHash = rulesHash,
            DecisionProfile = decisionProfile ?? "",
            ModelId = modelId ?? "",
            DifficultyId = campaign.DifficultyId,
            WorldSeed = campaign.WorldSeed,
            PlanHash = campaign.PlanHash,
            FinalBossVictory = campaign.FinalBossVictory,
            IntegrityValid = integrityValid,
            ArchiveEligible = campaign.FinalBossVictory
                              && integrityValid
                              && fullCoverage
                              && authoritativeEpisodes,
            CompletedBattles = campaign.CompletedBattles,
            TotalBattles = campaign.TotalBattles,
            FinalHp = campaign.FinalState.CurrentHp,
            FinalMaxHp = campaign.FinalState.MaxHp,
            FinalDeckSize = finalDeck.Count,
            TotalTurns = campaign.Battles.Sum(item => item.Turns),
            CardsPlayed = campaign.Battles.Sum(item => item.Metrics.CardsPlayed),
            DamageDealt = campaign.Battles.Sum(item => item.Metrics.DamageDealt),
            DamageTaken = campaign.Battles.Sum(item => item.Metrics.DamageTaken),
            CertifiedLoops = campaign.Battles.Sum(item =>
                item.Metrics.CertifiedLoops),
            SustainableControlLoops = campaign.Battles.Sum(item =>
                item.Metrics.SustainableControlLoops),
            FakeLoops = campaign.Battles.Sum(item => item.Metrics.FakeLoops),
            BlockedLoops = campaign.Battles.Sum(item => item.Metrics.BlockedLoops),
            BattleSemanticCoverage = campaign.BattleSemanticCoverage <= 0d
                ? battleCoverage
                : campaign.BattleSemanticCoverage,
            ProgressionSemanticCoverage = campaign.ProgressionSemanticCoverage,
            StrategyFingerprint = strategyFingerprint,
            PrimaryArchetype =
                campaign.FinalState.BuildPlan.PrimaryArchetype ?? "",
            SecondaryArchetype =
                campaign.FinalState.BuildPlan.SecondaryArchetype ?? "",
            FinalDeck = finalDeck,
            Relics = new List<string>(campaign.FinalState.Relics),
            Blessings = new List<string>(campaign.FinalState.Blessings),
            SelectedCards = selectedCards
        };
        observation.RobustnessScore = Robustness(observation);
        return observation;
    }

    public static CombatFoundationSuccessCase CreateSuccessCase(
        CombatCampaignResult campaign,
        CombatFoundationCampaignObservation observation,
        IReadOnlyList<CombatEpisode>? episodes = null)
    {
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        if (observation == null)
        {
            throw new ArgumentNullException(nameof(observation));
        }
        return new CombatFoundationSuccessCase
        {
            Observation = observation,
            Campaign = campaign,
            Episodes = episodes == null
                ? new List<CombatEpisode>()
                : new List<CombatEpisode>(episodes)
        };
    }

    public static CombatFoundationCaseAnalysis Analyze(
        IEnumerable<CombatFoundationCampaignObservation> source)
    {
        var observations = (source
                            ?? Array.Empty<CombatFoundationCampaignObservation>())
            .Where(item => item != null)
            .GroupBy(item => item.CaseId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.CaseId, StringComparer.Ordinal)
            .ToList();
        var valid = observations.Where(item => item.IntegrityValid).ToList();
        var successes = valid.Where(item => item.FinalBossVictory).ToList();
        var failures = valid.Where(item => !item.FinalBossVictory).ToList();
        var pairs = Match(successes, failures);
        var analysis = new CombatFoundationCaseAnalysis
        {
            ObservedCases = observations.Count,
            IntegrityValidCases = valid.Count,
            SuccessfulCases = successes.Count,
            FailedCases = failures.Count,
            ArchiveEligibleCases =
                successes.Count(item => item.ArchiveEligible),
            DistinctSuccessStrategies = successes
                .Select(item => item.StrategyFingerprint)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            MatchedPairs = pairs.Count,
            SuccessfulAverageDeckSize = Average(
                successes,
                item => item.FinalDeckSize),
            FailedAverageDeckSize = Average(
                failures,
                item => item.FinalDeckSize),
            SuccessfulAverageHpRatio = Average(
                successes,
                HpRatio),
            SuccessfulAverageRobustness = Average(
                successes,
                item => item.RobustnessScore),
            Pairs = pairs,
            Signals = BuildSignals(successes, failures)
        };
        analysis.Recommendations = BuildRecommendations(
            analysis,
            successes,
            failures);
        return analysis;
    }

    public static string CompatibilityKey(
        string campaignId,
        string campaignVersion,
        string rulesetHash)
    {
        return Hash(
            "compat-v1|"
            + (campaignId ?? "").Trim()
            + "|"
            + (campaignVersion ?? "").Trim()
            + "|"
            + (rulesetHash ?? "").Trim()
            + "|"
            + CombatPolicyValueProtocol.EpisodeProtocol
            + "|"
            + CombatPolicyValueProtocol.FeatureSchemaVersion);
    }

    public static List<CombatEpisode> SelectExpertEpisodes(
        IEnumerable<CombatFoundationSuccessCase> cases,
        string campaignId,
        string campaignVersion,
        string rulesetHash,
        int episodeLimit)
    {
        var limit = Math.Max(0, episodeLimit);
        if (limit == 0)
        {
            return new List<CombatEpisode>();
        }
        var selected = new List<CombatEpisode>(limit);
        var seenEpisodes = new HashSet<string>(StringComparer.Ordinal);
        var rankedCases = (cases ?? Array.Empty<CombatFoundationSuccessCase>())
            .Where(item =>
                item?.Observation != null
                && item.Observation.ArchiveEligible
                && string.Equals(
                    item.Observation.CampaignId,
                    campaignId,
                    StringComparison.Ordinal)
                && string.Equals(
                    item.Observation.CampaignVersion,
                    campaignVersion,
                    StringComparison.Ordinal)
                && string.Equals(
                    item.Observation.RulesetHash,
                    rulesetHash,
                    StringComparison.Ordinal))
            .GroupBy(
                item => item.Observation.StrategyFingerprint,
                StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderByDescending(item => item.Observation.RobustnessScore)
                .ThenBy(item => item.Observation.CaseId, StringComparer.Ordinal))
            .ToList();
        foreach (var successCase in rankedCases)
        {
            foreach (var episode in successCase.Episodes
                         .OrderByDescending(item => item.JourneyBattleIndex)
                         .ThenBy(item => item.EpisodeId, StringComparer.Ordinal))
            {
                if (selected.Count >= limit)
                {
                    return selected;
                }
                if (episode.Authoritative
                    && episode.Campaign?.IntegrityValid == true
                    && episode.Campaign.FinalBossVictory
                    && string.Equals(
                        episode.RulesetHash,
                        rulesetHash,
                        StringComparison.Ordinal)
                    && seenEpisodes.Add(EpisodeKey(episode)))
                {
                    selected.Add(episode);
                }
            }
        }
        return selected;
    }

    private static List<CombatFoundationMatchedCase> Match(
        IReadOnlyList<CombatFoundationCampaignObservation> successes,
        IReadOnlyList<CombatFoundationCampaignObservation> failures)
    {
        var pairs = new List<CombatFoundationMatchedCase>();
        foreach (var success in successes
                     .OrderByDescending(item => item.RobustnessScore)
                     .ThenBy(item => item.CaseId, StringComparer.Ordinal))
        {
            var nearest = failures
                .Where(item => string.Equals(
                    item.DifficultyId,
                    success.DifficultyId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => new
                {
                    Failure = item,
                    Distance = PairDistance(success, item)
                })
                .OrderBy(item => item.Distance)
                .ThenBy(item => item.Failure.CaseId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (nearest == null)
            {
                continue;
            }
            pairs.Add(new CombatFoundationMatchedCase
            {
                SuccessCaseId = success.CaseId,
                FailureCaseId = nearest.Failure.CaseId,
                DifficultyId = success.DifficultyId,
                SuccessSeed = success.WorldSeed,
                FailureSeed = nearest.Failure.WorldSeed,
                Distance = nearest.Distance,
                SuccessDeckSize = success.FinalDeckSize,
                FailureDeckSize = nearest.Failure.FinalDeckSize,
                SuccessCompletedBattles = success.CompletedBattles,
                FailureCompletedBattles = nearest.Failure.CompletedBattles,
                SuccessArchetype = success.PrimaryArchetype,
                FailureArchetype = nearest.Failure.PrimaryArchetype
            });
            if (pairs.Count >= 100)
            {
                break;
            }
        }
        return pairs;
    }

    private static List<CombatFoundationCaseSignal> BuildSignals(
        IReadOnlyList<CombatFoundationCampaignObservation> successes,
        IReadOnlyList<CombatFoundationCampaignObservation> failures)
    {
        var signals = PresenceSignals(
                "final-deck-card",
                successes,
                failures,
                item => item.FinalDeck)
            .Concat(PresenceSignals(
                "selected-card",
                successes,
                failures,
                item => item.SelectedCards))
            .Concat(PresenceSignals(
                "primary-archetype",
                successes,
                failures,
                item => string.IsNullOrWhiteSpace(item.PrimaryArchetype)
                    ? Array.Empty<string>()
                    : new[] { item.PrimaryArchetype }))
            .Where(item =>
                item.SuccessCases + item.FailureCases >= 3)
            .OrderByDescending(item => Math.Abs(item.Uplift))
            .ThenByDescending(item => item.SuccessCases + item.FailureCases)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(40)
            .ToList();
        return signals;
    }

    private static IEnumerable<CombatFoundationCaseSignal> PresenceSignals(
        string kind,
        IReadOnlyList<CombatFoundationCampaignObservation> successes,
        IReadOnlyList<CombatFoundationCampaignObservation> failures,
        Func<CombatFoundationCampaignObservation, IEnumerable<string>> selector)
    {
        var successSets = successes.Select(item => selector(item)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToList();
        var failureSets = failures.Select(item => selector(item)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToList();
        var ids = successSets.SelectMany(item => item)
            .Concat(failureSets.SelectMany(item => item))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var successCount = successSets.Count(items => items.Contains(id));
            var failureCount = failureSets.Count(items => items.Contains(id));
            var successRate = successes.Count == 0
                ? 0d
                : successCount / (double)successes.Count;
            var failureRate = failures.Count == 0
                ? 0d
                : failureCount / (double)failures.Count;
            yield return new CombatFoundationCaseSignal
            {
                Kind = kind,
                Id = id,
                SuccessCases = successCount,
                FailureCases = failureCount,
                SuccessPresenceRate = successRate,
                FailurePresenceRate = failureRate,
                Uplift = successRate - failureRate,
                Note = "association-only; verify across independent seeds"
            };
        }
    }

    private static List<string> BuildRecommendations(
        CombatFoundationCaseAnalysis analysis,
        IReadOnlyList<CombatFoundationCampaignObservation> successes,
        IReadOnlyList<CombatFoundationCampaignObservation> failures)
    {
        var recommendations = new List<string>();
        if (successes.Count == 0)
        {
            recommendations.Add(
                "No valid success case is available; keep exploration and failure curriculum active.");
            return recommendations;
        }
        if (failures.Count > 0)
        {
            var deckDelta = analysis.SuccessfulAverageDeckSize
                            - analysis.FailedAverageDeckSize;
            if (Math.Abs(deckDelta) >= 2d)
            {
                recommendations.Add(
                    deckDelta < 0d
                        ? "Successful cases use smaller final decks; test earlier removal and stricter reward skipping."
                        : "Successful cases use larger final decks; verify whether this is a real system requirement or reward-pool survivor bias.");
            }
        }
        var positive = analysis.Signals.FirstOrDefault(item =>
            item.Uplift >= 0.15d && item.SuccessCases >= 3);
        if (positive != null)
        {
            recommendations.Add(
                "Validate the positive signal "
                + positive.Kind
                + ":"
                + positive.Id
                + " on held-out seeds before changing reward weights.");
        }
        var negative = analysis.Signals.FirstOrDefault(item =>
            item.Uplift <= -0.15d && item.FailureCases >= 3);
        if (negative != null)
        {
            recommendations.Add(
                "Review the negative signal "
                + negative.Kind
                + ":"
                + negative.Id
                + " for removal timing, redundancy, or search misuse.");
        }
        if (successes.Sum(item => item.FakeLoops) > 0)
        {
            recommendations.Add(
                "Some successful cases still entered fake loops; keep damage-cap and net-health loop escape logic in validation.");
        }
        if (analysis.DistinctSuccessStrategies <= 1 && successes.Count >= 3)
        {
            recommendations.Add(
                "Successes collapse into one strategy cluster; retain hard failures and exploration to avoid policy narrowing.");
        }
        recommendations.Add(
            "Use archived demonstrations only as a bounded replay quota; do not replace failure and hard-seed samples.");
        return recommendations;
    }

    private static double Robustness(
        CombatFoundationCampaignObservation observation)
    {
        if (!observation.FinalBossVictory || !observation.IntegrityValid)
        {
            return 0d;
        }
        var hp = HpRatio(observation);
        var coverage = Math.Max(
            0d,
            Math.Min(
                1d,
                Math.Min(
                    observation.BattleSemanticCoverage,
                    observation.ProgressionSemanticCoverage)));
        var completion = observation.TotalBattles <= 0
            ? 0d
            : Math.Max(
                0d,
                Math.Min(
                    1d,
                    observation.CompletedBattles
                    / (double)observation.TotalBattles));
        var averageTurns = observation.CompletedBattles <= 0
            ? observation.TotalTurns
            : observation.TotalTurns / (double)observation.CompletedBattles;
        var efficiency = 1d / (1d + Math.Max(0d, averageTurns) / 20d);
        var loopPenalty = Math.Min(
            0.20d,
            observation.FakeLoops * 0.02d
            + observation.BlockedLoops * 0.01d);
        return Math.Max(
            0d,
            Math.Min(
                1d,
                coverage * 0.35d
                + hp * 0.25d
                + completion * 0.25d
                + efficiency * 0.15d
                - loopPenalty));
    }

    private static double PairDistance(
        CombatFoundationCampaignObservation success,
        CombatFoundationCampaignObservation failure)
    {
        var distance =
            Math.Abs(success.CompletedBattles - failure.CompletedBattles) * 2d
            + Math.Abs(success.FinalDeckSize - failure.FinalDeckSize) * 0.20d;
        if (!string.Equals(
                success.PrimaryArchetype,
                failure.PrimaryArchetype,
                StringComparison.OrdinalIgnoreCase))
        {
            distance += 2d;
        }
        if (!string.Equals(
                success.SourceStage,
                failure.SourceStage,
                StringComparison.OrdinalIgnoreCase))
        {
            distance += 1d;
        }
        if (success.WorldSeed == failure.WorldSeed)
        {
            distance -= 100d;
        }
        return distance;
    }

    private static double HpRatio(
        CombatFoundationCampaignObservation item)
    {
        return item.FinalMaxHp <= 0
            ? 0d
            : Math.Max(
                0d,
                Math.Min(1d, item.FinalHp / (double)item.FinalMaxHp));
    }

    private static double Average(
        IReadOnlyCollection<CombatFoundationCampaignObservation> items,
        Func<CombatFoundationCampaignObservation, double> selector)
    {
        return items.Count == 0 ? 0d : items.Average(selector);
    }

    private static string StableMultiset(IEnumerable<string> values)
    {
        return string.Join(
            ",",
            (values ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .GroupBy(item => item.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key + "*" + group.Count()));
    }

    private static string EpisodeKey(CombatEpisode episode)
    {
        return !string.IsNullOrWhiteSpace(episode.EpisodeId)
            ? episode.EpisodeId
            : episode.RulesetHash
              + "|"
              + episode.JourneyRunId
              + "|"
              + episode.JourneyBattleIndex
              + "|"
              + episode.Seed.ToString(CultureInfo.InvariantCulture);
    }

    private static string Hash(string value)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var item in bytes)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}
