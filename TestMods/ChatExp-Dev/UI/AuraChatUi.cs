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
    private const float PanelWidth = 420f;
    private const float PanelHeight = 330f;
    private const float ToggleSize = 56f;
    private const float EdgeMargin = 8f;
    private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.07f, 0.88f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.20f, 0.23f, 0.96f);
    private static readonly Color ActiveButtonColor = new Color(0.25f, 0.39f, 0.45f, 0.96f);
    private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.90f, 1f);
    private static readonly Color MutedTextColor = new Color(0.68f, 0.72f, 0.72f, 1f);

    private static GameObject? toggleButton;
    private static RectTransform? toggleRect;
    private static GameObject? root;
    private static RectTransform? chatContent;
    private static RectTransform? statusContent;
    private static RectTransform? choiceContent;
    private static GameObject? chatPage;
    private static GameObject? statusPage;
    private static Button? chatTab;
    private static Button? statusTab;
    private static Font? font;
    private static bool subscribed;
    private static bool expanded;
    private static string activeArea = AuraChatAreas.Chat;
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
        chatContent = null;
        statusContent = null;
        choiceContent = null;
        chatPage = null;
        statusPage = null;
        chatTab = null;
        statusTab = null;
    }

    private static void BuildToggle(Transform parent)
    {
        toggleButton = new GameObject(ToggleName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        toggleButton.transform.SetParent(parent, false);
        toggleRect = (RectTransform)toggleButton.transform;
        toggleRect.anchorMin = new Vector2(1f, 0f);
        toggleRect.anchorMax = new Vector2(1f, 0f);
        toggleRect.pivot = new Vector2(1f, 0f);
        toggleRect.sizeDelta = new Vector2(ToggleSize, ToggleSize);
        toggleRect.anchoredPosition = ClampToParent(savedButtonPosition, toggleRect, new Vector2(ToggleSize, ToggleSize));

        toggleButton.GetComponent<Image>().color = ActiveButtonColor;

        var label = CreateText("Label", toggleButton.transform, "Chat", 15, TextColor, TextAnchor.MiddleCenter);
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var dragHandle = toggleButton.AddComponent<AuraChatDragHandle>();
        dragHandle.Initialize(toggleRect, OnToggleDragged, ToggleExpanded);
    }

    private static void BuildPanel(Transform parent)
    {
        root = new GameObject(RootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(parent, false);

        var rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        root.GetComponent<Image>().color = PanelColor;

        BuildHeader(rect);
        BuildPages(rect);
        BuildChoiceBar(rect);
    }

    private static void BuildHeader(RectTransform parent)
    {
        var header = CreateRect("Header", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(-12f, 38f));
        var layout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 6f;
        layout.childControlWidth = false;
        layout.childControlHeight = true;

        chatTab = CreateButton(header, "聊天", 92f, () => ShowArea(AuraChatAreas.Chat));
        statusTab = CreateButton(header, "同步状态", 112f, () => ShowArea(AuraChatAreas.ModSyncStatus));

        var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(header, false);
        spacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

        CreateButton(header, "清空", 72f, AuraChatRuntime.ClearMessages);
        CreateButton(header, "收起", 72f, Collapse);
    }

    private static void BuildPages(RectTransform parent)
    {
        chatPage = CreatePage(parent, "ChatPage", 42f, 84f, out chatContent);
        statusPage = CreatePage(parent, "StatusPage", 42f, 12f, out statusContent);
    }

    private static GameObject CreatePage(RectTransform parent, string name, float top, float bottom, out RectTransform content)
    {
        var page = CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(-16f, -(top + bottom))).gameObject;
        var pageRect = (RectTransform)page.transform;
        pageRect.offsetMin = new Vector2(8f, bottom);
        pageRect.offsetMax = new Vector2(-8f, -top);

        var viewport = CreateRect("Viewport", pageRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.22f);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        content = CreateRect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-10f, 0f));
        var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(8, 8, 8, 8);
        contentLayout.spacing = 6f;
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

    private static void BuildChoiceBar(RectTransform parent)
    {
        choiceContent = CreateRect("ChoiceBar", parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(-16f, 68f));
        var bar = choiceContent;
        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 4, 4);
        layout.spacing = 6f;
        layout.childControlHeight = true;
        layout.childControlWidth = false;
    }

    private static Button CreateButton(Transform parent, string label, float width, UnityEngine.Events.UnityAction action)
    {
        var rootObject = new GameObject(label + "Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        rootObject.transform.SetParent(parent, false);
        rootObject.GetComponent<Image>().color = ButtonColor;
        rootObject.GetComponent<LayoutElement>().preferredWidth = width;

        var button = rootObject.GetComponent<Button>();
        button.onClick.AddListener(action);
        var colors = button.colors;
        colors.highlightedColor = ActiveButtonColor;
        colors.pressedColor = new Color(0.12f, 0.28f, 0.32f, 1f);
        button.colors = colors;

        var text = CreateText("Label", rootObject.transform, label, 15, TextColor, TextAnchor.MiddleCenter);
        var textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
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
        if (choiceContent == null)
        {
            return;
        }

        ClearChildren(choiceContent);
        if (!AuraChatCatalogStore.IsReady)
        {
            var disabled = CreateButton(choiceContent, "资源校验失败", 126f, () => { });
            disabled.interactable = false;
            return;
        }

        foreach (var message in AuraChatCatalogStore.Messages)
        {
            var label = string.IsNullOrWhiteSpace(message.Text) ? message.Id : message.Text;
            CreateButton(choiceContent, label, Mathf.Max(72f, Mathf.Min(118f, label.Length * 18f)), () => ChatExpNetworkApi.SendPresetMessage(message.Id));
        }

        foreach (var sticker in AuraChatCatalogStore.Stickers)
        {
            CreateButton(choiceContent, "[" + sticker.Id + "]", 86f, () => ChatExpNetworkApi.SendSticker(sticker.Id));
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
    }

    private static void AddChatRow(RectTransform parent, AuraChatMessage message)
    {
        var row = CreateRect("Message", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 28f));
        row.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 2f;
        layout.childForceExpandWidth = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        var name = CreateText("Name", row, (message.SenderName ?? "Player") + ": ", 14, new Color(0.68f, 0.86f, 0.92f, 1f), TextAnchor.UpperLeft);
        name.gameObject.AddComponent<LayoutElement>().preferredWidth = 82f;

        var body = CreateRect("Body", row, Vector2.zero, Vector2.one, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        body.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var bodyLayout = body.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 2f;
        bodyLayout.childForceExpandWidth = false;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;

        foreach (var segment in AuraChatEmojiParser.Parse(message.RawText))
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

    private static void AddSegmentText(RectTransform parent, string text)
    {
        var node = CreateText("TextSegment", parent, text, 14, TextColor, TextAnchor.UpperLeft);
        node.horizontalOverflow = HorizontalWrapMode.Wrap;
        node.gameObject.AddComponent<LayoutElement>().preferredWidth = Mathf.Min(220f, Mathf.Max(24f, text.Length * 14f));
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
        imageObject.GetComponent<Image>().sprite = sprite;
        var layout = imageObject.GetComponent<LayoutElement>();
        layout.preferredWidth = 24f;
        layout.preferredHeight = 24f;
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

        var status = AuraChatRuntime.ModSyncStatus;
        if (string.IsNullOrWhiteSpace(status))
        {
            status = "等待大厅玩家信息更新。";
        }

        foreach (var line in AuraChatTextLimiter.WrapPlainText(AuraChatTextLimiter.LimitSystemLine(status)).Split('\n'))
        {
            var text = CreateText("StatusLine", statusContent, line, 14, TextColor, TextAnchor.UpperLeft);
            text.gameObject.AddComponent<LayoutElement>().minHeight = 22f;
        }
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
        ApplyExpandedState();
    }

    private static void Collapse()
    {
        expanded = false;
        ApplyExpandedState();
    }

    private static void ApplyExpandedState()
    {
        if (root != null)
        {
            root.SetActive(expanded);
        }

        if (toggleButton != null)
        {
            toggleButton.SetActive(true);
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

        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = active ? ActiveButtonColor : ButtonColor;
        }
    }

    private static Transform? FindUiParent()
    {
        var manager = UIManager.Instance;
        if (manager?.upperCanvasTf != null)
        {
            return manager.upperCanvasTf;
        }

        if (manager?.canvasTf != null)
        {
            return manager.canvasTf;
        }

        var upperCanvas = GameObject.Find("Upper Canvas");
        if (upperCanvas != null)
        {
            return upperCanvas.transform;
        }

        var canvas = GameObject.Find("Canvas");
        if (canvas != null)
        {
            return canvas.transform;
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

    private sealed class AuraChatDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private RectTransform? target;
        private Action<Vector2>? onDragged;
        private Action? onClicked;
        private Vector2 dragStartPosition;
        private Vector2 pointerStartPosition;
        private bool dragged;

        public void Initialize(RectTransform dragTarget, Action<Vector2> dragCallback, Action clickCallback)
        {
            target = dragTarget;
            onDragged = dragCallback;
            onClicked = clickCallback;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!dragged)
            {
                onClicked?.Invoke();
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target == null)
            {
                return;
            }

            dragged = false;
            dragStartPosition = target.anchoredPosition;
            pointerStartPosition = eventData.position;
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
                eventData.Use();
            }
        }
    }
}
