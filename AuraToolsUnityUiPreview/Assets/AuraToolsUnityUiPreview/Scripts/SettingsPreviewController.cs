using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraTools.UnityUiPreview
{
    public sealed class SettingsPreviewController : MonoBehaviour
    {
        private sealed class TabView
        {
            internal GameObject Root;
            internal Image Background;
            internal Text Label;
        }

        private readonly List<GameObject> nativePages = new List<GameObject>();
        private readonly List<TabView> tabs = new List<TabView>();
        private readonly string[] scenarioOrder = { "default", "long-text", "warning", "empty", "extensions" };
        private ToolboxPreviewPage toolbox;
        private GameObject footer;
        private GameObject overlay;
        private Text overlayTitle;
        private Text overlaySummary;
        private GameObject genericOverlayContent;
        private GameObject roleCgOverlayContent;
        private GameObject eventCgOverlayContent;
        private GameObject eventCgPreviewContent;
        private GameObject toast;
        private Text toastText;
        private float toastUntil;
        private GameObject previewChrome;
        private Text scenarioLabel;
        private RectTransform settingsRect;
        private int scenarioIndex;
        private bool built;

        public Camera PreviewCamera { get; private set; }

        public Canvas PreviewCanvas { get; private set; }

        public GameObject SettingsWindow { get; private set; }

        internal ToolboxPreviewPage Toolbox
        {
            get { return toolbox; }
        }

        internal IReadOnlyList<GameObject> NativePages
        {
            get { return nativePages; }
        }

        internal int SelectedTabIndex { get; private set; }

        private void Awake()
        {
            Build();
        }

        private void Update()
        {
            RefreshResponsiveLayout();
            if (toast != null && toast.activeSelf && Time.unscaledTime >= toastUntil)
            {
                toast.SetActive(false);
            }
            if (Input.GetKeyDown(KeyCode.F1)) SelectTab(0);
            if (Input.GetKeyDown(KeyCode.F2)) SelectTab(1);
            if (Input.GetKeyDown(KeyCode.F3)) SelectTab(2);
            if (Input.GetKeyDown(KeyCode.F4)) SelectTab(3);
            if (Input.GetKeyDown(KeyCode.F5)) SelectTab(4);
            if (Input.GetKeyDown(KeyCode.F6)) CycleScenario();
            if (Input.GetKeyDown(KeyCode.F9)) CaptureManualScreenshot();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (overlay != null && overlay.activeSelf)
                {
                    CloseOverlay();
                }
                else
                {
                    Application.Quit();
                }
            }
        }

        internal void Build()
        {
            if (built)
            {
                return;
            }
            built = true;
            Application.runInBackground = true;

            PreviewCamera = CreateCamera();
            PreviewCanvas = CreateCanvas(PreviewCamera);
            CreateEventSystem();
            var backdrop = PreviewUi.Stretch("Backdrop", PreviewCanvas.transform, Vector4.zero);
            PreviewUi.Image(backdrop, PreviewTheme.Stage).raycastTarget = false;

            SettingsWindow = PreviewUi.Rect(
                "SettingUI",
                PreviewCanvas.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(PreviewTheme.SettingsWidth, PreviewTheme.SettingsHeight),
                Vector2.zero);
            settingsRect = SettingsWindow.GetComponent<RectTransform>();
            BuildWindow(SettingsWindow.transform);
            BuildPreviewChrome(PreviewCanvas.transform);
            BuildToast(PreviewCanvas.transform);
            SelectTab(4);
            RefreshResponsiveLayout();
        }

        internal void RefreshResponsiveLayout()
        {
            if (settingsRect == null || PreviewCanvas == null)
            {
                return;
            }
            var canvasRect = PreviewCanvas.transform as RectTransform;
            if (canvasRect == null)
            {
                return;
            }
            var availableHeight = Mathf.Clamp(
                canvasRect.rect.height - 32f,
                PreviewTheme.SettingsHeight,
                1090f);
            var targetSize = new Vector2(PreviewTheme.SettingsWidth, availableHeight);
            if ((settingsRect.sizeDelta - targetSize).sqrMagnitude > 0.25f)
            {
                settingsRect.sizeDelta = targetSize;
                Canvas.ForceUpdateCanvases();
            }
        }

        internal void SelectTab(int index)
        {
            index = Mathf.Clamp(index, 0, 4);
            SelectedTabIndex = index;
            for (var i = 0; i < nativePages.Count; i++)
            {
                nativePages[i].SetActive(i == index);
            }
            toolbox.Root.SetActive(index == 4);
            footer.SetActive(index < 4 && index != 1);
            for (var i = 0; i < tabs.Count; i++)
            {
                var selected = i == index;
                tabs[i].Background.color = selected
                    ? Color.white
                    : new Color(0.72f, 0.70f, 0.62f, 0.86f);
                tabs[i].Label.color = selected ? PreviewTheme.Text : new Color(0.91f, 0.85f, 0.67f, 1f);
            }
            CloseOverlay();
            Canvas.ForceUpdateCanvases();
        }

        internal void SetToolboxScenario(string scenario)
        {
            var normalized = NormalizeScenario(scenario);
            scenarioIndex = Array.IndexOf(scenarioOrder, normalized);
            if (scenarioIndex < 0) scenarioIndex = 0;
            toolbox.SetScenario(scenarioOrder[scenarioIndex]);
            if (scenarioLabel != null)
            {
                scenarioLabel.text = "场景：" + ScenarioDisplay(scenarioOrder[scenarioIndex]);
            }
        }

        internal void SetToolboxCategory(string category)
        {
            toolbox.SelectCategoryForPreview(category);
        }

        internal void SetPreviewChromeVisible(bool visible)
        {
            previewChrome?.SetActive(visible);
            toast?.SetActive(false);
        }

        internal void ShowToolSettings(string title, string summary, string description)
        {
            overlayTitle.text = title + "设置";
            overlaySummary.text = "";
            overlaySummary.gameObject.SetActive(false);
            SetOverlayContent(genericOverlayContent);
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling();
            var close = overlay.transform.Find("Window/Header/Close")?.gameObject;
            if (close != null)
            {
                EventSystem.current?.SetSelectedGameObject(close);
            }
        }

        internal void ShowCgSettingsPreview(string kind)
        {
            var normalized = (kind ?? "").Trim().ToLowerInvariant();
            if (normalized == "role-cg")
            {
                overlayTitle.text = "角色 CG 配置";
                SetOverlayContent(roleCgOverlayContent);
            }
            else if (normalized == "event-cg-preview")
            {
                overlayTitle.text = "事件 CG 配置";
                SetOverlayContent(eventCgPreviewContent);
            }
            else
            {
                overlayTitle.text = "事件 CG 配置";
                SetOverlayContent(eventCgOverlayContent);
            }
            overlaySummary.gameObject.SetActive(false);
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling();
        }

        internal void ShowToast(string message)
        {
            toastText.text = message ?? "";
            toast.SetActive(true);
            toast.transform.SetAsLastSibling();
            toastUntil = Time.unscaledTime + 1.5f;
        }

        internal List<string> ValidateToolbox()
        {
            return toolbox.Validate(nativePages);
        }

        internal List<string> ValidateNativeVisualLanguage()
        {
            var errors = new List<string>();
            var frame = SettingsWindow.transform.Find("WindowFrame")?.GetComponent<Image>();
            if (frame == null || frame.sprite == null || frame.sprite.name != "NativePanelLarge")
            {
                errors.Add("settings window does not use the native large nine-slice frame");
            }
            foreach (var tab in tabs)
            {
                if (tab.Background == null
                    || tab.Background.sprite == null
                    || tab.Background.sprite.name != "NativeButton")
                {
                    errors.Add("settings tab does not use the native button nine-slice: " + tab.Root.name);
                }
            }
            var booleanCount = 0;
            foreach (var page in nativePages)
            {
                booleanCount += page.GetComponentsInChildren<PreviewBooleanControl>(true).Length;
            }
            if (booleanCount < 8)
            {
                errors.Add("native settings pages do not contain the expected enable/disable checkbox groups");
            }
            if (toolbox.Root.GetComponentsInChildren<PreviewToolboxCheckboxControl>(true).Length < 6)
            {
                errors.Add("toolbox does not use V2 checkbox controls");
            }
            var hasSurface = false;
            var hasControl = false;
            var hasIconButton = false;
            foreach (var image in toolbox.Root.GetComponentsInChildren<Image>(true))
            {
                hasSurface |= image.sprite != null && image.sprite.name == "ToolboxSurface";
                hasControl |= image.sprite != null && image.sprite.name == "ToolboxControl";
                hasIconButton |= image.sprite != null && image.sprite.name.StartsWith("ToolboxIconButton-");
                if (image.sprite != null
                    && (image.sprite.name == "NativeButton"
                        || image.sprite.name == "NativePanelSmall"))
                {
                    errors.Add("toolbox directly reuses ornate native control: " + image.gameObject.name);
                }
            }
            if (!hasSurface || !hasControl || !hasIconButton)
            {
                errors.Add("toolbox V2 surface/control/icon-button resources are incomplete");
            }
            return errors;
        }

        private void BuildWindow(Transform parent)
        {
            var tabLabels = new[] { "音画", "游戏", "反馈", "键位", "妙妙工具" };
            for (var i = 0; i < tabLabels.Length; i++)
            {
                var index = i;
                var width = i == 4 ? 128f : 116f;
                var x = 4f + i * 120f;
                var tabRoot = PreviewUi.Rect(
                    "Tab-" + tabLabels[i],
                    parent,
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(0f, 1f),
                    new Vector2(width, i == 4 ? 58f : 54f),
                    new Vector2(x, 0f));
                var background = PreviewUi.Image(tabRoot, Color.white, PreviewAssets.NativeButton);
                var button = tabRoot.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SelectTab(index));
                var label = PreviewUi.FillText("Label", tabRoot.transform, tabLabels[i], 24, TextAnchor.MiddleCenter, PreviewTheme.Text, new Vector4(6f, 3f, 6f, 3f), true);
                tabs.Add(new TabView { Root = tabRoot, Background = background, Label = label });
            }

            var surfaceOuter = PreviewUi.Stretch("WindowFrame", parent, new Vector4(0f, PreviewTheme.TabHeight - 2f, 0f, 0f));
            PreviewUi.Image(surfaceOuter, Color.white, PreviewAssets.NativePanelLarge);
            var surface = PreviewUi.Stretch("WindowSurface", surfaceOuter.transform, new Vector4(8f, 8f, 8f, 8f));
            PreviewUi.Image(surface, PreviewTheme.Window);

            var nativeHost = PreviewUi.Stretch("NativePageHost", surface.transform, new Vector4(12f, 12f, 12f, 64f));
            nativePages.Add(NativeSettingsPageBuilder.BuildAudioVisual(nativeHost.transform));
            nativePages.Add(NativeSettingsPageBuilder.BuildGame(nativeHost.transform));
            nativePages.Add(NativeSettingsPageBuilder.BuildFeedback(nativeHost.transform));
            nativePages.Add(NativeSettingsPageBuilder.BuildKeyBindings(nativeHost.transform));

            var toolboxHost = PreviewUi.Stretch("ToolboxPageHost", surface.transform, new Vector4(10f, 10f, 10f, 10f));
            toolbox = new ToolboxPreviewPage(this);
            toolbox.Build(toolboxHost.transform);

            footer = PreviewUi.Rect(
                "Footer",
                surface.transform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(-24f, 46f),
                new Vector2(0f, 10f));
            var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
            footerLayout.spacing = 10f;
            footerLayout.childAlignment = TextAnchor.MiddleRight;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = false;
            footerLayout.childForceExpandHeight = true;
            var spacer = PreviewUi.Rect("Spacer", footer.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            PreviewUi.Flexible(spacer, 1f, 0f);
            var returnButton = PreviewUi.NativeButton("Return", footer.transform, "返回主菜单", () => ShowToast("独立预览不会切换游戏场景。"), 18);
            PreviewUi.Fixed(returnButton.gameObject, 158f, 46f);
            var exitButton = PreviewUi.NativeButton("Exit", footer.transform, "退出预览", Application.Quit, 18);
            PreviewUi.Fixed(exitButton.gameObject, 142f, 46f);

            BuildOverlay(parent);
        }

        private void BuildOverlay(Transform windowRoot)
        {
            overlay = PreviewUi.Stretch("SettingsOverlay", windowRoot, new Vector4(0f, PreviewTheme.TabHeight, 0f, 0f));
            PreviewUi.Image(overlay, new Color(0f, 0f, 0f, 0.72f)).raycastTarget = true;
            var window = PreviewUi.Rect(
                "Window",
                overlay.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(980f, 590f),
                Vector2.zero);
            PreviewUi.Image(window, Color.white, PreviewAssets.ToolboxSurface);
            var surface = PreviewUi.Stretch("Surface", window.transform, new Vector4(4f, 4f, 4f, 4f));
            PreviewUi.Image(surface, PreviewTheme.Background);
            var header = PreviewUi.Rect("Header", surface.transform, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(0f, 58f), Vector2.zero);
            PreviewUi.Image(header, Color.white, PreviewAssets.ToolboxControl);
            overlayTitle = PreviewUi.FillText("Title", header.transform, "设置", 18, TextAnchor.MiddleLeft, PreviewTheme.Accent, new Vector4(16f, 6f, 62f, 6f), true);
            var close = PreviewUi.ToolboxIconButton("Close", header.transform, "clear", CloseOverlay, 42f);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(42f, 42f);
            closeRect.anchoredPosition = new Vector2(-8f, 0f);
            var body = PreviewUi.Stretch("Body", surface.transform, new Vector4(16f, 74f, 16f, 16f));
            genericOverlayContent = PreviewUi.Stretch("Generic", body.transform, Vector4.zero);
            var marker = PreviewUi.Rect("Marker", genericOverlayContent.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(74f, 3f), Vector2.zero);
            PreviewUi.Image(marker, PreviewTheme.Accent).raycastTarget = false;
            overlaySummary = PreviewUi.FillText("Summary", genericOverlayContent.transform, "", 15, TextAnchor.UpperLeft, PreviewTheme.MutedText, new Vector4(0f, 24f, 0f, 150f));
            var placeholder = PreviewUi.Rect("Placeholder", genericOverlayContent.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(0f, 82f));
            PreviewUi.Image(placeholder, PreviewTheme.Control).raycastTarget = false;
            roleCgOverlayContent = CgSettingsPreviewPage.BuildRole(body.transform);
            eventCgOverlayContent = CgSettingsPreviewPage.BuildEvent(body.transform, false);
            eventCgPreviewContent = CgSettingsPreviewPage.BuildEvent(body.transform, true);
            SetOverlayContent(genericOverlayContent);
            overlay.SetActive(false);
        }

        private void SetOverlayContent(GameObject active)
        {
            foreach (var content in new[]
                     {
                         genericOverlayContent,
                         roleCgOverlayContent,
                         eventCgOverlayContent,
                         eventCgPreviewContent
                     })
            {
                if (content != null)
                {
                    content.SetActive(content == active);
                }
            }
        }

        private void BuildPreviewChrome(Transform parent)
        {
            previewChrome = PreviewUi.Rect(
                "PreviewChrome",
                parent,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(112f, 44f),
                new Vector2(-4f, -8f));
            var button = PreviewUi.Button("Scenario", previewChrome.transform, "场景：默认", CycleScenario, PreviewTheme.Panel, PreviewTheme.ControlHighlighted, 12);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            scenarioLabel = button.GetComponentInChildren<Text>();
        }

        private void BuildToast(Transform parent)
        {
            toast = PreviewUi.Rect(
                "Toast",
                parent,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(360f, 46f),
                new Vector2(-24f, 24f));
            PreviewUi.Image(toast, PreviewTheme.Panel);
            var marker = PreviewUi.Rect("Marker", toast.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(3f, 0f), Vector2.zero);
            PreviewUi.Image(marker, PreviewTheme.Accent).raycastTarget = false;
            toastText = PreviewUi.FillText("Text", toast.transform, "", 13, TextAnchor.MiddleLeft, PreviewTheme.Text, new Vector4(14f, 4f, 10f, 4f), true);
            toast.SetActive(false);
        }

        private void CycleScenario()
        {
            scenarioIndex = (scenarioIndex + 1) % scenarioOrder.Length;
            SetToolboxScenario(scenarioOrder[scenarioIndex]);
            if (SelectedTabIndex != 4) SelectTab(4);
        }

        private void CloseOverlay()
        {
            if (overlay != null)
            {
                overlay.SetActive(false);
            }
        }

        private void CaptureManualScreenshot()
        {
            var directory = Path.Combine(Application.persistentDataPath, "Screenshots");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "AuraToolsUiPreview-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            ShowToast("截图已保存：" + path);
        }

        private static Camera CreateCamera()
        {
            var root = new GameObject("Main Camera");
            root.tag = "MainCamera";
            var camera = root.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = PreviewTheme.Stage;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = -100f;
            camera.farClipPlane = 100f;
            return camera;
        }

        private static Canvas CreateCanvas(Camera camera)
        {
            var root = new GameObject("PreviewCanvas", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(PreviewTheme.ReferenceWidth, PreviewTheme.ReferenceHeight);
            scaler.matchWidthOrHeight = 0.12f;
            root.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void CreateEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            var root = new GameObject("EventSystem");
            root.AddComponent<EventSystem>();
            root.AddComponent<StandaloneInputModule>();
        }

        private static string NormalizeScenario(string value)
        {
            var normalized = (value ?? "default").Trim().ToLowerInvariant();
            foreach (var scenario in new[] { "default", "long-text", "warning", "empty", "extensions" })
            {
                if (normalized == scenario) return scenario;
            }
            return "default";
        }

        private static string ScenarioDisplay(string scenario)
        {
            switch (scenario)
            {
                case "long-text": return "长文本";
                case "warning": return "异常";
                case "empty": return "空结果";
                case "extensions": return "扩展";
                default: return "默认";
            }
        }
    }
}
