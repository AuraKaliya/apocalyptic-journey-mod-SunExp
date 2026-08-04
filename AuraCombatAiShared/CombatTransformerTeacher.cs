using System;
using System.Collections.Generic;
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

public sealed class CombatTransformerTeacherOptions
{
    public string Backend { get; set; } =
        CombatTransformerTeacherBackendNames.Disabled;

    public string PythonExecutable { get; set; } = "python";

    public int Epochs { get; set; } = 12;

    public int BatchSize { get; set; } = 64;

    public int StateDimensions { get; set; } = 128;

    public int ActionDimensions { get; set; } = 128;

    public int HiddenDimensions { get; set; } = 64;

    public int Layers { get; set; } = 2;

    public int AttentionHeads { get; set; } = 4;

    public int HistoryLength { get; set; } = 12;

    public int MinimumFrames { get; set; } = 1024;

    public int CpuThreads { get; set; }

    public double DistillationWeight { get; set; } = 0.35d;

    public int RandomSeed { get; set; } = 1701;

    public CombatTransformerTeacherOptions Normalized()
    {
        Backend = CombatTransformerTeacherBackendNames.Normalize(Backend);
        PythonExecutable = string.IsNullOrWhiteSpace(PythonExecutable)
            ? "python"
            : PythonExecutable.Trim();
        Epochs = Math.Max(1, Math.Min(100, Epochs));
        BatchSize = Math.Max(8, Math.Min(512, BatchSize));
        StateDimensions = Math.Max(32, Math.Min(256, StateDimensions));
        ActionDimensions = Math.Max(32, Math.Min(256, ActionDimensions));
        HiddenDimensions = Math.Max(32, Math.Min(256, HiddenDimensions));
        Layers = Math.Max(1, Math.Min(6, Layers));
        AttentionHeads = Math.Max(1, Math.Min(8, AttentionHeads));
        while (HiddenDimensions % AttentionHeads != 0
               && AttentionHeads > 1)
        {
            AttentionHeads--;
        }
        HistoryLength = Math.Max(1, Math.Min(32, HistoryLength));
        MinimumFrames = Math.Max(64, Math.Min(100000, MinimumFrames));
        CpuThreads = Math.Max(0, Math.Min(64, CpuThreads));
        DistillationWeight = Clamp(DistillationWeight, 0d, 0.75d, 0.35d);
        RandomSeed = RandomSeed == 0 ? 1701 : RandomSeed;
        return this;
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

    public string DecisionProfile { get; set; } = "balanced";

    public IReadOnlyList<CombatEpisode> Episodes { get; set; } =
        Array.Empty<CombatEpisode>();

    public CombatTransformerTeacherOptions Options { get; set; } = new();
}

public sealed class CombatTransformerTeacherReport
{
    public string Protocol { get; set; } =
        "aura.combat-transformer-teacher-report.v1";

    public int Iteration { get; set; }

    public bool Requested { get; set; }

    public bool Success { get; set; }

    public bool Applied { get; set; }

    public string RequestedBackend { get; set; } = "";

    public string EffectiveBackend { get; set; } = "";

    public string DeviceName { get; set; } = "";

    public string PythonVersion { get; set; } = "";

    public string TorchVersion { get; set; } = "";

    public int EpisodeCount { get; set; }

    public int FrameCount { get; set; }

    public int AnnotatedFrames { get; set; }

    public int AnnotatedCandidates { get; set; }

    public int TrainingFrames { get; set; }

    public int ValidationFrames { get; set; }

    public int EpochsExecuted { get; set; }

    public double ValidationPolicyCrossEntropy { get; set; }

    public double ValidationUniformPolicyCrossEntropy { get; set; }

    public bool QualityGatePassed { get; set; }

    public double ValidationPolicyTop1Accuracy { get; set; }

    public double ValidationValueMae { get; set; }

    public double ValidationStrategyAccuracy { get; set; }

    public double ElapsedSeconds { get; set; }

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
