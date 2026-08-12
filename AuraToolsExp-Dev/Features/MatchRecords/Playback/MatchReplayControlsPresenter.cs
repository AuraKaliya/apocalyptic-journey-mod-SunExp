using System;
using AuraToolsExp.Dll.Features.Settings;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayControlsPresenter
{
    private const string RootName = "AuraToolsMatchReplayControls";
    private static GameObject? root;
    private static Text? status;
    private static Text? playLabel;
    private static Text? speedLabel;
    private static Slider? progress;
    private static Button? continueButton;
    private static bool updating;

    internal static void Show()
    {
        Close();
        root = new GameObject(
            RootName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var toolbar = AuraToolsUi.CreateRect(
            "Toolbar",
            root.transform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(1160f, 66f));
        toolbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 22f);
        AuraToolsUi.AddPanelImage(toolbar, AuraToolsUi.Background);
        var layout = toolbar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        AuraToolsUi.AddButton(toolbar.transform, "上一回合", () => MatchReplayPlayer.SeekTurn(-1), 104f);
        var play = AuraToolsUi.AddButton(toolbar.transform, "暂停", MatchReplayPlayer.TogglePause, 82f);
        playLabel = play.GetComponentInChildren<Text>();
        AuraToolsUi.AddButton(toolbar.transform, "下一回合", () => MatchReplayPlayer.SeekTurn(1), 104f);

        var speed = AuraToolsUi.AddButton(toolbar.transform, "1x", MatchReplayPlayer.CycleSpeed, 64f);
        speedLabel = speed.GetComponentInChildren<Text>();
        progress = CreateProgress(toolbar.transform);
        status = AuraToolsUi.AddText(
            toolbar.transform,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleCenter,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        continueButton = AuraToolsUi.AddButton(toolbar.transform, "降级继续", MatchReplayPlayer.ContinueDegraded, 92f);
        AuraToolsUi.AddButton(toolbar.transform, "退出回放", MatchReplayPlayer.Stop, 104f);
        Refresh();
    }

    internal static void Refresh()
    {
        if (root == null || !MatchReplayPlayer.IsActive)
        {
            return;
        }

        if (playLabel != null)
        {
            playLabel.text = MatchReplayPlayer.IsPaused ? "播放" : "暂停";
        }

        if (speedLabel != null)
        {
            speedLabel.text = MatchReplayPlayer.Speed.ToString("0.#") + "x";
        }

        if (status != null)
        {
            status.text = "回合 " + MatchReplayPlayer.CurrentTurn + "/" + Math.Max(1, MatchReplayPlayer.TurnCount)
                          + "   事件 " + MatchReplayPlayer.EventIndex + "/" + MatchReplayPlayer.EventCount
                          + (string.IsNullOrWhiteSpace(MatchReplayPlayer.PlaybackIssue)
                              ? ""
                              : "   " + MatchReplayPlayer.PlaybackIssue);
        }

        if (continueButton != null) continueButton.interactable = MatchReplayPlayer.HasBlockingError;

        if (progress != null)
        {
            updating = true;
            progress.value = MatchReplayPlayer.EventCount <= 0
                ? 0f
                : MatchReplayPlayer.EventIndex / (float)MatchReplayPlayer.EventCount;
            updating = false;
        }
    }

    internal static void Close()
    {
        if (root != null)
        {
            Object.Destroy(root);
        }

        root = null;
        status = null;
        playLabel = null;
        speedLabel = null;
        progress = null;
        continueButton = null;
    }

    private static Slider CreateProgress(Transform parent)
    {
        var sliderRoot = AuraToolsUi.CreateLayout("ReplayProgress", parent);
        AuraToolsUi.SetFixedSize(sliderRoot, 240f, AuraToolsUi.ButtonHeight);
        var background = AuraToolsUi.AddImage(sliderRoot, new Color(0.02f, 0.02f, 0.04f, 1f));

        var fill = AuraToolsUi.CreateRect(
            "Fill",
            sliderRoot.transform,
            new Vector2(0f, 0.28f),
            new Vector2(1f, 0.72f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-12f, 0f));
        AuraToolsUi.AddImage(fill, AuraToolsUi.Accent);
        var handle = AuraToolsUi.CreateRect(
            "Handle",
            sliderRoot.transform,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(18f, 34f));
        AuraToolsUi.AddImage(handle, AuraToolsUi.Text);

        var slider = sliderRoot.AddComponent<Slider>();
        slider.targetGraphic = background;
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(value =>
        {
            if (!updating)
            {
                MatchReplayPlayer.SeekNormalized(value);
            }
        });
        return slider;
    }
}
