using System;

namespace AuraCombatAi.Shared;

public static class CombatFoundationExecutionProfileNames
{
    public const string Auto = "auto";
    public const string Cpu16 = "cpu-16";
    public const string Cpu32 = "cpu-32";
    public const string Custom = "custom";

    public const string DirectInference = "direct";
    public const string ShardedBatchInference = "sharded-batch";
}

public sealed class CombatFoundationExecutionPlan
{
    public string Profile { get; set; } =
        CombatFoundationExecutionProfileNames.Auto;

    public int CampaignParallelism { get; set; }

    public string InferenceMode { get; set; } =
        CombatFoundationExecutionProfileNames.DirectInference;

    public int InferenceParallelism { get; set; }

    public int InferenceLaneCount { get; set; }

    public int InferenceBatchSize { get; set; }

    public int ThreadPoolMinimumWorkerThreads { get; set; }

    public int CheckpointSerializationParallelism { get; set; }
}

public static class CombatFoundationExecutionProfiles
{
    public static CombatFoundationExecutionPlan Resolve(
        string? profile,
        int requestedCampaignParallelism,
        string? inferenceMode,
        int requestedInferenceParallelism,
        int requestedThreadPoolMinimumWorkerThreads,
        int requestedCheckpointSerializationParallelism,
        int? availableProcessorCount = null,
        int requestedInferenceLaneCount = 0,
        int requestedInferenceBatchSize = 0)
    {
        var processorCount = Math.Max(
            1,
            availableProcessorCount ?? Environment.ProcessorCount);
        var normalizedProfile = NormalizeProfile(profile);
        var campaignParallelism = normalizedProfile switch
        {
            CombatFoundationExecutionProfileNames.Cpu16 =>
                Math.Min(16, processorCount),
            CombatFoundationExecutionProfileNames.Cpu32 =>
                Math.Min(32, processorCount),
            CombatFoundationExecutionProfileNames.Auto =>
                Math.Min(64, processorCount),
            _ => Math.Min(
                processorCount,
                Math.Max(1, requestedCampaignParallelism))
        };
        var normalizedInferenceMode = NormalizeInferenceMode(inferenceMode);
        var inferenceParallelism = requestedInferenceParallelism <= 0
            ? campaignParallelism
            : Math.Max(
                1,
                Math.Min(campaignParallelism, requestedInferenceParallelism));
        var minimumWorkers = requestedThreadPoolMinimumWorkerThreads <= 0
            ? campaignParallelism + 8
            : Math.Max(
                campaignParallelism,
                requestedThreadPoolMinimumWorkerThreads);
        var checkpointParallelism = requestedCheckpointSerializationParallelism <= 0
            ? campaignParallelism >= 32 ? 2 : 1
            : Math.Max(
                1,
                Math.Min(2, requestedCheckpointSerializationParallelism));
        var directInference = string.Equals(
            normalizedInferenceMode,
            CombatFoundationExecutionProfileNames.DirectInference,
            StringComparison.Ordinal);
        var inferenceLaneCount = directInference
            ? inferenceParallelism
            : requestedInferenceLaneCount <= 0
                ? EffectiveLaneCount(inferenceParallelism)
                : Math.Max(
                    1,
                    Math.Min(inferenceParallelism, requestedInferenceLaneCount));
        var inferenceBatchSize = directInference
            ? 1
            : requestedInferenceBatchSize <= 0
                ? EffectiveBatchSize(inferenceParallelism, inferenceLaneCount)
                : Math.Max(2, Math.Min(32, requestedInferenceBatchSize));

        return new CombatFoundationExecutionPlan
        {
            Profile = normalizedProfile,
            CampaignParallelism = campaignParallelism,
            InferenceMode = normalizedInferenceMode,
            InferenceParallelism = inferenceParallelism,
            InferenceLaneCount = inferenceLaneCount,
            InferenceBatchSize = inferenceBatchSize,
            ThreadPoolMinimumWorkerThreads = Math.Min(256, minimumWorkers),
            CheckpointSerializationParallelism = checkpointParallelism
        };
    }

    public static string NormalizeProfile(string? profile)
    {
        var value = (profile ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            CombatFoundationExecutionProfileNames.Cpu16 => value,
            CombatFoundationExecutionProfileNames.Cpu32 => value,
            CombatFoundationExecutionProfileNames.Custom => value,
            _ => CombatFoundationExecutionProfileNames.Auto
        };
    }

    public static string NormalizeInferenceMode(string? mode)
    {
        return string.Equals(
            mode?.Trim(),
            CombatFoundationExecutionProfileNames.ShardedBatchInference,
            StringComparison.OrdinalIgnoreCase)
            ? CombatFoundationExecutionProfileNames.ShardedBatchInference
            : CombatFoundationExecutionProfileNames.DirectInference;
    }

    public static int EffectiveLaneCount(int parallelism)
    {
        // Keep enough callers on each queue for batching to be possible.
        // The previous one-lane-per-four-callers rule fragmented a 12-way run
        // into three mostly empty queues.
        return Math.Max(1, Math.Min(2, Math.Max(1, parallelism) / 8));
    }

    public static int EffectiveBatchSize(int parallelism, int laneCount = 0)
    {
        var lanes = laneCount <= 0
            ? EffectiveLaneCount(parallelism)
            : Math.Max(1, Math.Min(Math.Max(1, parallelism), laneCount));
        return Math.Max(
            2,
            Math.Min(4, (parallelism + lanes - 1) / lanes));
    }
}
