using System;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Mirrors OtherObj.UpdataActionShow: resolve the configured data path first,
/// then use the native deterministic fallback. Callers persist only ResolvedPath.
/// </summary>
internal static class ReplayIntentVisualCompatibilityApi
{
    internal static ReplayResolvedIntentVisualPathV17 ResolveIcon(string requestedPath) => Resolve(
        requestedPath,
        ReplayIntentVisualContractV17.DefaultIconResourcePath,
        "intent-icon");

    internal static ReplayResolvedIntentVisualPathV17 ResolveBackIcon(string requestedPath) => Resolve(
        requestedPath,
        ReplayIntentVisualContractV17.DefaultBackIconResourcePath,
        "intent-background");

    internal static bool Exists(string path) => !string.IsNullOrWhiteSpace(path)
        && (AuraToolsResourceCache.Load<Sprite>(path, true) != null
            || AuraToolsResourceCache.Load<Sprite>(path, false) != null);

    private static ReplayResolvedIntentVisualPathV17 Resolve(
        string requestedPath,
        string fallbackPath,
        string usage)
    {
        if (ReplayIntentVisualContractV17.TryResolve(
                requestedPath,
                fallbackPath,
                Exists,
                out var resolution,
                out var error)) return resolution;
        throw new InvalidOperationException("Replay native " + usage + " resolution failed: " + error);
    }
}
