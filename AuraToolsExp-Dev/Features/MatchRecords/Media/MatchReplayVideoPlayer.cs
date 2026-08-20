using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.Settings;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayVideoPlayer
{
    private const string OverlayName = "AuraToolsMatchVideoPlayer";
    private static MatchReplayVideoController? controller;

    internal static void Show(Transform parent, MatchMediaAsset asset)
    {
        Close();
        if (asset == null
            || !string.Equals(asset.Format, "MP4", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(asset.State, MatchMediaStates.Ready, StringComparison.Ordinal))
        {
            return;
        }
        var resolvedPath = MatchReplayMediaStore.ResolvePath(asset.FilePath);
        if (!File.Exists(resolvedPath))
        {
            return;
        }

        var window = AuraToolsUi.CreateOverlay(OverlayName, parent, "视频回放", Close, maxWidth: 1320f);
        var body = AuraToolsUi.CreateLayout("VideoPlayerBody", window.transform);
        var layout = body.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        AuraToolsUi.EnsureLayoutElement(body).flexibleHeight = 1f;

        var imageObject = AuraToolsUi.CreateLayout("VideoSurface", body.transform);
        AuraToolsUi.SetFixedHeight(imageObject, 620f);
        var image = imageObject.AddComponent<RawImage>();
        image.color = Color.white;

        var controls = AuraToolsUi.CreateLayout("VideoControls", body.transform);
        AuraToolsUi.SetFixedHeight(controls, AuraToolsUi.ToolbarHeight);
        var controlsLayout = controls.AddComponent<HorizontalLayoutGroup>();
        controlsLayout.spacing = 8f;
        controlsLayout.childControlWidth = true;
        controlsLayout.childControlHeight = true;
        controlsLayout.childForceExpandWidth = false;
        controlsLayout.childForceExpandHeight = false;

        var previous = AuraToolsUi.AddButton(controls.transform, "上一回合", () => controller?.SeekTurn(-1), 104f);
        var play = AuraToolsUi.AddButton(controls.transform, "播放", () => controller?.TogglePlay(), 82f);
        var next = AuraToolsUi.AddButton(controls.transform, "下一回合", () => controller?.SeekTurn(1), 104f);
        var slider = CreateSlider(controls.transform);
        var status = AuraToolsUi.AddText(controls.transform, "准备视频...", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleCenter, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);

        controller = window.AddComponent<MatchReplayVideoController>();
        controller.Initialize(asset, image, slider, play.GetComponentInChildren<Text>(), status);
        var timeline = controller.HasTimeline;
        previous.interactable = timeline;
        next.interactable = timeline;
    }

    internal static void Close()
    {
        if (controller != null)
        {
            controller.DisposePlayer();
            controller = null;
        }
    }

    private static Slider CreateSlider(Transform parent)
    {
        var root = AuraToolsUi.CreateLayout("VideoProgress", parent);
        AuraToolsUi.SetFixedSize(root, 280f, AuraToolsUi.ButtonHeight);
        var background = AuraToolsUi.AddImage(root, new Color(0.02f, 0.02f, 0.04f, 1f));
        var fill = AuraToolsUi.CreateRect("Fill", root.transform, new Vector2(0f, 0.28f), new Vector2(1f, 0.72f),
            new Vector2(0.5f, 0.5f), new Vector2(-12f, 0f));
        AuraToolsUi.AddImage(fill, AuraToolsUi.Accent);
        var handle = AuraToolsUi.CreateRect("Handle", root.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(18f, 34f));
        AuraToolsUi.AddImage(handle, AuraToolsUi.Text);
        var slider = root.AddComponent<Slider>();
        slider.targetGraphic = background;
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(value => controller?.SeekNormalized(value));
        return slider;
    }
}

internal sealed class MatchReplayVideoController : MonoBehaviour
{
    private VideoPlayer? player;
    private RenderTexture? target;
    private Slider? slider;
    private Text? playLabel;
    private Text? status;
    private List<MatchMediaTimelineEntry> timeline = new();
    private bool updating;

    internal bool HasTimeline => timeline.Count > 0;

    internal void Initialize(MatchMediaAsset asset, RawImage image, Slider progress, Text? playText, Text statusText)
    {
        slider = progress;
        playLabel = playText;
        status = statusText;
        try
        {
            timeline = string.IsNullOrWhiteSpace(asset.TimelineJson)
                ? new List<MatchMediaTimelineEntry>()
                : AuraSharedJson.Deserialize<List<MatchMediaTimelineEntry>>(asset.TimelineJson)
                  ?? new List<MatchMediaTimelineEntry>();
        }
        catch
        {
            timeline = new List<MatchMediaTimelineEntry>();
        }

        var width = Math.Max(320, asset.Width > 0 ? asset.Width : 1280);
        var height = Math.Max(180, asset.Height > 0 ? asset.Height : 720);
        target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        image.texture = target;
        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true;
        player.isLooping = false;
        player.source = VideoSource.Url;
        player.url = new Uri(MatchReplayMediaStore.ResolvePath(asset.FilePath)).AbsoluteUri;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = target;
        player.aspectRatio = VideoAspectRatio.FitInside;
        player.audioOutputMode = VideoAudioOutputMode.Direct;
        player.prepareCompleted += value =>
        {
            if (status != null) status.text = "已就绪   " + FormatTime(0) + " / " + FormatTime(value.length);
        };
        player.errorReceived += (_, error) =>
        {
            if (status != null) status.text = "视频无法播放：" + error;
        };
        player.Prepare();
    }

    internal void TogglePlay()
    {
        if (player == null || !player.isPrepared)
        {
            return;
        }

        if (player.isPlaying) player.Pause(); else player.Play();
    }

    internal void SeekNormalized(float value)
    {
        if (updating || player == null || !player.isPrepared || player.length <= 0d)
        {
            return;
        }

        player.time = Math.Max(0d, Math.Min(player.length, player.length * value));
    }

    internal void SeekTurn(int delta)
    {
        if (player == null || !player.isPrepared || timeline.Count == 0)
        {
            return;
        }

        var currentMs = player.time * 1000d;
        MatchMediaTimelineEntry? targetEntry;
        if (delta < 0)
        {
            targetEntry = timeline.Where(item => item.VideoMilliseconds < currentMs - 250d)
                .OrderByDescending(item => item.VideoMilliseconds).FirstOrDefault();
        }
        else
        {
            targetEntry = timeline.Where(item => item.VideoMilliseconds > currentMs + 250d)
                .OrderBy(item => item.VideoMilliseconds).FirstOrDefault();
        }

        if (targetEntry != null)
        {
            player.time = targetEntry.VideoMilliseconds / 1000d;
        }
    }

    internal void DisposePlayer()
    {
        if (player != null)
        {
            player.Stop();
            player.targetTexture = null;
        }

        if (target != null)
        {
            target.Release();
            Destroy(target);
        }

        player = null;
        target = null;
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        if (playLabel != null)
        {
            playLabel.text = player.isPlaying ? "暂停" : "播放";
        }

        if (status != null && player.isPrepared)
        {
            var turn = timeline.Where(item => item.VideoMilliseconds <= player.time * 1000d)
                .OrderByDescending(item => item.VideoMilliseconds).FirstOrDefault()?.TurnIndex ?? 0;
            status.text = (turn > 0 ? "回合 " + turn + "   " : "")
                          + FormatTime(player.time) + " / " + FormatTime(player.length);
        }

        if (slider != null && player.isPrepared && player.length > 0d)
        {
            updating = true;
            slider.value = (float)Math.Max(0d, Math.Min(1d, player.time / player.length));
            updating = false;
        }
    }

    private void OnDestroy()
    {
        DisposePlayer();
    }

    private static string FormatTime(double seconds)
    {
        return TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(@"mm\:ss");
    }
}
