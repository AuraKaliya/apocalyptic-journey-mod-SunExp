using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public static class EndlessAbyssShockPanel
{
    private const string PanelName = "SunExp_EndlessAbyssShockPanel";
    private const float HeaderHeight = 96f;
    private const float OptionHeight = 96f;
    private const float OptionSpacing = 10f;
    private const float StrategyPaddingHorizontal = 18f;
    private const float StrategyPaddingVertical = 18f;
    private const float StrategyMinHeight = OptionHeight * 2.25f + OptionSpacing + StrategyPaddingVertical * 2f;
    private const float StrategyPreferredHeight = OptionHeight * 3f + OptionSpacing * 2f + StrategyPaddingVertical * 2f;
    private const float ButtonWidth = 120f;
    private const float ButtonHeight = 46f;
    private const float FooterHeight = 54f;
    private const int ButtonFontSize = 16;

    private static readonly Color WindowTint = new(0.026f, 0.028f, 0.045f, 0.98f);
    private static readonly Color HeaderTint = new(0.055f, 0.046f, 0.07f, 0.98f);
    private static readonly Color OptionTint = new(0.07f, 0.072f, 0.1f, 0.98f);
    private static readonly Color SelectedTint = new(0.21f, 0.15f, 0.06f, 0.98f);
    private static readonly Color Gold = new(0.92f, 0.78f, 0.42f);
    private static readonly Color SoftText = new(0.9f, 0.92f, 0.86f);
    private static GameObject? activePanel;
    private static Text? hintText;
    private static Button? confirmButton;
    private static readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Image> optionImages = new(StringComparer.Ordinal);

    public static bool IsOpen => activePanel != null;

    public static bool TryOpenPending(Action? onClosed, string source)
    {
        try
        {
            if (activePanel != null)
            {
                return true;
            }

            var request = EndlessAbyssShockService.PendingRequest();
            if (request == null)
            {
                return false;
            }

            Open(request, onClosed, source);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Endless abyss shock panel failed", ex);
            Close("EndlessAbyssShockPanel.OpenFailed");
            return false;
        }
    }

    private static void Open(EndlessAbyssShockRequest request, Action? onClosed, string source)
    {
        selected.Clear();
        optionImages.Clear();
        var parent = SunExpModalHost.ModalParent();
        if (parent == null)
        {
            return;
        }

        activePanel = SunExpModalHost.CreateFullscreenRoot(PanelName, parent, new Color(0f, 0f, 0f, 0.72f));
        SunExpTransientUiRegistry.Register("EndlessAbyssShock", Close);
        var window = CreateRect("Window", activePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), ResolveWindowSize(parent));
        SunExpUiBuilder.ApplyPanelImage(window, SunExpUiSprites.Panel("[EndlessAbyssShock]"), WindowTint, true);
        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 18, 14);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateHeader(window.transform, request);
        CreateOptions(window.transform);
        CreateFlexibleSpacer(window.transform);
        CreateFooter(window.transform, request, onClosed);
        RefreshSelectionHint();
        SunExpLog.Info("[EndlessAbyssShock] opened from " + source + "; key=" + request.Key + ".");
    }

    private static void CreateHeader(Transform parent, EndlessAbyssShockRequest request)
    {
        var header = CreateLayoutObject("Header", parent);
        var element = header.AddComponent<LayoutElement>();
        element.minHeight = HeaderHeight;
        element.preferredHeight = HeaderHeight;
        SunExpUiBuilder.ApplyPanelImage(header, SunExpUiSprites.Panel("[EndlessAbyssShock]"), HeaderTint, true);
        var layout = header.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 8, 8);
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        AddTextBlock(header.transform, SunExpIds.EndlessAbyssShockName, 28, TextAnchor.MiddleCenter, Gold, 34f);
        AddTextBlock(
            header.transform,
            SunExpIds.EndlessAbyssGazeName + " " + EndlessAbyssGazeService.CurrentLevel()
            + " / " + "\u5fc5\u9009 " + EndlessAbyssGazeService.RequiredShockChoices(),
            15,
            TextAnchor.MiddleCenter,
            SoftText,
            24f);
        AddTextBlock(header.transform, TriggerText(request), 13, TextAnchor.MiddleCenter, SoftText, 22f);
    }

    private static void CreateOptions(Transform parent)
    {
        var root = CreateLayoutObject("StrategyArea", parent);
        var element = root.AddComponent<LayoutElement>();
        element.minHeight = StrategyMinHeight;
        element.preferredHeight = StrategyPreferredHeight;
        element.flexibleHeight = 0f;
        SunExpUiBuilder.ApplyPanelImage(root, SunExpUiSprites.Panel("[EndlessAbyssShock]"), new Color(0.012f, 0.014f, 0.03f, 0.92f), true);

        var rootLayout = root.AddComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(
            (int)StrategyPaddingHorizontal,
            (int)StrategyPaddingHorizontal,
            (int)StrategyPaddingVertical,
            (int)StrategyPaddingVertical);
        rootLayout.spacing = 0f;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = true;

        var viewport = CreateLayoutObject("Viewport", root.transform);
        var viewportElement = viewport.AddComponent<LayoutElement>();
        viewportElement.minHeight = StrategyMinHeight - StrategyPaddingVertical * 2f;
        viewportElement.flexibleWidth = 1f;
        viewportElement.flexibleHeight = 1f;
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.pivot = new Vector2(0.5f, 0.5f);
        viewportRect.sizeDelta = Vector2.zero;
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.04f);
        viewportImage.raycastTarget = true;
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = SunExpUiBuilder.CreateRect(
            "StrategyContent",
            viewport.transform,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = OptionSpacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 26f;

        CreateOption(content, EndlessAbyssShockOptionIds.DestroyRelic, "\u9057\u7269\u5760\u843d", "\u968f\u673a\u9500\u6bc1 1 \u4ef6\u5df2\u88c5\u5907\u9057\u7269\u3002");
        CreateOption(content, EndlessAbyssShockOptionIds.AnnihilateCards, "\u6e6e\u706d\u6d78\u67d3", "\u7ed9\u5f53\u524d\u5361\u7ec4\u5185\u968f\u673a 3 \u5f20\u5361\u6dfb\u52a0\u6e6e\u706d\u3002");
        CreateOption(content, EndlessAbyssShockOptionIds.IncreaseGaze, "\u6ce8\u89c6\u52a0\u6df1", SunExpIds.EndlessAbyssGazeName + " +1\u3002");
    }

    private static void CreateOption(RectTransform parent, string id, string title, string body)
    {
        var go = CreateLayoutObject("Option-" + id, parent);
        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = OptionHeight;
        element.minHeight = OptionHeight;
        var images = EndlessAbyssFramedTextCard.Create(
            go,
            "[EndlessAbyssShock]",
            OptionTint,
            title,
            body,
            Gold,
            SoftText);
        optionImages[id] = images.TintTarget;

        var button = go.AddComponent<Button>();
        button.targetGraphic = images.ButtonTarget;
        button.onClick.AddListener(() => ToggleOption(id));
    }

    private static void CreateFooter(Transform parent, EndlessAbyssShockRequest request, Action? onClosed)
    {
        var footer = CreateLayoutObject("Footer", parent);
        var element = footer.AddComponent<LayoutElement>();
        element.minHeight = FooterHeight;
        element.preferredHeight = FooterHeight;
        var layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 4, 4);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleCenter;

        hintText = AddTextBlock(footer.transform, "", 14, TextAnchor.MiddleLeft, SoftText, 34f, 1f);
        confirmButton = CreateButton(footer.transform, "\u627f\u53d7", new Vector2(ButtonWidth, ButtonHeight), () =>
        {
            var result = EndlessAbyssShockService.ApplyPending(selected.ToArray(), "EndlessAbyssShockPanel");
            if (!result.Success)
            {
                SetHint(result.Message);
                return;
            }

            Close("EndlessAbyssShockPanel.Confirm");
            onClosed?.Invoke();
        });
    }

    private static void ToggleOption(string id)
    {
        if (selected.Contains(id))
        {
            selected.Remove(id);
        }
        else if (selected.Count < EndlessAbyssGazeService.RequiredShockChoices())
        {
            selected.Add(id);
        }

        RefreshSelectionHint();
    }

    private static void RefreshSelectionHint()
    {
        var required = EndlessAbyssGazeService.RequiredShockChoices();
        foreach (var pair in optionImages)
        {
            pair.Value.color = selected.Contains(pair.Key) ? SelectedTint : OptionTint;
        }

        if (confirmButton != null)
        {
            confirmButton.interactable = selected.Count == required;
        }

        SetHint("\u5df2\u9009 " + selected.Count + "/" + required + "\uff0c\u6df1\u6e0a\u9707\u8361\u5fc5\u987b\u7ed3\u7b97\u540e\u624d\u80fd\u7ee7\u7eed\u3002");
    }

    private static void SetHint(string value)
    {
        if (hintText != null)
        {
            hintText.text = value;
        }
    }

    private static string TriggerText(EndlessAbyssShockRequest request)
    {
        return "\u6765\u6e90\uff1a"
            + request.Trigger
            + " / \u5c42\u6570 "
            + Math.Max(1, request.Floor)
            + (string.IsNullOrWhiteSpace(request.NodeKind) ? "" : " / " + request.NodeKind);
    }

    public static void Close(string source)
    {
        selected.Clear();
        optionImages.Clear();
        confirmButton = null;
        hintText = null;
        SunExpModalHost.Close(ref activePanel, source, "[EndlessAbyssShock]");
        SunExpTransientUiRegistry.Unregister("EndlessAbyssShock");
    }

    private static Button CreateButton(Transform parent, string label, Vector2 size, Action action)
    {
        var go = CreateLayoutObject("Button-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = size.x;
        element.preferredWidth = size.x;
        element.minHeight = size.y;
        element.preferredHeight = size.y;
        element.flexibleWidth = 0f;
        element.flexibleHeight = 0f;
        var image = go.AddComponent<Image>();
        image.sprite = SunExpUiSprites.Button("[EndlessAbyssShock]");
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.color = image.sprite != null ? Color.white : new Color(0.08f, 0.07f, 0.11f, 0.98f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        AddTextFill(go.transform, label, ButtonFontSize, TextAnchor.MiddleCenter, SoftText);
        return button;
    }

    private static void CreateFlexibleSpacer(Transform parent)
    {
        var spacer = CreateLayoutObject("Spacer", parent);
        var element = spacer.AddComponent<LayoutElement>();
        element.minHeight = 0f;
        element.preferredHeight = 0f;
        element.flexibleHeight = 1f;
    }

    private static Text AddTextBlock(Transform parent, string value, int fontSize, TextAnchor anchor, Color color, float preferredHeight, float flexibleWidth = 0f)
    {
        var go = CreateLayoutObject("Text", parent);
        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        element.flexibleWidth = flexibleWidth;
        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text AddTextFill(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var go = CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var text = go.AddComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Normal;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Math.Max(10, fontSize - 5);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateLayoutObject(string name, Transform parent)
    {
        return CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
    {
        var rect = SunExpUiBuilder.CreateRect(name, parent, anchorMin, anchorMax, pivot, size);
        return rect.gameObject;
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var rect = parent as RectTransform;
        var width = rect != null && rect.rect.width > 0f ? rect.rect.width : 1280f;
        var height = rect != null && rect.rect.height > 0f ? rect.rect.height : 720f;
        return new Vector2(Mathf.Clamp(width * 0.58f, 560f, 760f), Mathf.Clamp(height * 0.76f, 520f, 660f));
    }
}
