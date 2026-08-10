using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AuraCombatAi.Shared;

public static class CombatPolicyValueProtocol
{
    public const string EpisodeProtocol = "aura.combat-ai.episode.v8";

    public const string PreviousEpisodeProtocol = "aura.combat-ai.episode.v7";

    public const int FeatureSchemaVersion = 30;

    public const string TrainingSemanticsVersion =
        "decision-input-transition-partitioned-v5-actual-execution-v30";
}

public static class CombatPolicyValueFrameStratificationProtocol
{
    public const string Version = "frame-strata-v8-action-aligned-strategy-quota";

    public const double MinimumWeight = 0.50d;

    public const double DefaultMaximumWeight = 3.0d;
}

public static class CombatEpisodeTransitionProtocol
{
    public const string SameTurn = "same-turn";

    public const string CrossTurn = "cross-turn";

    public static void Link(
        CombatEpisodeFrame previous,
        CombatEpisodeFrame next)
    {
        if (previous == null) throw new ArgumentNullException(nameof(previous));
        if (next == null) throw new ArgumentNullException(nameof(next));

        previous.TransitionKnown = true;
        previous.TransitionNextTurn = next.Turn;
        previous.TransitionNextActionSequence = next.ActionSequence;
        previous.TransitionNextDecisionSequence = next.DecisionSequence;
        previous.TransitionNextStateFingerprint = next.StateFingerprint;
        previous.TransitionActionSequenceDelta =
            next.ActionSequence - previous.ActionSequence;
        previous.TransitionCrossedTurnBoundary = next.Turn != previous.Turn;
        previous.TransitionKind = previous.TransitionCrossedTurnBoundary
            ? CrossTurn
            : SameTurn;
        var decisionDelta = next.DecisionSequence - previous.DecisionSequence;
        previous.TransitionSpan = decisionDelta > 0
            ? (int)Math.Min(int.MaxValue, decisionDelta)
            : 0;

        previous.TransitionInvalidReason = InvalidReason(previous, next);
        previous.TransitionValid =
            string.IsNullOrWhiteSpace(previous.TransitionInvalidReason);
        if (previous.TransitionValid)
        {
            previous.SetCompactTransitionNextStateFeatures(
                next.CompactStateFeatures ?? CombatCompactFeatureVector.Empty);
        }
        else
        {
            previous.SetCompactTransitionNextStateFeatures(
                CombatCompactFeatureVector.Empty);
        }
    }

    public static void Normalize(CombatEpisode episode)
    {
        if (episode == null) throw new ArgumentNullException(nameof(episode));
        var frames = episode.Frames ?? new List<CombatEpisodeFrame>();
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            frame.DecisionSequence = index + 1L;
            frame.TerminalKnown = true;
            frame.Terminal = index == frames.Count - 1;
        }
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            if (index + 1 < frames.Count)
            {
                Link(frame, frames[index + 1]);
            }
            else
            {
                frame.TransitionKnown = false;
                frame.TransitionValid = false;
                frame.TransitionInvalidReason = "";
                frame.TransitionSpan = 0;
                frame.TransitionActionSequenceDelta = 0;
                frame.TransitionKind = "";
                frame.TransitionNextTurn = 0;
                frame.TransitionNextActionSequence = 0;
                frame.TransitionNextDecisionSequence = 0;
                frame.TransitionNextStateFingerprint = "";
                frame.SetCompactTransitionNextStateFeatures(
                    CombatCompactFeatureVector.Empty);
            }
        }
    }

    private static string InvalidReason(
        CombatEpisodeFrame previous,
        CombatEpisodeFrame next)
    {
        if (previous.BattleSessionId == 0 || next.BattleSessionId == 0)
        {
            return "battle-session-missing";
        }
        if (previous.BattleSessionId != next.BattleSessionId)
        {
            return "battle-session-changed";
        }
        if (next.DecisionSequence <= previous.DecisionSequence)
        {
            return "decision-sequence-not-advanced";
        }
        if (next.Turn < previous.Turn)
        {
            return "turn-regressed";
        }
        if (next.Turn == previous.Turn
            && next.ActionSequence < previous.ActionSequence)
        {
            return "action-sequence-regressed";
        }
        if (next.CompactStateFeatures?.Count <= 0
            && next.StateFeatures.Count <= 0)
        {
            return "next-state-missing";
        }
        return "";
    }
}

public static class CombatPolicyValueEpisodeMigration
{
    public static bool CanUpgrade(CombatEpisode? episode)
    {
        return episode != null
               && string.Equals(
                   episode.ModelProtocol,
                   CombatPolicyValueProtocol.PreviousEpisodeProtocol,
                   StringComparison.Ordinal)
               && episode.FeatureSchemaVersion
               == CombatPolicyValueProtocol.FeatureSchemaVersion;
    }

    public static bool UpgradeInPlace(CombatEpisode? episode)
    {
        if (!CanUpgrade(episode))
        {
            return false;
        }
        NormalizeSemanticsInPlace(episode!);
        episode!.ModelProtocol = CombatPolicyValueProtocol.EpisodeProtocol;
        return true;
    }

    public static void NormalizeSemanticsInPlace(CombatEpisode episode)
    {
        if (episode == null) throw new ArgumentNullException(nameof(episode));
        foreach (var frame in episode.Frames ?? new List<CombatEpisodeFrame>())
        {
            var supervision = CombatPolicyValueBatchTrainer
                .StrategicFrameSupervisionForExecutedAction(frame);
            frame.StrategyApplicabilityKnown = supervision.Known;
            frame.StrategyApplicableLabels =
                supervision.ApplicableLabels.ToList();
            frame.StrategyLabelsKnown = supervision.Known
                                        && supervision.ApplicableLabels.Count > 0;
            frame.StrategyLabels = supervision.PositiveLabels.ToList();
            frame.StrategyLabelSource = "v6-migration:"
                                        + supervision.Source;
            frame.StrategyPhase = CombatPolicyValueBatchTrainer
                .StrategicPhaseForFrame(frame);
        }
        CombatEpisodeTransitionProtocol.Normalize(episode);
    }
}

public sealed class CombatEpisode
{
    public string ModelProtocol { get; set; } =
        CombatPolicyValueProtocol.EpisodeProtocol;

    public int FeatureSchemaVersion { get; set; } =
        CombatPolicyValueProtocol.FeatureSchemaVersion;

    public string EpisodeId { get; set; } = "";

    public string ScenarioId { get; set; } = "";

    public string JourneyRunId { get; set; } = "";

    public long BattleSessionId { get; set; }

    public int JourneyBattleIndex { get; set; } = -1;

    public CombatCampaignEpisodeMetadata Campaign { get; set; } = new();

    public ulong Seed { get; set; }

    public string RulesetHash { get; set; } = "";

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string BaseModelId { get; set; } = "";

    public List<string> ActiveAdapterIds { get; set; } = new();

    public string PolicyId { get; set; } = "";

    public string DecisionProfile { get; set; } = "balanced";

    public List<CombatEpisodeFrame> Frames { get; set; } = new();

    public string Outcome { get; set; } = "unknown";

    public int Turns { get; set; }

    public int FinalPlayerHp { get; set; }

    public int FinalPlayerMaxHp { get; set; }

    public int DamageTaken { get; set; }

    public double SemanticCoverage { get; set; }

    public bool Authoritative { get; set; }

    public string Provenance { get; set; } = "offline-simulation";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CombatCampaignEpisodeMetadata
{
    public ulong WorldSeed { get; set; }

    public string DifficultyId { get; set; } = "";

    public bool FinalBossVictory { get; set; }

    public bool ReachedFinalBoss { get; set; }

    public int CampaignCompletedBattles { get; set; }

    public int CampaignTotalBattles { get; set; }

    public int FailureBattleIndex { get; set; } = -1;

    public string TerminalScenarioId { get; set; } = "";

    public string OutcomeClass { get; set; } = "unknown";

    public bool TerminalSnapshotKnown { get; set; }

    public int TerminalBattleIndex { get; set; } = -1;

    public int TerminalPlayerHp { get; set; }

    public int TerminalPlayerMaxHp { get; set; }

    public int TerminalDoomPower { get; set; }

    public string CurriculumStage { get; set; } = "";

    public int TrainingIteration { get; set; }

    public bool IntegrityValid { get; set; } = true;

    public double TrainingWeight { get; set; } = 1d;
}

public sealed class CombatEpisodeFrame
{
    private Dictionary<string, double>? stateFeatures;
    private Dictionary<string, double>? transitionNextStateFeatures;
    private CombatObservationEnvelope? observation;

    public int Turn { get; set; }

    public long ActionSequence { get; set; }

    /// <summary>
    /// Monotonic decision clock owned by the episode recorder. Unlike the
    /// simulation action sequence, this advances for EndTurn and no-effect
    /// decisions as well.
    /// </summary>
    public long DecisionSequence { get; set; }

    public long BattleSessionId { get; set; }

    public string StateFingerprint { get; set; } = "";

    public Dictionary<string, double> StateFeatures
    {
        get
        {
            if (stateFeatures == null)
            {
                if (CompactStateFeatures != null)
                {
                    CombatEpisodeStorageDiagnostics.StateDictionaryMaterialized();
                    return CompactStateFeatures.Materialize();
                }
                stateFeatures = new Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase);
                CombatEpisodeStorageDiagnostics.StateDictionaryMaterialized();
            }
            return stateFeatures;
        }
        set
        {
            stateFeatures = value ?? new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            CompactStateFeatures = null;
        }
    }

    public CombatObservationEnvelope Observation
    {
        get => observation ??= new CombatObservationEnvelope();
        set => observation = value;
    }

    public bool HasObservation => observation != null;

    public bool TransitionKnown { get; set; }

    public bool TransitionValid { get; set; }

    public string TransitionInvalidReason { get; set; } = "";

    public int TransitionSpan { get; set; }

    public long TransitionActionSequenceDelta { get; set; }

    public string TransitionKind { get; set; } = "";

    public bool TransitionCrossedTurnBoundary { get; set; }

    public int TransitionNextTurn { get; set; }

    public long TransitionNextActionSequence { get; set; }

    public long TransitionNextDecisionSequence { get; set; }

    public string TransitionNextStateFingerprint { get; set; } = "";

    public bool TerminalKnown { get; set; }

    public bool Terminal { get; set; }

    public bool StrategyLabelsKnown { get; set; }

    public bool StrategyApplicabilityKnown { get; set; }

    public List<string> StrategyApplicableLabels { get; set; } = new();

    public List<string> StrategyLabels { get; set; } = new();

    public string StrategyLabelSource { get; set; } = "";

    public int StrategyPhase { get; set; } = -1;

    public Dictionary<string, double> TransitionNextStateFeatures
    {
        get
        {
            if (transitionNextStateFeatures == null)
            {
                transitionNextStateFeatures =
                    CompactTransitionNextStateFeatures?.Materialize()
                    ?? new Dictionary<string, double>(
                        StringComparer.OrdinalIgnoreCase);
            }
            return transitionNextStateFeatures;
        }
        set
        {
            transitionNextStateFeatures = value
                ?? new Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase);
            CompactTransitionNextStateFeatures = null;
        }
    }

    internal CombatCompactFeatureVector? CompactStateFeatures { get; private set; }

    internal CombatCompactFeatureVector? CompactTransitionNextStateFeatures {
        get;
        private set;
    }

    internal bool HasMaterializedStateFeatures => stateFeatures != null;

    internal bool HasMaterializedTransitionNextStateFeatures =>
        transitionNextStateFeatures != null;

    internal void ReleaseTransientStorage()
    {
        observation = null;
        if (CompactStateFeatures != null)
        {
            stateFeatures = null;
        }
        if (CompactTransitionNextStateFeatures != null)
        {
            transitionNextStateFeatures = null;
        }
        foreach (var candidate in Candidates ?? new List<CombatEpisodeCandidate>())
        {
            candidate?.ReleaseTransientStorage();
        }
    }

    public int[]? CompactStateFeatureTokenIds
    {
        get => CompactStateFeatures?.TokenIds;
        set
        {
            if (value == null)
            {
                return;
            }
            pendingCompactStateTokenIds = value;
            RestoreCompactStateFeatures();
        }
    }

    public float[]? CompactStateFeatureValues
    {
        get => CompactStateFeatures?.Values;
        set
        {
            if (value == null)
            {
                return;
            }
            pendingCompactStateValues = value;
            RestoreCompactStateFeatures();
        }
    }

    private int[]? pendingCompactStateTokenIds;
    private float[]? pendingCompactStateValues;
    private int[]? pendingCompactTransitionNextStateTokenIds;
    private float[]? pendingCompactTransitionNextStateValues;

    public int[]? CompactTransitionNextStateFeatureTokenIds
    {
        get => CompactTransitionNextStateFeatures?.TokenIds;
        set
        {
            if (value == null)
            {
                return;
            }
            pendingCompactTransitionNextStateTokenIds = value;
            RestoreCompactTransitionNextStateFeatures();
        }
    }

    public float[]? CompactTransitionNextStateFeatureValues
    {
        get => CompactTransitionNextStateFeatures?.Values;
        set
        {
            if (value == null)
            {
                return;
            }
            pendingCompactTransitionNextStateValues = value;
            RestoreCompactTransitionNextStateFeatures();
        }
    }

    internal void SetCompactStateFeatures(CombatCompactFeatureVector features)
    {
        CompactStateFeatures = features ?? CombatCompactFeatureVector.Empty;
        stateFeatures = null;
        CombatEpisodeStorageDiagnostics.CompactStateVector(
            CompactStateFeatures.Count);
    }

    internal void SetCompactTransitionNextStateFeatures(
        CombatCompactFeatureVector features)
    {
        CompactTransitionNextStateFeatures =
            features ?? CombatCompactFeatureVector.Empty;
        transitionNextStateFeatures = null;
    }

    private void RestoreCompactStateFeatures()
    {
        if (pendingCompactStateTokenIds == null
            || pendingCompactStateValues == null)
        {
            return;
        }
        SetCompactStateFeatures(new CombatCompactFeatureVector(
            pendingCompactStateTokenIds,
            pendingCompactStateValues));
        pendingCompactStateTokenIds = null;
        pendingCompactStateValues = null;
    }

    private void RestoreCompactTransitionNextStateFeatures()
    {
        if (pendingCompactTransitionNextStateTokenIds == null
            || pendingCompactTransitionNextStateValues == null)
        {
            return;
        }
        SetCompactTransitionNextStateFeatures(new CombatCompactFeatureVector(
            pendingCompactTransitionNextStateTokenIds,
            pendingCompactTransitionNextStateValues));
        pendingCompactTransitionNextStateTokenIds = null;
        pendingCompactTransitionNextStateValues = null;
    }

    internal bool TryGetStateFeature(string key, out double value)
    {
        if (stateFeatures != null)
        {
            return stateFeatures.TryGetValue(key, out value);
        }
        if (CompactStateFeatures != null)
        {
            return CompactStateFeatures.TryGetValue(key, out value);
        }
        value = 0d;
        return false;
    }

    internal IEnumerable<KeyValuePair<string, double>> EnumerateStateFeatures()
    {
        if (stateFeatures != null)
        {
            foreach (var pair in stateFeatures)
            {
                yield return pair;
            }
            yield break;
        }
        if (CompactStateFeatures == null)
        {
            yield break;
        }
        for (var index = 0; index < CompactStateFeatures.Count; index++)
        {
            if (CombatFeatureTokenRegistry.TryResolve(
                    CompactStateFeatures.TokenIds[index],
                    out var key))
            {
                yield return new KeyValuePair<string, double>(
                    key,
                    CompactStateFeatures.Values[index]);
            }
        }
    }

    public List<CombatEpisodeCandidate> Candidates { get; set; } = new();

    public string ExecutedCandidateId { get; set; } = "";

    public double LongTermReturn { get; set; }

    public double WinTarget { get; set; }

    public double DeathTarget { get; set; }

    public double RemainingHpRatioTarget { get; set; }

    public double RemainingTurnsTarget { get; set; }

    public double TrainingWeight { get; set; } = 1d;
}

public sealed class CombatEpisodeCandidate
{
    private Dictionary<string, double>? features;

    public string CandidateId { get; set; } = "";

    public string SourceId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public bool Legal { get; set; }

    public int SearchVisits { get; set; }

    public double SearchPrior { get; set; }

    public double SearchValue { get; set; }

    public double SearchDeathRisk { get; set; }

    public double SearchMeanReturn { get; set; }

    public double SearchReturnStandardError { get; set; }

    public double SearchLowerTailMean { get; set; }

    public double BaseRuleScore { get; set; }

    public double RawResidualScore { get; set; }

    public double ResidualApplicability { get; set; }

    public double AppliedResidualScore { get; set; }

    public double RuleScore { get; set; }

    public List<double> SearchReturnQuantiles { get; set; } = new();

    public double TransformerTeacherProbability { get; set; } = -1d;

    public Dictionary<string, double> Features
    {
        get
        {
            if (features == null)
            {
                if (CompactFeatures != null)
                {
                    CombatEpisodeStorageDiagnostics
                        .CandidateDictionaryMaterialized();
                    return CompactFeatures.Materialize();
                }
                features = new Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase);
                CombatEpisodeStorageDiagnostics
                    .CandidateDictionaryMaterialized();
            }
            return features;
        }
        set
        {
            features = value ?? new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
            CompactFeatures = null;
        }
    }

    internal CombatCompactFeatureVector? CompactFeatures { get; private set; }

    internal bool HasMaterializedFeatures => features != null;

    internal void ReleaseTransientStorage()
    {
        if (CompactFeatures != null)
        {
            features = null;
        }
    }

    public int[]? CompactFeatureTokenIds
    {
        get => CompactFeatures?.TokenIds;
        set
        {
            if (value == null)
            {
                return;
            }
            pendingCompactTokenIds = value;
            RestoreCompactFeatures();
        }
    }

    public float[]? CompactFeatureValues
    {
        get => CompactFeatures?.Values;
        set
        {
            if (value == null)
            {
                return;
            }
            pendingCompactValues = value;
            RestoreCompactFeatures();
        }
    }

    private int[]? pendingCompactTokenIds;
    private float[]? pendingCompactValues;

    internal void SetCompactFeatures(CombatCompactFeatureVector compact)
    {
        CompactFeatures = compact ?? CombatCompactFeatureVector.Empty;
        features = null;
        CombatEpisodeStorageDiagnostics.CompactCandidateVector(
            CompactFeatures.Count);
    }

    private void RestoreCompactFeatures()
    {
        if (pendingCompactTokenIds == null || pendingCompactValues == null)
        {
            return;
        }
        SetCompactFeatures(new CombatCompactFeatureVector(
            pendingCompactTokenIds,
            pendingCompactValues));
        pendingCompactTokenIds = null;
        pendingCompactValues = null;
    }

    internal bool TryGetFeature(string key, out double value)
    {
        if (features != null)
        {
            return features.TryGetValue(key, out value);
        }
        if (CompactFeatures != null)
        {
            return CompactFeatures.TryGetValue(key, out value);
        }
        value = 0d;
        return false;
    }

    internal IEnumerable<KeyValuePair<string, double>> EnumerateFeatures()
    {
        if (features != null)
        {
            foreach (var pair in features)
            {
                yield return pair;
            }
            yield break;
        }
        if (CompactFeatures == null)
        {
            yield break;
        }
        for (var index = 0; index < CompactFeatures.Count; index++)
        {
            if (CombatFeatureTokenRegistry.TryResolve(
                    CompactFeatures.TokenIds[index],
                    out var key))
            {
                yield return new KeyValuePair<string, double>(
                    key,
                    CompactFeatures.Values[index]);
            }
        }
    }
}

public sealed class CombatPolicyValueTrainingOptions
{
    public int Epochs { get; set; } = 40;

    public double LearningRate { get; set; } = 0.00625d;

    public double L2 { get; set; } = 0.0015d;

    public int StateDimensions { get; set; } = 2048;

    public int ActionDimensions { get; set; } = 1024;

    public int HiddenDimensions { get; set; } = 512;

    public int ActionQuantileCount { get; set; } = 16;

    public double ActionQuantileLossWeight { get; set; } = 0.50d;

    public int MinimumSearchVisitsForActionQuantiles { get; set; } = 8;

    public string FeatureEncodingMode { get; set; } = "partitioned-v4";

    public int RandomSeed { get; set; } = 20260724;

    public int MinimumEpisodes { get; set; } = 8;

    public bool RequireAuthoritativeEpisodes { get; set; } = true;

    public int BatchSize { get; set; } = 64;

    public int GradientShardCount { get; set; } = 12;

    public int MaximumDegreeOfParallelism { get; set; } = 1;

    public int MinimumEpochs { get; set; } = 8;

    public int EarlyStoppingPatience { get; set; } = 8;

    public double EarlyStoppingMinimumDelta { get; set; } = 0.0002d;

    public int ReplayEpisodeLimit { get; set; } = 8000;

    public int ReplayFrameLimit { get; set; } = 384000;

    public long ReplayEstimatedBytesLimit { get; set; } =
        3L * 1024L * 1024L * 1024L;

    public int RetainedModelCandidates { get; set; } = 3;

    public bool EnableFrameStratification { get; set; } = true;

    public bool EnableEndTurnSpecialization { get; set; } = true;

    public double EndTurnFrameWeight { get; set; } = 1d;

    public double MaximumUnsafeEndTurnFrameShare { get; set; } = 0.20d;

    public double UnsafeEndTurnRiskAuxiliaryShare { get; set; } = 0.10d;

    public int MinimumValidationRunGroups { get; set; } = 16;

    public int MinimumTestRunGroups { get; set; } = 16;

    public double PolicyTargetTemperature { get; set; } = 1.25d;

    public double MaximumPolicyTargetProbability { get; set; } = 0.90d;

    public double TransformerDistillationWeight { get; set; }

    public double MaximumFrameStratumWeight { get; set; } =
        CombatPolicyValueFrameStratificationProtocol.DefaultMaximumWeight;

    public int MaximumFramesPerEpisode { get; set; } = 96;

    public CombatPolicyValueTrainingOptions Normalized()
    {
        var normalizedBatchSize = Math.Max(8, Math.Min(512, BatchSize));
        var normalizedParallelism = Math.Max(
            1,
            Math.Min(Environment.ProcessorCount, MaximumDegreeOfParallelism));
        var normalizedGradientShards = GradientShardCount <= 0
            ? Math.Max(
                1,
                Math.Min(
                    32,
                    Math.Min(
                        normalizedParallelism,
                        (normalizedBatchSize + 1) / 2)))
            : Math.Max(1, Math.Min(32, GradientShardCount));
        return new CombatPolicyValueTrainingOptions
        {
            Epochs = Math.Max(5, Math.Min(500, Epochs)),
            LearningRate = Clamp(LearningRate, 0.0001d, 0.1d, 0.00625d),
            L2 = Clamp(L2, 0d, 0.05d, 0.0015d),
            StateDimensions = Math.Max(16, Math.Min(2048, StateDimensions)),
            ActionDimensions = Math.Max(16, Math.Min(2048, ActionDimensions)),
            HiddenDimensions = Math.Max(8, Math.Min(1024, HiddenDimensions)),
            ActionQuantileCount = Math.Max(4, Math.Min(64, ActionQuantileCount)),
            ActionQuantileLossWeight = Clamp(
                ActionQuantileLossWeight,
                0d,
                2d,
                0.50d),
            MinimumSearchVisitsForActionQuantiles = Math.Max(
                1,
                Math.Min(128, MinimumSearchVisitsForActionQuantiles)),
            FeatureEncodingMode = "partitioned-v4",
            RandomSeed = RandomSeed,
            MinimumEpisodes = Math.Max(2, Math.Min(10000, MinimumEpisodes)),
            RequireAuthoritativeEpisodes = RequireAuthoritativeEpisodes,
            BatchSize = normalizedBatchSize,
            GradientShardCount = normalizedGradientShards,
            MaximumDegreeOfParallelism = normalizedParallelism,
            MinimumEpochs = Math.Max(1, Math.Min(Epochs, MinimumEpochs)),
            EarlyStoppingPatience = Math.Max(
                1,
                Math.Min(50, EarlyStoppingPatience)),
            EarlyStoppingMinimumDelta = Clamp(
                EarlyStoppingMinimumDelta,
                0.0000001d,
                0.1d,
                0.0002d),
            ReplayEpisodeLimit = Math.Max(
                64,
                Math.Min(20000, ReplayEpisodeLimit)),
            ReplayFrameLimit = Math.Max(
                4096,
                Math.Min(2_000_000, ReplayFrameLimit)),
            ReplayEstimatedBytesLimit = Math.Max(
                256L * 1024L * 1024L,
                Math.Min(
                    16L * 1024L * 1024L * 1024L,
                    ReplayEstimatedBytesLimit)),
            RetainedModelCandidates = Math.Max(
                1,
                Math.Min(5, RetainedModelCandidates)),
            EnableFrameStratification = EnableFrameStratification,
            EnableEndTurnSpecialization = EnableEndTurnSpecialization,
            EndTurnFrameWeight = Clamp(
                EndTurnFrameWeight,
                0.25d,
                1d,
                1d),
            MaximumUnsafeEndTurnFrameShare = Clamp(
                MaximumUnsafeEndTurnFrameShare,
                0.10d,
                0.80d,
                0.20d),
            UnsafeEndTurnRiskAuxiliaryShare = Clamp(
                UnsafeEndTurnRiskAuxiliaryShare,
                0d,
                0.40d,
                0.10d),
            MinimumValidationRunGroups = Math.Max(
                1,
                Math.Min(256, MinimumValidationRunGroups)),
            MinimumTestRunGroups = Math.Max(
                1,
                Math.Min(256, MinimumTestRunGroups)),
            PolicyTargetTemperature = Clamp(
                PolicyTargetTemperature,
                1d,
                3d,
                1.25d),
            MaximumPolicyTargetProbability = Clamp(
                MaximumPolicyTargetProbability,
                0.55d,
                1d,
                0.90d),
            TransformerDistillationWeight = Clamp(
                TransformerDistillationWeight,
                0d,
                0.75d,
                0d),
            MaximumFrameStratumWeight = Clamp(
                MaximumFrameStratumWeight,
                1d,
                5d,
                CombatPolicyValueFrameStratificationProtocol
                    .DefaultMaximumWeight),
            MaximumFramesPerEpisode = Math.Max(
                8,
                Math.Min(512, MaximumFramesPerEpisode))
        };
    }

    private static double Clamp(double value, double minimum, double maximum, double fallback)
    {
        var finite = double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        return Math.Max(minimum, Math.Min(maximum, finite));
    }
}

public sealed class CombatPolicyValueTrainingResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public int EpisodeCount { get; set; }

    public int FrameCount { get; set; }

    public int DroppedFramesByEpisodeCap { get; set; }

    public int TrainingFrameCount { get; set; }

    public int DroppedUnsafeEndTurnFrames { get; set; }

    public int UnsafeEndTurnPolicyFrames { get; set; }

    public int UnsafeEndTurnRiskAuxiliaryFrames { get; set; }

    public int TransformerDistillationTrainingFrames { get; set; }

    public int TransformerDistillationValidationFrames { get; set; }

    public int DroppedPolicyIntegrityFrames { get; set; }

    public CombatPolicyValueNetworkDefinition? Model { get; set; }

    public List<CombatPolicyValueModelCandidate> CandidateModels { get; set; } =
        new();

    public int CompletedEpochs { get; set; }

    public int BestEpoch { get; set; }

    public bool EarlyStopped { get; set; }

    public double ElapsedSeconds { get; set; }

    public double TestLoss { get; set; }

    public string FrameStratificationProtocol { get; set; } = "";

    public Dictionary<string, int> FrameStrata { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> EncodedStrategyFrames { get; set; } =
        new(StringComparer.Ordinal);

    public double MinimumFrameWeight { get; set; } = 1d;

    public double MaximumFrameWeight { get; set; } = 1d;

    public int EndTurnDecisionFrames { get; set; }

    public int UnsafeEndTurnFrames { get; set; }

    public double MeanPolicyTargetMaximum { get; set; }

    public CombatPolicyValueMetricSnapshot TrainingMetrics { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot ValidationMetrics { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot BaselineValidationMetrics {
        get;
        set;
    } = new();

    public CombatPolicyValueMetricSnapshot TestMetrics { get; set; } =
        new();

    public List<CombatPolicyValueEpochMetrics> EpochHistory { get; set; } =
        new();
}

public sealed class CombatPolicyValueModelCandidate
{
    public int Epoch { get; set; }

    public double ValidationLoss { get; set; }

    public CombatPolicyValueMetricSnapshot TrainingMetrics { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot ValidationMetrics { get; set; } =
        new();

    public CombatPolicyValueMetricSnapshot TestMetrics { get; set; } =
        new();

    public CombatPolicyValueNetworkDefinition Model { get; set; } = new();
}

public sealed class CombatPolicyValueTrainingProgress
{
    public string Stage { get; set; } = "";

    public int Epoch { get; set; }

    public int TotalEpochs { get; set; }

    public int CompletedFrames { get; set; }

    public int TotalFrames { get; set; }

    public double EpochsPerSecond { get; set; }

    public double EstimatedRemainingSeconds { get; set; }

    public double ValidationLoss { get; set; }

    public double BestValidationLoss { get; set; }

    public int BestEpoch { get; set; }

    public int StaleEpochs { get; set; }

    public bool EarlyStopped { get; set; }

    public CombatPolicyValueEpochMetrics? Metrics { get; set; }
}

public sealed class CombatPolicyValueMetricSnapshot
{
    public int FrameCount { get; set; }

    public int RunCount { get; set; }

    public double CompositeLoss { get; set; }

    public double CompositeLossStandardError { get; set; }

    public double CompositeLossCiLower { get; set; }

    public double CompositeLossCiUpper { get; set; }

    public double PolicyAccuracy { get; set; }

    public double CriticalPolicyAccuracy { get; set; }

    public double PolicyCrossEntropy { get; set; }

    public double ValueMae { get; set; }

    public double Brier { get; set; }

    public double DeathBrier { get; set; }

    public double HpMae { get; set; }

    public double TurnHuber { get; set; }

    public double ActionQuantilePinball { get; set; }

    public double ActionQuantileMae { get; set; }

    public int ActionQuantileLabelCount { get; set; }
}

public sealed class CombatPolicyValueEpochMetrics
{
    public int Iteration { get; set; }

    public int Epoch { get; set; }

    public bool Calibrated { get; set; }

    public string EventKind { get; set; } = "epoch";

    public string TrainingMeasurement { get; set; } = "full-evaluation";

    public double ElapsedSeconds { get; set; }

    public double LearningRate { get; set; }

    public double GradientNorm { get; set; }

    public int GradientClipCount { get; set; }

    public bool Improved { get; set; }

    public int BestEpoch { get; set; }

    public double BestValidationLoss { get; set; }

    public int StaleEpochs { get; set; }

    public bool EarlyStopped { get; set; }

    public string TrainingSplitHash { get; set; } = "";

    public string ValidationSplitHash { get; set; } = "";

    public CombatPolicyValueMetricSnapshot Training { get; set; } = new();

    public CombatPolicyValueMetricSnapshot Validation { get; set; } = new();
}

public sealed class CombatPolicyValueTrainingResumeState
{
    public int CompletedEpochs { get; set; }

    public CombatPolicyValueNetworkDefinition? Model { get; set; }

    public CombatPolicyValueNetworkDefinition? BestModel { get; set; }

    public double BestValidationLoss { get; set; } = double.MaxValue;

    public int BestEpoch { get; set; }

    public int BestValidationEpoch { get; set; }

    public int DeploymentSelectedEpoch { get; set; }

    public int StaleEpochs { get; set; }

    public CombatPolicyValueOptimizerState? Optimizer { get; set; }

    public List<CombatPolicyValueModelCandidate> TopModels { get; set; } =
        new();

    public List<CombatPolicyValueEpochMetrics> EpochHistory { get; set; } =
        new();
}

public sealed class CombatPolicyValueOptimizerState
{
    public long Step { get; set; }

    public double[] FirstMoment { get; set; } = Array.Empty<double>();

    public double[] SecondMoment { get; set; } = Array.Empty<double>();
}

public sealed class CombatPolicyValueTrainingSession
{
    public CombatPolicyValueTrainingResumeState? Resume { get; set; }

    public Action<CombatPolicyValueTrainingProgress>? Progress { get; set; }

    public Action<CombatPolicyValueEpochMetrics>? EpochCompleted { get; set; }

    public Action<CombatPolicyValueTrainingResumeState>? Checkpoint { get; set; }
}

public static class CombatPolicyValueTrainer
{
    public static CombatPolicyValueMetricSnapshot EvaluateFrozenAnchor(
        IEnumerable<CombatEpisode> source,
        CombatPolicyValueNetworkDefinition model,
        CombatPolicyValueTrainingOptions? trainingOptions = null,
        CancellationToken cancellationToken = default)
    {
        return CombatPolicyValueBatchTrainer.EvaluateFrozenAnchor(
            source,
            model,
            trainingOptions,
            cancellationToken);
    }

    public static CombatPolicyValueTrainingResult Train(
        IEnumerable<CombatEpisode> source,
        string decisionProfile,
        CombatPolicyValueTrainingOptions? trainingOptions = null)
    {
        return Train(
            source,
            decisionProfile,
            trainingOptions,
            CancellationToken.None);
    }

    public static CombatPolicyValueTrainingResult Train(
        IEnumerable<CombatEpisode> source,
        string decisionProfile,
        CombatPolicyValueTrainingOptions? trainingOptions,
        CancellationToken cancellationToken)
    {
        return CombatPolicyValueBatchTrainer.Train(
            source,
            decisionProfile,
            trainingOptions,
            cancellationToken,
            session: null);
    }

    public static CombatPolicyValueTrainingResult Train(
        IEnumerable<CombatEpisode> source,
        string decisionProfile,
        CombatPolicyValueTrainingOptions? trainingOptions,
        CancellationToken cancellationToken,
        CombatPolicyValueTrainingSession? session)
    {
        return CombatPolicyValueBatchTrainer.Train(
            source,
            decisionProfile,
            trainingOptions,
            cancellationToken,
            session);
    }

    private static CombatPolicyValueNetworkDefinition Initialize(
        string profile,
        CombatPolicyValueTrainingOptions options)
    {
        var random = new Random(options.RandomSeed);
        return new CombatPolicyValueNetworkDefinition
        {
            ModelId = "aura-combat-policy-value-" + DateTime.UtcNow.Ticks,
            DecisionProfile = profile,
            StateDimensions = options.StateDimensions,
            ActionDimensions = options.ActionDimensions,
            HiddenDimensions = options.HiddenDimensions,
            FeatureEncodingMode = options.FeatureEncodingMode,
            StateWeights = RandomWeights(
                random,
                options.StateDimensions * options.HiddenDimensions,
                options.StateDimensions),
            StateBias = new double[options.HiddenDimensions],
            ActionWeights = RandomWeights(
                random,
                options.ActionDimensions * options.HiddenDimensions,
                options.ActionDimensions),
            ActionBias = new double[options.HiddenDimensions],
            PolicyWeights = RandomWeights(random, options.HiddenDimensions, options.HiddenDimensions),
            ValueWeights = RandomWeights(random, options.HiddenDimensions, options.HiddenDimensions),
            WinWeights = RandomWeights(random, options.HiddenDimensions, options.HiddenDimensions),
            RiskWeights = RandomWeights(random, options.HiddenDimensions, options.HiddenDimensions),
            HpWeights = RandomWeights(random, options.HiddenDimensions, options.HiddenDimensions),
            TurnWeights = RandomWeights(random, options.HiddenDimensions, options.HiddenDimensions)
        };
    }

    private static double[] RandomWeights(Random random, int count, int fanIn)
    {
        var scale = Math.Sqrt(2d / Math.Max(1, fanIn)) * 0.25d;
        return Enumerable.Range(0, count)
            .Select(_ => (random.NextDouble() * 2d - 1d) * scale)
            .ToArray();
    }

    private static void TrainFrame(
        CombatPolicyValueNetworkDefinition model,
        CombatEpisodeFrame frame,
        double learningRate,
        double l2)
    {
        var legal = (frame.Candidates ?? new List<CombatEpisodeCandidate>())
            .Where(candidate => candidate.Legal)
            .ToList();
        if (legal.Count == 0)
        {
            return;
        }
        var state = CombatPolicyValueEncoding.EncodeState(
            frame.StateFeatures,
            model.StateDimensions,
            model.FeatureEncodingMode);
        var statePre = Dense(state, model.StateWeights, model.StateBias, model.HiddenDimensions);
        var stateHidden = statePre.Select(Math.Tanh).ToArray();
        var actionVectors = new List<double[]>(legal.Count);
        var actionPre = new List<double[]>(legal.Count);
        var actionHidden = new List<double[]>(legal.Count);
        var logits = new double[legal.Count];
        for (var i = 0; i < legal.Count; i++)
        {
            var vector = CombatPolicyValueEncoding.EncodeCandidate(
                new CombatPolicyValueCandidate
                {
                    CandidateId = legal[i].CandidateId,
                    SourceId = legal[i].SourceId,
                    Features = legal[i].Features
                },
                model.ActionDimensions,
                model.FeatureEncodingMode);
            var pre = Dense(vector, model.ActionWeights, model.ActionBias, model.HiddenDimensions);
            var hidden = pre.Select(Math.Tanh).ToArray();
            actionVectors.Add(vector);
            actionPre.Add(pre);
            actionHidden.Add(hidden);
            logits[i] = Interaction(stateHidden, hidden, model.PolicyWeights) + model.PolicyBias;
        }
        var probabilities = Softmax(logits);
        var targets = PolicyTargets(legal, frame.ExecutedCandidateId);
        var stateGradient = new double[model.HiddenDimensions];
        for (var i = 0; i < legal.Count; i++)
        {
            var gradient = probabilities[i] - targets[i];
            model.PolicyBias -= learningRate * gradient;
            var actionGradient = new double[model.HiddenDimensions];
            for (var hidden = 0; hidden < model.HiddenDimensions; hidden++)
            {
                var oldWeight = model.PolicyWeights[hidden];
                model.PolicyWeights[hidden] -= learningRate
                                              * (gradient
                                                 * stateHidden[hidden]
                                                 * actionHidden[i][hidden]
                                                 + l2 * oldWeight);
                stateGradient[hidden] += gradient * oldWeight * actionHidden[i][hidden];
                actionGradient[hidden] += gradient * oldWeight * stateHidden[hidden];
            }
            BackpropDense(
                actionVectors[i],
                actionHidden[i],
                actionGradient,
                model.ActionWeights,
                model.ActionBias,
                learningRate,
                l2);
        }

        var value = Dot(stateHidden, model.ValueWeights) + model.ValueBias;
        model.ValueBias = AddLinearGradient(
            stateHidden,
            model.ValueWeights,
            model.ValueBias,
            value - Clamp(frame.LongTermReturn, -1d, 1d),
            stateGradient,
            learningRate,
            l2);
        var win = Sigmoid(Dot(stateHidden, model.WinWeights) + model.WinBias);
        model.WinBias = AddLinearGradient(
            stateHidden,
            model.WinWeights,
            model.WinBias,
            win - Clamp(frame.WinTarget, 0d, 1d),
            stateGradient,
            learningRate,
            l2);
        var risk = Sigmoid(Dot(stateHidden, model.RiskWeights) + model.RiskBias);
        model.RiskBias = AddLinearGradient(
            stateHidden,
            model.RiskWeights,
            model.RiskBias,
            risk - Clamp(frame.DeathTarget, 0d, 1d),
            stateGradient,
            learningRate,
            l2);
        var hp = Sigmoid(Dot(stateHidden, model.HpWeights) + model.HpBias);
        model.HpBias = AddLinearGradient(
            stateHidden,
            model.HpWeights,
            model.HpBias,
            (hp - Clamp(frame.RemainingHpRatioTarget, 0d, 1d)) * hp * (1d - hp),
            stateGradient,
            learningRate,
            l2);
        var turnRaw = Dot(stateHidden, model.TurnWeights) + model.TurnBias;
        var turn = SoftPlus(turnRaw);
        model.TurnBias = AddLinearGradient(
            stateHidden,
            model.TurnWeights,
            model.TurnBias,
            (turn - Math.Max(0d, frame.RemainingTurnsTarget)) * Sigmoid(turnRaw) * 0.1d,
            stateGradient,
            learningRate,
            l2);
        BackpropDense(
            state,
            stateHidden,
            stateGradient,
            model.StateWeights,
            model.StateBias,
            learningRate,
            l2);
    }

    private static double AddLinearGradient(
        IReadOnlyList<double> hidden,
        double[] weights,
        double bias,
        double outputGradient,
        double[] hiddenGradient,
        double learningRate,
        double l2)
    {
        bias -= learningRate * outputGradient;
        for (var i = 0; i < hidden.Count; i++)
        {
            var old = weights[i];
            weights[i] -= learningRate * (outputGradient * hidden[i] + l2 * old);
            hiddenGradient[i] += outputGradient * old;
        }
        return bias;
    }

    private static void BackpropDense(
        IReadOnlyList<double> input,
        IReadOnlyList<double> hidden,
        IReadOnlyList<double> hiddenGradient,
        double[] weights,
        double[] bias,
        double learningRate,
        double l2)
    {
        for (var output = 0; output < hidden.Count; output++)
        {
            var gradient = hiddenGradient[output] * (1d - hidden[output] * hidden[output]);
            bias[output] -= learningRate * gradient;
            var offset = output * input.Count;
            for (var inputIndex = 0; inputIndex < input.Count; inputIndex++)
            {
                var index = offset + inputIndex;
                weights[index] -= learningRate
                                  * (gradient * input[inputIndex] + l2 * weights[index]);
            }
        }
    }

    private static Metrics Evaluate(
        CombatPolicyValueNetworkDefinition definition,
        IReadOnlyList<CombatEpisode> episodes,
        CancellationToken cancellationToken)
    {
        var model = new ManagedCombatPolicyValueModel(definition);
        var count = 0;
        var correct = 0;
        var valueError = 0d;
        var brier = 0d;
        foreach (var frame in episodes.SelectMany(episode => episode.Frames))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var legal = frame.Candidates.Where(candidate => candidate.Legal).ToList();
            if (legal.Count == 0)
            {
                continue;
            }
            var input = new CombatPolicyValueInput
            {
                StateFeatures = frame.StateFeatures,
                Candidates = legal.Select(candidate => new CombatPolicyValueCandidate
                {
                    CandidateId = candidate.CandidateId,
                    SourceId = candidate.SourceId,
                    Features = candidate.Features
                }).ToList()
            };
            var prediction = model.Evaluate(input);
            var predicted = legal[0].CandidateId;
            var predictedLogit = double.NegativeInfinity;
            foreach (var candidate in legal)
            {
                if (prediction.TryGetPolicyLogit(
                        candidate.CandidateId,
                        out var candidateLogit)
                    && candidateLogit > predictedLogit)
                {
                    predicted = candidate.CandidateId;
                    predictedLogit = candidateLogit;
                }
            }
            var target = legal
                .OrderByDescending(candidate => candidate.SearchVisits)
                .ThenByDescending(candidate =>
                    string.Equals(
                        candidate.CandidateId,
                        frame.ExecutedCandidateId,
                        StringComparison.Ordinal))
                .First()
                .CandidateId;
            if (string.Equals(predicted, target, StringComparison.Ordinal))
            {
                correct++;
            }
            valueError += Math.Abs(prediction.ExpectedReturn - frame.LongTermReturn);
            var winDifference = prediction.WinProbability - frame.WinTarget;
            brier += winDifference * winDifference;
            count++;
        }
        return new Metrics
        {
            PolicyAccuracy = count == 0 ? 0d : (double)correct / count,
            ValueMae = count == 0 ? 0d : valueError / count,
            Brier = count == 0 ? 0d : brier / count
        };
    }

    private static double[] PolicyTargets(
        IReadOnlyList<CombatEpisodeCandidate> candidates,
        string executedCandidateId)
    {
        var executed = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(
                    candidates[i].CandidateId,
                    executedCandidateId,
                    StringComparison.Ordinal))
            {
                executed = i;
                break;
            }
        }
        var visits = candidates.Sum(candidate => Math.Max(0, candidate.SearchVisits));
        if (visits > 0)
        {
            if (executed >= 0 && candidates[executed].SearchVisits <= 0)
            {
                return Enumerable.Range(0, candidates.Count)
                    .Select(index => index == executed ? 1d : 0d)
                    .ToArray();
            }
            return candidates
                .Select(candidate => (double)Math.Max(0, candidate.SearchVisits) / visits)
                .ToArray();
        }
        if (executed < 0)
        {
            return Enumerable.Repeat(1d / candidates.Count, candidates.Count).ToArray();
        }
        return Enumerable.Range(0, candidates.Count)
            .Select(index => index == executed ? 1d : 0d)
            .ToArray();
    }

    private static double[] Dense(
        IReadOnlyList<double> input,
        IReadOnlyList<double> weights,
        IReadOnlyList<double> bias,
        int outputs)
    {
        var result = new double[outputs];
        for (var output = 0; output < outputs; output++)
        {
            var value = bias[output];
            var offset = output * input.Count;
            for (var inputIndex = 0; inputIndex < input.Count; inputIndex++)
            {
                value += input[inputIndex] * weights[offset + inputIndex];
            }
            result[output] = value;
        }
        return result;
    }

    private static double Interaction(
        IReadOnlyList<double> state,
        IReadOnlyList<double> action,
        IReadOnlyList<double> weights)
    {
        var value = 0d;
        for (var i = 0; i < state.Count; i++)
        {
            value += state[i] * action[i] * weights[i];
        }
        return value;
    }

    private static double[] Softmax(IReadOnlyList<double> logits)
    {
        var maximum = logits.Max();
        var result = logits.Select(logit => Math.Exp(Clamp(logit - maximum, -30d, 30d))).ToArray();
        var total = Math.Max(0.0000001d, result.Sum());
        for (var i = 0; i < result.Length; i++)
        {
            result[i] /= total;
        }
        return result;
    }

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var value = 0d;
        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            value += left[i] * right[i];
        }
        return value;
    }

    private static double Sigmoid(double value)
    {
        value = Clamp(value, -30d, 30d);
        return 1d / (1d + Math.Exp(-value));
    }

    private static double SoftPlus(double value)
    {
        value = Clamp(value, -30d, 30d);
        return Math.Log(1d + Math.Exp(value));
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Max(minimum, Math.Min(maximum, value));
    }

    private static string NormalizeProfile(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "aggressive" => "aggressive",
            "defensive" => "defensive",
            _ => "balanced"
        };
    }

    private sealed class Metrics
    {
        public double PolicyAccuracy { get; set; }

        public double ValueMae { get; set; }

        public double Brier { get; set; }
    }
}
