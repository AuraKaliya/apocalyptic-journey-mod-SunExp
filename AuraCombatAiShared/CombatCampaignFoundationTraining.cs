using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatCampaignFoundationTrainingRequest
{
    public string DecisionProfile { get; set; } = "balanced";

    public int Iterations { get; set; } = 8;

    public int TrainingCampaignsPerIteration { get; set; } = 64;

    public int ArenaCampaignsPerDifficulty { get; set; } = 32;

    public int ValidationCampaignsPerDifficulty { get; set; } = 10;

    public int NormalValidationCampaigns { get; set; } = 200;

    public int AdvancedValidationCampaigns { get; set; } = 500;

    public ulong TrainingSeedStart { get; set; } = 10_000UL;

    public ulong ArenaSeedStart { get; set; } = 1_000_000UL;

    public ulong ValidationSeedStart { get; set; } = 2_000_000UL;

    public CombatDecisionProfile Profile { get; set; } = new();

    public CombatPolicyValueTrainingOptions Training { get; set; } = new();

    public CombatCampaignDefinition TrainingCampaign { get; set; } = new();

    public CombatCampaignDefinition ValidationCampaign { get; set; } = new();

    public Action<int, int, string>? Progress { get; set; }
}

public sealed class CombatCampaignFoundationIteration
{
    public int Iteration { get; set; }

    public int ReplayEpisodes { get; set; }

    public string CandidateModelId { get; set; } = "";

    public double ChampionArenaScore { get; set; }

    public double CandidateArenaScore { get; set; }

    public double ChampionNormalWinRate { get; set; }

    public double CandidateNormalWinRate { get; set; }

    public double ChampionAdvancedWinRate { get; set; }

    public double CandidateAdvancedWinRate { get; set; }

    public int InvalidCandidateCampaigns { get; set; }

    public bool Promoted { get; set; }

    public bool CurriculumCheckpointAccepted { get; set; }

    public string PromotionKind { get; set; } = "rejected";
}

public sealed class CombatCampaignFoundationValidation
{
    public int CampaignsPerDifficulty { get; set; }

    public int NormalCampaigns { get; set; }

    public int AdvancedCampaigns { get; set; }

    public int NormalVictories { get; set; }

    public int AdvancedVictories { get; set; }

    public int InvalidCampaigns { get; set; }

    public double NormalWinRate { get; set; }

    public double AdvancedWinRate { get; set; }

    public bool Passed { get; set; }
}

public sealed class CombatCampaignFoundationTrainingResult
{
    public bool Success { get; set; }

    public bool AcceptancePassed { get; set; }

    public string Message { get; set; } = "";

    public CombatPolicyValueNetworkDefinition? Champion { get; set; }

    public List<CombatEpisode> Replay { get; set; } = new();

    public List<CombatCampaignFoundationIteration> Iterations { get; set; } = new();

    public CombatCampaignFoundationValidation Validation { get; set; } = new();

    public List<CombatCampaignResult> ValidationRuns { get; set; } = new();
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
        var legacyValidationPerDifficulty = Math.Max(
            5,
            Math.Min(1000, request.ValidationCampaignsPerDifficulty));
        var normalValidationCampaigns = request.NormalValidationCampaigns > 0
            ? Math.Max(5, Math.Min(1000, request.NormalValidationCampaigns))
            : legacyValidationPerDifficulty;
        var advancedValidationCampaigns = request.AdvancedValidationCampaigns > 0
            ? Math.Max(5, Math.Min(1000, request.AdvancedValidationCampaigns))
            : legacyValidationPerDifficulty;
        ValidateSeedPartitions(
            request,
            iterations,
            trainingCampaigns,
            arenaPerDifficulty,
            normalValidationCampaigns,
            advancedValidationCampaigns);

        var result = new CombatCampaignFoundationTrainingResult
        {
            Champion = initialChampion
        };
        var foundationTrainingOptions = request.Training.Normalized();
        foundationTrainingOptions.RequireAuthoritativeEpisodes = true;
        ICombatPolicyValueModel championModel = initialChampion == null
            ? NullCombatPolicyValueModel.Instance
            : new ManagedCombatPolicyValueModel(initialChampion);
        var trainingSeed = request.TrainingSeedStart;
        var arenaSeed = request.ArenaSeedStart;
        var completedCampaigns = 0;
        var totalCampaigns = iterations
                             * (trainingCampaigns + arenaPerDifficulty * 4)
                             + normalValidationCampaigns
                             + advancedValidationCampaigns;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var campaignIndex = 0; campaignIndex < trainingCampaigns; campaignIndex++)
            {
                var difficulty = (campaignIndex + iteration) % 2 == 0
                    ? "normal"
                    : "advanced";
                var factory = new RecordingCampaignPolicyFactory(
                    request.Profile,
                    championModel,
                    request.DecisionProfile);
                var campaign = RunCampaign(
                    request.TrainingCampaign,
                    difficulty,
                    trainingSeed++,
                    ruleset,
                    factory,
                    cancellationToken);
                var episodes = factory.Complete(campaign);
                ApplyCampaignReturn(episodes, campaign);
                result.Replay.AddRange(episodes);
                completedCampaigns++;
                request.Progress?.Invoke(
                    completedCampaigns,
                    totalCampaigns,
                    "第 " + (iteration + 1) + " 轮：七层训练推演");
            }

            var replayWindow = result.Replay.Count <= 20_000
                ? result.Replay
                : result.Replay.Skip(result.Replay.Count - 20_000).ToList();
            var trained = CombatPolicyValueTrainer.Train(
                replayWindow,
                request.DecisionProfile,
                foundationTrainingOptions,
                cancellationToken);
            if (!trained.Success || trained.Model == null)
            {
                result.Message = "第 "
                                 + (iteration + 1)
                                 + " 轮底模训练失败："
                                 + trained.Message;
                return result;
            }

            var candidateModel = new ManagedCombatPolicyValueModel(trained.Model);
            var championArena = new List<CombatCampaignResult>();
            var candidateArena = new List<CombatCampaignResult>();
            foreach (var difficulty in new[] { "normal", "advanced" })
            {
                for (var arenaIndex = 0; arenaIndex < arenaPerDifficulty; arenaIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var seed = arenaSeed++;
                    championArena.Add(RunCampaign(
                        request.TrainingCampaign,
                        difficulty,
                        seed,
                        ruleset,
                        new CombatDecisionSimulationPolicyFactory(
                            request.Profile,
                            policyValueModel: championModel),
                        cancellationToken));
                    completedCampaigns++;
                    candidateArena.Add(RunCampaign(
                        request.TrainingCampaign,
                        difficulty,
                        seed,
                        ruleset,
                        new CombatDecisionSimulationPolicyFactory(
                            request.Profile,
                            policyValueModel: candidateModel),
                        cancellationToken));
                    completedCampaigns++;
                    request.Progress?.Invoke(
                        completedCampaigns,
                        totalCampaigns,
                        "第 " + (iteration + 1) + " 轮：隔离种子竞技场");
                }
            }

            var invalid = candidateArena.Count(item => item.Invalid);
            var championScore = championArena.Average(Score);
            var candidateScore = candidateArena.Average(Score);
            var championNormal = WinRate(championArena, "normal");
            var candidateNormal = WinRate(candidateArena, "normal");
            var championAdvanced = WinRate(championArena, "advanced");
            var candidateAdvanced = WinRate(candidateArena, "advanced");
            var curriculumCheckpoint = invalid == 0
                                       && candidateNormal + 0.0000001d >= championNormal
                                       && candidateAdvanced + 0.0000001d >= championAdvanced
                                       && candidateScore + 0.0000001d >= championScore;
            var promoted = curriculumCheckpoint
                           && (candidateNormal > 0d || candidateAdvanced > 0d);
            result.Iterations.Add(new CombatCampaignFoundationIteration
            {
                Iteration = iteration + 1,
                ReplayEpisodes = result.Replay.Count,
                CandidateModelId = trained.Model.ModelId,
                ChampionArenaScore = championScore,
                CandidateArenaScore = candidateScore,
                ChampionNormalWinRate = championNormal,
                CandidateNormalWinRate = candidateNormal,
                ChampionAdvancedWinRate = championAdvanced,
                CandidateAdvancedWinRate = candidateAdvanced,
                InvalidCandidateCampaigns = invalid,
                Promoted = promoted,
                CurriculumCheckpointAccepted = curriculumCheckpoint,
                PromotionKind = promoted
                    ? "formal-champion"
                    : curriculumCheckpoint
                        ? "curriculum-checkpoint"
                        : "rejected"
            });
            if (curriculumCheckpoint)
            {
                championModel = candidateModel;
            }
            if (promoted)
            {
                result.Champion = trained.Model;
            }
        }

        if (result.Champion == null)
        {
            result.Message =
                "底模训练完成，但候选只达到课程检查点；0% 胜率模型不会晋升为正式底模。";
            return result;
        }

        foreach (var difficulty in new[] { "normal", "advanced" })
        {
            var validationCount = difficulty == "normal"
                ? normalValidationCampaigns
                : advancedValidationCampaigns;
            for (var index = 0; index < validationCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var seed = request.ValidationSeedStart
                           + (ulong)(difficulty == "advanced"
                               ? normalValidationCampaigns
                               : 0)
                           + (ulong)index;
                var validationRun = RunCampaign(
                    request.ValidationCampaign,
                    difficulty,
                    seed,
                    ruleset,
                    new CombatDecisionSimulationPolicyFactory(
                        request.Profile,
                        policyValueModel: championModel),
                    cancellationToken);
                for (var battleIndex = 0;
                     battleIndex < validationRun.Battles.Count - 1;
                     battleIndex++)
                {
                    validationRun.Battles[battleIndex].Events.Clear();
                }
                result.ValidationRuns.Add(validationRun);
                completedCampaigns++;
                request.Progress?.Invoke(
                    completedCampaigns,
                    totalCampaigns,
                    "最终隔离验证：" + difficulty);
            }
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
            NormalCampaigns = normalRuns.Count,
            AdvancedCampaigns = advancedRuns.Count,
            NormalVictories = normalRuns.Count(item => item.FinalBossVictory),
            AdvancedVictories = advancedRuns.Count(item => item.FinalBossVictory),
            InvalidCampaigns = result.ValidationRuns.Count(item => item.Invalid),
            NormalWinRate = normalRuns.Count == 0
                ? 0d
                : normalRuns.Count(item => item.FinalBossVictory) / (double)normalRuns.Count,
            AdvancedWinRate = advancedRuns.Count == 0
                ? 0d
                : advancedRuns.Count(item => item.FinalBossVictory) / (double)advancedRuns.Count
        };
        result.Validation.Passed = result.Validation.InvalidCampaigns == 0
                                   && result.Validation.NormalVictories
                                   == normalValidationCampaigns
                                   && result.Validation.AdvancedVictories
                                   >= (int)Math.Ceiling(advancedValidationCampaigns * 0.8d);
        result.AcceptancePassed = result.Validation.Passed;
        result.Success = true;
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
              + normalValidationCampaigns
              + "（要求全部通过），高级 "
              + result.Validation.AdvancedVictories
              + "/"
              + advancedValidationCampaigns
              + "（要求至少 "
              + (int)Math.Ceiling(advancedValidationCampaigns * 0.8d)
              + "）";
        return result;
    }

    private CombatCampaignResult RunCampaign(
        CombatCampaignDefinition campaign,
        string difficulty,
        ulong seed,
        CombatRuleset ruleset,
        ICombatSimulationPolicyFactory factory,
        CancellationToken cancellationToken)
    {
        return campaignRunner.Run(
            campaign,
            CombatCampaignWorldPlanner.Build(campaign, difficulty, seed),
            ruleset,
            factory,
            cancellationToken: cancellationToken);
    }

    private static void ApplyCampaignReturn(
        IReadOnlyList<CombatEpisode> episodes,
        CombatCampaignResult campaign)
    {
        var progress = Math.Max(0d, Math.Min(1d, campaign.CompletedBattles / 37d));
        var campaignReturn = campaign.FinalBossVictory ? 1d : -1d;
        var remainingBattles = episodes.Count;
        for (var episodeIndex = 0; episodeIndex < episodes.Count; episodeIndex++)
        {
            var episode = episodes[episodeIndex];
            var journeySignal = campaignReturn
                                * Math.Pow(0.995d, Math.Max(0, remainingBattles - episodeIndex - 1));
            foreach (var frame in episode.Frames)
            {
                frame.StateFeatures["journeyProgress"] = progress;
                frame.StateFeatures["finalBossVictory"] =
                    campaign.FinalBossVictory ? 1d : 0d;
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

    private static void ValidateSeedPartitions(
        CombatCampaignFoundationTrainingRequest request,
        int iterations,
        int trainingCampaigns,
        int arenaPerDifficulty,
        int normalValidationCampaigns,
        int advancedValidationCampaigns)
    {
        var trainingEnd = request.TrainingSeedStart
                          + (ulong)(iterations * trainingCampaigns);
        var arenaEnd = request.ArenaSeedStart
                       + (ulong)(iterations * arenaPerDifficulty * 2);
        var validationEnd = request.ValidationSeedStart
                            + (ulong)(normalValidationCampaigns + advancedValidationCampaigns);
        var ranges = new[]
        {
            (Start: request.TrainingSeedStart, End: trainingEnd, Name: "training"),
            (Start: request.ArenaSeedStart, End: arenaEnd, Name: "arena"),
            (Start: request.ValidationSeedStart, End: validationEnd, Name: "validation")
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
        private readonly List<CombatEpisodeRecordingPolicy> policies = new();

        public RecordingCampaignPolicyFactory(
            CombatDecisionProfile profile,
            ICombatPolicyValueModel policyValue,
            string decisionProfile)
        {
            this.profile = profile;
            this.policyValue = policyValue;
            this.decisionProfile = decisionProfile;
        }

        public string PolicyId => "aura-foundation-training:" + decisionProfile;

        public ICombatSimulationPolicy Create()
        {
            var policy = new CombatEpisodeRecordingPolicy(
                new CombatDecisionSimulationPolicy(
                    profile,
                    policyValueModel: policyValue),
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
            return policies.Select((policy, index) =>
                policy.Complete(result.Battles[index])).ToList();
        }
    }
}
