using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using Terrias.Dll.Hooks;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

public static class PolymorphRoleSelectionWindow
{
    private const string WindowName = "Terrias_PolymorphRoleSelection";
    private const float RoleCardWidth = 142f;
    private const float RoleCardHeight = 188f;
    private const float RoleImageSize = 118f;
    private static readonly Color WindowTint = new(0.04f, 0.045f, 0.075f, 0.98f);
    private static readonly Color HeaderTint = new(0.055f, 0.05f, 0.075f, 0.98f);
    private static readonly Color CardTint = new(0.075f, 0.075f, 0.105f, 0.98f);
    private static readonly Color Gold = new(0.88f, 0.77f, 0.45f);
    private static readonly Color TextColor = new(0.94f, 0.92f, 0.84f);
    private static GameObject? activeRoot;
    private static Transform? roleListContent;
    private static Text? hintText;

    public static bool Open(ScriptExecutor self)
    {
        return Open(self, PolymorphRoleSelectionRequest.Polymorph(self));
    }

    public static bool Open(ScriptExecutor self, PolymorphRoleSelectionRequest request)
    {
        try
        {
            Close("PolymorphRoleSelection.Open");
            request ??= PolymorphRoleSelectionRequest.Polymorph(self);
            var parent = TerriasModalHost.ModalParent();
            if (parent == null)
            {
                TerriasLog.Warn("[PolymorphRoleSelection] skipped: UI canvas unavailable.");
                return false;
            }

            var roles = PolymorphRoleRegistry.AllRoles();
            if (roles.Count == 0)
            {
                TerriasLog.Warn("[PolymorphRoleSelection] no registered roles.");
                return false;
            }

            activeRoot = TerriasModalHost.CreateFullscreenRoot(
                WindowName,
                parent,
                new Color(0f, 0f, 0f, 0.72f));
            var localization = TerriasLocalizationScope.Attach(activeRoot);
            TerriasTransientUiRegistry.Register("PolymorphRoleSelection", Close);

            var window = CreateRect("Window", activeRoot.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ResolveWindowSize(parent));
            ApplyPanelImage(window.gameObject, WindowTint);
            var layout = window.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 18, 16);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateHeader(window.transform, roles.Count, request, localization);
            roleListContent = CreateRoleScroll(window.transform);
            for (var i = 0; i < roles.Count; i++)
            {
                CreateRoleCard(roleListContent, self, roles[i], i, request);
            }

            CreateFooter(window.transform, request, localization);
            TerriasLog.Info("[" + request.LogPrefix + "] opened; roles=" + roles.Count);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Failed to open role selection", ex);
            Close("PolymorphRoleSelection.OpenFailed");
            return false;
        }
    }

    public static void Close(string source)
    {
        TerriasUiPool.ReleaseOrDestroyChildren(roleListContent, "PolymorphRoleSelection.Close.RoleList", "[PolymorphRoleSelection]");
        TerriasModalHost.Close(ref activeRoot, source, "[PolymorphRoleSelection]");
        roleListContent = null;
        hintText = null;
        TerriasTransientUiRegistry.Unregister("PolymorphRoleSelection");
    }

    private static void CreateHeader(
        Transform parent,
        int roleCount,
        PolymorphRoleSelectionRequest request,
        TerriasLocalizationScope localization)
    {
        var header = CreateLayoutObject("Header", parent);
        header.AddComponent<LayoutElement>().preferredHeight = 74f;
        ApplyPanelImage(header, HeaderTint);
        var layout = header.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 8, 8);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var title = AddTextBlock(header.transform, request.Title, 28, TextAnchor.MiddleCenter, Gold, 32f);
        localization.Bind(title, () => TerriasTextCatalog.ResolveLegacy(request.Title));
        var subtitle = AddTextBlock(header.transform, request.Subtitle + roleCount, 15, TextAnchor.MiddleCenter, TextColor, 22f);
        localization.Bind(subtitle, () => TerriasTextCatalog.ResolveLegacy(request.Subtitle) + roleCount);
    }

    private static Transform CreateRoleScroll(Transform parent)
    {
        var root = CreateLayoutObject("RoleScroll", parent);
        var element = root.AddComponent<LayoutElement>();
        element.flexibleHeight = 1f;
        element.minHeight = 420f;
        ApplyPanelImage(root, new Color(0.025f, 0.028f, 0.045f, 0.96f));

        var viewport = CreateRect("Viewport", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(12f, 12f);
        viewport.offsetMax = new Vector2(-12f, -12f);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.08f);
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(RoleCardWidth, RoleCardHeight);
        grid.spacing = new Vector2(12f, 12f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.childAlignment = TextAnchor.UpperCenter;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        return content;
    }

    private static void CreateFooter(
        Transform parent,
        PolymorphRoleSelectionRequest request,
        TerriasLocalizationScope localization)
    {
        var footer = CreateLayoutObject("Footer", parent);
        footer.AddComponent<LayoutElement>().preferredHeight = 42f;
        ApplyPanelImage(footer, HeaderTint);
        var layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 5, 5);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        hintText = AddTextBlock(footer.transform, request.FooterHint, 14, TextAnchor.MiddleLeft, TextColor, 30f, 1f);
        localization.Bind(hintText, () => TerriasTextCatalog.ResolveLegacy(request.FooterHint));
        CreateButton(footer.transform, "关闭", new Vector2(102f, 30f), () => Close("PolymorphRoleSelection.CloseButton"));
    }

    private static void CreateRoleCard(Transform parent, ScriptExecutor executor, PolymorphRoleSpec role, int index, PolymorphRoleSelectionRequest request)
    {
        var view = TerriasUiPool.AcquireComponent(
            "PolymorphRoleSelection.RoleCard",
            parent,
            "Role-" + role.Id,
            CreateRoleCardTemplate);
        view.Bind(role, index < ImmediateWarmupCount(), () =>
        {
            if (request.Select(executor, role))
            {
                Close("PolymorphRoleSelection.RoleSelected");
            }
            else if (hintText != null)
            {
                var scope = TerriasLocalizationScope.Find(hintText.transform);
                if (scope != null) scope.Bind(hintText, () => request.SelectionFailureText);
                else hintText.text = request.SelectionFailureText;
            }
        });

        if (index >= ImmediateWarmupCount())
        {
            ScheduleDeferredRoleImage(view, role, index);
        }
    }

    private static void ScheduleDeferredRoleImage(RoleCardView view, PolymorphRoleSpec role, int index)
    {
        TerriasFrameScheduler.RunOnceNextFrame("PolymorphRoleSelection.RoleImage." + index, () =>
        {
            view.EnsureImage(role);
        });
    }

    private static RoleCardView CreateRoleCardTemplate(Transform parent, string name)
    {
        var root = CreateLayoutObject(name, parent);
        var element = root.AddComponent<LayoutElement>();
        element.minWidth = RoleCardWidth;
        element.preferredWidth = RoleCardWidth;
        element.minHeight = RoleCardHeight;
        element.preferredHeight = RoleCardHeight;
        var background = ApplyPanelImage(root, CardTint);
        background.raycastTarget = true;
        var layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 8);
        layout.spacing = 7f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var imageHost = CreateLayoutObject("ImageHost", root.transform);
        var imageElement = imageHost.AddComponent<LayoutElement>();
        imageElement.minHeight = RoleImageSize;
        imageElement.preferredHeight = RoleImageSize;
        imageElement.minWidth = RoleImageSize;
        imageElement.preferredWidth = RoleImageSize;
        TerriasUiBuilder.ApplyPanelImage(imageHost, TerriasUiSprites.Panel("[PolymorphRoleSelection]"), new Color(0.02f, 0.02f, 0.035f, 0.92f));
        var imageRect = CreateRect("RoleImage", imageHost.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        imageRect.offsetMin = new Vector2(4f, 4f);
        imageRect.offsetMax = new Vector2(-4f, -4f);
        var image = imageRect.gameObject.AddComponent<Image>();
        image.color = Color.white;
        image.preserveAspect = false;
        image.raycastTarget = false;

        var nameText = AddTextBlock(root.transform, "", 15, TextAnchor.MiddleCenter, TextColor, 32f);
        var lockText = AddTextBlock(root.transform, "", 11, TextAnchor.MiddleCenter, Gold, 18f);
        var button = root.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, background, Gold);

        var view = root.AddComponent<RoleCardView>();
        view.Initialize(image, nameText, lockText, button);
        return view;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 size, Action action)
    {
        var go = CreateLayoutObject("Button-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = size.x;
        element.preferredWidth = size.x;
        element.minHeight = size.y;
        element.preferredHeight = size.y;
        var image = go.AddComponent<Image>();
        image.sprite = TerriasUiSprites.Button("[PolymorphRoleSelection]");
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = image.sprite != null ? Color.white : new Color(0.08f, 0.075f, 0.12f, 0.98f);
        var button = go.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, Gold);
        button.onClick.AddListener(() => action());
        AddTextFill(go.transform, label, 14, TextAnchor.MiddleCenter, TextColor);
        return button;
    }

    private static Image ApplyPanelImage(GameObject go, Color fallbackOrTint)
    {
        return TerriasUiBuilder.ApplyPanelImage(go, TerriasUiSprites.Panel("[PolymorphRoleSelection]"), fallbackOrTint);
    }

    private static Text AddTextBlock(Transform parent, string value, int fontSize, TextAnchor anchor, Color color, float preferredHeight, float flexibleWidth = 0f)
    {
        var go = CreateLayoutObject("Text", parent);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = preferredHeight;
        element.preferredHeight = preferredHeight;
        if (flexibleWidth > 0f)
        {
            element.flexibleWidth = flexibleWidth;
        }

        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text AddTextFill(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var go = CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return ConfigureText(go.gameObject, value, fontSize, anchor, color);
    }

    private static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
    {
        return TerriasUiComponents.ConfigureText(go, value, fontSize, anchor, color);
    }

    private static GameObject CreateLayoutObject(string name, Transform parent)
    {
        return TerriasUiComponents.CreateLayoutObject(name, parent);
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        return TerriasUiComponents.CreateRectTransform(name, parent, anchorMin, anchorMax, pivot, sizeDelta);
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var available = new Vector2(Screen.width, Screen.height);
        if (parent is RectTransform rect && rect.rect.width > 0f && rect.rect.height > 0f)
        {
            available = rect.rect.size;
        }

        var width = Mathf.Min(980f, Mathf.Max(680f, available.x - 70f));
        var height = Mathf.Min(740f, Mathf.Max(560f, available.y - 48f));
        return new Vector2(width, height);
    }

    private static int ImmediateWarmupCount()
    {
        return 12;
    }

    private static int DeferredWarmupBatchSize()
    {
        return 6;
    }

    private sealed class RoleCardView : TerriasPooledUiBehaviour
    {
        private readonly TerriasUiLifetimeScope lifetime = new();
        private Image? image;
        private Text? nameText;
        private Text? lockText;
        private Button? button;

        public void Initialize(Image image, Text nameText, Text lockText, Button button)
        {
            this.image = image;
            this.nameText = nameText;
            this.lockText = lockText;
            this.button = button;
        }

        private string roleId = "";

        public void Bind(PolymorphRoleSpec role, bool loadImageNow, Action onClick)
        {
            lifetime.Clear();
            roleId = role.Id;
            if (nameText != null)
            {
                var scope = TerriasLocalizationScope.Find(nameText.transform);
                if (scope != null) scope.Bind(nameText, () => role.DisplayName);
                else nameText.text = role.DisplayName;
            }

            if (lockText != null)
            {
                var scope = TerriasLocalizationScope.Find(lockText.transform);
                if (scope != null)
                {
                    scope.Bind(lockText, () => role.IsLocked
                        ? TerriasTextCatalog.Get("ui.role_selection.locked")
                        : "");
                }
                else
                {
                    lockText.text = role.IsLocked ? "未解锁" : "";
                }
            }

            if (image != null)
            {
                image.sprite = null;
                image.gameObject.SetActive(false);
                if (loadImageNow)
                {
                    EnsureImage(role);
                }
            }

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.interactable = true;
                lifetime.Listen(button, () => onClick());
            }
        }

        public void EnsureImage(PolymorphRoleSpec role)
        {
            if (image == null
                || !gameObject.activeInHierarchy
                || !string.Equals(roleId, role.Id, StringComparison.Ordinal))
            {
                return;
            }

            var asset = PolymorphCardFaceCache.GetOrCreate(role);
            image.sprite = asset?.Sprite;
            image.gameObject.SetActive(asset != null);
        }

        public override void ResetForPool()
        {
            lifetime.Clear();
            roleId = "";
            if (image != null)
            {
                image.sprite = null;
                image.gameObject.SetActive(false);
            }

            if (nameText != null)
            {
                nameText.text = "";
            }

            if (lockText != null)
            {
                lockText.text = "";
            }

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.interactable = false;
            }
        }
    }
}
