using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayIntentVisualContractV17
{
    internal const string DefaultIconResourcePath = "Icon/ActionIcon/蓄力";
    internal const string DefaultBackIconResourcePath = "Icon/ActionIcon/攻击底";

    internal static bool TryResolve(
        string requestedPath,
        string fallbackPath,
        Func<string, bool> exists,
        out ReplayResolvedIntentVisualPathV17 resolution,
        out string error)
    {
        if (exists == null) throw new ArgumentNullException(nameof(exists));
        var requested = (requestedPath ?? "").Trim();
        var fallback = (fallbackPath ?? "").Trim();
        if (requested.Length > 0 && exists(requested))
        {
            resolution = new ReplayResolvedIntentVisualPathV17(requested, requested, usedFallback: false);
            error = "";
            return true;
        }
        if (fallback.Length > 0 && exists(fallback))
        {
            resolution = new ReplayResolvedIntentVisualPathV17(requested, fallback, usedFallback: true);
            error = "";
            return true;
        }
        resolution = default;
        error = "intent-visual-resource-unresolvable:" + (requested.Length == 0 ? "<empty>" : requested)
                + ":fallback=" + (fallback.Length == 0 ? "<empty>" : fallback);
        return false;
    }
}

internal readonly struct ReplayResolvedIntentVisualPathV17
{
    internal ReplayResolvedIntentVisualPathV17(string requestedPath, string resolvedPath, bool usedFallback)
    {
        RequestedPath = requestedPath ?? "";
        ResolvedPath = resolvedPath ?? "";
        UsedFallback = usedFallback;
    }

    internal string RequestedPath { get; }
    internal string ResolvedPath { get; }
    internal bool UsedFallback { get; }
}
