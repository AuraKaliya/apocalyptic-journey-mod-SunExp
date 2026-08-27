using System;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

/// <summary>Renders only Aura's isolated replay camera into the export target.</summary>
internal sealed class ReplayRenderSurfaceV12 : IDisposable
{
    private readonly Camera camera;
    private readonly RenderTexture? originalTarget;
    private readonly bool previousHudVisible;
    private bool disposed;

    internal ReplayRenderSurfaceV12(RenderTexture target, bool includeHud)
    {
        camera = MatchReplayPlayer.RenderCamera
                 ?? throw new InvalidOperationException("独立回放相机不可用。");
        originalTarget = camera.targetTexture;
        previousHudVisible = true;
        MatchReplayPlayer.SetRenderHudVisible(includeHud);
        camera.targetTexture = target ?? throw new ArgumentNullException(nameof(target));
    }

    internal void Render()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ReplayRenderSurfaceV12));
        Canvas.ForceUpdateCanvases();
        camera.Render();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (camera != null) camera.targetTexture = originalTarget;
        MatchReplayPlayer.SetRenderHudVisible(previousHudVisible);
    }
}
