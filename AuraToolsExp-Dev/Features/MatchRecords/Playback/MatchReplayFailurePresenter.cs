using System;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UiRaycastSafetyShared;
using UiTransitionGuardShared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.UI;
using WitchUiManager = Witch.UI.UIManager;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayFailurePresenter
{
    private const string RootName = "AuraToolsMatchReplayFailure";
    private static readonly MatchReplayFailureNotificationState NotificationState = new();
    private static GameObject? root;

    internal static void Schedule(string title, string detail)
    {
        var ticket = NotificationState.Schedule();
        CloseRoot("Replay failure replaced");
        UiTransitionGuardRuntime.RunAfterGuard(
            null,
            AuraToolsIds.ModId,
            "Match replay failure notification",
            () =>
            {
                if (!NotificationState.TryPresent(ticket))
                {
                    return;
                }

                Show(title, detail);
            },
            2);
    }

    internal static void Dismiss()
    {
        NotificationState.Dismiss();
        CloseRoot("Replay failure dismissed");
        RestoreNativeInput("dismiss");
        AuraToolsLog.Debug("[MatchRecords] replay failure notification dismissed.");
    }

    private static void Show(string title, string detail)
    {
        try
        {
            CloseRoot("Replay failure recreated");
            root = new GameObject(
                RootName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(CanvasGroup));
            Object.DontDestroyOnLoad(root);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32600;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            root.AddComponent<MatchReplayFailureDismissDriver>().Arm(Dismiss);

            var blocker = AuraToolsUi.CreateRect(
                "Blocker",
                root.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            var blockerImage = AuraToolsUi.AddImage(blocker, new Color(0f, 0f, 0f, 0.72f));
            var blockerButton = blocker.AddComponent<Button>();
            blockerButton.targetGraphic = blockerImage;
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.AddListener(Dismiss);

            var window = AuraToolsUi.CreateRect(
                "Window",
                blocker.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(760f, 420f));
            window.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            AuraToolsUi.AddPanelImage(window, AuraToolsUi.Background);
            var layout = window.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            AuraToolsUi.AddText(
                window.transform,
                string.IsNullOrWhiteSpace(title) ? "回放启动失败" : title,
                AuraToolsUi.SectionFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Accent,
                54f,
                1f);
            AuraToolsUi.AddText(
                window.transform,
                detail ?? "",
                AuraToolsUi.BodyFontSize,
                TextAnchor.UpperLeft,
                AuraToolsUi.Text,
                190f,
                1f);

            var buttons = AuraToolsUi.CreateLayout("Buttons", window.transform);
            AuraToolsUi.SetFixedHeight(buttons, AuraToolsUi.ButtonHeight + 8f);
            var buttonLayout = buttons.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonLayout.childControlWidth = false;
            buttonLayout.childControlHeight = true;
            buttonLayout.childForceExpandWidth = false;
            buttonLayout.childForceExpandHeight = false;
            AuraToolsUi.AddButton(buttons.transform, "确定", Dismiss, 116f);
            AuraToolsUi.AddText(
                window.transform,
                "单击面板任意位置关闭",
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleCenter,
                AuraToolsUi.MutedText,
                32f,
                1f);
            RestoreNativeInput("show");
            AuraToolsLog.Debug("[MatchRecords] replay failure notification shown with full-screen dismiss target.");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[MatchRecords] replay failure presenter could not be created", ex);
            CloseRoot("Replay failure presenter creation failed");
            // This fallback runs only after the transition guard has expired.
            WitchUiManager.Instance?.ShowModalWindow(
                string.IsNullOrWhiteSpace(title) ? "回放启动失败" : title,
                detail ?? "");
        }
    }

    private static void CloseRoot(string source)
    {
        if (root == null)
        {
            return;
        }

        var closing = root;
        root = null;
        UiRaycastSafeDestroyRuntime.DisableAndHide(closing, source);
        AuraToolsUi.ClearChildren(closing.transform);
        Object.Destroy(closing);
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(6, source);
    }

    private static void RestoreNativeInput(string source)
    {
        try
        {
            var manager = WitchUiManager.Instance;
            var upper = manager?.upperCanvasTf;
            upper?.GetComponent<UpperCanvasController>()?.RefreshRaycasterState();
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.enabled = true;
                if (eventSystem.currentInputModule != null)
                {
                    eventSystem.currentInputModule.enabled = true;
                }
            }

            var mainRaycaster = manager?.canvasTf?.GetComponent<GraphicRaycaster>();
            AuraToolsLog.Debug(
                "[MatchRecords] replay failure input restored: source=" + source
                + ", eventSystem=" + (eventSystem != null)
                + ", eventSystemEnabled=" + (eventSystem != null && eventSystem.enabled)
                + ", inputModule=" + (eventSystem?.currentInputModule != null)
                + ", inputModuleEnabled=" + (eventSystem?.currentInputModule != null
                                               && eventSystem.currentInputModule.enabled)
                + ", mainGraphicRaycaster=" + (mainRaycaster != null)
                + ", mainGraphicRaycasterEnabled=" + (mainRaycaster != null && mainRaycaster.enabled));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] native UI input recovery skipped: " + ex.Message);
        }
    }
}

internal sealed class MatchReplayFailureDismissDriver : MonoBehaviour
{
    private Action? dismiss;
    private int armedAfterFrame;
    private bool invoked;
    private bool inputFailureLogged;

    internal void Arm(Action action)
    {
        dismiss = action;
        armedAfterFrame = Time.frameCount + 1;
    }

    private void Update()
    {
        if (invoked || dismiss == null || Time.frameCount < armedAfterFrame)
        {
            return;
        }

        try
        {
            if (!Input.GetMouseButtonDown(0)
                && !Input.GetKeyDown(KeyCode.Return)
                && !Input.GetKeyDown(KeyCode.KeypadEnter)
                && !Input.GetKeyDown(KeyCode.Escape))
            {
                return;
            }

            invoked = true;
            dismiss();
        }
        catch (Exception ex)
        {
            if (inputFailureLogged)
            {
                return;
            }

            inputFailureLogged = true;
            AuraToolsLog.Warn("[MatchRecords] direct failure-dismiss input unavailable; EventSystem path remains active: " + ex.Message);
        }
    }
}
