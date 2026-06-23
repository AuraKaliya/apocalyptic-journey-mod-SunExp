using System;
using System.Collections.Generic;
using AuraOnline.Shared;
using ChatExp.Dll.GameApi;
using ChatExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.Core;
using Witch.UI;
using Object = UnityEngine.Object;

namespace ChatExp.Dll.UI;

public static class AuraChatUi
{
    private const string RootName = "ChatExpAuraChatUI";
    private const string ToggleName = "ChatExpAuraChatToggle";
    private const string ButtonTintName = "ButtonTint";
    private const string ButtonSpritePath = "Mods/ChatExp/ModResource/Images/UI/button-\u4e5d\u5bab\u683c.png";
    private const string PanelSpritePath = "Mods/ChatExp/ModResource/Images/UI/background-\u4e5d\u5bab\u683c.png";
    private const string ToggleIconPath = "Mods/ChatExp/ModResource/Images/UI/chat.png";
    private const float PanelWidth = 700f;
    private const float PanelHeight = 430f;
    private const float ToggleSize = 54f;
    private const float HeaderHeight = 44f;
    private const float PickerWidth = 224f;
    private const float PickerTabButtonWidth = 106f;
    private const float PickerTabHeight = 40f;
    private const float StickerChoiceSize = 64f;
    private const float ChatStickerSize = 32f;
    private const float StatusModColumnWidth = 150f;
    private const float StatusPlayerColumnWidth = 96f;
    private const float MessageNameWidth = 104f;
    private const float MessageBodyWidth = 310f;
    private const float EdgeMargin = 8f;
    private const string PickerQuick = "Quick";
    private const string PickerSticker = "Sticker";
    private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.07f, 0.84f);
    private static readonly Color ViewportColor = new Color(0f, 0f, 0f, 0.28f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.20f, 0.23f, 0.94f);
    private static readonly Color ActiveButtonColor = new Color(0.23f, 0.38f, 0.44f, 0.96f);
    private static readonly Color StickerCellTintColor = new Color(0.07f, 0.08f, 0.10f, 0.72f);
    private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.90f, 1f);
    private static readonly Color ActiveTextColor = new Color(1f, 0.86f, 0.48f, 1f);
    private static readonly Color MutedTextColor = new Color(0.68f, 0.72f, 0.72f, 1f);

    private static GameObject? toggleButton;
    private static RectTransform? toggleRect;
    private static GameObject? root;
    private static CanvasGroup? panelCanvasGroup;
    private static RectTransform? chatContent;
    private static RectTransform? statusContent;
    private static ScrollRect? chatScroll;
    private static ScrollRect? pickerScroll;
    private static RectTransform? quickPickerContent;
    private static RectTransform? stickerPickerContent;
    private static GameObject? chatPage;
    private static GameObject? statusPage;
    private static Button? chatTab;
    private static Button? statusTab;
    private static Button? quickPickerTab;
    private static Button? stickerPickerTab;
    private static Sprite? buttonSprite;
    private static Sprite? panelSprite;
    private static Sprite? toggleIconSprite;
    private static Font? font;
    private static bool buttonSpriteLoadAttempted;
    private static bool panelSpriteLoadAttempted;
    private static bool toggleIconLoadAttempted;
    private static bool subscribed;
    private static bool available;
    private static bool expanded;
    private static string activeArea = AuraChatAreas.Chat;
    private static string activePicker = PickerQuick;
    private static Vector2 savedButtonPosition = new(-24f, 88f);

    public static void Ensure()
    {
        if (toggleButton != null && root != null)
        {
            ApplyExpandedState();
            RefreshAll();
            return;
        }

        DestroyPartialUi();
        var parent = FindUiParent();
        if (parent == null)
        {
            ChatExpLog.Warn("UI parent not found; AuraChatUI creation skipped.");
            return;
        }

        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        BuildToggle(parent);
        BuildPanel(parent);
        Subscribe();
        ShowArea(AuraChatAreas.Chat);
        RefreshAll();
        ApplyExpandedState();
    }

    public static void EnsureAvailable(string reason)
    {
        Ensure();
        SetAvailable(true, reason);
    }

    public static void SetAvailable(bool value, string reason)
    {
        if (available == value)
        {
            ApplyExpandedState();
            return;
        }

        available = value;
        if (!available)
        {
            expanded = false;
        }

        ChatExpLog.Info("UI availability changed: available=" + available + ", reason=" + reason);
        ApplyExpandedState();
    }

    private static void DestroyPartialUi()
    {
        if (toggleButton != null)
        {
            Object.Destroy(toggleButton);
        }

        if (root != null)
        {
            Object.Destroy(root);
        }

        toggleButton = null;
        toggleRect = null;
        root = null;
        panelCanvasGroup = null;
        chatContent = null;
        statusContent = null;
        chatScroll = null;
        pickerScroll = null;
        quickPickerContent = null;
        stickerPickerContent = null;
        chatPage = null;
        statusPage = null;
        chatTab = null;
        statusTab = null;
        quickPickerTab = null;
        stickerPickerTab = null;
    }

    private static void BuildToggle(Transform parent)
    {
        toggleButton = new GameObject(ToggleName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        toggleButton.transform.SetParent(parent, false);
        toggleRect = (RectTransform)toggleButton.transform;
        toggleRect.anchorMin = new Vector2(1f, 0f);
        toggleRect.anchorMax = new Vector2(1f, 0f);
        toggleRect.pivot = new Vector2(1f, 0f);
        toggleRect.sizeDelta = new Vector2(ToggleSize, ToggleSize);
        toggleRect.anchoredPosition = ClampToParent(savedButtonPosition, toggleRect, new Vector2(ToggleSize, ToggleSize));

        var toggleImage = toggleButton.GetComponent<Image>();
        toggleImage.color = new Color(1f, 1f, 1f, 0f);
        toggleImage.raycastTarget = true;

        if (!AddToggleIcon(toggleButton.transform))
        {
            var label = CreateText("Label", toggleButton.transform, "Chat", 15, TextColor, TextAnchor.MiddleCenter);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        var dragHandle = toggleButton.AddComponent<AuraChatDragHandle>();
        dragHandle.Initialize(toggleRect, OnToggleDragged, ToggleExpanded);
    }

    private static void BuildPanel(Transform parent)
    {
        root = new GameObject(RootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(parent, false);
        panelCanvasGroup = root.AddComponent<CanvasGroup>();

        var rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        var panelImage = ApplyPanelImage(root, PanelColor);
        panelImage.raycastTarget = false;

        BuildHeader(rect);
        BuildPages(rect);
        ChatExpLog.Info("UI panel built. parent=" + parent.name + ", size=" + PanelWidth + "x" + PanelHeight);
    }

    private static void BuildHeader(RectTransform parent)
    {
        var header = CreateRect("Header", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(-16f, HeaderHeight));
        var layout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.spacing = 8f;
        layout.childControlWidth = false;
        layout.childControlHeight = true;

        chatTab = CreateButton(header, "聊天", 98f, () => ShowArea(AuraChatAreas.Chat));
        statusTab = CreateButton(header, "同步状态", 120f, () => ShowArea(AuraChatAreas.ModSyncStatus));

        var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(header, false);
        spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

        CreateButton(header, "清空", 78f, AuraChatRuntime.ClearMessages);
        CreateButton(header, "收起", 78f, Collapse);
    }

    private static void BuildPages(RectTransform parent)
    {
        chatPage = CreateChatPage(parent, HeaderHeight + 12f, 12f, out chatContent);
        statusPage = CreatePage(parent, "StatusPage", HeaderHeight + 12f, 14f, out statusContent);
    }

    private static GameObject CreateChatPage(RectTransform parent, float top, float bottom, out RectTransform content)
    {
        var page = CreateRect("ChatPage", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-16f, -(top + bottom))).gameObject;
        var pageRect = (RectTransform)page.transform;
        pageRect.offsetMin = new Vector2(8f, bottom);
        pageRect.offsetMax = new Vector2(-8f, -top);

        var layout = page.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        var chatLog = CreateRect("ChatLogArea", pageRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var chatLogElement = chatLog.gameObject.AddComponent<LayoutElement>();
        chatLogElement.flexibleWidth = 1f;
        chatLogElement.minWidth = 420f;
        content = BuildVerticalScrollContent(chatLog, "ChatLogContent");
        chatScroll = chatLog.GetComponent<ScrollRect>();

        BuildPickerArea(pageRect);
        return page;
    }

    private static GameObject CreatePage(RectTransform parent, string name, float top, float bottom, out RectTransform content)
    {
        var page = CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(-16f, -(top + bottom))).gameObject;
        var pageRect = (RectTransform)page.transform;
        pageRect.offsetMin = new Vector2(8f, bottom);
        pageRect.offsetMax = new Vector2(-8f, -top);

        var viewport = CreateRect("Viewport", pageRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = ViewportColor;
        viewportImage.raycastTarget = false;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        content = CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-14f, 0f));
        var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(12, 12, 10, 10);
        contentLayout.spacing = 3f;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = page.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return page;
    }

    private static RectTransform BuildVerticalScrollContent(RectTransform parent, string contentName)
    {
        var viewport = CreateRect("Viewport", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = ViewportColor;
        viewportImage.raycastTarget = false;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var content = CreateRect(contentName, viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-14f, 0f));
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 2f;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = parent.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }

    private static void BuildPickerArea(RectTransform parent)
    {
        var picker = CreateRect("PickerArea", parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var pickerElement = picker.gameObject.AddComponent<LayoutElement>();
        pickerElement.preferredWidth = PickerWidth;
        pickerElement.minWidth = PickerWidth;
        pickerElement.flexibleWidth = 0f;

        var pickerLayout = picker.gameObject.AddComponent<VerticalLayoutGroup>();
        pickerLayout.spacing = 6f;
        pickerLayout.childControlWidth = true;
        pickerLayout.childControlHeight = true;
        pickerLayout.childForceExpandWidth = true;
        pickerLayout.childForceExpandHeight = false;

        var tabs = CreateRect("PickerTabs", picker, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, PickerTabHeight));
        var tabsElement = tabs.gameObject.AddComponent<LayoutElement>();
        tabsElement.preferredHeight = PickerTabHeight;
        tabsElement.minHeight = PickerTabHeight;
        tabsElement.flexibleHeight = 0f;
        var tabsLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 6f;
        tabsLayout.childControlWidth = false;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandHeight = false;

        quickPickerTab = CreateButton(tabs, "快捷信息", PickerTabButtonWidth, () => ShowPicker(PickerQuick), PickerTabHeight);
        stickerPickerTab = CreateButton(tabs, "表情包", PickerTabButtonWidth, () => ShowPicker(PickerSticker), PickerTabHeight);

        var viewport = CreateRect("PickerViewport", picker, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.16f);
        viewportImage.raycastTarget = false;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        quickPickerContent = CreateRect("QuickPickerContent", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-10f, 0f));
        var quickLayout = quickPickerContent.gameObject.AddComponent<VerticalLayoutGroup>();
        quickLayout.padding = new RectOffset(4, 4, 4, 4);
        quickLayout.spacing = 3f;
        quickLayout.childControlHeight = true;
        quickLayout.childControlWidth = true;
        quickLayout.childForceExpandHeight = false;
        var quickFitter = quickPickerContent.gameObject.AddComponent<ContentSizeFitter>();
        quickFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        stickerPickerContent = CreateRect("StickerPickerContent", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(-10f, 0f));
        var stickerLayout = stickerPickerContent.gameObject.AddComponent<GridLayoutGroup>();
        stickerLayout.padding = new RectOffset(4, 4, 4, 4);
        stickerLayout.spacing = new Vector2(4f, 4f);
        stickerLayout.cellSize = new Vector2(StickerChoiceSize, StickerChoiceSize);
        stickerLayout.childAlignment = TextAnchor.UpperLeft;
        stickerLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
        stickerLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        stickerLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        stickerLayout.constraintCount = 3;
        var stickerFitter = stickerPickerContent.gameObject.AddComponent<ContentSizeFitter>();
        stickerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        stickerPickerContent.gameObject.SetActive(false);

        pickerScroll = viewport.gameObject.AddComponent<ScrollRect>();
        pickerScroll.viewport = viewport;
        pickerScroll.content = quickPickerContent;
        pickerScroll.horizontal = false;
        pickerScroll.vertical = true;
        pickerScroll.movementType = ScrollRect.MovementType.Clamped;
    }

    private static Button CreateButton(Transform parent, string label, float width, UnityEngine.Events.UnityAction action, float? height = null)
    {
        var rootObject = new GameObject(label + "Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        rootObject.transform.SetParent(parent, false);
        var image = ApplyButtonImage(rootObject, ButtonColor);
        image.raycastTarget = true;
        var element = rootObject.GetComponent<LayoutElement>();
        element.preferredWidth = width;
        if (height.HasValue)
        {
            element.preferredHeight = height.Value;
            element.minHeight = height.Value;
            element.flexibleHeight = 0f;
        }

        var button = rootObject.GetComponent<Button>();
        button.targetGraphic = FindTint(rootObject.transform) ?? image;
        button.onClick.AddListener(() =>
        {
            ChatExpLog.Info("UI button clicked: " + label);
            action();
        });
        button.colors = ConfigureButtonColors(button.colors, image.sprite != null, false);

        var text = CreateText("Label", rootObject.transform, label, 15, TextColor, TextAnchor.MiddleCenter);
        var textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    private static Image ApplyButtonImage(GameObject target, Color fallbackTint)
    {
        var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
        image.sprite = GetButtonSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? Color.white : fallbackTint;
        if (image.sprite != null)
        {
            AddInsetTint(target, fallbackTint, new Vector2(6f, 6f));
        }

        return image;
    }

    private static Image ApplyPanelImage(GameObject target, Color fallbackOrTint)
    {
        var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
        image.sprite = GetPanelSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, fallbackOrTint.a) : fallbackOrTint;
        if (image.sprite != null)
        {
            AddInsetTint(target, fallbackOrTint, new Vector2(3f, 3f));
        }

        return image;
    }

    private static Image AddInsetTint(GameObject target, Color color, Vector2 inset)
    {
        var tint = new GameObject(ButtonTintName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        tint.transform.SetParent(target.transform, false);
        tint.transform.SetAsFirstSibling();
        var rect = (RectTransform)tint.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = inset;
        rect.offsetMax = -inset;
        var image = tint.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static bool AddToggleIcon(Transform parent)
    {
        var sprite = GetToggleIconSprite();
        if (sprite == null)
        {
            return false;
        }

        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.transform.SetParent(parent, false);
        var rect = (RectTransform)iconObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(5f, 5f);
        rect.offsetMax = new Vector2(-5f, -5f);
        var image = iconObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return true;
    }

    private static Sprite? GetToggleIconSprite()
    {
        if (toggleIconSprite != null)
        {
            return toggleIconSprite;
        }

        if (toggleIconLoadAttempted)
        {
            return null;
        }

        toggleIconLoadAttempted = true;
        try
        {
            toggleIconSprite = ResourceLoader.Load<Sprite>(ToggleIconPath, true);
            if (toggleIconSprite == null)
            {
                ChatExpLog.Warn("Toggle icon missing: " + ToggleIconPath);
            }
        }
        catch (Exception ex)
        {
            ChatExpLog.Warn("Toggle icon load failed: " + ToggleIconPath + " -> " + ex.Message);
        }

        return toggleIconSprite;
    }

    private static Sprite? GetButtonSprite()
    {
        if (buttonSprite != null)
        {
            return buttonSprite;
        }

        if (buttonSpriteLoadAttempted)
        {
            return null;
        }

        buttonSpriteLoadAttempted = true;
        buttonSprite = CreateNineSliceSprite(ButtonSpritePath, new Vector4(14f, 8f, 14f, 8f));
        return buttonSprite;
    }

    private static Sprite? GetPanelSprite()
    {
        if (panelSprite != null)
        {
            return panelSprite;
        }

        if (panelSpriteLoadAttempted)
        {
            return null;
        }

        panelSpriteLoadAttempted = true;
        panelSprite = CreateNineSliceSprite(PanelSpritePath, new Vector4(4f, 4f, 4f, 4f));
        return panelSprite;
    }

    private static Sprite? CreateNineSliceSprite(string path, Vector4 border)
    {
        try
        {
            var source = ResourceLoader.Load<Sprite>(path, true);
            if (source == null || source.texture == null)
            {
                ChatExpLog.Warn("UI sprite missing: " + path);
                return null;
            }

            var texture = source.texture;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(
                texture,
                source.rect,
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
        }
        catch (Exception ex)
        {
            ChatExpLog.Warn("UI sprite load failed: " + path + " -> " + ex.Message);
            return null;
        }
    }

    private static Text CreateText(string name, Transform parent, string value, int size, Color color, TextAnchor anchor)
    {
        var rootObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        rootObject.transform.SetParent(parent, false);
        var text = rootObject.GetComponent<Text>();
        text.font = font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        var rootObject = new GameObject(name, typeof(RectTransform));
        rootObject.transform.SetParent(parent, false);
        var rect = (RectTransform)rootObject.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        return rect;
    }

    private static void ShowArea(string area)
    {
        activeArea = area;
        if (chatPage != null)
        {
            chatPage.SetActive(area == AuraChatAreas.Chat);
        }

        if (statusPage != null)
        {
            statusPage.SetActive(area == AuraChatAreas.ModSyncStatus);
        }

        SetButtonColor(chatTab, area == AuraChatAreas.Chat);
        SetButtonColor(statusTab, area == AuraChatAreas.ModSyncStatus);
    }

    private static void RefreshAll()
    {
        UpdatePanelPosition();
        RefreshMessages();
        RefreshStatus();
        RefreshChoices();
        ShowArea(activeArea);
    }

    private static void RefreshChoices()
    {
        if (quickPickerContent == null || stickerPickerContent == null || pickerScroll == null)
        {
            return;
        }

        ClearChildren(quickPickerContent);
        ClearChildren(stickerPickerContent);
        SetButtonColor(quickPickerTab, activePicker == PickerQuick);
        SetButtonColor(stickerPickerTab, activePicker == PickerSticker);
        var activeContent = activePicker == PickerSticker ? stickerPickerContent : quickPickerContent;
        quickPickerContent.gameObject.SetActive(activePicker == PickerQuick);
        stickerPickerContent.gameObject.SetActive(activePicker == PickerSticker);
        pickerScroll.content = activeContent;
        pickerScroll.verticalNormalizedPosition = 1f;

        if (!AuraChatCatalogStore.IsReady)
        {
            var disabled = CreatePickerListButton(activeContent, "资源校验失败", () => { });
            disabled.interactable = false;
            return;
        }

        if (activePicker == PickerQuick)
        {
            foreach (var message in AuraChatCatalogStore.Messages)
            {
                var label = string.IsNullOrWhiteSpace(message.Text) ? message.Id : message.Text;
                CreatePickerListButton(quickPickerContent, label, () => ChatExpNetworkApi.SendPresetMessage(message.Id));
            }

            return;
        }

        foreach (var sticker in AuraChatCatalogStore.Stickers)
        {
            CreateStickerPickerButton(stickerPickerContent, sticker);
        }
    }

    private static void ShowPicker(string picker)
    {
        activePicker = picker == PickerSticker ? PickerSticker : PickerQuick;
        RefreshChoices();
    }

    private static Button CreatePickerListButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
    {
        var button = CreateButton(parent, label, PickerWidth - 20f, action);
        var element = button.GetComponent<LayoutElement>();
        if (element != null)
        {
            element.preferredHeight = 42f;
            element.minHeight = 42f;
        }

        return button;
    }

    private static Button CreateStickerPickerButton(Transform parent, AuraChatCatalogSticker sticker)
    {
        var rootObject = new GameObject(sticker.Id + "StickerButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        rootObject.transform.SetParent(parent, false);
        var background = ApplyPanelImage(rootObject, StickerCellTintColor);
        background.raycastTarget = true;

        var element = rootObject.GetComponent<LayoutElement>();
        element.preferredWidth = StickerChoiceSize;
        element.preferredHeight = StickerChoiceSize;
        element.minWidth = StickerChoiceSize;
        element.minHeight = StickerChoiceSize;

        var button = rootObject.GetComponent<Button>();
        button.targetGraphic = FindTint(rootObject.transform) ?? background;
        button.onClick.AddListener(() =>
        {
            ChatExpLog.Info("UI sticker clicked: " + sticker.Id);
            ChatExpNetworkApi.SendSticker(sticker.Id);
        });
        button.colors = ConfigureButtonColors(button.colors, background.sprite != null, false);

        var sprite = TryLoadStickerSprite(sticker);
        if (sprite != null)
        {
            var imageObject = new GameObject("StickerImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(rootObject.transform, false);
            var imageRect = (RectTransform)imageObject.transform;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = new Vector2(4f, 4f);
            imageRect.offsetMax = new Vector2(-4f, -4f);
            var image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        else
        {
            var label = CreateText("Label", rootObject.transform, sticker.Id, 13, TextColor, TextAnchor.MiddleCenter);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 4f);
            labelRect.offsetMax = new Vector2(-4f, -4f);
        }

        return button;
    }

    private static Sprite? TryLoadStickerSprite(AuraChatCatalogSticker sticker)
    {
        var spec = AuraChatStickerRegistry.Resolve(sticker.PackId, sticker.StickerId);
        if (spec == null)
        {
            return null;
        }

        try
        {
            return ResourceLoader.Load<Sprite>(spec.ResourcePath, true);
        }
        catch (Exception ex)
        {
            ChatExpLog.Warn("Sticker picker load failed: " + spec.ResourcePath + " -> " + ex.Message);
            return null;
        }
    }

    private static void RefreshMessages()
    {
        if (chatContent == null)
        {
            return;
        }

        ClearChildren(chatContent);
        foreach (var message in AuraChatRuntime.Messages)
        {
            if (message.Area == AuraChatAreas.Chat)
            {
                AddChatRow(chatContent, message);
            }
        }

        ScrollChatToBottom();
    }

    private static void ScrollChatToBottom()
    {
        if (chatScroll == null || chatContent == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
        Canvas.ForceUpdateCanvases();
        chatScroll.verticalNormalizedPosition = 0f;
        chatScroll.velocity = Vector2.zero;
    }

    private static void AddChatRow(RectTransform parent, AuraChatMessage message)
    {
        var row = CreateRect("Message", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 28f));
        row.gameObject.AddComponent<LayoutElement>().minHeight = EstimateMessageHeight(message.RawText);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.childForceExpandWidth = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var name = CreateText("Name", row, (message.SenderName ?? "Player") + ": ", 14, new Color(0.68f, 0.86f, 0.92f, 1f), TextAnchor.UpperLeft);
        name.gameObject.AddComponent<LayoutElement>().preferredWidth = MessageNameWidth;

        var segments = AuraChatEmojiParser.Parse(message.RawText);
        if (!ContainsSticker(segments))
        {
            AddFullMessageText(row, message.RawText);
            return;
        }

        var body = CreateRect("Body", row, Vector2.zero, Vector2.one, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        var bodyElement = body.gameObject.AddComponent<LayoutElement>();
        bodyElement.preferredWidth = MessageBodyWidth;
        bodyElement.flexibleWidth = 0f;
        var bodyLayout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 2f;
        bodyLayout.childForceExpandWidth = false;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;

        foreach (var segment in segments)
        {
            if (string.Equals(segment.Kind, "Sticker", StringComparison.Ordinal))
            {
                AddSticker(body, segment);
            }
            else if (!string.IsNullOrEmpty(segment.Text))
            {
                AddSegmentText(body, segment.Text);
            }
        }
    }

    private static bool ContainsSticker(IEnumerable<AuraChatRenderSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (string.Equals(segment.Kind, "Sticker", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddFullMessageText(RectTransform parent, string text)
    {
        var node = CreateText("MessageText", parent, text, 14, TextColor, TextAnchor.UpperLeft);
        node.horizontalOverflow = HorizontalWrapMode.Wrap;
        var layout = node.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = MessageBodyWidth;
        layout.minWidth = MessageBodyWidth;
        layout.flexibleWidth = 0f;
        layout.minHeight = EstimateTextHeight(text);
    }

    private static void AddSegmentText(RectTransform parent, string text)
    {
        var node = CreateText("TextSegment", parent, text, 14, TextColor, TextAnchor.UpperLeft);
        node.horizontalOverflow = HorizontalWrapMode.Wrap;
        var layout = node.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = MessageBodyWidth;
        layout.minWidth = Mathf.Min(MessageBodyWidth, 96f);
        layout.flexibleWidth = 0f;
        layout.minHeight = EstimateTextHeight(text);
    }

    private static void AddSticker(RectTransform parent, AuraChatRenderSegment segment)
    {
        var spec = AuraChatStickerRegistry.Resolve(segment.PackId ?? string.Empty, segment.StickerId ?? string.Empty);
        Sprite? sprite = null;
        if (spec != null)
        {
            try
            {
                sprite = ResourceLoader.Load<Sprite>(spec.ResourcePath, true);
            }
            catch (Exception ex)
            {
                ChatExpLog.Warn("Sticker load failed: " + spec.ResourcePath + " -> " + ex.Message);
            }
        }

        if (sprite == null)
        {
            AddSegmentText(parent, AuraChatEmojiParser.StickerFallback(segment.PackId ?? string.Empty, segment.StickerId ?? string.Empty));
            return;
        }

        var imageObject = new GameObject("Sticker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        imageObject.transform.SetParent(parent, false);
        var image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        var layout = imageObject.GetComponent<LayoutElement>();
        layout.preferredWidth = ChatStickerSize;
        layout.preferredHeight = ChatStickerSize;
        layout.minWidth = ChatStickerSize;
        layout.minHeight = ChatStickerSize;
    }

    private static void RefreshStatus()
    {
        if (statusContent == null)
        {
            return;
        }

        ClearChildren(statusContent);
        foreach (var line in AuraChatTextLimiter.WrapPlainText(AuraChatCatalogStore.Status).Split('\n'))
        {
            var catalogText = CreateText("CatalogStatusLine", statusContent, line, 14, AuraChatCatalogStore.IsReady ? TextColor : new Color(1f, 0.62f, 0.52f, 1f), TextAnchor.UpperLeft);
            catalogText.gameObject.AddComponent<LayoutElement>().minHeight = 22f;
        }

        AddHostModSyncAction(statusContent);

        var status = AuraChatRuntime.ModSyncStatus;
        if (string.IsNullOrWhiteSpace(status))
        {
            status = "等待大厅玩家信息更新。";
        }

        foreach (var line in AuraChatTextLimiter.LimitSystemLine(status).Split('\n'))
        {
            if (line.IndexOf('\t') >= 0)
            {
                CreateStatusTableRow(statusContent, line.Split('\t'));
                continue;
            }

            var text = CreateText("StatusLine", statusContent, line, 14, TextColor, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.gameObject.AddComponent<LayoutElement>().minHeight = 20f;
        }
    }

    private static void CreateStatusTableRow(RectTransform parent, string[] cells)
    {
        var row = CreateRect("StatusTableRow", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 20f));
        row.gameObject.AddComponent<LayoutElement>().minHeight = 20f;

        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        for (var index = 0; index < cells.Length; index++)
        {
            var width = index == 0 ? StatusModColumnWidth : StatusPlayerColumnWidth;
            var cell = CreateText("StatusCell", row, cells[index], 14, TextColor, TextAnchor.UpperLeft);
            cell.horizontalOverflow = HorizontalWrapMode.Wrap;
            var element = cell.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.flexibleWidth = 0f;
            element.minHeight = 20f;
        }
    }

    private static void AddHostModSyncAction(RectTransform parent)
    {
        var pending = AuraChatHostModSyncService.CountPendingActions(AuraChatRuntime.ModSyncState);
        var row = CreateRect("HostModSyncAction", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 38f));
        row.gameObject.AddComponent<LayoutElement>().minHeight = 38f;

        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var button = CreateButton(row, "\u540c\u6b65\u623f\u4e3bMOD", 132f, AuraChatHostModSyncService.StartSync, 34f);
        button.interactable = !AuraChatHostModSyncService.IsRunning && pending > 0;

        var status = AuraChatRuntime.ModSyncActionStatus;
        if (string.IsNullOrWhiteSpace(status))
        {
            status = pending > 0
                ? "\u5f85\u540c\u6b65 " + pending + " \u9879"
                : "\u5f53\u524d\u6ca1\u6709\u9700\u8981\u540c\u6b65\u7684\u623f\u4e3bMOD\u5dee\u5f02";
        }

        var text = CreateText("HostModSyncStatus", row, status, 14, MutedTextColor, TextAnchor.MiddleLeft);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        var textElement = text.gameObject.AddComponent<LayoutElement>();
        textElement.flexibleWidth = 1f;
        textElement.minHeight = 34f;
    }

    private static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }

        subscribed = true;
        AuraChatRuntime.Changed += RefreshMessages;
        AuraChatRuntime.StatusChanged += RefreshStatus;
    }

    private static void ToggleExpanded()
    {
        expanded = !expanded;
        ChatExpLog.Info("UI toggle clicked. expanded=" + expanded);
        ApplyExpandedState();
    }

    private static void Collapse()
    {
        ChatExpLog.Info("UI collapse requested.");
        expanded = false;
        ApplyExpandedState();
    }

    private static void ApplyExpandedState()
    {
        var panelVisible = available && expanded;
        if (root != null)
        {
            root.SetActive(panelVisible);
            if (panelVisible)
            {
                root.transform.SetAsLastSibling();
            }
        }

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = panelVisible ? 1f : 0f;
            panelCanvasGroup.interactable = panelVisible;
            panelCanvasGroup.blocksRaycasts = panelVisible;
        }

        if (toggleButton != null)
        {
            toggleButton.SetActive(available);
            if (available)
            {
                toggleButton.transform.SetAsLastSibling();
            }
        }

        UpdatePanelPosition();
    }

    private static void OnToggleDragged(Vector2 anchoredPosition)
    {
        if (toggleRect == null)
        {
            return;
        }

        savedButtonPosition = ClampToParent(anchoredPosition, toggleRect, new Vector2(ToggleSize, ToggleSize));
        toggleRect.anchoredPosition = savedButtonPosition;
        UpdatePanelPosition();
    }

    private static void UpdatePanelPosition()
    {
        if (root == null || toggleRect == null)
        {
            return;
        }

        var panelRect = (RectTransform)root.transform;
        var abovePosition = savedButtonPosition + new Vector2(0f, ToggleSize + 12f);
        panelRect.anchoredPosition = ClampToParent(abovePosition, panelRect, new Vector2(PanelWidth, PanelHeight));
    }

    private static void SetButtonColor(Button? button, bool active)
    {
        if (button == null)
        {
            return;
        }

        var tint = FindTint(button.transform);
        if (tint != null)
        {
            tint.color = ButtonColor;
            button.colors = ConfigureButtonColors(button.colors, true, false);
            SetButtonTextColor(button.transform, active);
            return;
        }

        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = ButtonColor;
            button.colors = ConfigureButtonColors(button.colors, false, false);
            SetButtonTextColor(button.transform, active);
        }
    }

    private static void SetButtonTextColor(Transform parent, bool active)
    {
        var label = parent.Find("Label");
        var text = label != null ? label.GetComponent<Text>() : null;
        if (text != null)
        {
            text.color = active ? ActiveTextColor : TextColor;
        }
    }

    private static ColorBlock ConfigureButtonColors(ColorBlock colors, bool useTintLayer, bool active)
    {
        var normal = active ? ActiveButtonColor : ButtonColor;
        var highlighted = active
            ? new Color(0.28f, 0.44f, 0.50f, 1f)
            : new Color(0.21f, 0.27f, 0.31f, 0.98f);
        colors.normalColor = normal;
        colors.highlightedColor = highlighted;
        colors.pressedColor = new Color(0.12f, 0.22f, 0.26f, 1f);
        colors.selectedColor = highlighted;
        colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0.45f);
        colors.colorMultiplier = useTintLayer ? 1f : 1.05f;
        colors.fadeDuration = 0.06f;
        return colors;
    }

    private static Image? FindTint(Transform parent)
    {
        var child = parent.Find(ButtonTintName);
        return child != null ? child.GetComponent<Image>() : null;
    }

    private static Transform? FindUiParent()
    {
        var manager = UIManager.Instance;
        if (manager?.canvasTf != null)
        {
            return manager.canvasTf;
        }

        var canvas = GameObject.Find("Canvas");
        if (canvas != null)
        {
            return canvas.transform;
        }

        if (manager?.upperCanvasTf != null)
        {
            return manager.upperCanvasTf;
        }

        var upperCanvas = GameObject.Find("Upper Canvas");
        if (upperCanvas != null)
        {
            return upperCanvas.transform;
        }

#pragma warning disable CS0618
        return Object.FindObjectOfType<Canvas>()?.transform;
#pragma warning restore CS0618
    }

    private static Vector2 ClampToParent(Vector2 position, RectTransform rect, Vector2 size)
    {
        var parent = rect.parent as RectTransform;
        if (parent == null)
        {
            return position;
        }

        var bounds = parent.rect;
        if (bounds.width <= 0f || bounds.height <= 0f)
        {
            return position;
        }

        var minX = -bounds.width + size.x + EdgeMargin;
        var maxX = -EdgeMargin;
        var minY = EdgeMargin;
        var maxY = bounds.height - size.y - EdgeMargin;
        if (maxX < minX)
        {
            minX = maxX = -EdgeMargin;
        }

        if (maxY < minY)
        {
            minY = maxY = EdgeMargin;
        }

        return new Vector2(
            Mathf.Clamp(position.x, minX, maxX),
            Mathf.Clamp(position.y, minY, maxY));
    }

    private static void ClearChildren(Transform parent)
    {
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            Object.Destroy(parent.GetChild(index).gameObject);
        }
    }

    private static float EstimateMessageHeight(string? rawText)
    {
        var textHeight = EstimateTextHeight(rawText, 62, 20f, 18f);
        return ContainsSticker(AuraChatEmojiParser.Parse(rawText ?? ""))
            ? Mathf.Max(textHeight, ChatStickerSize + 4f)
            : textHeight;
    }

    private static float EstimateTextHeight(string? rawText)
    {
        return EstimateTextHeight(rawText, 62, 20f, 18f);
    }

    private static float EstimateTextHeight(string? rawText, int lineUnits, float minHeight, float lineHeight)
    {
        var length = Mathf.Max(1, AuraChatEmojiParser.DisplayLength(rawText ?? ""));
        var lines = Mathf.Max(1, Mathf.CeilToInt(length / (float)Mathf.Max(1, lineUnits)));
        return Mathf.Max(minHeight, lines * lineHeight);
    }

    private sealed class AuraChatDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private const float ClickMoveThresholdSqr = 36f;
        private RectTransform? target;
        private Action<Vector2>? onDragged;
        private Action? onClicked;
        private Vector2 dragStartPosition;
        private Vector2 pointerStartPosition;
        private Vector2 pointerDownPosition;
        private bool dragged;
        private bool pointerDownSeen;
        private bool suppressNextClick;

        public void Initialize(RectTransform dragTarget, Action<Vector2> dragCallback, Action clickCallback)
        {
            target = dragTarget;
            onDragged = dragCallback;
            onClicked = clickCallback;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pointerDownSeen = true;
            pointerDownPosition = eventData.position;
            dragged = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            var clickStart = pointerDownSeen ? pointerDownPosition : eventData.pressPosition;
            var clickDelta = eventData.position - clickStart;
            if (suppressNextClick || dragged || clickDelta.sqrMagnitude > ClickMoveThresholdSqr)
            {
                ChatExpLog.Info("UI toggle click suppressed after drag. delta=" + clickDelta);
                suppressNextClick = false;
                dragged = false;
                pointerDownSeen = false;
                eventData.Use();
                return;
            }

            dragged = false;
            pointerDownSeen = false;
            ChatExpLog.Info("UI toggle pointer click accepted. position=" + eventData.position);
            onClicked?.Invoke();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target == null)
            {
                return;
            }

            dragged = false;
            suppressNextClick = false;
            dragStartPosition = target.anchoredPosition;
            pointerStartPosition = eventData.position;
            pointerDownPosition = eventData.position;
            pointerDownSeen = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null || onDragged == null)
            {
                return;
            }

            var scaleFactor = 1f;
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0f)
            {
                scaleFactor = canvas.scaleFactor;
            }

            var delta = (eventData.position - pointerStartPosition) / scaleFactor;
            if (delta.sqrMagnitude > 16f)
            {
                dragged = true;
            }

            onDragged(dragStartPosition + delta);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragged)
            {
                suppressNextClick = true;
                eventData.Use();
            }
        }
    }
}
