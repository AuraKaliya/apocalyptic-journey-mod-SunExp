using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatFoundationAutoTuneProtocol
{
    public const string Version =
        "foundation-auto-tune-v12-signed-microbenchmark";

    public const string CacheFileName = "foundation-auto-tune-v12.json";

    public const string CampaignKernelVersion =
        "campaign-kernel-v7-adaptive-parallelism";

    public const string InferenceKernelVersion =
        "managed-fp32-sparse-transposed-v6-balanced-lanes";

    public const string InferenceCalibrationKind =
        "inference-microbenchmark-v1";

    public const int MinimumInferenceMicrobenchmarkSamples = 64;

    public const int MaximumInferenceMicrobenchmarkSamples = 512;

    public const int InferenceHealthCooldownMinutes = 120;

    public const int MaximumInferenceHealthCooldownMinutes = 24 * 60;

    public const int MinimumCampaignWaves = 2;

    public const double UnderutilizedCpuPercent = 60d;

    public const double HighParallelismExplorationTolerance = 0.05d;

    public const double MaximumHealthyInferenceTimeoutRate = 0.50d;
}

public static class CombatFoundationAutoTuneObjectiveNames
{
    public const string BalancedEfficiency = "balanced-efficiency";

    public const string MaximumThroughput = "maximum-throughput";

    public static string Normalize(string? value)
    {
        return string.Equals(
            value?.Trim(),
            MaximumThroughput,
            StringComparison.OrdinalIgnoreCase)
            ? MaximumThroughput
            : BalancedEfficiency;
    }
}

public sealed class CombatFoundationAutoTuneMeasurement
{
    public string MeasurementKind { get; set; } = "campaign";

    public int Parallelism { get; set; }

    public string InferenceMode { get; set; } =
        CombatFoundationExecutionProfileNames.DirectInference;

    public int InferenceLaneCount { get; set; }

    public int InferenceBatchSize { get; set; } = 1;

    public int Campaigns { get; set; }

    public int Battles { get; set; }

    public long SearchSimulations { get; set; }

    public double ElapsedSeconds { get; set; }

    public double CpuUtilizationPercent { get; set; }

    public double AllocationMegabytesPerSecond { get; set; }

    public double Gen2CollectionsPerSecond { get; set; }

    public double UsefulWorkPerSecond { get; set; }

    public double EfficiencyScore { get; set; }

    public double P95LatencyMicroseconds { get; set; }

    public double AverageBatchFill { get; set; }

    public long InferenceRequests { get; set; }

    public long InferenceBatchEvaluations { get; set; }

    public long InferenceTimeoutFlushes { get; set; }

    public int InvalidCampaigns { get; set; }

    public int TrialCount { get; set; } = 1;

    public double MinimumUsefulWorkPerSecond { get; set; }

    public double MaximumUsefulWorkPerSecond { get; set; }

    public double UsefulWorkStandardDeviation { get; set; }
}

public sealed class CombatFoundationAutoTuneResult
{
    public string Version { get; set; } = CombatFoundationAutoTuneProtocol.Version;

    public string CacheKey { get; set; } = "";

    public string CampaignCacheKey { get; set; } = "";

    public string InferenceCacheKey { get; set; } = "";

    public string HardwareKey { get; set; } = "";

    public DateTime MeasuredUtc { get; set; } = DateTime.UtcNow;

    public bool CacheHit { get; set; }

    public bool LowConfidence { get; set; }

    public int SelectedParallelism { get; set; }

    public bool InferenceCalibrated { get; set; }

    public DateTime InferenceMeasuredUtc { get; set; }

    public string InferenceCalibrationKind { get; set; } = "";

    public int InferenceCalibrationSamples { get; set; }

    public bool InferenceFallbackActive { get; set; }

    public int InferenceHealthFailureCount { get; set; }

    public string LastInferenceHealthFailureReason { get; set; } = "";

    public DateTime InferenceRecalibrationNotBeforeUtc { get; set; }

    public string SelectedInferenceMode { get; set; } =
        CombatFoundationExecutionProfileNames.DirectInference;

    public int SelectedInferenceLaneCount { get; set; }

    public int SelectedInferenceBatchSize { get; set; } = 1;

    public double ThroughputTolerance { get; set; } = 0.02d;

    public string Objective { get; set; } =
        CombatFoundationAutoTuneObjectiveNames.MaximumThroughput;

    public int MeasurementCampaignsPerTrial { get; set; }

    public int MinimumCampaignWaves { get; set; } =
        CombatFoundationAutoTuneProtocol.MinimumCampaignWaves;

    public List<CombatFoundationAutoTuneMeasurement> Measurements { get; set; } =
        new();
}

public static class CombatFoundationAutoTuneSelector
{
    public static bool HasCampaignConfidence(
        IReadOnlyList<CombatFoundationAutoTuneMeasurement> measurements,
        int maximumParallelism,
        int minimumCampaignWaves =
            CombatFoundationAutoTuneProtocol.MinimumCampaignWaves)
    {
        var maximum = Math.Max(1, maximumParallelism);
        var requiredCampaigns = maximum * Math.Max(1, minimumCampaignWaves);
        return (measurements
                ?? Array.Empty<CombatFoundationAutoTuneMeasurement>())
            .Where(item => item != null
                           && !IsInferenceMeasurement(item.MeasurementKind)
                           && item.InvalidCampaigns == 0)
            .Any(item => item.Parallelism == maximum
                         && item.TrialCount >= Math.Max(1, minimumCampaignWaves)
                         && item.Campaigns >= requiredCampaigns
                         && item.UsefulWorkPerSecond > 0d);
    }

    public static CombatFoundationAutoTuneMeasurement? SelectInference(
        IReadOnlyList<CombatFoundationAutoTuneMeasurement> measurements,
        double throughputTolerance)
    {
        return SelectInference(
            measurements,
            throughputTolerance,
            CombatFoundationAutoTuneObjectiveNames.BalancedEfficiency);
    }

    public static CombatFoundationAutoTuneMeasurement? SelectInference(
        IReadOnlyList<CombatFoundationAutoTuneMeasurement> measurements,
        double throughputTolerance,
        string? objective)
    {
        var usable = (measurements
                      ?? Array.Empty<CombatFoundationAutoTuneMeasurement>())
            .Where(item => item != null
                           && IsInferenceMeasurement(item.MeasurementKind)
                           && item.InvalidCampaigns == 0
                           && (string.Equals(
                                   item.InferenceMode,
                                   CombatFoundationExecutionProfileNames
                                       .DirectInference,
                                   StringComparison.Ordinal)
                               || InferenceTimeoutRate(item)
                               <= CombatFoundationAutoTuneProtocol
                                   .MaximumHealthyInferenceTimeoutRate)
                           && SelectionScore(item, objective) > 0d)
            .ToList();
        if (usable.Count == 0)
        {
            return null;
        }
        var tolerance = double.IsNaN(throughputTolerance)
                        || double.IsInfinity(throughputTolerance)
            ? 0.02d
            : Math.Max(0d, Math.Min(0.20d, throughputTolerance));
        var normalizedObjective =
            CombatFoundationAutoTuneObjectiveNames.Normalize(objective);
        var direct = usable
            .Where(item => string.Equals(
                item.InferenceMode,
                CombatFoundationExecutionProfileNames.DirectInference,
                StringComparison.Ordinal))
            .OrderByDescending(item => SelectionScore(item, normalizedObjective))
            .FirstOrDefault();
        if (direct != null)
        {
            var directScore = SelectionScore(direct, normalizedObjective);
            usable = usable.Where(item =>
                    string.Equals(
                        item.InferenceMode,
                        CombatFoundationExecutionProfileNames.DirectInference,
                        StringComparison.Ordinal)
                    || item.AverageBatchFill >= 0.20d
                    || SelectionScore(item, normalizedObjective)
                       >= directScore * 1.05d)
                .ToList();
        }
        if (string.Equals(
                normalizedObjective,
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput,
                StringComparison.Ordinal))
        {
            return usable
                .OrderByDescending(item => SelectionScore(item, normalizedObjective))
                .ThenBy(item => item.P95LatencyMicroseconds)
                .ThenBy(item => item.InferenceLaneCount)
                .ThenBy(item => item.InferenceBatchSize)
                .First();
        }
        var threshold = usable.Max(item => SelectionScore(item, normalizedObjective))
                        * (1d - tolerance);
        return usable
            .Where(item => SelectionScore(item, normalizedObjective) >= threshold)
            .OrderBy(item => item.P95LatencyMicroseconds)
            .ThenBy(item => string.Equals(
                item.InferenceMode,
                CombatFoundationExecutionProfileNames.DirectInference,
                StringComparison.Ordinal)
                ? 0
                : 1)
            .ThenBy(item => item.InferenceLaneCount)
            .ThenBy(item => item.InferenceBatchSize)
            .First();
    }

    public static int Select(
        IReadOnlyList<CombatFoundationAutoTuneMeasurement> measurements,
        double throughputTolerance)
    {
        return Select(
            measurements,
            throughputTolerance,
            CombatFoundationAutoTuneObjectiveNames.BalancedEfficiency);
    }

    public static int Select(
        IReadOnlyList<CombatFoundationAutoTuneMeasurement> measurements,
        double throughputTolerance,
        string? objective)
    {
        var usable = (measurements
                      ?? Array.Empty<CombatFoundationAutoTuneMeasurement>())
            .Where(item => item != null
                           && !IsInferenceMeasurement(item.MeasurementKind)
                           && item.Parallelism > 0
                           && item.InvalidCampaigns == 0
                           && SelectionScore(item, objective) > 0d)
            .OrderBy(item => item.Parallelism)
            .ToList();
        if (usable.Count == 0)
        {
            return 1;
        }
        var tolerance = double.IsNaN(throughputTolerance)
                        || double.IsInfinity(throughputTolerance)
            ? 0.02d
            : Math.Max(0d, Math.Min(0.20d, throughputTolerance));
        var normalizedObjective =
            CombatFoundationAutoTuneObjectiveNames.Normalize(objective);
        if (string.Equals(
                normalizedObjective,
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput,
                StringComparison.Ordinal))
        {
            var ordered = usable
                .OrderByDescending(item => SelectionScore(item, normalizedObjective))
                .ThenByDescending(item => item.Parallelism)
                .ToList();
            var best = ordered[0];
            var bestScore = SelectionScore(best, normalizedObjective);
            var nearMaximumThreshold = bestScore * (1d - tolerance);
            var nearMaximum = usable
                .Where(item => SelectionScore(item, normalizedObjective)
                               >= nearMaximumThreshold)
                .OrderByDescending(item => item.Parallelism)
                .First();
            var highest = usable
                .OrderByDescending(item => item.Parallelism)
                .First();
            var explorationThreshold = bestScore
                                       * (1d - Math.Max(
                                           tolerance,
                                           CombatFoundationAutoTuneProtocol
                                               .HighParallelismExplorationTolerance));
            if (highest.CpuUtilizationPercent
                < CombatFoundationAutoTuneProtocol.UnderutilizedCpuPercent
                && SelectionScore(highest, normalizedObjective)
                   >= explorationThreshold)
            {
                return highest.Parallelism;
            }
            return nearMaximum.Parallelism;
        }
        var maximum = usable.Max(item => SelectionScore(item, normalizedObjective));
        var threshold = maximum * (1d - tolerance);
        return usable.First(item =>
                SelectionScore(item, normalizedObjective) >= threshold)
            .Parallelism;
    }

    private static bool IsInferenceMeasurement(string? kind)
    {
        return (kind ?? "").StartsWith(
            "inference",
            StringComparison.Ordinal);
    }

    private static double InferenceTimeoutRate(
        CombatFoundationAutoTuneMeasurement item)
    {
        var batches = item.InferenceBatchEvaluations > 0L
            ? item.InferenceBatchEvaluations
            : item.InferenceRequests;
        return batches <= 0L
            ? 0d
            : item.InferenceTimeoutFlushes / (double)batches;
    }

    private static double SelectionScore(
        CombatFoundationAutoTuneMeasurement item,
        string? objective)
    {
        var normalized = CombatFoundationAutoTuneObjectiveNames.Normalize(
            objective);
        if (string.Equals(
                normalized,
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput,
                StringComparison.Ordinal)
            && item.UsefulWorkPerSecond > 0d)
        {
            return item.UsefulWorkPerSecond;
        }
        return item.EfficiencyScore;
    }

    public static double Score(
        double usefulWorkPerSecond,
        double gen2CollectionsPerSecond,
        double allocationMegabytesPerSecond)
    {
        var gcPenalty = Math.Min(
            0.20d,
            Math.Max(0d, gen2CollectionsPerSecond) * 0.02d);
        var allocationPenalty = Math.Min(
            0.10d,
            Math.Max(0d, allocationMegabytesPerSecond - 4096d)
            / 32768d);
        return Math.Max(
            0d,
            usefulWorkPerSecond * (1d - gcPenalty - allocationPenalty));
    }
}
