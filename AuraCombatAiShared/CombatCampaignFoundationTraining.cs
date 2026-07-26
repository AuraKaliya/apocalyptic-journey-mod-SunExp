using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatCampaignFoundationTrainingRequest
{
    public ulong RunSeed { get; set; }

    public string DecisionProfile { get; set; } = "balanced";

    public int Iterations { get; set; } = 8;

    public int TrainingCampaignsPerIteration { get; set; } = 64;

    public int ArenaCampaignsPerDifficulty { get; set; } = 32;

    public int ArenaConfirmationCampaignsPerDifficulty { get; set; } = 64;

    public int ValidationCampaignsPerDifficulty { get; set; } = 10;

    public int NormalValidationCampaigns { get; set; } = 200;

    public int AdvancedValidationCampaigns { get; set; } = 500;

    public int PreflightCampaignsPerDifficulty { get; set; }

    public ulong PreflightSeedStart { get; set; } = 1_000_000UL;

    public bool PreflightOnly { get; set; }

    public int MaximumDegreeOfParallelism { get; set; } = 1;

    public bool EnableEarlyValidationStop { get; set; } = true;

    public bool EnableCurriculum { get; set; } = true;

    public bool EnableStratifiedReplay { get; set; } = true;

    public bool EnableHardSeedCurriculum { get; set; } = true;

    public double HardSeedReplayShare { get; set; } = 0.35d;

    public double SelfPlayExplorationProbability { get; set; } = 0.15d;

    public double SelfPlayExplorationTemperature { get; set; } = 1d;

    public ulong TrainingSeedStart { get; set; } = 10_000UL;

    public ulong ArenaSeedStart { get; set; } = 1_000_000UL;

    public ulong ValidationSeedStart { get; set; } = 2_000_000UL;

    public CombatDecisionProfile Profile { get; set; } = new();

    public CombatPolicyValueTrainingOptions Training { get; set; } = new();

    public CombatCampaignDefinition TrainingCampaign { get; set; } = new();

    public CombatCampaignDefinition ValidationCampaign { get; set; } = new();

    public Action<int, int, string>? Progress { get; set; }

    public Action<CombatCampaignFoundationTelemetry>? Telemetry { get; set; }

    public CombatCampaignFoundationResumeState? Resume { get; set; }

    public Action<CombatCampaignFoundationResumeState>? Checkpoint { get; set; }
}

public sealed class CombatCampaignFoundationResumeState
{
    public int SchemaVersion { get; set; } = 2;

    public string Stage { get; set; } = "";

    public int NextIteration { get; set; }

    public int CompletedCampaigns { get; set; }

    public CombatPolicyValueNetworkDefinition? Champion { get; set; }

    public CombatPolicyValueNetworkDefinition? WorkingChampion { get; set; }

    public List<CombatEpisode> Replay { get; set; } = new();

    public List<CombatCampaignFoundationIteration> Iterations { get; set; } =
        new();

    public CombatPolicyValueTrainingResumeState? ModelTraining { get; set; }

    public CombatCampaignFoundationTelemetry Telemetry { get; set; } = new();
}

public sealed class CombatCampaignFoundationTelemetry
{
    public string Stage { get; set; } = "";

    public string Phase { get; set; } = "";

    public int Iteration { get; set; }

    public int TotalIterations { get; set; }

    public int EffectiveParallelism { get; set; }

    public int ActiveCampaigns { get; set; }

    public int PeakConcurrentCampaigns { get; set; }

    public int ObservedWorkerThreads { get; set; }

    public int CompletedCampaigns { get; set; }

    public int RequestedCampaigns { get; set; }

    public int CompletedBattles { get; set; }

    public int MaximumCompletedBattleDepth { get; set; }

    public int MaximumActiveBattleDepth { get; set; }

    public int Depth1To5Campaigns { get; set; }

    public int Depth6To10Campaigns { get; set; }

    public int Depth11To20Campaigns { get; set; }

    public int Depth21To30Campaigns { get; set; }

    public int Depth31To37Campaigns { get; set; }

    public double ProjectedBattleDepth { get; set; }

    public double EstimatedRemainingSeconds { get; set; }

    public int ModelEpoch { get; set; }

    public int ModelTotalEpochs { get; set; }

    public int ModelCompletedFrames { get; set; }

    public int ModelTotalFrames { get; set; }

    public double ModelEpochsPerSecond { get; set; }

    public double ModelValidationLoss { get; set; }

    public double ModelBestValidationLoss { get; set; }

    public int ModelBestEpoch { get; set; }

    public int ModelStaleEpochs { get; set; }

    public bool ModelEarlyStopped { get; set; }

    public double PhaseEstimatedRemainingSeconds { get; set; }

    public long PolicyDecisions { get; set; }

    public long SearchSimulations { get; set; }

    public long SearchNodes { get; set; }

    public int SearchEarlyStops { get; set; }

    public int RuleTerminalOverrides { get; set; }

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }

    public double SearchSimulationsPerSecond { get; set; }

    public double ElapsedSeconds { get; set; }

    public double CampaignsPerSecond { get; set; }

    public double BattlesPerSecond { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }
}

public sealed class CombatCampaignFoundationIteration
{
    public int Iteration { get; set; }

    public int ReplayEpisodes { get; set; }

    public int TrainingReplayEpisodes { get; set; }

    public int TrainingReplayNormalEpisodes { get; set; }

    public int TrainingReplayAdvancedEpisodes { get; set; }

    public int TrainingReplaySuccessfulEpisodes { get; set; }

    public int TrainingReplayDroppedDuplicates { get; set; }

    public double TrainingReplayTargetNormalShare { get; set; }

    public int TrainingReplaySourceCampaigns { get; set; }

    public int TrainingReplaySelectedCampaigns { get; set; }

    public int TrainingReplaySuccessfulCampaigns { get; set; }

    public Dictionary<string, int> TrainingReplayQuotaShortfalls { get; set; } =
        new(StringComparer.Ordinal);

    public int HardSeedSourceCampaigns { get; set; }

    public int HardSeedTrainingCampaigns { get; set; }

    public int HardSeedTrainingVictories { get; set; }

    public Dictionary<string, int> HardSeedClusters { get; set; } =
        new(StringComparer.Ordinal);

    public int AdvancedTrainingCampaigns { get; set; }

    public string CurriculumStage { get; set; } = "";

    public double NormalWilsonLowerBound { get; set; }

    public double AdvancedWilsonLowerBound { get; set; }

    public double SelfPlayExplorationProbability { get; set; }

    public string CandidateModelId { get; set; } = "";

    public double ChampionArenaScore { get; set; }

    public double CandidateArenaScore { get; set; }

    public double ChampionNormalWinRate { get; set; }

    public double CandidateNormalWinRate { get; set; }

    public double ChampionAdvancedWinRate { get; set; }

    public double CandidateAdvancedWinRate { get; set; }

    public int InvalidCandidateCampaigns { get; set; }

    public int InvalidChampionCampaigns { get; set; }

    public int ValidArenaPairs { get; set; }

    public int ArenaScreeningPairs { get; set; }

    public int ArenaConfirmationPairs { get; set; }

    public int ValidNormalArenaPairs { get; set; }

    public int ValidAdvancedArenaPairs { get; set; }

    public int CandidateOnlyWins { get; set; }

    public int ChampionOnlyWins { get; set; }

    public double ChampionAverageCompletedBattles { get; set; }

    public double CandidateAverageCompletedBattles { get; set; }

    public bool Promoted { get; set; }

    public bool CurriculumCheckpointAccepted { get; set; }

    public string PromotionKind { get; set; } = "rejected";

    public string PromotionReason { get; set; } = "";
}

public sealed class CombatCampaignFoundationValidation
{
    public int CampaignsPerDifficulty { get; set; }

    public int NormalPlannedCampaigns { get; set; }

    public int AdvancedPlannedCampaigns { get; set; }

    public int NormalCampaigns { get; set; }

    public int AdvancedCampaigns { get; set; }

    public string NormalStatus { get; set; } = "not-run";

    public string AdvancedStatus { get; set; } = "not-run";

    public int NormalVictories { get; set; }

    public int AdvancedVictories { get; set; }

    public int InvalidCampaigns { get; set; }

    public double NormalWinRate { get; set; }

    public double AdvancedWinRate { get; set; }

    public bool Passed { get; set; }

    public bool EarlyStopped { get; set; }

    public string EarlyStopReason { get; set; } = "";
}

public sealed class CombatCampaignFoundationIntegrityFailure
{
    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public int CompletedBattles { get; set; }

    public List<string> Reasons { get; set; } = new();
}

public sealed class CombatCampaignFoundationIntegrityReport
{
    public int CampaignsPerDifficulty { get; set; }

    public int CompletedCampaigns { get; set; }

    public int InvalidCampaigns { get; set; }

    public int TerminalConsistencyViolations { get; set; }

    public bool Passed { get; set; }

    public Dictionary<string, int> FailureCounts { get; set; } =
        new(StringComparer.Ordinal);

    public List<CombatCampaignFoundationIntegrityFailure> Failures { get; set; } =
        new();
}

public sealed class CombatCampaignFoundationArenaFailure
{
    public int Iteration { get; set; }

    public string Competitor { get; set; } = "";

    public string DifficultyId { get; set; } = "";

    public ulong WorldSeed { get; set; }

    public int CompletedBattles { get; set; }

    public List<string> Reasons { get; set; } = new();
}

public sealed class CombatCampaignFoundationTrainingResult
{
    public ulong RunSeed { get; set; }

    public ulong TrainingSeedStart { get; set; }

    public ulong ArenaSeedStart { get; set; }

    public ulong ValidationSeedStart { get; set; }

    public int ModelRandomSeed { get; set; }

    public bool Success { get; set; }

    public bool AcceptancePassed { get; set; }

    public string Message { get; set; } = "";

    public CombatPolicyValueNetworkDefinition? Champion { get; set; }

    public List<CombatEpisode> Replay { get; set; } = new();

    public int GeneratedReplayEpisodes { get; set; }

    public int PersistedReplayEpisodes { get; set; }

    public List<CombatCampaignFoundationIteration> Iterations { get; set; } = new();

    public CombatCampaignFoundationValidation Validation { get; set; } = new();

    public CombatCampaignFoundationIntegrityReport Preflight { get; set; } = new();

    public List<CombatCampaignResult> ValidationRuns { get; set; } = new();

    public int RequestedCampaigns { get; set; }

    public int CompletedCampaigns { get; set; }

    public int InvalidTrainingCampaigns { get; set; }

    public int DiscardedInvalidEpisodes { get; set; }

    public int TerminalConsistencyViolations { get; set; }

    public int FeatureLeakageViolations { get; set; }

    public Dictionary<string, int> TrainingFailureCounts { get; set; } =
        new(StringComparer.Ordinal);

    public List<CombatCampaignFoundationIntegrityFailure> TrainingFailures {
        get;
        set;
    } = new();

    public Dictionary<string, int> ArenaFailureCounts { get; set; } =
        new(StringComparer.Ordinal);

    public List<CombatCampaignFoundationArenaFailure> ArenaFailures { get; set; } =
        new();

    public string EarlyStopReason { get; set; } = "";

    public int EffectiveParallelism { get; set; }

    public int PeakConcurrentCampaigns { get; set; }

    public int ObservedWorkerThreads { get; set; }

    public int CompletedBattles { get; set; }

    public int MaximumCompletedBattleDepth { get; set; }

    public int Depth1To5Campaigns { get; set; }

    public int Depth6To10Campaigns { get; set; }

    public int Depth11To20Campaigns { get; set; }

    public int Depth21To30Campaigns { get; set; }

    public int Depth31To37Campaigns { get; set; }

    public double ProjectedBattleDepth { get; set; }

    public double EstimatedRemainingSeconds { get; set; }

    public long PolicyDecisions { get; set; }

    public long SearchSimulations { get; set; }

    public long SearchNodes { get; set; }

    public int SearchEarlyStops { get; set; }

    public int RuleTerminalOverrides { get; set; }

    public int CertifiedLoops { get; set; }

    public int SustainableControlLoops { get; set; }

    public int FakeLoops { get; set; }

    public int BlockedLoops { get; set; }

    public int ModelCompletedEpochs { get; set; }

    public int ModelConfiguredEpochs { get; set; }

    public int ModelBestEpoch { get; set; }

    public bool ModelEarlyStopped { get; set; }

    public double ModelBestValidationLoss { get; set; }

    public double ElapsedSeconds { get; set; }

    public int Gen0Collections { get; set; }

    public int Gen1Collections { get; set; }

    public int Gen2Collections { get; set; }
}

public sealed class CombatCampaignFoundationTrainer
{
    private readonly CombatCampaignRunner campaignRunner;

    public CombatCampaignFoundationTrainer(CombatCampaignRunner? campaignRunner = null)
    {
        this.campaignRunner = campaignRunner ?? new CombatCampaignRunner();
    }

    public CombatCampaignFoundationTrainingResult Run(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        CombatPolicyValueNetworkDefinition? initialChampion = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        CombatCampaignWorldPlanner.Validate(request.TrainingCampaign);
        CombatCampaignWorldPlanner.Validate(request.ValidationCampaign);
        if (!request.TrainingCampaign.RequireAuthoritativeRules
            || !request.ValidationCampaign.RequireAuthoritativeRules)
        {
            throw new ArgumentException(
                "Formal foundation training requires authoritative training and validation campaigns.");
        }
        if (!string.Equals(
                request.TrainingCampaign.CampaignId,
                request.ValidationCampaign.CampaignId,
                StringComparison.Ordinal)
            || !string.Equals(
                request.TrainingCampaign.CampaignVersion,
                request.ValidationCampaign.CampaignVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Training and validation campaigns must share identity.");
        }

        var iterations = Math.Max(1, Math.Min(20, request.Iterations));
        var trainingCampaigns = Math.Max(
            2,
            Math.Min(1000, request.TrainingCampaignsPerIteration));
        var arenaPerDifficulty = Math.Max(
            1,
            Math.Min(100, request.ArenaCampaignsPerDifficulty));
        var arenaConfirmationPerDifficulty =
            arenaPerDifficulty >= 32
                ? Math.Max(
                    0,
                    Math.Min(
                        200,
                        request.ArenaConfirmationCampaignsPerDifficulty))
                : 0;
        var legacyValidationPerDifficulty = Math.Max(
            5,
            Math.Min(1000, request.ValidationCampaignsPerDifficulty));
        var normalValidationCampaigns = request.NormalValidationCampaigns > 0
            ? Math.Max(5, Math.Min(1000, request.NormalValidationCampaigns))
            : legacyValidationPerDifficulty;
        var advancedValidationCampaigns = request.AdvancedValidationCampaigns > 0
            ? Math.Max(5, Math.Min(1000, request.AdvancedValidationCampaigns))
            : legacyValidationPerDifficulty;
        var parallelism = Math.Max(
            1,
            Math.Min(Environment.ProcessorCount, request.MaximumDegreeOfParallelism));
        var preflightPerDifficulty = Math.Max(
            0,
            Math.Min(100, request.PreflightCampaignsPerDifficulty));
        var seedPlan = request.RunSeed == 0UL
            ? new CombatFoundationSeedPlan
            {
                RunSeed = 0UL,
                TrainingSeedStart = request.TrainingSeedStart,
                ArenaSeedStart = request.ArenaSeedStart,
                ValidationSeedStart = request.ValidationSeedStart,
                ModelRandomSeed = request.Training.RandomSeed
            }
            : CombatFoundationSeedPlan.Create(
                request.RunSeed,
                request.ValidationSeedStart);
        ValidateSeedPartitions(
            seedPlan.TrainingSeedStart,
            seedPlan.ArenaSeedStart,
            seedPlan.ValidationSeedStart,
            iterations,
            trainingCampaigns,
            arenaPerDifficulty + arenaConfirmationPerDifficulty,
            normalValidationCampaigns,
            advancedValidationCampaigns);

        var resume = request.Resume?.SchemaVersion == 2
                     && ResumeCompatible(request.Resume)
            ? request.Resume
            : null;
        var compatibleInitialChampion =
            CombatPolicyValueNetworkValidator.TryValidate(
                initialChampion,
                out _)
                ? initialChampion
                : null;
        var result = new CombatCampaignFoundationTrainingResult
        {
            Champion = resume?.Champion ?? compatibleInitialChampion,
            RunSeed = seedPlan.RunSeed,
            TrainingSeedStart = seedPlan.TrainingSeedStart,
            ArenaSeedStart = seedPlan.ArenaSeedStart,
            ValidationSeedStart = seedPlan.ValidationSeedStart,
            ModelRandomSeed = seedPlan.ModelRandomSeed
        };
        var foundationTrainingOptions = request.Training.Normalized();
        foundationTrainingOptions.RequireAuthoritativeEpisodes = true;
        foundationTrainingOptions.MaximumDegreeOfParallelism = parallelism;
        foundationTrainingOptions.RandomSeed = seedPlan.ModelRandomSeed;
        if (resume != null)
        {
            result.Replay.AddRange(resume.Replay ?? new List<CombatEpisode>());
            result.Iterations.AddRange(
                resume.Iterations
                ?? new List<CombatCampaignFoundationIteration>());
        }
        var workingChampion = resume?.WorkingChampion ?? result.Champion;
        ICombatPolicyValueModel championModel = workingChampion == null
            ? NullCombatPolicyValueModel.Instance
            : new ManagedCombatPolicyValueModel(workingChampion);
        var startIteration = Math.Max(
            0,
            Math.Min(iterations, resume?.NextIteration ?? 0));
        var trainingSeed = seedPlan.TrainingSeedStart
                           + (ulong)(startIteration * trainingCampaigns);
        var arenaSeed = seedPlan.ArenaSeedStart
                        + (ulong)(startIteration
                                  * (arenaPerDifficulty
                                     + arenaConfirmationPerDifficulty)
                                  * 2);
        var completedCampaigns = Math.Max(
            0,
            resume?.CompletedCampaigns ?? 0);
        var totalCampaigns = iterations
                             * (trainingCampaigns
                                + (arenaPerDifficulty
                                   + arenaConfirmationPerDifficulty) * 4)
                             + normalValidationCampaigns
                             + advancedValidationCampaigns;
        result.RequestedCampaigns = totalCampaigns;
        var telemetry = new FoundationTelemetryTracker(
            request,
            parallelism,
            totalCampaigns,
            resume?.Telemetry,
            completedCampaigns);
        telemetry.ReportStage(
            resume == null ? "starting" : "resumed");

        if (preflightPerDifficulty > 0)
        {
            result.Preflight = RunIntegrityPreflight(
                request,
                ruleset,
                championModel,
                telemetry,
                preflightPerDifficulty,
                request.RunSeed == 0UL
                    ? request.PreflightSeedStart
                    : seedPlan.TrainingSeedStart,
                parallelism,
                cancellationToken);
            result.TerminalConsistencyViolations +=
                result.Preflight.TerminalConsistencyViolations;
            if (!result.Preflight.Passed)
            {
                result.CompletedCampaigns =
                    Volatile.Read(ref completedCampaigns);
                result.Message =
                    "底模训练前权威快检失败："
                    + result.Preflight.InvalidCampaigns
                    + "/"
                    + result.Preflight.CompletedCampaigns
                    + " 个战役无效；未写入训练轨迹，也未开始模型训练。";
                telemetry.ApplyTo(result);
                return result;
            }
            if (request.PreflightOnly)
            {
                result.Success = true;
                result.CompletedCampaigns =
                    Volatile.Read(ref completedCampaigns);
                result.Message =
                    "底模训练前权威快检通过："
                    + result.Preflight.CompletedCampaigns
                    + " 个战役全部有效；未执行自博弈与模型训练。";
                telemetry.ApplyTo(result);
                return result;
            }
        }

        for (var iteration = startIteration;
             iteration < iterations;
             iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var iterationNumber = iteration + 1;
            var priorNormalTrials = result.Iterations.Sum(item =>
                item.ValidNormalArenaPairs);
            var priorAdvancedTrials = result.Iterations.Sum(item =>
                item.ValidAdvancedArenaPairs);
            var priorNormalWins = result.Iterations.Sum(item =>
                (int)Math.Round(
                    item.CandidateNormalWinRate
                    * item.ValidNormalArenaPairs));
            var priorAdvancedWins = result.Iterations.Sum(item =>
                (int)Math.Round(
                    item.CandidateAdvancedWinRate
                    * item.ValidAdvancedArenaPairs));
            var priorNormalWinRate = priorNormalTrials <= 0
                ? double.NaN
                : priorNormalWins / (double)priorNormalTrials;
            var priorAdvancedWinRate = priorAdvancedTrials <= 0
                ? double.NaN
                : priorAdvancedWins / (double)priorAdvancedTrials;
            var curriculumPlan = CombatFoundationCurriculum.Evaluate(
                request.EnableCurriculum,
                priorNormalWins,
                priorNormalTrials,
                priorAdvancedWins,
                priorAdvancedTrials);
            var effectiveExplorationProbability =
                CombatFoundationCurriculum.ExplorationProbability(
                    curriculumPlan,
                    request.SelfPlayExplorationProbability);
            var plannedTrainingDifficulties =
                CombatFoundationCurriculum.BuildDifficulties(
                    trainingCampaigns,
                    iteration,
                    iterations,
                    seedPlan.RunSeed,
                    request.EnableCurriculum,
                    priorNormalWinRate,
                    priorNormalTrials,
                    priorAdvancedWinRate,
                    priorAdvancedTrials);
            var hardSeedPlan = CombatFoundationHardSeedCurriculum.Select(
                result.Replay,
                trainingCampaigns,
                request.HardSeedReplayShare,
                iteration,
                seedPlan.RunSeed,
                request.EnableHardSeedCurriculum);
            var hardSeedTrainingVictories = 0;
            var resumeModelTraining = resume != null
                                      && resume.NextIteration == iteration
                                      && string.Equals(
                                          resume.Stage,
                                          "model-training",
                                          StringComparison.Ordinal);
            if (resumeModelTraining)
            {
                trainingSeed += (ulong)trainingCampaigns;
            }
            if (!resumeModelTraining)
            {
                var invalidTrainingCampaignsBefore =
                    result.InvalidTrainingCampaigns;
                var trainingSeedBase = trainingSeed;
                trainingSeed += (ulong)trainingCampaigns;
                var trainingRuns =
                    new FoundationTrainingCampaignRun?[trainingCampaigns];
                Parallel.For(
                    0,
                    trainingCampaigns,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = parallelism
                    },
                    campaignIndex =>
                    {
                        var hardSeed = campaignIndex < hardSeedPlan.Seeds.Count
                            ? hardSeedPlan.Seeds[campaignIndex]
                            : null;
                        var difficulty = hardSeed?.DifficultyId
                                         ?? plannedTrainingDifficulties[
                                             campaignIndex];
                        var campaignSeed = hardSeed?.WorldSeed
                                           ?? trainingSeedBase
                                           + (ulong)campaignIndex;
                        var factory = new RecordingCampaignPolicyFactory(
                            request.Profile,
                            championModel,
                            request.DecisionProfile,
                            effectiveExplorationProbability,
                            request.SelfPlayExplorationTemperature,
                            campaignSeed);
                        var campaign = RunCampaign(
                            request.TrainingCampaign,
                            difficulty,
                            campaignSeed,
                            ruleset,
                            factory,
                            telemetry,
                            "training:" + iterationNumber,
                            cancellationToken);
                        var episodes = factory.Complete(campaign);
                        trainingRuns[campaignIndex] =
                            new FoundationTrainingCampaignRun
                            {
                                Campaign = campaign,
                                Episodes = episodes,
                                HardSeed = hardSeed != null
                            };
                        ReportProgress(
                            request,
                            telemetry,
                            campaign,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 "
                            + iterationNumber
                            + " 轮：七层训练推演");
                    });
                for (var campaignIndex = 0;
                     campaignIndex < trainingRuns.Length;
                     campaignIndex++)
                {
                    var trainingRun = trainingRuns[campaignIndex]!;
                    if (trainingRun.Campaign.Invalid)
                    {
                        result.InvalidTrainingCampaigns++;
                        result.DiscardedInvalidEpisodes +=
                            trainingRun.Episodes.Count;
                        AddIntegrityFailure(
                            result.TrainingFailures,
                            result.TrainingFailureCounts,
                            trainingRun.Campaign);
                        trainingRun.Episodes.Clear();
                    }
                    else
                    {
                        ApplyCampaignTargets(
                            trainingRun.Episodes,
                            trainingRun.Campaign,
                            curriculumPlan.Stage);
                        result.FeatureLeakageViolations +=
                            SanitizeEpisodeFeatures(trainingRun.Episodes);
                        if (trainingRun.HardSeed
                            && trainingRun.Campaign.FinalBossVictory)
                        {
                            hardSeedTrainingVictories++;
                        }
                    }
                    result.TerminalConsistencyViolations +=
                        CountTerminalConsistencyViolations(
                            trainingRun.Campaign);
                    result.Replay.AddRange(trainingRun.Episodes);
                }
                result.TrainingFailures = result.TrainingFailures
                    .OrderBy(item => item.WorldSeed)
                    .ThenBy(item => item.DifficultyId, StringComparer.Ordinal)
                    .ToList();
                if (result.InvalidTrainingCampaigns
                    > invalidTrainingCampaignsBefore)
                {
                    result.CompletedCampaigns =
                        Volatile.Read(ref completedCampaigns);
                    result.Message =
                        "自博弈阶段出现无效战役；相关战役的全部轨迹已隔离，"
                        + "训练已停止，避免污染底模。失败摘要："
                        + FormatIntegrityFailureSummary(
                            result.TrainingFailures,
                            4);
                    telemetry.ApplyTo(result);
                    return result;
                }
                PublishCheckpoint(
                    request,
                    CreateResumeState(
                        "model-training",
                        iteration,
                        completedCampaigns,
                        result,
                        telemetry,
                        workingChampion,
                        modelTraining: null));
            }

            var replaySelection = CombatFoundationReplaySampler.Select(
                result.Replay,
                foundationTrainingOptions.ReplayEpisodeLimit,
                request.EnableStratifiedReplay);
            var replayWindow = replaySelection.Episodes;
            telemetry.ReportStage("model-training:" + iterationNumber);
            var trainingSession = new CombatPolicyValueTrainingSession
            {
                Resume = resumeModelTraining
                    ? resume!.ModelTraining
                    : workingChampion == null
                        ? null
                        : new CombatPolicyValueTrainingResumeState
                        {
                            Model = workingChampion,
                            BestModel = workingChampion,
                            BestValidationLoss = double.MaxValue
                        },
                Progress = progress => telemetry.ModelTrainingProgress(
                    iterationNumber,
                    iterations,
                    progress),
                Checkpoint = modelTraining => PublishCheckpoint(
                    request,
                    CreateResumeState(
                        "model-training",
                        iteration,
                        completedCampaigns,
                        result,
                        telemetry,
                        workingChampion,
                        modelTraining))
            };
            var trained = CombatPolicyValueTrainer.Train(
                replayWindow,
                request.DecisionProfile,
                foundationTrainingOptions,
                cancellationToken,
                trainingSession);
            if (!trained.Success || trained.Model == null)
            {
                result.CompletedCampaigns = Volatile.Read(ref completedCampaigns);
                result.Message = "第 "
                                 + (iteration + 1)
                                 + " 轮底模训练失败："
                                 + trained.Message;
                telemetry.ApplyTo(result);
                return result;
            }

            var candidateModel = new ManagedCombatPolicyValueModel(trained.Model);
            var championArena = new List<CombatCampaignResult>();
            var candidateArena = new List<CombatCampaignResult>();
            foreach (var difficulty in new[] { "normal", "advanced" })
            {
                var arenaSeedBase = arenaSeed;
                arenaSeed += (ulong)arenaPerDifficulty;
                var arenaPairs = new FoundationArenaPair?[arenaPerDifficulty];
                Parallel.For(
                    0,
                    arenaPerDifficulty,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = parallelism
                    },
                    arenaIndex =>
                    {
                        var seed = arenaSeedBase + (ulong)arenaIndex;
                        var champion = RunCampaign(
                            request.TrainingCampaign,
                            difficulty,
                            seed,
                            ruleset,
                            new CombatDecisionSimulationPolicyFactory(
                                request.Profile,
                                policyValueModel: championModel),
                            telemetry,
                            "arena:" + difficulty + ":champion",
                            cancellationToken);
                        ReportProgress(
                            request,
                            telemetry,
                            champion,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 " + iterationNumber + " 轮：隔离种子竞技场");
                        var candidate = RunCampaign(
                            request.TrainingCampaign,
                            difficulty,
                            seed,
                            ruleset,
                            new CombatDecisionSimulationPolicyFactory(
                                request.Profile,
                                policyValueModel: candidateModel),
                            telemetry,
                            "arena:" + difficulty + ":candidate",
                            cancellationToken);
                        ReportProgress(
                            request,
                            telemetry,
                            candidate,
                            ref completedCampaigns,
                            totalCampaigns,
                            "第 " + iterationNumber + " 轮：隔离种子竞技场");
                        arenaPairs[arenaIndex] = new FoundationArenaPair
                        {
                            Champion = champion,
                            Candidate = candidate
                        };
                    });
                for (var arenaIndex = 0;
                     arenaIndex < arenaPairs.Length;
                     arenaIndex++)
                {
                    championArena.Add(arenaPairs[arenaIndex]!.Champion);
                    candidateArena.Add(arenaPairs[arenaIndex]!.Candidate);
                }
            }

            var screeningPairIndexes = Enumerable
                .Range(0, Math.Min(championArena.Count, candidateArena.Count))
                .Where(index => !championArena[index].Invalid
                                && !candidateArena[index].Invalid)
                .ToList();
            var screeningChampionNormal = WinRate(
                screeningPairIndexes.Select(index => championArena[index])
                    .ToList(),
                "normal");
            var screeningCandidateNormal = WinRate(
                screeningPairIndexes.Select(index => candidateArena[index])
                    .ToList(),
                "normal");
            var screeningChampionAdvanced = WinRate(
                screeningPairIndexes.Select(index => championArena[index])
                    .ToList(),
                "advanced");
            var screeningCandidateAdvanced = WinRate(
                screeningPairIndexes.Select(index => candidateArena[index])
                    .ToList(),
                "advanced");
            var screeningCandidateOnlyWins = screeningPairIndexes.Count(index =>
                candidateArena[index].FinalBossVictory
                && !championArena[index].FinalBossVictory);
            var screeningChampionOnlyWins = screeningPairIndexes.Count(index =>
                championArena[index].FinalBossVictory
                && !candidateArena[index].FinalBossVictory);
            var screeningPassed =
                screeningPairIndexes.Count == arenaPerDifficulty * 2
                && screeningCandidateNormal + 0.0000001d
                   >= screeningChampionNormal
                && screeningCandidateAdvanced + 0.0000001d
                   >= screeningChampionAdvanced
                && (workingChampion == null
                    || screeningCandidateOnlyWins
                       > screeningChampionOnlyWins);
            var confirmationRan =
                screeningPassed && arenaConfirmationPerDifficulty > 0;
            if (confirmationRan)
            {
                foreach (var difficulty in new[] { "normal", "advanced" })
                {
                    var arenaSeedBase = arenaSeed;
                    arenaSeed += (ulong)arenaConfirmationPerDifficulty;
                    var arenaPairs =
                        new FoundationArenaPair?[arenaConfirmationPerDifficulty];
                    Parallel.For(
                        0,
                        arenaConfirmationPerDifficulty,
                        new ParallelOptions
                        {
                            CancellationToken = cancellationToken,
                            MaxDegreeOfParallelism = parallelism
                        },
                        arenaIndex =>
                        {
                            var seed = arenaSeedBase + (ulong)arenaIndex;
                            var champion = RunCampaign(
                                request.TrainingCampaign,
                                difficulty,
                                seed,
                                ruleset,
                                new CombatDecisionSimulationPolicyFactory(
                                    request.Profile,
                                    policyValueModel: championModel),
                                telemetry,
                                "arena-confirmation:"
                                + difficulty
                                + ":champion",
                                cancellationToken);
                            ReportProgress(
                                request,
                                telemetry,
                                champion,
                                ref completedCampaigns,
                                totalCampaigns,
                                "第 "
                                + iterationNumber
                                + " 轮：晋级确认竞技场");
                            var candidate = RunCampaign(
                                request.TrainingCampaign,
                                difficulty,
                                seed,
                                ruleset,
                                new CombatDecisionSimulationPolicyFactory(
                                    request.Profile,
                                    policyValueModel: candidateModel),
                                telemetry,
                                "arena-confirmation:"
                                + difficulty
                                + ":candidate",
                                cancellationToken);
                            ReportProgress(
                                request,
                                telemetry,
                                candidate,
                                ref completedCampaigns,
                                totalCampaigns,
                                "第 "
                                + iterationNumber
                                + " 轮：晋级确认竞技场");
                            arenaPairs[arenaIndex] = new FoundationArenaPair
                            {
                                Champion = champion,
                                Candidate = candidate
                            };
                        });
                    foreach (var pair in arenaPairs)
                    {
                        championArena.Add(pair!.Champion);
                        candidateArena.Add(pair.Candidate);
                    }
                }
            }
            else
            {
                // Reserve the confirmation seed partition even when screening
                // rejects the candidate so resumed and uninterrupted runs use
                // identical later-iteration seeds.
                arenaSeed += (ulong)(arenaConfirmationPerDifficulty * 2);
            }

            var invalidCandidate =
                candidateArena.Count(item => item.Invalid);
            var invalidChampion =
                championArena.Count(item => item.Invalid);
            result.TerminalConsistencyViolations += championArena.Sum(
                CountTerminalConsistencyViolations);
            result.TerminalConsistencyViolations += candidateArena.Sum(
                CountTerminalConsistencyViolations);
            for (var arenaIndex = 0;
                 arenaIndex < Math.Min(
                     championArena.Count,
                     candidateArena.Count);
                 arenaIndex++)
            {
                if (championArena[arenaIndex].Invalid)
                {
                    AddArenaFailure(
                        result,
                        iterationNumber,
                        "champion",
                        championArena[arenaIndex]);
                }
                if (candidateArena[arenaIndex].Invalid)
                {
                    AddArenaFailure(
                        result,
                        iterationNumber,
                        "candidate",
                        candidateArena[arenaIndex]);
                }
            }
            result.ArenaFailures = result.ArenaFailures
                .OrderBy(item => item.Iteration)
                .ThenBy(item => item.WorldSeed)
                .ThenBy(item => item.Competitor, StringComparer.Ordinal)
                .ToList();
            var validPairIndexes = Enumerable
                .Range(0, Math.Min(championArena.Count, candidateArena.Count))
                .Where(index => !championArena[index].Invalid
                                && !candidateArena[index].Invalid)
                .ToList();
            var validChampionArena = validPairIndexes
                .Select(index => championArena[index])
                .ToList();
            var validCandidateArena = validPairIndexes
                .Select(index => candidateArena[index])
                .ToList();
            var validNormalPairs = validPairIndexes.Count(index =>
                string.Equals(
                    candidateArena[index].DifficultyId,
                    "normal",
                    StringComparison.Ordinal));
            var validAdvancedPairs = validPairIndexes.Count(index =>
                string.Equals(
                    candidateArena[index].DifficultyId,
                    "advanced",
                    StringComparison.Ordinal));
            var candidateOnlyWins = validPairIndexes.Count(index =>
                candidateArena[index].FinalBossVictory
                && !championArena[index].FinalBossVictory);
            var championOnlyWins = validPairIndexes.Count(index =>
                championArena[index].FinalBossVictory
                && !candidateArena[index].FinalBossVictory);
            var championScore = validChampionArena.Count == 0
                ? 0d
                : validChampionArena.Average(Score);
            var candidateScore = validCandidateArena.Count == 0
                ? 0d
                : validCandidateArena.Average(Score);
            var championNormal = WinRate(validChampionArena, "normal");
            var candidateNormal = WinRate(validCandidateArena, "normal");
            var championAdvanced = WinRate(validChampionArena, "advanced");
            var candidateAdvanced = WinRate(validCandidateArena, "advanced");
            var championAverageDepth = validChampionArena.Count == 0
                ? 0d
                : validChampionArena.Average(item => item.CompletedBattles);
            var candidateAverageDepth = validCandidateArena.Count == 0
                ? 0d
                : validCandidateArena.Average(item => item.CompletedBattles);
            var expectedArenaPairs =
                (arenaPerDifficulty
                 + (confirmationRan
                     ? arenaConfirmationPerDifficulty
                     : 0)) * 2;
            var curriculumCheckpoint =
                validPairIndexes.Count == expectedArenaPairs
                                       && candidateNormal + 0.0000001d >= championNormal
                                       && candidateAdvanced + 0.0000001d >= championAdvanced;
            var meaningfulWinGain =
                candidateOnlyWins > championOnlyWins;
            var bootstrapPromotion = workingChampion == null;
            var promoted = curriculumCheckpoint
                           && (bootstrapPromotion || meaningfulWinGain);
            var promotionReason = !curriculumCheckpoint
                ? "regression-or-incomplete-arena"
                : bootstrapPromotion
                    ? "bootstrap-champion"
                : !meaningfulWinGain
                    ? "no-paired-win-gain"
                    : promoted
                        ? "paired-win-gain"
                        : "no-meaningful-gain";
            result.Iterations.Add(new CombatCampaignFoundationIteration
            {
                Iteration = iteration + 1,
                ReplayEpisodes = result.Replay.Count,
                TrainingReplayEpisodes = replaySelection.Episodes.Count,
                TrainingReplayNormalEpisodes =
                    replaySelection.NormalEpisodes,
                TrainingReplayAdvancedEpisodes =
                    replaySelection.AdvancedEpisodes,
                TrainingReplaySuccessfulEpisodes =
                    replaySelection.SuccessfulEpisodes,
                TrainingReplayDroppedDuplicates =
                    replaySelection.DroppedDuplicateEpisodes,
                TrainingReplayTargetNormalShare =
                    replaySelection.TargetNormalShare,
                TrainingReplaySourceCampaigns =
                    replaySelection.SourceCampaigns,
                TrainingReplaySelectedCampaigns =
                    replaySelection.SelectedCampaigns,
                TrainingReplaySuccessfulCampaigns =
                    replaySelection.SuccessfulCampaigns,
                TrainingReplayQuotaShortfalls =
                    new Dictionary<string, int>(
                        replaySelection.QuotaShortfalls,
                        StringComparer.Ordinal),
                HardSeedSourceCampaigns =
                    hardSeedPlan.SourceCampaigns,
                HardSeedTrainingCampaigns =
                    hardSeedPlan.Seeds.Count,
                HardSeedTrainingVictories =
                    hardSeedTrainingVictories,
                HardSeedClusters =
                    new Dictionary<string, int>(
                        hardSeedPlan.Clusters,
                        StringComparer.Ordinal),
                AdvancedTrainingCampaigns =
                    hardSeedPlan.Seeds.Count(seed => string.Equals(
                        seed.DifficultyId,
                        "advanced",
                        StringComparison.Ordinal))
                    + plannedTrainingDifficulties
                        .Skip(hardSeedPlan.Seeds.Count)
                        .Count(difficulty => string.Equals(
                            difficulty,
                            "advanced",
                            StringComparison.Ordinal)),
                CurriculumStage = curriculumPlan.Stage,
                NormalWilsonLowerBound =
                    curriculumPlan.NormalWilsonLowerBound,
                AdvancedWilsonLowerBound =
                    curriculumPlan.AdvancedWilsonLowerBound,
                SelfPlayExplorationProbability =
                    effectiveExplorationProbability,
                CandidateModelId = trained.Model.ModelId,
                ChampionArenaScore = championScore,
                CandidateArenaScore = candidateScore,
                ChampionNormalWinRate = championNormal,
                CandidateNormalWinRate = candidateNormal,
                ChampionAdvancedWinRate = championAdvanced,
                CandidateAdvancedWinRate = candidateAdvanced,
                InvalidCandidateCampaigns = invalidCandidate,
                InvalidChampionCampaigns = invalidChampion,
                ValidArenaPairs = validPairIndexes.Count,
                ArenaScreeningPairs = arenaPerDifficulty * 2,
                ArenaConfirmationPairs = confirmationRan
                    ? arenaConfirmationPerDifficulty * 2
                    : 0,
                ValidNormalArenaPairs = validNormalPairs,
                ValidAdvancedArenaPairs = validAdvancedPairs,
                CandidateOnlyWins = candidateOnlyWins,
                ChampionOnlyWins = championOnlyWins,
                ChampionAverageCompletedBattles = championAverageDepth,
                CandidateAverageCompletedBattles = candidateAverageDepth,
                Promoted = promoted,
                CurriculumCheckpointAccepted = curriculumCheckpoint,
                PromotionKind = promoted
                    ? "formal-champion"
                    : "rejected",
                PromotionReason = promotionReason
            });
            if (invalidCandidate > 0 || invalidChampion > 0)
            {
                result.CompletedCampaigns =
                    Volatile.Read(ref completedCampaigns);
                result.Message =
                    "竞技场阶段出现无效战役（基准 "
                    + invalidChampion
                    + "，候选 "
                    + invalidCandidate
                    + "）；本轮未计分、未晋级，训练已停止。失败定位："
                    + FormatArenaFailureSummary(
                        result.ArenaFailures.Where(item =>
                            item.Iteration == iterationNumber),
                        4);
                telemetry.ApplyTo(result);
                return result;
            }
            if (promoted)
            {
                championModel = candidateModel;
                workingChampion = trained.Model;
            }
            if (promoted)
            {
                result.Champion = trained.Model;
            }
            PublishCheckpoint(
                request,
                CreateResumeState(
                    "iteration-complete",
                    iteration + 1,
                    completedCampaigns,
                    result,
                    telemetry,
                    workingChampion,
                    modelTraining: null));
            resume = null;
        }

        if (result.Champion == null)
        {
            result.CompletedCampaigns = Volatile.Read(ref completedCampaigns);
            result.Message = result.Iterations.Any(item =>
                item.CurriculumCheckpointAccepted)
                ? "工作模型已完成课程迭代，但尚无最终 Boss 胜利；未执行正式隔离验证，也不会发布为正式底模。"
                : "没有候选通过竞技场课程门槛；未执行正式隔离验证，也不会发布为正式底模。";
            telemetry.ApplyTo(result);
            return result;
        }

        PublishCheckpoint(
            request,
            CreateResumeState(
                "validation",
                iterations,
                completedCampaigns,
                result,
                telemetry,
                workingChampion,
                modelTraining: null));
        var earlyStopReason = "";
        foreach (var difficulty in new[] { "normal", "advanced" })
        {
            if (!string.IsNullOrWhiteSpace(earlyStopReason))
            {
                break;
            }
            var validationCount = difficulty == "normal"
                ? normalValidationCampaigns
                : advancedValidationCampaigns;
            var difficultySeedStart = seedPlan.ValidationSeedStart
                                      + (ulong)(difficulty == "advanced"
                                          ? normalValidationCampaigns
                                          : 0);
            var difficultyRuns = new CombatCampaignResult?[validationCount];
            for (var batchStart = 0;
                 batchStart < validationCount;
                 batchStart += parallelism)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batchCount = Math.Min(parallelism, validationCount - batchStart);
                Parallel.For(
                    0,
                    batchCount,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = parallelism
                    },
                    batchOffset =>
                    {
                        var index = batchStart + batchOffset;
                        var validationRun = RunCampaign(
                            request.ValidationCampaign,
                            difficulty,
                            difficultySeedStart + (ulong)index,
                            ruleset,
                            new CombatDecisionSimulationPolicyFactory(
                                request.Profile,
                                policyValueModel: championModel),
                            telemetry,
                            "validation:" + difficulty,
                            cancellationToken);
                        for (var battleIndex = 0;
                             battleIndex < validationRun.Battles.Count - 1;
                             battleIndex++)
                        {
                            validationRun.Battles[battleIndex].Events.Clear();
                        }
                        difficultyRuns[index] = validationRun;
                        ReportProgress(
                            request,
                            telemetry,
                            validationRun,
                            ref completedCampaigns,
                            totalCampaigns,
                            "最终隔离验证：" + difficulty);
                    });
                if (!request.EnableEarlyValidationStop)
                {
                    continue;
                }
                var completedRuns = difficultyRuns
                    .Where(item => item != null)
                    .Select(item => item!)
                    .ToList();
                var failures = completedRuns.Count(item =>
                    item.Invalid || !item.FinalBossVictory);
                var allowedFailures = string.Equals(
                    difficulty,
                    "normal",
                    StringComparison.Ordinal)
                    ? 0
                    : validationCount
                      - (int)Math.Ceiling(validationCount * 0.8d);
                if (failures > allowedFailures)
                {
                    earlyStopReason = string.Equals(
                        difficulty,
                        "normal",
                        StringComparison.Ordinal)
                        ? "普通难度已出现失败，无法再达到 100% 验收线"
                        : "高级难度失败数已超过 20% 验收上限";
                    break;
                }
            }
            result.ValidationRuns.AddRange(
                difficultyRuns
                    .Where(item => item != null)
                    .Select(item => item!));
        }

        var normalRuns = result.ValidationRuns.Where(item =>
            string.Equals(item.DifficultyId, "normal", StringComparison.Ordinal)).ToList();
        var advancedRuns = result.ValidationRuns.Where(item =>
            string.Equals(item.DifficultyId, "advanced", StringComparison.Ordinal)).ToList();
        result.Validation = new CombatCampaignFoundationValidation
        {
            CampaignsPerDifficulty = normalValidationCampaigns == advancedValidationCampaigns
                ? normalValidationCampaigns
                : 0,
            NormalPlannedCampaigns = normalValidationCampaigns,
            AdvancedPlannedCampaigns = advancedValidationCampaigns,
            NormalCampaigns = normalRuns.Count,
            AdvancedCampaigns = advancedRuns.Count,
            NormalStatus = normalRuns.Count == 0
                ? "not-run"
                : "executed",
            AdvancedStatus = advancedRuns.Count == 0
                ? "not-run"
                : "executed",
            NormalVictories = normalRuns.Count(item => item.FinalBossVictory),
            AdvancedVictories = advancedRuns.Count(item => item.FinalBossVictory),
            InvalidCampaigns = result.ValidationRuns.Count(item => item.Invalid),
            NormalWinRate = normalRuns.Count == 0
                ? 0d
                : normalRuns.Count(item => item.FinalBossVictory) / (double)normalRuns.Count,
            AdvancedWinRate = advancedRuns.Count == 0
                ? 0d
                : advancedRuns.Count(item => item.FinalBossVictory) / (double)advancedRuns.Count,
            EarlyStopped = !string.IsNullOrWhiteSpace(earlyStopReason),
            EarlyStopReason = earlyStopReason
        };
        result.TerminalConsistencyViolations +=
            result.ValidationRuns.Sum(CountTerminalConsistencyViolations);
        result.Validation.Passed = result.Validation.InvalidCampaigns == 0
                                   && result.TerminalConsistencyViolations == 0
                                   && result.FeatureLeakageViolations == 0
                                   && result.Validation.NormalVictories
                                   == normalValidationCampaigns
                                   && result.Validation.AdvancedVictories
                                   >= (int)Math.Ceiling(advancedValidationCampaigns * 0.8d);
        result.AcceptancePassed = result.Validation.Passed;
        result.Success = true;
        result.GeneratedReplayEpisodes = result.Replay.Count;
        var persistedReplay = CombatFoundationReplaySampler.Select(
            result.Replay,
            Math.Min(1024, foundationTrainingOptions.ReplayEpisodeLimit),
            request.EnableStratifiedReplay);
        result.Replay = persistedReplay.Episodes;
        result.PersistedReplayEpisodes = result.Replay.Count;
        result.CompletedCampaigns = Volatile.Read(ref completedCampaigns);
        result.EarlyStopReason = earlyStopReason;
        result.Message = result.AcceptancePassed
            ? "底模通过隔离验收：普通 "
              + result.Validation.NormalVictories
              + "/"
              + normalValidationCampaigns
              + "，高级 "
              + result.Validation.AdvancedVictories
              + "/"
              + advancedValidationCampaigns
            : "底模尚未达到隔离验收线：普通 "
              + result.Validation.NormalVictories
              + "/"
              + result.Validation.NormalCampaigns
              + "（已执行；计划 "
              + normalValidationCampaigns
              + "，要求计划样本全部通过）"
              + "，高级 "
              + result.Validation.AdvancedVictories
              + "/"
              + result.Validation.AdvancedCampaigns
              + "（已执行；计划 "
              + advancedValidationCampaigns
              + "，要求至少 "
              + (int)Math.Ceiling(advancedValidationCampaigns * 0.8d)
              + "）"
              + (string.IsNullOrWhiteSpace(earlyStopReason)
                  ? ""
                  : "；已提前结束验证：" + earlyStopReason);
        telemetry.ApplyTo(result);
        return result;
    }

    private static bool ResumeCompatible(
        CombatCampaignFoundationResumeState resume)
    {
        if (!ModelCompatible(resume.Champion)
            || !ModelCompatible(resume.WorkingChampion)
            || !ModelCompatible(resume.ModelTraining?.Model)
            || !ModelCompatible(resume.ModelTraining?.BestModel))
        {
            return false;
        }
        return (resume.Replay ?? new List<CombatEpisode>()).All(episode =>
            episode != null
            && episode.ModelProtocol
            == CombatPolicyValueProtocol.EpisodeProtocol
            && episode.FeatureSchemaVersion
            == CombatPolicyValueProtocol.FeatureSchemaVersion);
    }

    private static bool ModelCompatible(
        CombatPolicyValueNetworkDefinition? model)
    {
        return model == null
               || CombatPolicyValueNetworkValidator.TryValidate(
                   model,
                   out _);
    }

    private static CombatCampaignFoundationResumeState CreateResumeState(
        string stage,
        int nextIteration,
        int completedCampaigns,
        CombatCampaignFoundationTrainingResult result,
        FoundationTelemetryTracker telemetry,
        CombatPolicyValueNetworkDefinition? workingChampion,
        CombatPolicyValueTrainingResumeState? modelTraining)
    {
        return new CombatCampaignFoundationResumeState
        {
            Stage = stage,
            NextIteration = Math.Max(0, nextIteration),
            CompletedCampaigns = Math.Max(0, completedCampaigns),
            Champion = result.Champion,
            WorkingChampion = workingChampion,
            Replay = new List<CombatEpisode>(result.Replay),
            Iterations = new List<CombatCampaignFoundationIteration>(
                result.Iterations),
            ModelTraining = modelTraining,
            Telemetry = telemetry.Current(stage)
        };
    }

    private static void PublishCheckpoint(
        CombatCampaignFoundationTrainingRequest request,
        CombatCampaignFoundationResumeState checkpoint)
    {
        request.Checkpoint?.Invoke(checkpoint);
    }

    private static void ReportProgress(
        CombatCampaignFoundationTrainingRequest request,
        FoundationTelemetryTracker telemetry,
        CombatCampaignResult campaign,
        ref int completedCampaigns,
        int totalCampaigns,
        string message)
    {
        var current = Interlocked.Increment(ref completedCampaigns);
        request.Progress?.Invoke(current, totalCampaigns, message);
        telemetry.CampaignCompleted(current, campaign, message);
    }

    private CombatCampaignResult RunCampaign(
        CombatCampaignDefinition campaign,
        string difficulty,
        ulong seed,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory factory,
        FoundationTelemetryTracker telemetry,
        string stage,
        CancellationToken cancellationToken)
    {
        var campaignWorkId = telemetry.EnterCampaign(stage);
        CombatCampaignResult? result = null;
        try
        {
            result = campaignRunner.RunMonitored(
                campaign,
                CombatCampaignWorldPlanner.Build(campaign, difficulty, seed),
                ruleset,
                factory,
                (depth, battle) =>
                    telemetry.BattleCompleted(
                        campaignWorkId,
                        depth,
                        battle,
                        stage),
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Foundation campaign failed. stage="
                + stage
                + ", difficulty="
                + difficulty
                + ", seed="
                + seed,
                ex);
        }
        finally
        {
            telemetry.ExitCampaign(campaignWorkId, result, stage);
        }
    }

    private CombatCampaignFoundationIntegrityReport RunIntegrityPreflight(
        CombatCampaignFoundationTrainingRequest request,
        CombatRuleset ruleset,
        ICombatPolicyValueModel policyValueModel,
        FoundationTelemetryTracker telemetry,
        int campaignsPerDifficulty,
        ulong seedStart,
        int parallelism,
        CancellationToken cancellationToken)
    {
        telemetry.ReportStage("preflight");
        var difficulties = new[] { "normal", "advanced" };
        var runs =
            new CombatCampaignResult?[campaignsPerDifficulty * difficulties.Length];
        Parallel.For(
            0,
            runs.Length,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelism
            },
            index =>
            {
                var difficulty = difficulties[index % difficulties.Length];
                var seed = seedStart + (ulong)index;
                runs[index] = RunCampaign(
                    request.TrainingCampaign,
                    difficulty,
                    seed,
                    ruleset,
                    new CombatDecisionSimulationPolicyFactory(
                        request.Profile,
                        policyValueModel: policyValueModel),
                    telemetry,
                    "preflight:" + difficulty,
                    cancellationToken);
            });
        var completed = runs
            .Where(item => item != null)
            .Select(item => item!)
            .ToList();
        var report = new CombatCampaignFoundationIntegrityReport
        {
            CampaignsPerDifficulty = campaignsPerDifficulty,
            CompletedCampaigns = completed.Count,
            InvalidCampaigns = completed.Count(item => item.Invalid),
            TerminalConsistencyViolations = completed.Sum(
                CountTerminalConsistencyViolations)
        };
        foreach (var campaign in completed.Where(item => item.Invalid))
        {
            AddIntegrityFailure(
                report.Failures,
                report.FailureCounts,
                campaign);
        }
        report.Failures = report.Failures
            .OrderBy(item => item.WorldSeed)
            .ThenBy(item => item.DifficultyId, StringComparer.Ordinal)
            .ToList();
        report.Passed = report.CompletedCampaigns == runs.Length
                        && report.InvalidCampaigns == 0
                        && report.TerminalConsistencyViolations == 0;
        return report;
    }

    private static int CountTerminalConsistencyViolations(
        CombatCampaignResult campaign)
    {
        return campaign?.Battles.Count(battle =>
            !battle.TerminalConsistencyValid) ?? 0;
    }

    private static int SanitizeEpisodeFeatures(
        IEnumerable<CombatEpisode> episodes)
    {
        var removed = 0;
        foreach (var frame in (episodes ?? Array.Empty<CombatEpisode>())
                     .SelectMany(episode =>
                         episode.Frames ?? new List<CombatEpisodeFrame>()))
        {
            var forbidden = frame.StateFeatures.Keys
                .Where(key =>
                    !CombatPolicyValueEncoding.IsPermittedStateFeature(key))
                .ToList();
            foreach (var key in forbidden)
            {
                if (frame.StateFeatures.Remove(key))
                {
                    removed++;
                }
            }
        }
        return removed;
    }

    private static void AddIntegrityFailure(
        ICollection<CombatCampaignFoundationIntegrityFailure> failures,
        IDictionary<string, int> failureCounts,
        CombatCampaignResult campaign)
    {
        var failure = CreateIntegrityFailure(campaign);
        failures.Add(failure);
        foreach (var reason in failure.Reasons)
        {
            failureCounts.TryGetValue(reason, out var count);
            failureCounts[reason] = count + 1;
        }
    }

    private static void AddArenaFailure(
        CombatCampaignFoundationTrainingResult result,
        int iteration,
        string competitor,
        CombatCampaignResult campaign)
    {
        var integrityFailure = CreateIntegrityFailure(campaign);
        result.ArenaFailures.Add(new CombatCampaignFoundationArenaFailure
        {
            Iteration = iteration,
            Competitor = competitor,
            DifficultyId = integrityFailure.DifficultyId,
            WorldSeed = integrityFailure.WorldSeed,
            CompletedBattles = integrityFailure.CompletedBattles,
            Reasons = integrityFailure.Reasons
        });
        foreach (var reason in integrityFailure.Reasons)
        {
            result.ArenaFailureCounts.TryGetValue(reason, out var count);
            result.ArenaFailureCounts[reason] = count + 1;
        }
    }

    private static CombatCampaignFoundationIntegrityFailure
        CreateIntegrityFailure(CombatCampaignResult campaign)
    {
        var reasons = campaign.Battles
            .Where(item => item.Outcome == CombatSimulationOutcome.Invalid)
            .SelectMany(item => new[]
            {
                "battle:" + item.TerminationReason,
                "battle-scenario:" + item.ScenarioId
            })
            .Concat(campaign.Battles.SelectMany(item =>
                item.UnsupportedDefinitions.Select(definition =>
                    "unsupported:" + definition)))
            .Concat(campaign.Battles
                .Where(item => item.Outcome == CombatSimulationOutcome.Invalid
                               && !string.IsNullOrWhiteSpace(
                                   item.FailureDiagnostics.LimitScope))
                .SelectMany(item => new[]
                    {
                        "command-limit-scope:"
                        + item.FailureDiagnostics.LimitScope,
                        "command-limit-action:"
                        + item.FailureDiagnostics.ActionDefinitionId,
                        "command-limit-pending:"
                        + item.FailureDiagnostics.PendingCommand,
                        "command-limit-count:"
                        + item.FailureDiagnostics.TotalCommandCount
                        + "/"
                        + item.FailureDiagnostics.ActionCommandCount
                    }
                    .Concat(item.FailureDiagnostics.RecentCommands.Select(
                        command => "command-recent:" + command))
                    .Concat(item.FailureDiagnostics.StateSummary.Select(
                        summary => "failure-state:" + summary))))
            .Concat(campaign.UnsupportedDefinitions.Select(definition =>
                "campaign:" + definition))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();
        if (reasons.Count == 0)
        {
            reasons.Add("campaign:invalid-state");
        }
        return new CombatCampaignFoundationIntegrityFailure
        {
            DifficultyId = campaign.DifficultyId,
            WorldSeed = campaign.WorldSeed,
            CompletedBattles = campaign.CompletedBattles,
            Reasons = reasons
        };
    }

    private static string FormatIntegrityFailureSummary(
        IEnumerable<CombatCampaignFoundationIntegrityFailure> failures,
        int maximumFailures)
    {
        return string.Join(
            "；",
            failures.Take(Math.Max(1, maximumFailures)).Select(failure =>
            {
                var reason = failure.Reasons.FirstOrDefault()
                             ?? "campaign:invalid-state";
                if (reason.Length > 180)
                {
                    reason = reason.Substring(0, 180) + "...";
                }
                return failure.DifficultyId
                       + "/"
                       + failure.WorldSeed
                       + "@"
                       + failure.CompletedBattles
                       + "："
                       + reason;
            }));
    }

    private static string FormatArenaFailureSummary(
        IEnumerable<CombatCampaignFoundationArenaFailure> failures,
        int maximumFailures)
    {
        return string.Join(
            "；",
            failures.Take(Math.Max(1, maximumFailures)).Select(failure =>
            {
                var reason = failure.Reasons.FirstOrDefault()
                             ?? "campaign:invalid-state";
                if (reason.Length > 180)
                {
                    reason = reason.Substring(0, 180) + "...";
                }
                return "iteration="
                       + failure.Iteration
                       + ", competitor="
                       + failure.Competitor
                       + ", difficulty="
                       + failure.DifficultyId
                       + ", seed="
                       + failure.WorldSeed
                       + ", completedBattles="
                       + failure.CompletedBattles
                       + ", reason="
                       + reason;
            }));
    }

    private static void ApplyCampaignTargets(
        IReadOnlyList<CombatEpisode> episodes,
        CombatCampaignResult campaign,
        string curriculumStage)
    {
        var totalBattles = Math.Max(
            1,
            campaign.TotalBattles > 0
                ? campaign.TotalBattles
                : Math.Max(campaign.CompletedBattles, episodes.Count));
        var progress = Math.Max(
            0d,
            Math.Min(1d, campaign.CompletedBattles / (double)totalBattles));
        var campaignReturn = campaign.FinalBossVictory
            ? 1d
            : Math.Max(-1d, Math.Min(-0.75d, -1d + progress * 0.25d));
        var remainingBattles = episodes.Count;
        for (var episodeIndex = 0; episodeIndex < episodes.Count; episodeIndex++)
        {
            var episode = episodes[episodeIndex];
            episode.Campaign ??= new CombatCampaignEpisodeMetadata();
            episode.Campaign.WorldSeed = campaign.WorldSeed;
            episode.Campaign.DifficultyId = campaign.DifficultyId;
            episode.Campaign.FinalBossVictory = campaign.FinalBossVictory;
            episode.Campaign.ReachedFinalBoss = campaign.ReachedFinalBoss;
            episode.Campaign.CampaignCompletedBattles =
                campaign.CompletedBattles;
            episode.Campaign.CampaignTotalBattles = totalBattles;
            episode.Campaign.FailureBattleIndex =
                campaign.FinalBossVictory
                    ? -1
                    : Math.Max(0, campaign.CompletedBattles - 1);
            episode.Campaign.TerminalScenarioId =
                campaign.Battles.LastOrDefault()?.ScenarioId ?? "";
            episode.Campaign.OutcomeClass = campaign.FinalBossVictory
                ? "victory"
                : campaign.Invalid
                    ? "invalid"
                    : "defeat";
            episode.Campaign.CurriculumStage = curriculumStage ?? "";
            episode.Campaign.IntegrityValid =
                !campaign.Invalid
                && campaign.Battles.All(battle =>
                    battle.TerminalConsistencyValid);
            var journeySignal = campaignReturn
                                * Math.Pow(0.995d, Math.Max(0, remainingBattles - episodeIndex - 1));
            foreach (var frame in episode.Frames)
            {
                frame.LongTermReturn = Math.Max(
                    -1d,
                    Math.Min(1d, journeySignal));
            }
        }
    }

    private static double WinRate(
        IReadOnlyList<CombatCampaignResult> results,
        string difficulty)
    {
        var selected = results.Where(item => string.Equals(
            item.DifficultyId,
            difficulty,
            StringComparison.Ordinal)).ToList();
        return selected.Count == 0
            ? 0d
            : selected.Count(item => item.FinalBossVictory) / (double)selected.Count;
    }

    private static double Score(CombatCampaignResult result)
    {
        if (result.Invalid)
        {
            return -10_000d;
        }
        var hpRatio = result.FinalState.MaxHp <= 0
            ? 0d
            : result.FinalState.CurrentHp / (double)result.FinalState.MaxHp;
        return (result.FinalBossVictory ? 10_000d : 0d)
               + result.CompletedBattles * 100d
               + hpRatio * 10d;
    }

    private sealed class FoundationTelemetryTracker
    {
        private readonly CombatCampaignFoundationTrainingRequest request;
        private readonly int effectiveParallelism;
        private readonly int requestedCampaigns;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly object workerGate = new();
        private readonly HashSet<int> observedWorkerThreads = new();
        private readonly Dictionary<long, int> activeCampaignDepths = new();
        private readonly int[] completedDepthBuckets = new int[5];
        private readonly int initialGen0 = GC.CollectionCount(0);
        private readonly int initialGen1 = GC.CollectionCount(1);
        private readonly int initialGen2 = GC.CollectionCount(2);
        private int activeCampaigns;
        private int peakConcurrentCampaigns;
        private int completedCampaigns;
        private int completedBattles;
        private int maximumCompletedBattleDepth;
        private long completedCampaignDepthTotal;
        private int completedCampaignDepthCount;
        private long policyDecisions;
        private long searchSimulations;
        private long searchNodes;
        private int searchEarlyStops;
        private int ruleTerminalOverrides;
        private int certifiedLoops;
        private int sustainableControlLoops;
        private int fakeLoops;
        private int blockedLoops;
        private long nextCampaignWorkId;
        private long lastReportMilliseconds = -1000L;
        private int modelIteration;
        private int modelEpoch;
        private int modelTotalEpochs;
        private int modelCompletedFrames;
        private int modelTotalFrames;
        private double modelEpochsPerSecond;
        private double modelValidationLoss;
        private double modelBestValidationLoss;
        private int modelBestEpoch;
        private int modelStaleEpochs;
        private bool modelEarlyStopped;
        private double phaseEstimatedRemainingSeconds;

        public FoundationTelemetryTracker(
            CombatCampaignFoundationTrainingRequest request,
            int effectiveParallelism,
            int requestedCampaigns,
            CombatCampaignFoundationTelemetry? initial = null,
            int initialCompletedCampaigns = 0)
        {
            this.request = request;
            this.effectiveParallelism = effectiveParallelism;
            this.requestedCampaigns = requestedCampaigns;
            completedCampaigns = Math.Max(
                initialCompletedCampaigns,
                initial?.CompletedCampaigns ?? 0);
            completedBattles = Math.Max(0, initial?.CompletedBattles ?? 0);
            maximumCompletedBattleDepth = Math.Max(
                0,
                initial?.MaximumCompletedBattleDepth ?? 0);
            if (initial != null)
            {
                completedDepthBuckets[0] = initial.Depth1To5Campaigns;
                completedDepthBuckets[1] = initial.Depth6To10Campaigns;
                completedDepthBuckets[2] = initial.Depth11To20Campaigns;
                completedDepthBuckets[3] = initial.Depth21To30Campaigns;
                completedDepthBuckets[4] = initial.Depth31To37Campaigns;
                completedCampaignDepthCount = completedDepthBuckets.Sum();
                completedCampaignDepthTotal = (long)Math.Round(
                    initial.ProjectedBattleDepth
                    * completedCampaignDepthCount);
                policyDecisions = Math.Max(0L, initial.PolicyDecisions);
                searchSimulations = Math.Max(0L, initial.SearchSimulations);
                searchNodes = Math.Max(0L, initial.SearchNodes);
                searchEarlyStops = Math.Max(0, initial.SearchEarlyStops);
                ruleTerminalOverrides = Math.Max(
                    0,
                    initial.RuleTerminalOverrides);
                certifiedLoops = Math.Max(0, initial.CertifiedLoops);
                sustainableControlLoops = Math.Max(
                    0,
                    initial.SustainableControlLoops);
                fakeLoops = Math.Max(0, initial.FakeLoops);
                blockedLoops = Math.Max(0, initial.BlockedLoops);
                peakConcurrentCampaigns = Math.Max(
                    0,
                    initial.PeakConcurrentCampaigns);
            }
        }

        public CombatCampaignFoundationTelemetry Current(string stage)
        {
            return Snapshot(stage);
        }

        public void ModelTrainingProgress(
            int iteration,
            int totalIterations,
            CombatPolicyValueTrainingProgress progress)
        {
            if (progress == null)
            {
                return;
            }
            lock (workerGate)
            {
                modelIteration = Math.Max(0, iteration);
                modelEpoch = Math.Max(0, progress.Epoch);
                modelTotalEpochs = Math.Max(0, progress.TotalEpochs);
                modelCompletedFrames = Math.Max(
                    0,
                    progress.CompletedFrames);
                modelTotalFrames = Math.Max(0, progress.TotalFrames);
                modelEpochsPerSecond = Math.Max(
                    0d,
                    progress.EpochsPerSecond);
                if (progress.ValidationLoss > 0d
                    || string.Equals(
                        progress.Stage,
                        "completed",
                        StringComparison.Ordinal)
                    || string.Equals(
                        progress.Stage,
                        "early-stopped",
                        StringComparison.Ordinal))
                {
                    modelValidationLoss = progress.ValidationLoss;
                }
                modelBestValidationLoss = progress.BestValidationLoss;
                modelBestEpoch = Math.Max(0, progress.BestEpoch);
                modelStaleEpochs = Math.Max(0, progress.StaleEpochs);
                modelEarlyStopped = progress.EarlyStopped;
                phaseEstimatedRemainingSeconds = Math.Max(
                    0d,
                    progress.EstimatedRemainingSeconds);
            }
            Report(
                "model-training:"
                + Math.Max(1, iteration)
                + ":"
                + (progress.Stage ?? "training"),
                force: true);
        }

        public long EnterCampaign(string stage)
        {
            var workId = Interlocked.Increment(ref nextCampaignWorkId);
            lock (workerGate)
            {
                observedWorkerThreads.Add(Thread.CurrentThread.ManagedThreadId);
                activeCampaignDepths[workId] = 0;
            }
            var active = Interlocked.Increment(ref activeCampaigns);
            UpdateMaximum(ref peakConcurrentCampaigns, active);
            Report(stage, force: active == Volatile.Read(ref peakConcurrentCampaigns));
            return workId;
        }

        public void ExitCampaign(
            long workId,
            CombatCampaignResult? result,
            string stage)
        {
            lock (workerGate)
            {
                activeCampaignDepths.Remove(workId);
                if (result != null
                    && !stage.StartsWith(
                        "preflight",
                        StringComparison.Ordinal))
                {
                    var depth = Math.Max(0, result.CompletedBattles);
                    completedDepthBuckets[DepthBucket(depth)]++;
                    completedCampaignDepthTotal += depth;
                    completedCampaignDepthCount++;
                    maximumCompletedBattleDepth = Math.Max(
                        maximumCompletedBattleDepth,
                        depth);
                }
            }
            Interlocked.Decrement(ref activeCampaigns);
            Report(stage);
        }

        public void BattleCompleted(
            long workId,
            int depth,
            CombatSimulationResult battle,
            string stage)
        {
            lock (workerGate)
            {
                activeCampaignDepths[workId] = Math.Max(0, depth);
            }
            Interlocked.Increment(ref completedBattles);
            if (battle?.Metrics != null)
            {
                Interlocked.Add(
                    ref policyDecisions,
                    Math.Max(0, battle.Metrics.PolicyDecisions));
                Interlocked.Add(
                    ref searchSimulations,
                    Math.Max(0L, battle.Metrics.SearchSimulations));
                Interlocked.Add(
                    ref searchNodes,
                    Math.Max(0L, battle.Metrics.SearchNodes));
                Interlocked.Add(
                    ref searchEarlyStops,
                    Math.Max(0, battle.Metrics.SearchEarlyStops));
                Interlocked.Add(
                    ref ruleTerminalOverrides,
                    Math.Max(0, battle.Metrics.RuleTerminalOverrides));
                Interlocked.Add(
                    ref certifiedLoops,
                    Math.Max(0, battle.Metrics.CertifiedLoops));
                Interlocked.Add(
                    ref sustainableControlLoops,
                    Math.Max(0, battle.Metrics.SustainableControlLoops));
                Interlocked.Add(
                    ref fakeLoops,
                    Math.Max(0, battle.Metrics.FakeLoops));
                Interlocked.Add(
                    ref blockedLoops,
                    Math.Max(0, battle.Metrics.BlockedLoops));
            }
            Report(stage);
        }

        public void CampaignCompleted(
            int completed,
            CombatCampaignResult campaign,
            string stage)
        {
            Volatile.Write(ref completedCampaigns, completed);
            Report(stage, force: true);
        }

        public void ReportStage(string stage)
        {
            Report(stage, force: true);
        }

        public void ApplyTo(CombatCampaignFoundationTrainingResult result)
        {
            var snapshot = Snapshot("completed");
            result.EffectiveParallelism = snapshot.EffectiveParallelism;
            result.PeakConcurrentCampaigns = snapshot.PeakConcurrentCampaigns;
            result.ObservedWorkerThreads = snapshot.ObservedWorkerThreads;
            result.CompletedBattles = snapshot.CompletedBattles;
            result.MaximumCompletedBattleDepth =
                snapshot.MaximumCompletedBattleDepth;
            result.Depth1To5Campaigns = snapshot.Depth1To5Campaigns;
            result.Depth6To10Campaigns = snapshot.Depth6To10Campaigns;
            result.Depth11To20Campaigns = snapshot.Depth11To20Campaigns;
            result.Depth21To30Campaigns = snapshot.Depth21To30Campaigns;
            result.Depth31To37Campaigns = snapshot.Depth31To37Campaigns;
            result.ProjectedBattleDepth = snapshot.ProjectedBattleDepth;
            result.EstimatedRemainingSeconds =
                snapshot.EstimatedRemainingSeconds;
            result.PolicyDecisions = snapshot.PolicyDecisions;
            result.SearchSimulations = snapshot.SearchSimulations;
            result.SearchNodes = snapshot.SearchNodes;
            result.SearchEarlyStops = snapshot.SearchEarlyStops;
            result.RuleTerminalOverrides = snapshot.RuleTerminalOverrides;
            result.CertifiedLoops = snapshot.CertifiedLoops;
            result.SustainableControlLoops =
                snapshot.SustainableControlLoops;
            result.FakeLoops = snapshot.FakeLoops;
            result.BlockedLoops = snapshot.BlockedLoops;
            result.ModelCompletedEpochs = snapshot.ModelEpoch;
            result.ModelConfiguredEpochs = snapshot.ModelTotalEpochs;
            result.ModelBestEpoch = snapshot.ModelBestEpoch;
            result.ModelEarlyStopped = snapshot.ModelEarlyStopped;
            result.ModelBestValidationLoss =
                snapshot.ModelBestValidationLoss;
            result.ElapsedSeconds = snapshot.ElapsedSeconds;
            result.Gen0Collections = snapshot.Gen0Collections;
            result.Gen1Collections = snapshot.Gen1Collections;
            result.Gen2Collections = snapshot.Gen2Collections;
        }

        private void Report(string stage, bool force = false)
        {
            var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            if (!force)
            {
                var previous = Volatile.Read(ref lastReportMilliseconds);
                if (elapsedMilliseconds - previous < 1000L
                    || Interlocked.CompareExchange(
                        ref lastReportMilliseconds,
                        elapsedMilliseconds,
                        previous) != previous)
                {
                    return;
                }
            }
            else
            {
                Interlocked.Exchange(
                    ref lastReportMilliseconds,
                    elapsedMilliseconds);
            }

            try
            {
                request.Telemetry?.Invoke(Snapshot(stage));
            }
            catch
            {
                // Diagnostics must never abort formal training.
            }
        }

        private CombatCampaignFoundationTelemetry Snapshot(string stage)
        {
            int observedThreads;
            int activeMaximumDepth;
            int maximumDepth;
            int[] depthBuckets;
            long completedDepthTotal;
            int completedDepthCount;
            int activeDepthCount;
            int snapshotModelIteration;
            int snapshotModelEpoch;
            int snapshotModelTotalEpochs;
            int snapshotModelCompletedFrames;
            int snapshotModelTotalFrames;
            double snapshotModelEpochsPerSecond;
            double snapshotModelValidationLoss;
            double snapshotModelBestValidationLoss;
            int snapshotModelBestEpoch;
            int snapshotModelStaleEpochs;
            bool snapshotModelEarlyStopped;
            double snapshotPhaseRemainingSeconds;
            lock (workerGate)
            {
                observedThreads = observedWorkerThreads.Count;
                activeDepthCount = activeCampaignDepths.Count;
                activeMaximumDepth = activeCampaignDepths.Count == 0
                    ? 0
                    : activeCampaignDepths.Values.Max();
                maximumDepth = maximumCompletedBattleDepth;
                depthBuckets = (int[])completedDepthBuckets.Clone();
                completedDepthTotal = completedCampaignDepthTotal;
                completedDepthCount = completedCampaignDepthCount;
                snapshotModelIteration = modelIteration;
                snapshotModelEpoch = modelEpoch;
                snapshotModelTotalEpochs = modelTotalEpochs;
                snapshotModelCompletedFrames = modelCompletedFrames;
                snapshotModelTotalFrames = modelTotalFrames;
                snapshotModelEpochsPerSecond = modelEpochsPerSecond;
                snapshotModelValidationLoss = modelValidationLoss;
                snapshotModelBestValidationLoss = modelBestValidationLoss;
                snapshotModelBestEpoch = modelBestEpoch;
                snapshotModelStaleEpochs = modelStaleEpochs;
                snapshotModelEarlyStopped = modelEarlyStopped;
                snapshotPhaseRemainingSeconds =
                    phaseEstimatedRemainingSeconds;
            }
            var elapsedSeconds = Math.Max(0.001d, stopwatch.Elapsed.TotalSeconds);
            var campaigns = Volatile.Read(ref completedCampaigns);
            var battles = Volatile.Read(ref completedBattles);
            var battleRate = battles / elapsedSeconds;
            var averageCompletedDepth = completedDepthCount <= 0
                ? 0d
                : completedDepthTotal / (double)completedDepthCount;
            var projectedDepth = Math.Max(
                1d,
                Math.Min(
                    37d,
                    Math.Max(averageCompletedDepth, activeMaximumDepth)));
            var remainingCampaigns = Math.Max(0, requestedCampaigns - campaigns);
            var remainingBattleWork = Math.Max(
                0d,
                remainingCampaigns * projectedDepth
                - activeDepthCount);
            var simulationCount = Volatile.Read(ref searchSimulations);
            var battleEstimatedRemainingSeconds = battleRate <= 0d
                ? 0d
                : remainingBattleWork / battleRate;
            var phase = ResolvePhase(stage);
            return new CombatCampaignFoundationTelemetry
            {
                Stage = stage ?? "",
                Phase = phase,
                Iteration = snapshotModelIteration,
                TotalIterations = Math.Max(1, request.Iterations),
                EffectiveParallelism = effectiveParallelism,
                ActiveCampaigns = Math.Max(0, Volatile.Read(ref activeCampaigns)),
                PeakConcurrentCampaigns = Volatile.Read(ref peakConcurrentCampaigns),
                ObservedWorkerThreads = observedThreads,
                CompletedCampaigns = campaigns,
                RequestedCampaigns = requestedCampaigns,
                CompletedBattles = battles,
                MaximumCompletedBattleDepth = maximumDepth,
                MaximumActiveBattleDepth = activeMaximumDepth,
                Depth1To5Campaigns = depthBuckets[0],
                Depth6To10Campaigns = depthBuckets[1],
                Depth11To20Campaigns = depthBuckets[2],
                Depth21To30Campaigns = depthBuckets[3],
                Depth31To37Campaigns = depthBuckets[4],
                ProjectedBattleDepth = projectedDepth,
                EstimatedRemainingSeconds =
                    string.Equals(
                        phase,
                        "model-training",
                        StringComparison.Ordinal)
                    && snapshotPhaseRemainingSeconds > 0d
                        ? snapshotPhaseRemainingSeconds
                        : battleEstimatedRemainingSeconds,
                ModelEpoch = snapshotModelEpoch,
                ModelTotalEpochs = snapshotModelTotalEpochs,
                ModelCompletedFrames = snapshotModelCompletedFrames,
                ModelTotalFrames = snapshotModelTotalFrames,
                ModelEpochsPerSecond = snapshotModelEpochsPerSecond,
                ModelValidationLoss = snapshotModelValidationLoss,
                ModelBestValidationLoss = snapshotModelBestValidationLoss,
                ModelBestEpoch = snapshotModelBestEpoch,
                ModelStaleEpochs = snapshotModelStaleEpochs,
                ModelEarlyStopped = snapshotModelEarlyStopped,
                PhaseEstimatedRemainingSeconds =
                    snapshotPhaseRemainingSeconds,
                PolicyDecisions = Volatile.Read(ref policyDecisions),
                SearchSimulations = simulationCount,
                SearchNodes = Volatile.Read(ref searchNodes),
                SearchEarlyStops = Volatile.Read(ref searchEarlyStops),
                RuleTerminalOverrides =
                    Volatile.Read(ref ruleTerminalOverrides),
                CertifiedLoops = Volatile.Read(ref certifiedLoops),
                SustainableControlLoops =
                    Volatile.Read(ref sustainableControlLoops),
                FakeLoops = Volatile.Read(ref fakeLoops),
                BlockedLoops = Volatile.Read(ref blockedLoops),
                SearchSimulationsPerSecond = simulationCount / elapsedSeconds,
                ElapsedSeconds = elapsedSeconds,
                CampaignsPerSecond = campaigns / elapsedSeconds,
                BattlesPerSecond = battleRate,
                Gen0Collections = Math.Max(0, GC.CollectionCount(0) - initialGen0),
                Gen1Collections = Math.Max(0, GC.CollectionCount(1) - initialGen1),
                Gen2Collections = Math.Max(0, GC.CollectionCount(2) - initialGen2)
            };
        }

        private static string ResolvePhase(string stage)
        {
            var value = stage ?? "";
            if (value.StartsWith(
                    "model-training",
                    StringComparison.Ordinal))
            {
                return "model-training";
            }
            if (value.StartsWith("training", StringComparison.Ordinal)
                || value.Contains("七层训练推演"))
            {
                return "self-play";
            }
            if (value.StartsWith("arena", StringComparison.Ordinal)
                || value.Contains("竞技场"))
            {
                return "arena";
            }
            if (value.StartsWith("validation", StringComparison.Ordinal)
                || value.Contains("验证"))
            {
                return "validation";
            }
            return value;
        }

        private static int DepthBucket(int depth)
        {
            if (depth <= 5) return 0;
            if (depth <= 10) return 1;
            if (depth <= 20) return 2;
            if (depth <= 30) return 3;
            return 4;
        }

        private static void UpdateMaximum(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref target,
                    value,
                    current);
                if (observed == current)
                {
                    return;
                }
                current = observed;
            }
        }
    }

    private sealed class FoundationTrainingCampaignRun
    {
        public CombatCampaignResult Campaign { get; set; } = new();

        public List<CombatEpisode> Episodes { get; set; } = new();

        public bool HardSeed { get; set; }
    }

    private sealed class FoundationArenaPair
    {
        public CombatCampaignResult Champion { get; set; } = new();

        public CombatCampaignResult Candidate { get; set; } = new();
    }

    private static void ValidateSeedPartitions(
        ulong trainingSeedStart,
        ulong arenaSeedStart,
        ulong validationSeedStart,
        int iterations,
        int trainingCampaigns,
        int arenaPerDifficulty,
        int normalValidationCampaigns,
        int advancedValidationCampaigns)
    {
        var trainingEnd = trainingSeedStart
                          + (ulong)(iterations * trainingCampaigns);
        var arenaEnd = arenaSeedStart
                       + (ulong)(iterations * arenaPerDifficulty * 2);
        var validationEnd = validationSeedStart
                            + (ulong)(normalValidationCampaigns + advancedValidationCampaigns);
        var ranges = new[]
        {
            (Start: trainingSeedStart, End: trainingEnd, Name: "training"),
            (Start: arenaSeedStart, End: arenaEnd, Name: "arena"),
            (Start: validationSeedStart, End: validationEnd, Name: "validation")
        };
        for (var left = 0; left < ranges.Length; left++)
        {
            for (var right = left + 1; right < ranges.Length; right++)
            {
                if (ranges[left].Start < ranges[right].End
                    && ranges[right].Start < ranges[left].End)
                {
                    throw new ArgumentException(
                        "Foundation seed partitions overlap: "
                        + ranges[left].Name
                        + " / "
                        + ranges[right].Name);
                }
            }
        }
    }

    private sealed class RecordingCampaignPolicyFactory : ICombatSimulationPolicyFactory
    {
        private readonly CombatDecisionProfile profile;
        private readonly ICombatPolicyValueModel policyValue;
        private readonly string decisionProfile;
        private readonly double explorationProbability;
        private readonly double explorationTemperature;
        private readonly ulong campaignSeed;
        private readonly List<CombatEpisodeRecordingPolicy> policies = new();

        public RecordingCampaignPolicyFactory(
            CombatDecisionProfile profile,
            ICombatPolicyValueModel policyValue,
            string decisionProfile,
            double explorationProbability,
            double explorationTemperature,
            ulong campaignSeed)
        {
            this.profile = profile;
            this.policyValue = policyValue;
            this.decisionProfile = decisionProfile;
            this.explorationProbability = explorationProbability;
            this.explorationTemperature = explorationTemperature;
            this.campaignSeed = campaignSeed;
        }

        public string PolicyId => "aura-foundation-training:" + decisionProfile;

        public ICombatSimulationPolicy Create()
        {
            var policy = new CombatEpisodeRecordingPolicy(
                new CombatDecisionSimulationPolicy(
                    profile,
                    policyValueModel: policyValue,
                    exploration: new CombatSelfPlayExplorationOptions
                    {
                        Probability = explorationProbability,
                        Temperature = explorationTemperature,
                        RandomSeed = CombatFoundationSeedPlan.ToRandomSeed(
                            campaignSeed
                            ^ (ulong)(policies.Count + 1))
                    }),
                decisionProfile);
            policies.Add(policy);
            return policy;
        }

        public List<CombatEpisode> Complete(CombatCampaignResult result)
        {
            if (policies.Count != result.Battles.Count)
            {
                throw new InvalidOperationException(
                    "Campaign policy/battle count mismatch: "
                    + policies.Count
                    + "/"
                    + result.Battles.Count);
            }
            var journeyRunId = result.CampaignId
                               + ":"
                               + result.DifficultyId
                               + ":"
                               + result.WorldSeed;
            return policies.Select((policy, index) =>
            {
                var episode = policy.Complete(result.Battles[index]);
                episode.JourneyRunId = journeyRunId;
                episode.JourneyBattleIndex = index;
                return episode;
            }).ToList();
        }
    }
}
