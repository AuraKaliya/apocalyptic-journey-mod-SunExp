using System;
using System.Collections.Generic;
using AuraOnline.Shared;
using ChatExp.Dll.GameApi;
using ChatExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Object = UnityEngine.Object;

namespace ChatExp.Dll.UI;

public static class AuraChatUi
{
    private const string RootName = "ChatExpAuraChatUI";
    private static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.07f, 0.88f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.20f, 0.23f, 0.96f);
    private static readonly Color ActiveButtonColor = new Color(0.25f, 0.39f, 0.45f, 0.96f);
    private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.90f, 1f);
    private static readonly Color MutedTextColor = new Color(0.68f, 0.72f, 0.72f, 1f);

    private static GameObject? root;
    private static RectTransform? chatContent;
    private static RectTransform? statusContent;
    private static GameObject? chatPage;
    private static GameObject? statusPage;
    private static InputField? input;
    private static Text? counter;
    private static Button? chatTab;
    private static Button? statusTab;
    private static Font? font;
    private static bool subscribed;
    private static string activeArea = AuraChatAreas.Chat;

    public static void Ensure()
    {
        if (root != null)
        {
            root.SetActive(true);
            RefreshAll();
            return;
        }

        var canvas = FindCanvas();
        if (canvas == null)
        {
            ChatExpLog.Warn("Canvas not found; AuraChatUI creation skipped.");
            return;
        }

        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        root = new GameObject(RootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);

        var rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 88f);
        rect.sizeDelta = new Vector2(420f, 330f);
        root.GetComponent<Image>().color = PanelColor;

        BuildHeader(rect);
        BuildPages(rect);
        BuildInputBar(rect);
        Subscribe();
        ShowArea(AuraChatAreas.Chat);
        RefreshAll();
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
    }

    private static void BuildPages(RectTransform parent)
    {
        chatPage = CreatePage(parent, "ChatPage", 42f, 54f, out chatContent);
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

    private static void BuildInputBar(RectTransform parent)
    {
        var bar = CreateRect("InputBar", parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(-16f, 38f));
        var layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 6f;
        layout.childControlHeight = true;
        layout.childControlWidth = false;

        input = CreateInput(bar);
        input.onValueChanged.AddListener(OnInputChanged);

        counter = CreateText("Counter", bar, "0/20", 14, MutedTextColor, TextAnchor.MiddleCenter);
        counter.gameObject.AddComponent<LayoutElement>().preferredWidth = 46f;

        CreateButton(bar, "发送", 70f, SendCurrentInput);
    }

    private static InputField CreateInput(Transform parent)
    {
        var rootObject = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField), typeof(LayoutElement));
        rootObject.transform.SetParent(parent, false);
        rootObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.11f);
        rootObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var text = CreateText("Text", rootObject.transform, string.Empty, 15, TextColor, TextAnchor.MiddleLeft);
        var textRect = (RectTransform)text.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 2f);
        textRect.offsetMax = new Vector2(-8f, -2f);
        text.supportRichText = false;

        var placeholder = CreateText("Placeholder", rootObject.transform, "输入聊天...", 15, MutedTextColor, TextAnchor.MiddleLeft);
        var placeholderRect = (RectTransform)placeholder.transform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(8f, 2f);
        placeholderRect.offsetMax = new Vector2(-8f, -2f);

        var field = rootObject.GetComponent<InputField>();
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = InputField.LineType.SingleLine;
        field.characterLimit = 256;
        return field;
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
        RefreshMessages();
        RefreshStatus();
        UpdateCounter();
        ShowArea(activeArea);
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

    private static void SendCurrentInput()
    {
        if (input == null)
        {
            return;
        }

        var text = AuraChatTextLimiter.LimitPlayerText(input.text);
        if (ChatExpNetworkApi.SendPlayerText(text))
        {
            input.text = string.Empty;
            UpdateCounter();
        }
    }

    private static void OnInputChanged(string value)
    {
        if (input == null)
        {
            return;
        }

        var limited = AuraChatTextLimiter.LimitPlayerText(value);
        if (!string.Equals(value, limited, StringComparison.Ordinal))
        {
            input.text = limited;
            input.MoveTextEnd(false);
            return;
        }

        UpdateCounter();
    }

    private static void UpdateCounter()
    {
        if (counter == null)
        {
            return;
        }

        counter.text = AuraChatEmojiParser.DisplayLength(input?.text ?? string.Empty).ToString() + "/20";
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

    private static Canvas? FindCanvas()
    {
        var gameObject = GameObject.Find("Canvas");
        if (gameObject != null)
        {
            return gameObject.GetComponent<Canvas>();
        }

#pragma warning disable CS0618
        return Object.FindObjectOfType<Canvas>();
#pragma warning restore CS0618
    }

    private static void ClearChildren(Transform parent)
    {
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            Object.Destroy(parent.GetChild(index).gameObject);
        }
    }
}
