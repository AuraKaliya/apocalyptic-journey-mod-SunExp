using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuraCg.Shared;

internal sealed class AuraCgSceneLayerPresentation
{
    public AuraCgSceneParticipantPlan Plan { get; set; } = new();

    public string DisplayName { get; set; } = "";

    public IReadOnlyList<Sprite> Frames { get; set; } = Array.Empty<Sprite>();

    internal AuraCgNormalizedBounds VisibleBounds { get; set; } = AuraCgNormalizedBounds.Full;

    internal float CanvasWidth { get; set; } = 1f;

    internal float CanvasHeight { get; set; } = 1f;

    public float FrameSeconds { get; set; } = 0.08f;

    public bool Loop { get; set; } = true;

    internal AuraCgPortraitFraming Portrait { get; set; } = new();

    internal List<AuraCgSceneArtLayerPresentation> Attachments { get; set; } = new();
}

internal sealed class AuraCgSceneArtLayerPresentation
{
    internal AuraCgSceneArtLayerSpec Spec { get; set; } = new();
    internal IReadOnlyList<Sprite> Frames { get; set; } = Array.Empty<Sprite>();
    internal float FrameSeconds { get; set; } = 1f;
    internal bool Loop { get; set; }
}

internal sealed class AuraCgScenePresentation : IDisposable
{
    private readonly List<IDisposable> media = new();
    internal bool IsDisposed { get; private set; }
    internal bool Ready { get; set; }
    internal Sprite? Background { get; set; }
    internal AuraCgSceneArtwork Artwork { get; set; } = new();
    internal List<AuraCgSceneArtLayerPresentation> SceneLayers { get; } = new();
    internal List<AuraCgSceneLayerPresentation> Participants { get; } = new();

    internal void Retain(IDisposable lease)
    {
        if (IsDisposed) lease.Dispose();
        else media.Add(lease);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        foreach (var lease in media) lease.Dispose();
        media.Clear();
        Participants.Clear();
        SceneLayers.Clear();
        Background = null;
        Ready = false;
    }
}
