using System;
using AuraUi.Shared;
using SunExp.Dll.GameApi;
using SunExp.Dll.Mechanics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public sealed class FieldBuffHudTooltipView : MonoBehaviour
{
    private const float Width = 420f;
    private const float Height = 260f;
    internal const float DescriptionHeight = Height * 0.5f;
    private static readonly Color PanelTint = new(0.035f, 0.028f, 0.018f, 0.96f);
    private static readonly Color HeaderTint = new(0.16f, 0.095f, 0.035f, 0.96f);
    private static readonly Color BodyTint = new(0.07f, 0.052f, 0.03f, 0.9f);
    private static readonly Color TitleColor = new(1f, 0.88f, 0.52f, 1f);
    private static readonly Color TextColor = new(0.96f, 0.91f, 0.78f, 1f);
    private static readonly Color MutedTextColor = new(0.78f, 0.66f, 0.46f, 1f);

    private RectTransform? rectTransform;
    private TMP_Text? titleText;
    private TMP_Text? subtitleText;
    private TMP_Text? descriptionText;
    private string lastContentKey = "";

    public static FieldBuffHudTooltipView Create(Transform parent, RectTransform hudRect)
    {
        var go = new GameObject("SunExp_FieldBuffHudTooltip", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);

        var view = go.AddComponent<FieldBuffHudTooltipView>();
        view.Build(hudRect);
        view.Hide();
        return view;
    }

    public void Show(FieldBuffSnapshot snapshot)
    {
        if (!snapshot.IsActive || titleText == null || subtitleText == null || descriptionText == null)
        {
            Hide();
            return;
        }

        var contentKey = snapshot.BuffId + ":" + snapshot.Stacks + ":" + snapshot.MaxStacks + ":" + snapshot.Epoch;
        if (contentKey != lastContentKey)
        {
            lastContentKey = contentKey;
            titleText.text = DisplayName(snapshot);
            subtitleText.text = "\u573a\u5730 Buff \u00b7 " + snapshot.Stacks + "/" + snapshot.MaxStacks + " \u5c42";
            descriptionText.text = Description(snapshot);
        }

        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void AlignTo(RectTransform hudRect)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = hudRect.anchorMin;
        rectTransform.anchorMax = hudRect.anchorMax;
        rectTransform.pivot = new Vector2(0f, 1f);
        var scaleX = Mathf.Abs(hudRect.localScale.x) > 0.001f ? hudRect.localScale.x : 1f;
        rectTransform.anchoredPosition = hudRect.anchoredPosition + new Vector2(hudRect.sizeDelta.x * scaleX * 0.5f + 14f, -2f);
    }

    private void Build(RectTransform hudRect)
    {
        rectTransform = GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(Width, Height);
        AlignTo(hudRect);

        var group = GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;

        SunExpUiBuilder.ApplyPanelImage(gameObject, SunExpUiSprites.Panel("[FieldBuffHud.Tooltip]"), PanelTint);

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        titleText = CreateTextBlock(transform, "Title", "", 44f, 24f, FontStyles.Bold, TextAnchor.MiddleLeft, TitleColor, HeaderTint, true);
        subtitleText = CreateTextBlock(transform, "Subtitle", "", 34f, 18f, FontStyles.Bold, TextAnchor.MiddleLeft, MutedTextColor, BodyTint, true);
        descriptionText = CreateTextBlock(transform, "Description", "", DescriptionHeight, 21f, FontStyles.Normal, TextAnchor.UpperLeft, TextColor, BodyTint, false);
    }

    private static TMP_Text CreateTextBlock(
        Transform parent,
        string name,
        string value,
        float height,
        float fontSize,
        FontStyles style,
        TextAnchor anchor,
        Color textColor,
        Color tint,
        bool autoSize)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var element = go.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;

        var image = go.AddComponent<Image>();
        image.color = tint;
        image.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(11f, 4f);
        textRect.offsetMax = new Vector2(-11f, -4f);

        var text = AuraUiComponents.ConfigureTmpText(
            textGo,
            value,
            fontSize,
            Math.Max(13f, fontSize - 5f),
            anchor,
            textColor,
            autoSize,
            SunExpUiTheme.Current);
        text.fontStyle = style;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static string DisplayName(FieldBuffSnapshot snapshot)
    {
        return FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).DisplayName;
    }

    private static string Description(FieldBuffSnapshot snapshot)
    {
        return FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).Description;
    }
}
