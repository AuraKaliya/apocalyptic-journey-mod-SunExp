using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AuraCombatAi.Shared;

public static class CombatTransformerTeacherBackendNames
{
    public const string Disabled = "disabled";
    public const string Auto = "auto";
    public const string Cpu = "cpu";
    public const string Cuda = "cuda";

    public static string Normalize(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            Auto => Auto,
            Cpu => Cpu,
            Cuda => Cuda,
            _ => Disabled
        };
    }
}

public static class CombatTransformerWorldModelProtocol
{
    public const string Model =
        "aura.combat-transformer-world-model.v4";

    public const string Report =
        "aura.combat-transformer-world-model-report.v4";

    public const string SparseDataset =
        "aura.combat-transformer-dataset.sparse-index-value.v3";
}

public static class CombatTransformerTeacherFailureKinds
{
    public const string None = "";
    public const string Configuration = "configuration";
    public const string Protocol = "protocol";
    public const string TransientResource = "transient-resource";
    public const string Process = "process";
}

public static class CombatTransformerTeacherFailureProtocol
{
    public static bool BlocksFormalModel(
        CombatTransformerTeacherReport? report)
    {
        return report?.Requested == true
               && report.FormalModelBlocked
               && !report.Success;
    }

    public static void Mark(
        CombatTransformerTeacherReport report,
        string failureKind,
        bool retryable,
        bool formalModelBlocked,
        int processExitCode = 0)
    {
        if (report == null) throw new ArgumentNullException(nameof(report));
        report.FailureKind = failureKind ?? CombatTransformerTeacherFailureKinds.None;
        report.RetryableFailure = retryable;
        report.FormalModelBlocked = formalModelBlocked;
        report.ProcessExitCode = processExitCode;
    }
}

public static class CombatTransformerTeacherCorpusProtocol
{
    public const string Version =
        "transformer-teacher-corpus-v5-backlog-applicability-teacher-split";

    public const string CollectingMaturity = "collecting";

    public const string BootstrapMaturity = "bootstrap";

    public const string ProvisionalMaturity = "provisional";

    public const string MatureMaturity = "mature";

    public static bool ShouldUseIncrementalExport(
        int existingFrames,
        int sourceFrameUpperBound)
    {
        return Math.Max(0, existingFrames) > 0
               && Math.Max(0, sourceFrameUpperBound) > 0;
    }

    public static bool ShouldUseIncrementalColdStartExport(
        int existingFrames,
        int sourceFrameUpperBound,
        int minimumTrainingFrames)
    {
        var existing = Math.Max(0, existingFrames);
        var source = Math.Max(0, sourceFrameUpperBound);
        var minimum = Math.Max(1, minimumTrainingFrames);
        return existing > 0
               && existing < minimum
               && (long)existing + source < minimum;
    }

    public static string CorpusMaturity(int frames, int minimumTrainingFrames)
    {
        var count = Math.Max(0, frames);
        var minimum = Math.Max(1, minimumTrainingFrames);
        if (count < minimum)
        {
            return CollectingMaturity;
        }
        if (count < minimum * 2L)
        {
            return BootstrapMaturity;
        }
        return count < minimum * 4L
            ? ProvisionalMaturity
            : MatureMaturity;
    }

    public static double DistillationWeightCap(
        int frames,
        int minimumTrainingFrames)
    {
        return CorpusMaturity(frames, minimumTrainingFrames) switch
        {
            CollectingMaturity => 0d,
            BootstrapMaturity => 0.10d,
            ProvisionalMaturity => 0.20d,
            _ => 1d
        };
    }

    public static double IncrementalReplayShare(int rejectedUpdateStreak)
    {
        return Math.Max(0, rejectedUpdateStreak) switch
        {
            >= 2 => 0.75d,
            1 => 0.50d,
            _ => 0.25d
        };
    }

    public static IReadOnlyList<int> SelectWholeRunRows(
        IEnumerable<CombatTransformerTrainingRow> rows,
        int maximumFrames,
        string? seed)
    {
        var limit = Math.Max(0, maximumFrames);
        if (limit == 0)
        {
            return Array.Empty<int>();
        }
        var selected = new List<int>(limit);
        var groups = (rows ?? Array.Empty<CombatTransformerTrainingRow>())
            .Where(row => row != null && row.RowIndex >= 0)
            .GroupBy(
                row => string.IsNullOrWhiteSpace(row.RunKey)
                    ? row.Identity ?? ""
                    : row.RunKey,
                StringComparer.Ordinal)
            .Select(group => new
            {
                RunKey = group.Key,
                Priority = group.Min(row => row.Priority),
                Rows = group
                    .GroupBy(row => row.RowIndex)
                    .Select(items => items.First())
                    .OrderBy(row => row.RowIndex)
                    .ToArray()
            })
            .Where(group => group.Rows.Length > 0)
            .OrderBy(group => group.Priority)
            .ThenBy(group => StableSelectionKey(seed, group.RunKey),
                StringComparer.Ordinal)
            .ThenBy(group => group.RunKey, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            // Sequence history and fixed-anchor isolation are run-scoped. An
            // oversized run is skipped rather than partially leaking one run
            // across the selected and unselected sets.
            if (group.Rows.Length > limit - selected.Count)
            {
                continue;
            }
            selected.AddRange(group.Rows.Select(row => row.RowIndex));
            if (selected.Count >= limit)
            {
                break;
            }
        }
        return selected.OrderBy(index => index).ToArray();
    }

    public static string CorpusCompatibilityKey(
        CombatFoundationCompatibilityManifest manifest,
        string? decisionProfile,
        CombatTransformerTeacherOptions options)
    {
        return Hash(string.Join("|", new[]
        {
            Version,
            manifest.RulesetHash ?? "",
            manifest.ContentSetHash ?? "",
            manifest.OwnerModSetHash ?? "",
            manifest.NativeProgramPackageHash ?? "",
            manifest.ActionContractVersion ?? "",
            manifest.TrainingSemanticsVersion ?? "",
            manifest.FeatureSchemaVersion.ToString(),
            manifest.FeatureEncodingMode ?? "",
            (decisionProfile ?? "").Trim().ToLowerInvariant(),
            options.StateDimensions.ToString(),
            options.ActionDimensions.ToString()
        }));
    }

    public static string TeacherCompatibilityKey(
        string corpusCompatibilityKey,
        CombatTransformerTeacherOptions options)
    {
        return Hash(string.Join("|", new[]
        {
            CombatTransformerWorldModelProtocol.Model,
            corpusCompatibilityKey ?? "",
            options.HiddenDimensions.ToString(),
            options.Layers.ToString(),
            options.AttentionHeads.ToString(),
            options.FeedForwardDimensions.ToString(),
            options.HistoryLength.ToString()
        }));
    }

    private static string Hash(string value)
    {
        byte[] digest;
        using (var sha256 = SHA256.Create())
        {
            digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        }
        var builder = new StringBuilder(digest.Length * 2);
        foreach (var item in digest)
        {
            builder.Append(item.ToString("X2"));
        }
        return builder.ToString();
    }

    private static string StableSelectionKey(string? seed, string runKey)
    {
        return Hash((seed ?? "") + "|" + runKey);
    }
}

public sealed class CombatTransformerTrainingRow
{
    public int RowIndex { get; set; }

    public string RunKey { get; set; } = "";

    public string Identity { get; set; } = "";

    public int Priority { get; set; }
}

public sealed class CombatTransformerTeacherOptions
{
    public string Backend { get; set; } =
        CombatTransformerTeacherBackendNames.Disabled;

    public string PythonExecutable { get; set; } =
        CombatTransformerRuntimeProtocol.AutomaticExecutable;

    public int Epochs { get; set; } = 12;

    public int BatchSize { get; set; } = 64;

    public int StateDimensions { get; set; } = 2048;

    public int ActionDimensions { get; set; } = 1024;

    public int HiddenDimensions { get; set; } = 384;

    public int Layers { get; set; } = 6;

    public int AttentionHeads { get; set; } = 8;

    public int FeedForwardDimensions { get; set; } = 1536;

    public int HistoryLength { get; set; } = 12;

    public int MinimumFrames { get; set; } = 1024;

    public int MaximumFrames { get; set; } = 10000;

    public bool EnableWarmStart { get; set; } = true;

    public int CpuRefreshInterval { get; set; } = 4;

    public int AcceleratorRefreshInterval { get; set; } = 3;

    public int MinimumFreshFramesForRefresh { get; set; } = 2048;

    public int CpuEpochs { get; set; } = 4;

    public int CpuIncrementalEpochs { get; set; } = 1;

    public int CpuFinalEpochs { get; set; } = 4;

    public bool EnableAdaptiveRefresh { get; set; } = true;

    public double AdaptiveRefreshDriftThreshold { get; set; } = 0.15d;

    public bool EnableFixedAnchorValidation { get; set; } = true;

    public double MaximumHeadRegression { get; set; } = 0.05d;

    public int IncrementalEpochs { get; set; } = 4;

    public int FinalEpochs { get; set; } = 12;

    public int IncrementalReplayFrames { get; set; } = 1024;

    public int MaximumIncrementalTrainingFrames { get; set; } = 4096;

    public int MaximumObjectTokens { get; set; } = 64;

    public int CpuThreads { get; set; }

    public int CpuInteropThreads { get; set; }

    public int MicroBatchSize { get; set; }

    public int DataLoaderWorkers { get; set; }

    public bool DisableDataLoaderWorkers { get; set; }

    public int PrefetchBatches { get; set; } = 2;

    public bool EnableShardedDataset { get; set; } = true;

    public int DatasetShardFrames { get; set; } = 512;

    public int ResidentDatasetMaximumFrames { get; set; } = 4096;

    public long MemoryReserveBytes { get; set; } =
        CombatFoundationParallelismProtocol.DefaultTeacherReserveBytes;

    public bool EnablePinnedMemory { get; set; } = true;

    public bool EnableMixedPrecision { get; set; } = true;

    public bool EnableDeterministicTraining { get; set; } = true;

    public double DistillationWeight { get; set; } = 0.15d;

    public int MaximumPolicyTeacherStalenessIterations { get; set; } = 3;

    public bool BlockTrainingWhenPolicyTeacherStale { get; set; } = true;

    public int RandomSeed { get; set; } = 1701;

    public CombatTransformerTeacherOptions Normalized()
    {
        Backend = CombatTransformerTeacherBackendNames.Normalize(Backend);
        PythonExecutable = string.IsNullOrWhiteSpace(PythonExecutable)
                           || string.Equals(
                               PythonExecutable.Trim(),
                               "python",
                               StringComparison.OrdinalIgnoreCase)
            ? CombatTransformerRuntimeProtocol.AutomaticExecutable
            : PythonExecutable.Trim();
        Epochs = Math.Max(1, Math.Min(100, Epochs));
        BatchSize = Math.Max(8, Math.Min(512, BatchSize));
        StateDimensions = Math.Max(32, Math.Min(2048, StateDimensions));
        ActionDimensions = Math.Max(32, Math.Min(2048, ActionDimensions));
        HiddenDimensions = Math.Max(32, Math.Min(512, HiddenDimensions));
        Layers = Math.Max(1, Math.Min(6, Layers));
        AttentionHeads = Math.Max(1, Math.Min(16, AttentionHeads));
        while (HiddenDimensions % AttentionHeads != 0
               && AttentionHeads > 1)
        {
            AttentionHeads--;
        }
        FeedForwardDimensions = Math.Max(
            HiddenDimensions,
            Math.Min(4096, FeedForwardDimensions));
        HistoryLength = Math.Max(1, Math.Min(32, HistoryLength));
        MinimumFrames = Math.Max(64, Math.Min(100000, MinimumFrames));
        MaximumFrames = Math.Max(
            MinimumFrames,
            Math.Min(100000, MaximumFrames));
        CpuRefreshInterval = Math.Max(1, Math.Min(8, CpuRefreshInterval));
        AcceleratorRefreshInterval = Math.Max(
            1,
            Math.Min(8, AcceleratorRefreshInterval));
        MinimumFreshFramesForRefresh = Math.Max(
            64,
            Math.Min(MaximumFrames, MinimumFreshFramesForRefresh));
        CpuEpochs = Math.Max(1, Math.Min(Epochs, CpuEpochs));
        CpuIncrementalEpochs = Math.Max(
            1,
            Math.Min(CpuEpochs, CpuIncrementalEpochs));
        CpuFinalEpochs = Math.Max(1, Math.Min(100, CpuFinalEpochs));
        IncrementalEpochs = Math.Max(1, Math.Min(Epochs, IncrementalEpochs));
        FinalEpochs = Math.Max(1, Math.Min(100, FinalEpochs));
        IncrementalReplayFrames = Math.Max(
            0,
            Math.Min(MaximumFrames, IncrementalReplayFrames));
        MaximumIncrementalTrainingFrames = Math.Max(
            MinimumFrames,
            Math.Min(MaximumFrames, MaximumIncrementalTrainingFrames));
        MaximumObjectTokens = Math.Max(16, Math.Min(192, MaximumObjectTokens));
        CpuThreads = Math.Max(0, Math.Min(64, CpuThreads));
        CpuInteropThreads = Math.Max(0, Math.Min(8, CpuInteropThreads));
        MicroBatchSize = Math.Max(0, Math.Min(BatchSize, MicroBatchSize));
        DataLoaderWorkers = Math.Max(0, Math.Min(8, DataLoaderWorkers));
        PrefetchBatches = Math.Max(1, Math.Min(8, PrefetchBatches));
        DatasetShardFrames = Math.Max(256, Math.Min(4096, DatasetShardFrames));
        ResidentDatasetMaximumFrames = Math.Max(
            256,
            Math.Min(MaximumFrames, ResidentDatasetMaximumFrames));
        MemoryReserveBytes = Math.Max(
            128L * 1024L * 1024L,
            Math.Min(16L * 1024L * 1024L * 1024L, MemoryReserveBytes));
        DistillationWeight = Clamp(DistillationWeight, 0d, 0.75d, 0.15d);
        MaximumPolicyTeacherStalenessIterations = Math.Max(
            0,
            Math.Min(32, MaximumPolicyTeacherStalenessIterations));
        AdaptiveRefreshDriftThreshold = Clamp(
            AdaptiveRefreshDriftThreshold,
            0.01d,
            1d,
            0.15d);
        MaximumHeadRegression = Clamp(
            MaximumHeadRegression,
            0d,
            0.50d,
            0.05d);
        RandomSeed = RandomSeed == 0 ? 1701 : RandomSeed;
        return this;
    }

    public CombatTransformerTeacherOptions Clone()
    {
        return (CombatTransformerTeacherOptions)MemberwiseClone();
    }

    public long EstimatedEncoderParameters()
    {
        var hidden = Math.Max(1, HiddenDimensions);
        var feedForward = Math.Max(hidden, FeedForwardDimensions);
        var perLayer = 4L * hidden * hidden
                       + 2L * hidden * feedForward
                       + 9L * hidden
                       + feedForward;
        return Math.Max(1, Layers) * perLayer;
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum,
        double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Max(minimum, Math.Min(maximum, value));
    }
}

public sealed class CombatTransformerTeacherContext
{
    public int Iteration { get; set; }

    public int TotalIterations { get; set; } = 1;

    public bool FinalRefreshRequested { get; set; }

    public string DecisionProfile { get; set; } = "balanced";

    public IReadOnlyList<CombatEpisode> Episodes { get; set; } =
        Array.Empty<CombatEpisode>();

    public CombatTransformerTeacherOptions Options { get; set; } = new();

    public string CorpusCompatibilityKey { get; set; } = "";

    public string TeacherCompatibilityKey { get; set; } = "";

    public Action<CombatTransformerTeacherProgress>? Progress { get; set; }

    /// <summary>
    /// Called after the authoritative sparse dataset has been exported and no
    /// world-model observation envelopes are needed by the teacher boundary.
    /// The host may release those envelopes before the Python process starts.
    /// </summary>
    public Func<CombatTransformerTeacherHostReleaseReport>?
        ReleaseExportedDataset { get; set; }
}

public sealed class CombatTransformerTeacherHostReleaseReport
{
    public bool Attempted { get; set; }

    public int ReleasedEpisodes { get; set; }

    public int ReleasedFrames { get; set; }

    public long WorkingSetBeforeBytes { get; set; }

    public long WorkingSetAfterBytes { get; set; }

    public long GcHeapBeforeBytes { get; set; }

    public long GcHeapAfterBytes { get; set; }

    public string Diagnostic { get; set; } = "";
}

public static class CombatTransformerTeacherRefreshProtocol
{
    public static bool IsFinalRefresh(CombatTransformerTeacherContext context)
    {
        return context != null
               && context.FinalRefreshRequested
               && context.Iteration >= Math.Max(1, context.TotalIterations);
    }

    public static bool ShouldRefresh(
        bool warmStarted,
        bool finalRefresh,
        bool cpuBackend,
        int currentIteration,
        int lastRefreshIteration,
        int lastAttemptIteration,
        int rejectedUpdateStreak,
        int pendingFrames,
        int freshPendingFrames,
        bool driftRefresh,
        CombatTransformerTeacherOptions options,
        out string reason)
    {
        options ??= new CombatTransformerTeacherOptions();
        var interval = cpuBackend
            ? Math.Max(1, options.CpuRefreshInterval)
            : Math.Max(1, options.AcceleratorRefreshInterval);
        var intervalDue = Math.Max(1, currentIteration)
                          - Math.Max(0, lastRefreshIteration) >= interval;
        var rejectionBackoffInterval = interval * (1 << Math.Min(
            2,
            Math.Max(0, rejectedUpdateStreak)));
        var rejectionBackoffDue = rejectedUpdateStreak <= 0
                                  || Math.Max(1, currentIteration)
                                     - Math.Max(0, lastAttemptIteration)
                                     >= rejectionBackoffInterval;
        if (finalRefresh)
        {
            reason = "final-refresh";
            return true;
        }
        if (!rejectionBackoffDue)
        {
            reason = "rejected-update-backoff:" + rejectionBackoffInterval;
            return false;
        }
        if (!warmStarted)
        {
            reason = "cold-start";
            return true;
        }
        if (driftRefresh)
        {
            reason = "dataset-drift";
            return true;
        }
        if (freshPendingFrames
            >= Math.Max(64, options.MinimumFreshFramesForRefresh))
        {
            reason = "fresh-frame-threshold";
            return true;
        }
        if (pendingFrames > 0 && intervalDue)
        {
            reason = "maximum-staleness";
            return true;
        }
        reason = "stable-teacher-reuse";
        return false;
    }
}

public static class CombatTransformerTeacherApplicationProtocol
{
    public static bool HasUsableTeacherSource(
        CombatTransformerTeacherReport? report)
    {
        if (report == null)
        {
            return false;
        }

        // A rejected refresh may reuse only a teacher whose accepted
        // generation was already persisted. Merely loading a legacy or
        // external checkpoint is not proof that its weights passed the gate.
        return report.TeacherGeneration > 0
               && (report.WarmStarted
                   || (report.TrainingRefreshed && report.UpdateAccepted));
    }
}

public sealed class CombatTransformerTeacherProgress
{
    public int Iteration { get; set; }

    public int TotalIterations { get; set; }

    public string Stage { get; set; } = "starting";

    public int Epoch { get; set; }

    public int TotalEpochs { get; set; }

    public int CompletedFrames { get; set; }

    public int TotalFrames { get; set; }

    public double FramesPerSecond { get; set; }

    public double ElapsedSeconds { get; set; }

    public double EstimatedRemainingSeconds { get; set; }

    public double ProcessCpuPercent { get; set; }

    public double ProcessCpuSeconds { get; set; }

    public long WorkingSetBytes { get; set; }

    public long PeakWorkingSetBytes { get; set; }

    public double StageElapsedSeconds { get; set; }

    public Dictionary<string, double> StageSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool WarmStarted { get; set; }

    public bool TrainingEnabled { get; set; } = true;

    public string Message { get; set; } = "";
}

public sealed class CombatTransformerTeacherReport
{
    public string Protocol { get; set; } =
        CombatTransformerWorldModelProtocol.Report;

    public int Iteration { get; set; }

    public bool Requested { get; set; }

    public bool Success { get; set; }

    public bool Applied { get; set; }

    public string FailureKind { get; set; } =
        CombatTransformerTeacherFailureKinds.None;

    public int ProcessExitCode { get; set; }

    public bool RetryableFailure { get; set; }

    public bool FormalModelBlocked { get; set; }

    public string RequestedBackend { get; set; } = "";

    public string EffectiveBackend { get; set; } = "";

    public string DeviceName { get; set; } = "";

    public string PythonVersion { get; set; } = "";

    public string TorchVersion { get; set; } = "";

    public string NumpyVersion { get; set; } = "";

    public string ResolvedPythonExecutable { get; set; } = "";

    public string RuntimeResolutionSource { get; set; } = "";

    public bool RuntimeAutoTuned { get; set; }

    public bool RuntimeAutoTuneCacheHit { get; set; }

    public bool CudaFallbackTriggered { get; set; }

    public string CudaFallbackReason { get; set; } = "";

    public int EffectiveCpuThreads { get; set; }

    public int EffectiveCpuInteropThreads { get; set; }

    public int EffectiveBatchSize { get; set; }

    public int EffectiveMicroBatchSize { get; set; }

    public int EffectiveDataLoaderWorkers { get; set; }

    public int EffectivePrefetchBatches { get; set; }

    public bool PinnedMemoryEnabled { get; set; }

    public string NumericPrecision { get; set; } = "float32";

    public bool DeterministicTrainingEnabled { get; set; }

    public long ParameterCount { get; set; }

    public int HiddenDimensions { get; set; }

    public int Layers { get; set; }

    public int AttentionHeads { get; set; }

    public int FeedForwardDimensions { get; set; }

    public int EpisodeCount { get; set; }

    public int FrameCount { get; set; }

    public string CorpusMaturity { get; set; } =
        CombatTransformerTeacherCorpusProtocol.CollectingMaturity;

    public double CorpusDistillationWeightCap { get; set; }

    public int CorpusGrowthFrames { get; set; }

    public double CorpusGrowthRatio { get; set; }

    public bool RefreshTriggeredByCorpusGrowth { get; set; }

    public int RefreshInterval { get; set; }

    public int RefreshRejectedUpdateStreak { get; set; }

    public int RefreshLastAttemptIteration { get; set; }

    public int RefreshMinimumFreshFrames { get; set; }

    public int RefreshFreshPendingFrames { get; set; }

    public int CurrentFrameCount { get; set; }

    public bool IncrementalCorpusUpdate { get; set; }

    public int SkippedExistingCorpusFrames { get; set; }

    public int ReusedCorpusFrames { get; set; }

    public int DeduplicatedCorpusFrames { get; set; }

    public int DroppedCorpusFrames { get; set; }

    public int CorpusBacklogFrames { get; set; }

    public string CorpusCompatibilityKey { get; set; } = "";

    public string TeacherCompatibilityKey { get; set; } = "";

    public int AnnotatedFrames { get; set; }

    public int AnnotatedCandidates { get; set; }

    public int DistillationTrainingFrames { get; set; }

    public int DistillationValidationFrames { get; set; }

    public double DistillationUtilization { get; set; }

    public double EffectiveDistillationWeight { get; set; }

    public bool DistillationStudentGuardApplied { get; set; }

    public string DistillationStudentGuardReason { get; set; } = "";

    public int TrainingFrames { get; set; }

    public int ValidationFrames { get; set; }

    public int EpochsExecuted { get; set; }

    public int RequestedEpochs { get; set; }

    public bool WarmStarted { get; set; }

    public bool TrainingRefreshed { get; set; }

    public bool UpdateAccepted { get; set; }

    public int TeacherGeneration { get; set; }

    public int StablePolicyTeacherGeneration { get; set; }

    public int StableWorldTeacherGeneration { get; set; }

    public int AnnotationTeacherGeneration { get; set; }

    public bool PolicyTeacherApplied { get; set; }

    public int PolicyTeacherFreshnessAgeIterations { get; set; }

    public bool PolicyTeacherFreshnessGatePassed { get; set; } = true;

    public bool WorldTeacherApplied { get; set; }

    public double DatasetDriftScore { get; set; }

    public string DatasetFingerprint { get; set; } = "";

    public Dictionary<string, int> DatasetStrategyFrames { get; set; } =
        new(StringComparer.Ordinal);

    public string RefreshReason { get; set; } = "";

    public string ResumeModelPath { get; set; } = "";

    public double ValidationPolicyCrossEntropy { get; set; }

    public double ValidationUniformPolicyCrossEntropy { get; set; }

    public bool QualityGatePassed { get; set; }

    public bool TeacherSourceGatePassed { get; set; }

    public bool PolicyQualityGatePassed { get; set; }

    public bool WorldModelQualityGatePassed { get; set; }

    public double ValidationPolicyTop1Accuracy { get; set; }

    public double ValidationValueMae { get; set; }

    public double ValidationStrategyAccuracy { get; set; }

    public double ValidationPhaseAccuracy { get; set; }

    public int StrategyLabelFrames { get; set; }

    public Dictionary<string, int> StrategyLabelCounts { get; set; } =
        new(StringComparer.Ordinal);

    public int StrategyApplicableFrames { get; set; }

    public Dictionary<string, int> StrategyApplicableCounts { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> StrategyNegativeCounts { get; set; } =
        new(StringComparer.Ordinal);

    public bool StrategyQualityGatePassed { get; set; } = true;

    public double ValidationDynamicsMse { get; set; }

    public int DynamicsTrainingFrames { get; set; }

    public int DynamicsValidationFrames { get; set; }

    public int InvalidTransitionFrames { get; set; }

    public int TerminalKnownFrames { get; set; }

    public double ValidationOutcomeMae { get; set; }

    public double ValidationDeathBrier { get; set; }

    public double ValidationTerminalAccuracy { get; set; }

    public int AnchorValidationFrames { get; set; }

    public int RequiredAnchorValidationFrames { get; set; }

    public bool AnchorCreated { get; set; }

    public string AnchorPath { get; set; } = "";

    public double BaselinePolicyCrossEntropy { get; set; }

    public double BaselineValueMae { get; set; }

    public double BaselineOutcomeMae { get; set; }

    public double BaselineDeathBrier { get; set; }

    public double ValidationCompositeScore { get; set; }

    public double BaselineCompositeScore { get; set; }

    public double CompositeImprovement { get; set; }

    public bool HeadRegressionGatePassed { get; set; } = true;

    public bool AnchorCoverageGatePassed { get; set; } = true;

    public double ElapsedSeconds { get; set; }

    public double ProcessCpuSeconds { get; set; }

    public long PeakWorkingSetBytes { get; set; }

    public bool MemoryAdmissionPassed { get; set; } = true;

    public long AvailablePhysicalMemoryBytes { get; set; }

    public long MemoryReserveBytes { get; set; }

    public long PredictedPeakWorkingSetBytes { get; set; }

    public long NormalPlanPredictedPeakWorkingSetBytes { get; set; }

    public string MemoryAdmissionMode { get; set; } = "training-refresh";

    public string MemoryPlanFingerprint { get; set; } = "";

    public bool LowMemoryFallbackAttempted { get; set; }

    public bool LowMemoryRuntimeFallbackApplied { get; set; }

    public CombatTransformerTeacherHostReleaseReport HostDatasetRelease {
        get;
        set;
    } = new();

    public string DatasetStorageMode { get; set; } = "resident";

    public int DatasetShardFrames { get; set; }

    public string DatasetEncoding { get; set; } = "";

    public int LoadedDatasetFrames { get; set; }

    public bool IncrementalTrainingSelection { get; set; }

    public int IncrementalTrainingFrames { get; set; }

    public int IncrementalNewFrames { get; set; }

    public int IncrementalFreshFrames { get; set; }

    public int IncrementalRetryFrames { get; set; }

    public int IncrementalReplayFrames { get; set; }

    public int IncrementalPendingFrames { get; set; }

    public int IncrementalDeferredFrames { get; set; }

    public int IncrementalReplayEscalationLevel { get; set; }

    public int AnnotationSelectionFrames { get; set; }

    public long DenseFeatureSlots { get; set; }

    public long NonZeroFeatureValues { get; set; }

    public double SparseFeatureDensity { get; set; }

    public int ObjectTokenFrames { get; set; }

    public int EmptyObjectTokenFrames { get; set; }

    public double ObjectTokenFrameCoverage { get; set; }

    public bool ObjectTokenAuditPassed { get; set; } = true;

    public bool ObjectTokenAuditAdvisoryOnly { get; set; }

    public List<string> DataQualityWarnings { get; set; } = new();

    public double DataLoadingSeconds { get; set; }

    public double DataPreparationSeconds { get; set; }

    public double RuntimeCalibrationSeconds { get; set; }

    public double TrainingSeconds { get; set; }

    public double EvaluationSeconds { get; set; }

    public double AnnotationSeconds { get; set; }

    public double SavingSeconds { get; set; }

    public Dictionary<string, double> StageSeconds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public double TrainingFramesPerSecond { get; set; }

    public double AnnotationFramesPerSecond { get; set; }

    public long PeakDeviceMemoryBytes { get; set; }

    public string DatasetPath { get; set; } = "";

    public string ModelPath { get; set; } = "";

    public string ReportPath { get; set; } = "";

    public string Message { get; set; } = "";
}

public interface ICombatTransformerTeacher
{
    CombatTransformerTeacherReport TrainAndAnnotate(
        CombatTransformerTeacherContext context,
        CancellationToken cancellationToken);
}
