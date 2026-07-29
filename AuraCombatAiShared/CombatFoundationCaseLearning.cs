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
    public int SchemaVersion { get; set; } =
        CombatFoundationCaseLearning.ArchiveSchemaVersion;

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
    public int SchemaVersion { get; set; } =
        CombatFoundationCaseLearning.ArchiveSchemaVersion;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public CombatFoundationCampaignObservation Observation { get; set; } = new();

    public CombatCampaignResult Campaign { get; set; } = new();

    public List<CombatEpisode> Episodes { get; set; } = new();
}

public sealed class CombatFoundationExpertReplaySelection
{
    public List<CombatEpisode> Episodes { get; set; } = new();

    public int CompatibleCases { get; set; }

    public int SelectedCases { get; set; }

    public int SelectedNormalEpisodes { get; set; }

    public int SelectedAdvancedEpisodes { get; set; }

    public int DistinctRuns { get; set; }

    public double TargetAdvancedShare { get; set; }

    public Dictionary<string, int> QuotaShortfalls { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class CombatFoundationRewardResidualTrainingResult
{
    public Dictionary<string, double> Residuals { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int EligibleObservations { get; set; }

    public int SuccessfulObservations { get; set; }

    public int FailedObservations { get; set; }

    public int MinimumCompletedBattles { get; set; }

    public double MaximumAbsoluteResidual { get; set; }

    public int CardResiduals { get; set; }

    public int RelicResiduals { get; set; }

    public int BlessingResiduals { get; set; }
}

public sealed class CombatFoundationCaseArchiveLoadDiagnostics
{
    public string ProtocolVersion { get; set; } =
        CombatFoundationCaseArchiveProtocol.Version;

    public string OwnerRuntime { get; set; } = "";

    public int StorageVersion { get; set; } =
        CombatFoundationCaseArchiveProtocol.StorageVersion;

    public bool ArchiveExists { get; set; }

    public bool CompatibilityDirectoryExists { get; set; }

    public bool ExpertCasesDirectoryExists { get; set; }

    public bool ObservationsDirectoryExists { get; set; }

    public int ExpertCaseFiles { get; set; }

    public int LoadedCases { get; set; }

    public int DistinctLoadedCases { get; set; }

    public int RejectedCaseFiles { get; set; }

    public int ObservationFiles { get; set; }

    public int LoadedObservations { get; set; }

    public int DistinctLoadedObservations { get; set; }

    public int RejectedObservationFiles { get; set; }

    public int PathAccessFailures { get; set; }

    public int MaximumObservedPathLength { get; set; }

    public Dictionary<string, int> RejectionReasons { get; set; } =
        new(StringComparer.Ordinal);

    public string CompatibilityKey { get; set; } = "";

    public string Message { get; set; } = "";
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
    public const int ArchiveSchemaVersion = 2;

    public static CombatFoundationCampaignObservation Observe(
        CombatCampaignResult campaign,
        string sourceStage,
        int iteration,
        string competitor,
        string rulesetHash,
        string campaignFingerprint,
        string nativeProgramPackageHash,
        string trainingPolicyVersion,
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
            campaignFingerprint,
            rulesHash,
            nativeProgramPackageHash,
            trainingPolicyVersion);
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
            + compatibilityKey
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
        string campaignFingerprint,
        string rulesetHash,
        string nativeProgramPackageHash,
        string trainingPolicyVersion)
    {
        return Hash(
            "compat-v2|"
            + (campaignId ?? "").Trim()
            + "|"
            + (campaignVersion ?? "").Trim()
            + "|"
            + (campaignFingerprint ?? "").Trim()
            + "|"
            + (rulesetHash ?? "").Trim()
            + "|"
            + (nativeProgramPackageHash ?? "").Trim()
            + "|"
            + (trainingPolicyVersion ?? "").Trim()
            + "|"
            + CombatPolicyValueProtocol.EpisodeProtocol
            + "|"
            + CombatPolicyValueProtocol.FeatureSchemaVersion
            + "|"
            + CombatPolicyValueProtocol.TrainingSemanticsVersion
            + "|"
            + CombatFoundationTrainingProtocol.SearchPolicyVersion
            + "|"
            + CombatFoundationTrainingProtocol.CurriculumVersion
            + "|partitioned-v3");
    }

    public static List<CombatEpisode> SelectExpertEpisodes(
        IEnumerable<CombatFoundationSuccessCase> cases,
        string campaignId,
        string campaignVersion,
        string campaignFingerprint,
        string rulesetHash,
        string nativeProgramPackageHash,
        string trainingPolicyVersion,
        int episodeLimit)
    {
        return SelectExpertReplay(
            cases,
            campaignId,
            campaignVersion,
            campaignFingerprint,
            rulesetHash,
            nativeProgramPackageHash,
            trainingPolicyVersion,
            episodeLimit).Episodes;
    }

    public static CombatFoundationExpertReplaySelection SelectExpertReplay(
        IEnumerable<CombatFoundationSuccessCase> cases,
        string campaignId,
        string campaignVersion,
        string campaignFingerprint,
        string rulesetHash,
        string nativeProgramPackageHash,
        string trainingPolicyVersion,
        int episodeLimit,
        double targetAdvancedShare = 0.35d,
        int maximumEpisodesPerRun = 8)
    {
        var limit = Math.Max(0, episodeLimit);
        var result = new CombatFoundationExpertReplaySelection
        {
            TargetAdvancedShare = Math.Max(
                0d,
                Math.Min(0.5d, targetAdvancedShare))
        };
        if (limit == 0)
        {
            return result;
        }
        var expectedCompatibilityKey = CompatibilityKey(
            campaignId,
            campaignVersion,
            campaignFingerprint,
            rulesetHash,
            nativeProgramPackageHash,
            trainingPolicyVersion);
        var compatible = (cases ?? Array.Empty<CombatFoundationSuccessCase>())
            .Where(item =>
                item?.Observation != null
                && item.Observation.ArchiveEligible
                && !(item.Observation.SourceStage ?? "").StartsWith(
                    "validation",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Observation.CompatibilityKey,
                    expectedCompatibilityKey,
                    StringComparison.Ordinal))
            .GroupBy(item => item.Observation.CaseId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        result.CompatibleCases = compatible.Count;
        if (compatible.Count == 0)
        {
            return result;
        }

        var advancedTarget = (int)Math.Round(
            limit * result.TargetAdvancedShare,
            MidpointRounding.AwayFromZero);
        var normalTarget = limit - advancedTarget;
        var selectedCaseIds = new HashSet<string>(StringComparer.Ordinal);
        AddExpertDifficulty(
            result.Episodes,
            compatible.Where(item => !IsAdvanced(item)).ToList(),
            normalTarget,
            Math.Max(1, maximumEpisodesPerRun),
            selectedCaseIds);
        AddExpertDifficulty(
            result.Episodes,
            compatible.Where(IsAdvanced).ToList(),
            advancedTarget,
            Math.Max(1, maximumEpisodesPerRun),
            selectedCaseIds);
        RecordShortfall(
            result.QuotaShortfalls,
            "normal",
            normalTarget - result.Episodes.Count(episode =>
                !EpisodeIsAdvanced(episode)));
        RecordShortfall(
            result.QuotaShortfalls,
            "advanced",
            advancedTarget - result.Episodes.Count(EpisodeIsAdvanced));

        if (result.Episodes.Count < limit)
        {
            var seen = result.Episodes
                .Select(EpisodeKey)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var successCase in DiverseCases(compatible))
            {
                foreach (var episode in RepresentativeEpisodes(
                             successCase,
                             Math.Max(1, maximumEpisodesPerRun)))
                {
                    if (result.Episodes.Count >= limit)
                    {
                        break;
                    }
                    if (EligibleExpertEpisode(episode, rulesetHash)
                        && seen.Add(EpisodeKey(episode)))
                    {
                        result.Episodes.Add(episode);
                        selectedCaseIds.Add(
                            successCase.Observation.CaseId);
                    }
                }
                if (result.Episodes.Count >= limit)
                {
                    break;
                }
            }
        }
        result.Episodes = result.Episodes
            .Take(limit)
            .OrderBy(EpisodeKey, StringComparer.Ordinal)
            .ToList();
        result.SelectedCases = selectedCaseIds.Count;
        result.SelectedNormalEpisodes = result.Episodes.Count(episode =>
            !EpisodeIsAdvanced(episode));
        result.SelectedAdvancedEpisodes =
            result.Episodes.Count(EpisodeIsAdvanced);
        result.DistinctRuns = result.Episodes
            .Select(episode => episode.JourneyRunId ?? "")
            .Distinct(StringComparer.Ordinal)
            .Count();
        return result;
    }

    public static CombatFoundationRewardResidualTrainingResult
        TrainRewardResiduals(
            IEnumerable<CombatFoundationCampaignObservation> source,
            int minimumCompletedBattles = 31,
            int minimumSupport = 20,
            double maximumAbsoluteResidual = 0.20d)
    {
        var maximum = Math.Max(0d, Math.Min(0.50d, maximumAbsoluteResidual));
        var eligible = (source
                        ?? Array.Empty<CombatFoundationCampaignObservation>())
            .Where(item =>
                item != null
                && item.IntegrityValid
                && !(item.SourceStage ?? "").StartsWith(
                    "validation",
                    StringComparison.OrdinalIgnoreCase)
                && item.CompletedBattles >= Math.Max(
                    1,
                    minimumCompletedBattles))
            .GroupBy(item => item.CaseId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var successes = eligible.Where(item => item.FinalBossVictory).ToList();
        var failures = eligible.Where(item => !item.FinalBossVictory).ToList();
        var result = new CombatFoundationRewardResidualTrainingResult
        {
            EligibleObservations = eligible.Count,
            SuccessfulObservations = successes.Count,
            FailedObservations = failures.Count,
            MinimumCompletedBattles = Math.Max(1, minimumCompletedBattles),
            MaximumAbsoluteResidual = maximum
        };
        if (successes.Count == 0 || failures.Count == 0 || maximum <= 0d)
        {
            return result;
        }
        var ids = successes.SelectMany(SelectedRewardIds)
            .Concat(failures.SelectMany(SelectedRewardIds))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var successCount = successes.Count(item =>
                SelectedRewardIds(item).Contains(
                    id,
                    StringComparer.OrdinalIgnoreCase));
            var failureCount = failures.Count(item =>
                SelectedRewardIds(item).Contains(
                    id,
                    StringComparer.OrdinalIgnoreCase));
            var support = successCount + failureCount;
            if (support < Math.Max(1, minimumSupport))
            {
                continue;
            }
            var successRate = (successCount + 1d) / (successes.Count + 2d);
            var failureRate = (failureCount + 1d) / (failures.Count + 2d);
            var uplift = successRate - failureRate;
            var shrinkage = support / (support + 100d);
            var residual = Math.Max(
                -maximum,
                Math.Min(maximum, uplift * shrinkage));
            if (Math.Abs(residual) >= 0.005d)
            {
                result.Residuals[id] = residual;
            }
        }
        result.CardResiduals = result.Residuals.Keys.Count(IsCard);
        result.RelicResiduals = result.Residuals.Keys.Count(IsRelic);
        result.BlessingResiduals =
            result.Residuals.Count
            - result.CardResiduals
            - result.RelicResiduals;
        return result;
    }

    private static IEnumerable<string> SelectedRewardIds(
        CombatFoundationCampaignObservation observation)
    {
        return (observation.SelectedCards ?? new List<string>())
            .Concat(observation.Relics ?? new List<string>())
            .Concat(observation.Blessings ?? new List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsRelic(string id)
    {
        return (id ?? "").StartsWith(
                   "relic_",
                   StringComparison.OrdinalIgnoreCase)
               || (id ?? "").StartsWith(
                   "CrowdFundingRelic_",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCard(string id)
    {
        return !IsRelic(id)
               && !(id ?? "").StartsWith(
                   "blessing_",
                   StringComparison.OrdinalIgnoreCase)
               && !(id ?? "").StartsWith(
                   "CrowdfundingBlessing_",
                   StringComparison.OrdinalIgnoreCase);
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

    private static void AddExpertDifficulty(
        ICollection<CombatEpisode> target,
        IReadOnlyList<CombatFoundationSuccessCase> cases,
        int count,
        int maximumEpisodesPerRun,
        ISet<string> selectedCaseIds)
    {
        if (count <= 0 || cases.Count == 0)
        {
            return;
        }
        var seen = target
            .Select(EpisodeKey)
            .ToHashSet(StringComparer.Ordinal);
        var rulesetHash = cases[0].Observation.RulesetHash;
        foreach (var successCase in DiverseCases(cases))
        {
            if (target.Count(episode =>
                    EpisodeIsAdvanced(episode)
                    == IsAdvanced(successCase)) >= count)
            {
                break;
            }
            var added = false;
            foreach (var episode in RepresentativeEpisodes(
                         successCase,
                         maximumEpisodesPerRun))
            {
                if (!EligibleExpertEpisode(episode, rulesetHash)
                    || !seen.Add(EpisodeKey(episode)))
                {
                    continue;
                }
                target.Add(episode);
                added = true;
                if (target.Count(item =>
                        EpisodeIsAdvanced(item)
                        == IsAdvanced(successCase)) >= count)
                {
                    break;
                }
            }
            if (added)
            {
                selectedCaseIds.Add(successCase.Observation.CaseId);
            }
        }
    }

    private static List<CombatFoundationSuccessCase> DiverseCases(
        IEnumerable<CombatFoundationSuccessCase> source)
    {
        var groups = (source ?? Array.Empty<CombatFoundationSuccessCase>())
            .GroupBy(
                item => item.Observation.StrategyFingerprint ?? "",
                StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new Queue<CombatFoundationSuccessCase>(
                group.OrderByDescending(item =>
                        item.Observation.RobustnessScore)
                    .ThenBy(item =>
                        item.Observation.CaseId,
                        StringComparer.Ordinal)))
            .ToList();
        var result = new List<CombatFoundationSuccessCase>();
        while (groups.Any(group => group.Count > 0))
        {
            foreach (var group in groups)
            {
                if (group.Count > 0)
                {
                    result.Add(group.Dequeue());
                }
            }
        }
        return result;
    }

    private static IEnumerable<CombatEpisode> RepresentativeEpisodes(
        CombatFoundationSuccessCase successCase,
        int maximumEpisodesPerRun)
    {
        var limit = Math.Max(1, maximumEpisodesPerRun);
        var episodes = successCase.Episodes
            .Where(item => item != null)
            .OrderBy(item => item.JourneyBattleIndex)
            .ThenBy(item => item.EpisodeId, StringComparer.Ordinal)
            .ToList();
        var selected = new List<CombatEpisode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(CombatEpisode episode)
        {
            if (selected.Count < limit && seen.Add(EpisodeKey(episode)))
            {
                selected.Add(episode);
            }
        }
        foreach (var episode in episodes
                     .OrderByDescending(item => item.JourneyBattleIndex)
                     .Take(3))
        {
            Add(episode);
        }
        var boundaries = new HashSet<int> { 4, 9, 14, 19, 24, 29, 36 };
        foreach (var episode in episodes.Where(item =>
                     boundaries.Contains(item.JourneyBattleIndex)))
        {
            Add(episode);
        }
        foreach (var episode in episodes)
        {
            Add(episode);
        }
        return selected;
    }

    private static bool EligibleExpertEpisode(
        CombatEpisode episode,
        string rulesetHash)
    {
        return episode.Authoritative
               && episode.Campaign?.IntegrityValid == true
               && episode.Campaign.FinalBossVictory
               && string.Equals(
                   episode.RulesetHash,
                   rulesetHash,
                   StringComparison.Ordinal);
    }

    private static bool IsAdvanced(CombatFoundationSuccessCase successCase)
    {
        return string.Equals(
            successCase.Observation.DifficultyId,
            "advanced",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool EpisodeIsAdvanced(CombatEpisode episode)
    {
        return string.Equals(
            episode.Campaign?.DifficultyId,
            "advanced",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void RecordShortfall(
        IDictionary<string, int> target,
        string key,
        int value)
    {
        if (value > 0)
        {
            target[key] = value;
        }
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
