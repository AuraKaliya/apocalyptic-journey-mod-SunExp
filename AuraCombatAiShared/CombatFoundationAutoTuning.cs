using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatFoundationAutoTuneProtocol
{
    public const string Version = "foundation-auto-tune-v1";

    public const string CacheFileName = "foundation-auto-tune-v1.json";
}

public sealed class CombatFoundationAutoTuneMeasurement
{
    public int Parallelism { get; set; }

    public int Campaigns { get; set; }

    public int Battles { get; set; }

    public long SearchSimulations { get; set; }

    public double ElapsedSeconds { get; set; }

    public double CpuUtilizationPercent { get; set; }

    public double AllocationMegabytesPerSecond { get; set; }

    public double Gen2CollectionsPerSecond { get; set; }

    public double UsefulWorkPerSecond { get; set; }

    public double EfficiencyScore { get; set; }
}

public sealed class CombatFoundationAutoTuneResult
{
    public string Version { get; set; } = CombatFoundationAutoTuneProtocol.Version;

    public string CacheKey { get; set; } = "";

    public string HardwareKey { get; set; } = "";

    public DateTime MeasuredUtc { get; set; } = DateTime.UtcNow;

    public bool CacheHit { get; set; }

    public bool LowConfidence { get; set; }

    public int SelectedParallelism { get; set; }

    public double ThroughputTolerance { get; set; } = 0.02d;

    public List<CombatFoundationAutoTuneMeasurement> Measurements { get; set; } =
        new();
}

public static class CombatFoundationAutoTuneSelector
{
    public static int Select(
        IReadOnlyList<CombatFoundationAutoTuneMeasurement> measurements,
        double throughputTolerance)
    {
        var usable = (measurements
                      ?? Array.Empty<CombatFoundationAutoTuneMeasurement>())
            .Where(item => item != null
                           && item.Parallelism > 0
                           && item.EfficiencyScore > 0d)
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
        var maximum = usable.Max(item => item.EfficiencyScore);
        var threshold = maximum * (1d - tolerance);
        return usable.First(item => item.EfficiencyScore >= threshold)
            .Parallelism;
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
