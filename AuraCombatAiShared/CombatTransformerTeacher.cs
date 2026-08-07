using System;
using System.Collections.Generic;
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
        "aura.combat-transformer-world-model.v2";

    public const string Report =
        "aura.combat-transformer-world-model-report.v2";
}

public static class CombatTransformerTeacherCorpusProtocol
{
    public const string Version = "transformer-teacher-corpus-v2-semantic-key";

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
}

public sealed class CombatTransformerTeacherOptions
{
    public string Backend { get; set; } =
        CombatTransformerTeacherBackendNames.Disabled;

    public string PythonExecutable { get; set; } =
        CombatTransformerRuntimeProtocol.AutomaticExecutable;

    public int Epochs { get; set; } = 12;

    public int BatchSize { get; set; } = 64;

    public int StateDimensions { get; set; } = 1024;

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

    public int CpuEpochs { get; set; } = 4;

    public int CpuIncrementalEpochs { get; set; } = 1;

    public int CpuFinalEpochs { get; set; } = 4;

    public bool EnableAdaptiveRefresh { get; set; } = true;

    public double AdaptiveRefreshDriftThreshold { get; set; } = 0.15d;

    public bool EnableFixedAnchorValidation { get; set; } = true;

    public double MaximumHeadRegression { get; set; } = 0.05d;

    public int IncrementalEpochs { get; set; } = 4;

    public int FinalEpochs { get; set; } = 12;

    public int CpuThreads { get; set; }

    public int CpuInteropThreads { get; set; }

    public int MicroBatchSize { get; set; }

    public int DataLoaderWorkers { get; set; }

    public int PrefetchBatches { get; set; } = 2;

    public bool EnableShardedDataset { get; set; } = true;

    public int DatasetShardFrames { get; set; } = 64;

    public long MemoryReserveBytes { get; set; } =
        CombatFoundationParallelismProtocol.DefaultTeacherReserveBytes;

    public bool EnablePinnedMemory { get; set; } = true;

    public bool EnableMixedPrecision { get; set; } = true;

    public double DistillationWeight { get; set; } = 0.35d;

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
        CpuEpochs = Math.Max(1, Math.Min(Epochs, CpuEpochs));
        CpuIncrementalEpochs = Math.Max(
            1,
            Math.Min(CpuEpochs, CpuIncrementalEpochs));
        CpuFinalEpochs = Math.Max(1, Math.Min(100, CpuFinalEpochs));
        IncrementalEpochs = Math.Max(1, Math.Min(Epochs, IncrementalEpochs));
        FinalEpochs = Math.Max(1, Math.Min(100, FinalEpochs));
        CpuThreads = Math.Max(0, Math.Min(64, CpuThreads));
        CpuInteropThreads = Math.Max(0, Math.Min(8, CpuInteropThreads));
        MicroBatchSize = Math.Max(0, Math.Min(BatchSize, MicroBatchSize));
        DataLoaderWorkers = Math.Max(0, Math.Min(8, DataLoaderWorkers));
        PrefetchBatches = Math.Max(1, Math.Min(8, PrefetchBatches));
        DatasetShardFrames = Math.Max(16, Math.Min(512, DatasetShardFrames));
        MemoryReserveBytes = Math.Max(
            128L * 1024L * 1024L,
            Math.Min(16L * 1024L * 1024L * 1024L, MemoryReserveBytes));
        DistillationWeight = Clamp(DistillationWeight, 0d, 0.75d, 0.35d);
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
}

public static class CombatTransformerTeacherRefreshProtocol
{
    public static bool IsFinalRefresh(CombatTransformerTeacherContext context)
    {
        return context != null
               && context.FinalRefreshRequested
               && context.Iteration >= Math.Max(1, context.TotalIterations);
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

    public int EffectiveCpuThreads { get; set; }

    public int EffectiveCpuInteropThreads { get; set; }

    public int EffectiveBatchSize { get; set; }

    public int EffectiveMicroBatchSize { get; set; }

    public int EffectiveDataLoaderWorkers { get; set; }

    public int EffectivePrefetchBatches { get; set; }

    public bool PinnedMemoryEnabled { get; set; }

    public string NumericPrecision { get; set; } = "float32";

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

    public int CurrentFrameCount { get; set; }

    public bool IncrementalCorpusUpdate { get; set; }

    public int SkippedExistingCorpusFrames { get; set; }

    public int ReusedCorpusFrames { get; set; }

    public int DeduplicatedCorpusFrames { get; set; }

    public int DroppedCorpusFrames { get; set; }

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

    public double DatasetDriftScore { get; set; }

    public string DatasetFingerprint { get; set; } = "";

    public Dictionary<string, int> DatasetStrategyFrames { get; set; } =
        new(StringComparer.Ordinal);

    public string RefreshReason { get; set; } = "";

    public string ResumeModelPath { get; set; } = "";

    public double ValidationPolicyCrossEntropy { get; set; }

    public double ValidationUniformPolicyCrossEntropy { get; set; }

    public bool QualityGatePassed { get; set; }

    public bool PolicyQualityGatePassed { get; set; }

    public bool WorldModelQualityGatePassed { get; set; }

    public double ValidationPolicyTop1Accuracy { get; set; }

    public double ValidationValueMae { get; set; }

    public double ValidationStrategyAccuracy { get; set; }

    public double ValidationDynamicsMse { get; set; }

    public int DynamicsTrainingFrames { get; set; }

    public double ValidationOutcomeMae { get; set; }

    public double ValidationDeathBrier { get; set; }

    public double ValidationTerminalAccuracy { get; set; }

    public int AnchorValidationFrames { get; set; }

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

    public string DatasetStorageMode { get; set; } = "resident";

    public int DatasetShardFrames { get; set; }

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
