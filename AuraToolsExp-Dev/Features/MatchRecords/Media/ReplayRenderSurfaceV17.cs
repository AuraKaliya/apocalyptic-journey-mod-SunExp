using System;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

/// <summary>Renders only Aura's isolated replay camera into the export target.</summary>
internal sealed class ReplayRenderSurfaceV17 : IDisposable
{
    private readonly ReplayRenderExportLeaseV17 lease;
    private readonly bool previousHudVisible;
    private bool disposed;

    internal ReplayRenderSurfaceV17(RenderTexture target, bool includeHud)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        previousHudVisible = MatchReplayPlayer.RenderHudVisible;
        var acquired = MatchReplayPlayer.AcquireExportTarget(target);
        try
        {
            MatchReplayPlayer.SetRenderHudVisible(includeHud);
            lease = acquired;
        }
        catch
        {
            acquired.Dispose();
            throw;
        }
    }

    internal void Render()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ReplayRenderSurfaceV17));
        lease.Render();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { MatchReplayPlayer.SetRenderHudVisible(previousHudVisible); }
        finally { lease.Dispose(); }
    }
}
