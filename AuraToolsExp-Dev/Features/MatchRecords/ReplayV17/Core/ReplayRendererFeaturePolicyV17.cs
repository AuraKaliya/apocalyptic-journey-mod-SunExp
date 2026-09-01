using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal enum ReplayRendererFeatureDispositionV17
{
    RetainOwnedClone,
    ExcludeFromReplay,
    RejectProfile
}

internal sealed class ReplayRendererFeatureDecisionV17
{
    internal string FeatureType { get; set; } = "";
    internal bool SourceActive { get; set; }
    internal ReplayRendererFeatureDispositionV17 Disposition { get; set; }
    internal bool RequiresIntermediateColor { get; set; }
    internal string Reason { get; set; } = "";
}

internal static class ReplayRendererFeaturePolicyV17
{
    internal const string FullScreenPassRendererFeature =
        "UnityEngine.Rendering.Universal.FullScreenPassRendererFeature";
    internal const string UiBlurGrabPassFeature = "UIBlurGrabPassFeature";

    internal static ReplayRendererFeatureDecisionV17 Decide(string featureType, bool sourceActive)
    {
        var normalized = (featureType ?? "").Trim();
        if (normalized.Length == 0)
            return Reject(normalized, sourceActive, "renderer-feature-type-missing");
        if (!sourceActive)
            return new ReplayRendererFeatureDecisionV17
            {
                FeatureType = normalized,
                SourceActive = false,
                Disposition = ReplayRendererFeatureDispositionV17.ExcludeFromReplay,
                Reason = "source-feature-inactive"
            };
        if (string.Equals(normalized, FullScreenPassRendererFeature, StringComparison.Ordinal))
            return new ReplayRendererFeatureDecisionV17
            {
                FeatureType = normalized,
                SourceActive = true,
                Disposition = ReplayRendererFeatureDispositionV17.RetainOwnedClone,
                RequiresIntermediateColor = true,
                Reason = "render-graph-full-screen-pass-with-owned-intermediate-color"
            };
        if (string.Equals(normalized, UiBlurGrabPassFeature, StringComparison.Ordinal)
            || normalized.EndsWith("." + UiBlurGrabPassFeature, StringComparison.Ordinal))
            return new ReplayRendererFeatureDecisionV17
            {
                FeatureType = normalized,
                SourceActive = true,
                Disposition = ReplayRendererFeatureDispositionV17.ExcludeFromReplay,
                Reason = "main-camera-ui-blur-pass-has-no-render-graph-implementation"
            };
        return Reject(normalized, sourceActive, "unknown-active-renderer-feature");
    }

    private static ReplayRendererFeatureDecisionV17 Reject(
        string featureType,
        bool sourceActive,
        string reason) => new()
    {
        FeatureType = featureType,
        SourceActive = sourceActive,
        Disposition = ReplayRendererFeatureDispositionV17.RejectProfile,
        Reason = reason
    };
}
