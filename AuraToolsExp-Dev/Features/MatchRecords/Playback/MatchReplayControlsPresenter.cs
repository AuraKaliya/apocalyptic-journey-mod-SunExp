using System;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Features.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UiRaycastSafetyShared;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayControlsPresenter
{
    private const string RootName = "AuraToolsMatchReplayControls";
    private const float ExpandedY = 10f;
    private const float CollapsedY = -56f;
    private const float AutoHideDelayMilliseconds = 1200f;
    private const float SlideSpeed = 320f;
    private static GameObject? root;
    private static RectTransform? toolbarRect;
    private static Text? status;
    private static Text? playLabel;
    private static Text? speedLabel;
    private static Slider? progress;
    private static Button? previousButton;
    private static Button? playButton;
    private static Button? nextButton;
    private static Button? speedButton;
    private static bool updating;
    private static bool draggingProgress;
    private static bool pointerInside;
    private static float idleMilliseconds;
    private static float targetY = ExpandedY;

    internal static GameObject? RootObject => root;

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
            new Vector2(1120f, 58f));
        toolbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, ExpandedY);
        toolbarRect = toolbar.GetComponent<RectTransform>();
        AuraToolsUi.AddListRowImage(toolbar, AuraToolsUi.Background);
        var toolbarTrigger = toolbar.AddComponent<EventTrigger>();
        AddTrigger(toolbarTrigger, EventTriggerType.PointerEnter, _ =>
        {
            pointerInside = true;
            Wake();
        });
        AddTrigger(toolbarTrigger, EventTriggerType.PointerExit, _ => pointerInside = false);
        AddTrigger(toolbarTrigger, EventTriggerType.PointerDown, _ => Wake());

        var revealTab = AuraToolsUi.CreateRect(
            "RevealTab",
            toolbar.transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 0f),
            new Vector2(168f, 18f));
        revealTab.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -2f);
        AuraToolsUi.EnsureLayoutElement(revealTab).ignoreLayout = true;
        AuraToolsUi.AddListRowImage(revealTab, AuraToolsUi.Header);
        AuraToolsUi.AddFillText(
            revealTab.transform,
            "回放控制",
            12,
            TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText);
        var layout = toolbar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        previousButton = AuraToolsUi.AddButton(toolbar.transform, "上一回合", () => InvokeAndWake(() => MatchReplayPlayer.SeekTurn(-1)), 104f);
        playButton = AuraToolsUi.AddButton(toolbar.transform, "暂停", () => InvokeAndWake(MatchReplayPlayer.TogglePause), 82f);
        playLabel = playButton.GetComponentInChildren<Text>();
        nextButton = AuraToolsUi.AddButton(toolbar.transform, "下一回合", () => InvokeAndWake(() => MatchReplayPlayer.SeekTurn(1)), 104f);

        speedButton = AuraToolsUi.AddButton(toolbar.transform, "1x", () => InvokeAndWake(MatchReplayPlayer.CycleSpeed), 64f);
        speedLabel = speedButton.GetComponentInChildren<Text>();
        progress = CreateProgress(toolbar.transform);
        status = AuraToolsUi.AddText(
            toolbar.transform,
            "",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleCenter,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddButton(toolbar.transform, "退出回放", () => InvokeAndWake(MatchReplayPlayer.Stop), 104f);
        pointerInside = false;
        idleMilliseconds = 0f;
        targetY = ExpandedY;
        Refresh();
    }

    internal static void Tick(float deltaMilliseconds)
    {
        if (toolbarRect == null)
        {
            return;
        }

        var elapsed = Math.Max(0f, deltaMilliseconds);
        if (!pointerInside && !draggingProgress)
        {
            idleMilliseconds += elapsed;
            if (idleMilliseconds >= AutoHideDelayMilliseconds)
            {
                targetY = CollapsedY;
            }
        }

        var position = toolbarRect.anchoredPosition;
        var nextY = Mathf.MoveTowards(position.y, targetY, SlideSpeed * elapsed / 1000f);
        if (!Mathf.Approximately(position.y, nextY))
        {
            toolbarRect.anchoredPosition = new Vector2(position.x, nextY);
        }
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
            status.text = MatchReplayPlayer.IsRuntimeReady
                ? "回合 " + MatchReplayPlayer.CurrentTurn + "/" + Math.Max(1, MatchReplayPlayer.TurnCount)
                  + "   动作 " + MatchReplayPlayer.CompletedActionCount + "/" + MatchReplayPlayer.ActionCount
                  + (string.IsNullOrWhiteSpace(MatchReplayPlayer.PlaybackIssue)
                      ? ""
                      : "   " + MatchReplayPlayer.PlaybackIssue)
                : MatchReplayPlayer.PreparationStatus;
        }

        var ready = MatchReplayPlayer.IsRuntimeReady;
        var canControl = ready && !MatchReplayPlayer.IsSeeking;
        if (previousButton != null) previousButton.interactable = canControl;
        if (playButton != null) playButton.interactable = canControl;
        if (nextButton != null) nextButton.interactable = canControl;
        if (speedButton != null) speedButton.interactable = canControl;

        if (progress != null)
        {
            progress.interactable = canControl;
            if (!draggingProgress)
            {
                updating = true;
                progress.value = MatchReplayPlayer.Progress;
                updating = false;
            }
        }
    }

    internal static void Close()
    {
        if (root != null)
        {
            var closingRoot = root;
            root = null;
            UiRaycastSafeDestroyRuntime.DisableAndHide(
                closingRoot,
                "Match replay controls close",
                AuraToolsLog.Debug);
            Object.Destroy(closingRoot);
            UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(
                6,
                "Match replay controls close",
                AuraToolsLog.Debug);
        }

        root = null;
        toolbarRect = null;
        status = null;
        playLabel = null;
        speedLabel = null;
        progress = null;
        previousButton = null;
        playButton = null;
        nextButton = null;
        speedButton = null;
        draggingProgress = false;
        pointerInside = false;
        idleMilliseconds = 0f;
        targetY = ExpandedY;
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
                if (draggingProgress)
                {
                    MatchReplayPlayer.PreviewSeekNormalized(value);
                }
                else
                {
                    MatchReplayPlayer.SeekNormalized(value);
                }
            }
        });
        var trigger = sliderRoot.AddComponent<EventTrigger>();
        AddTrigger(trigger, EventTriggerType.PointerDown, _ =>
        {
            Wake();
            draggingProgress = true;
            MatchReplayPlayer.BeginSeekPreview(slider.value);
        });
        AddTrigger(trigger, EventTriggerType.PointerUp, _ =>
        {
            Wake();
            if (!draggingProgress)
            {
                return;
            }

            draggingProgress = false;
            MatchReplayPlayer.CommitSeekPreview(slider.value);
        });
        return slider;
    }

    private static void InvokeAndWake(Action action)
    {
        Wake();
        action();
    }

    private static void Wake()
    {
        idleMilliseconds = 0f;
        targetY = ExpandedY;
    }

    private static void AddTrigger(
        EventTrigger trigger,
        EventTriggerType type,
        Action<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(data => action(data));
        trigger.triggers.Add(entry);
    }
}
