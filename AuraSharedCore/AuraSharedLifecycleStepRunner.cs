using System;
using System.Collections.Generic;

namespace AuraShared.Core;

public enum AuraSharedLifecycleDeduplicateScope
{
    None,
    OwnerFeatureLifecycle,
    OwnerFeatureLifecycleSession,
    Custom
}

public sealed class AuraSharedLifecycleStepRequest
{
    public string OwnerId { get; set; } = "";

    public string FeatureId { get; set; } = "";

    public string LifecycleId { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string Source { get; set; } = "";

    public string CustomDeduplicateKey { get; set; } = "";

    public AuraSharedLifecycleDeduplicateScope DeduplicateScope { get; set; } =
        AuraSharedLifecycleDeduplicateScope.OwnerFeatureLifecycleSession;

    public int InitialDelayFrames { get; set; } = 1;

    public int DefaultStepDelayFrames { get; set; } = 1;

    public AuraSharedFramePhase Phase { get; set; } = AuraSharedFramePhase.Presentation;

    public int Priority { get; set; }

    public int EstimatedCost { get; set; } = 1;

    public IReadOnlyList<AuraSharedFrameStep> Steps { get; set; } = Array.Empty<AuraSharedFrameStep>();

    public Func<bool>? IsCancelled { get; set; }

    public Action<string, Exception>? OnStepFailed { get; set; }

    public Action<Exception>? OnFailed { get; set; }

    public Action? OnCompleted { get; set; }
}

public static class AuraSharedLifecycleStepRunner
{
    public static bool Run(AuraSharedLifecycleStepRequest? request)
    {
        if (request == null || request.Steps == null || request.Steps.Count == 0)
        {
            return false;
        }

        return AuraSharedFrameStepRunner.Run(new AuraSharedFrameStepSequence
        {
            OwnerId = request.OwnerId ?? "",
            Source = SourceFor(request),
            DeduplicateKey = DeduplicateKeyFor(request),
            InitialDelayFrames = Math.Max(1, request.InitialDelayFrames),
            DefaultStepDelayFrames = Math.Max(1, request.DefaultStepDelayFrames),
            Phase = request.Phase,
            Priority = request.Priority,
            EstimatedCost = request.EstimatedCost,
            Steps = request.Steps,
            IsCancelled = request.IsCancelled,
            OnStepFailed = request.OnStepFailed,
            OnFailed = request.OnFailed,
            OnCompleted = request.OnCompleted
        });
    }

    private static string SourceFor(AuraSharedLifecycleStepRequest request)
    {
        var source = Normalize(request.Source);
        if (source.Length > 0)
        {
            return source;
        }

        return Join(
            "AuraLifecycleStep",
            request.OwnerId,
            request.FeatureId,
            request.LifecycleId);
    }

    private static string DeduplicateKeyFor(AuraSharedLifecycleStepRequest request)
    {
        return request.DeduplicateScope switch
        {
            AuraSharedLifecycleDeduplicateScope.None => "",
            AuraSharedLifecycleDeduplicateScope.Custom => Normalize(request.CustomDeduplicateKey),
            AuraSharedLifecycleDeduplicateScope.OwnerFeatureLifecycle => Join(
                "lifecycle",
                request.OwnerId,
                request.FeatureId,
                request.LifecycleId),
            _ => Join(
                "lifecycle",
                request.OwnerId,
                request.FeatureId,
                request.LifecycleId,
                Normalize(request.SessionId).Length > 0
                    ? request.SessionId
                    : AuraBattleLifecycleRouter.EnsureBattleSession().ToString())
        };
    }

    private static string Join(params string[] values)
    {
        var result = "";
        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (normalized.Length == 0)
            {
                continue;
            }

            result = result.Length == 0 ? normalized : result + "." + normalized;
        }

        return result;
    }

    private static string Normalize(string? value)
    {
        return value == null || string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }
}
