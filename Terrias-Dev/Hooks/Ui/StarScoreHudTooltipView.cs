using System;
using AuraUi.Shared;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

public sealed class StarScoreHudTooltipView : MonoBehaviour
{
    private const float Width = 392f;
    private const float HeaderHeight = 28f;
    private const float RowHeight = 30f;
    private const int MaxRows = 6;
    private static readonly Color PanelTint = new(0.025f, 0.022f, 0.075f, 0.96f);
    private static readonly Color HeaderTint = new(0.13f, 0.105f, 0.22f, 0.98f);
    private static readonly Color RowTint = new(0.05f, 0.045f, 0.12f, 0.93f);
    private static readonly Color TextColor = new(0.92f, 0.86f, 0.66f, 1f);
    private static readonly Color MutedTextColor = new(0.72f, 0.68f, 0.58f, 1f);

    private RectTransform? rectTransform;
    private Text? headerText;
    private Transform? contentRoot;
    private string lastContentKey = "";

    public static StarScoreHudTooltipView Create(Transform parent, RectTransform hudRect)
    {
        var go = new GameObject("Terrias_StarScoreHudTooltip", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);

        var view = go.AddComponent<StarScoreHudTooltipView>();
        view.Build(hudRect);
        view.Hide();
        return view;
    }

    public void Show(StarScoreDisplaySnapshot snapshot)
    {
        if (headerText == null || contentRoot == null)
        {
            return;
        }

        var contentKey = snapshot.Version + ":" + StarScoreNoteCodes.PatternFromNotes(snapshot.Notes) + ":" + snapshot.IsCadencePreview;
        if (contentKey == lastContentKey)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            return;
        }

        lastContentKey = contentKey;
        headerText.text = "\u5f53\u524d\u661f\u8c31\uff1a" + StarScoreCadenceCatalog.CurrentStateText(snapshot.Notes);
        ClearChildren(contentRoot);

        var rows = StarScoreCadenceCatalog.CandidatesForPrefix(snapshot.Notes);
        var count = Math.Min(rows.Count, MaxRows);
        for (var i = 0; i < count; i++)
        {
            CreateRow(contentRoot, rows[i].DisplayText, i % 2 == 0 ? RowTint : new Color(0.04f, 0.037f, 0.1f, 0.93f));
        }

        if (rows.Count > MaxRows)
        {
            CreateRow(contentRoot, "\u5176\u4f59\u7ec4\u5408\uff1a\u4e09\u58f0\u548c\u5f26\u3002\u4f59\u97f3+1\uff1b\u62bd1\u5f20\u724c", RowTint, MutedTextColor);
        }

        gameObject.SetActive(true);
        // The parent is BattleHudHost, so this only raises the tooltip within the
        // persistent HUD layer and can never overtake FightUI transient overlays.
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
        rectTransform.pivot = new Vector2(0f, 0.5f);
        var scaleX = Mathf.Abs(hudRect.localScale.x) > 0.001f ? hudRect.localScale.x : 1f;
        rectTransform.anchoredPosition = hudRect.anchoredPosition + new Vector2(hudRect.sizeDelta.x * scaleX + 16f, 0f);
    }

    private void Build(RectTransform hudRect)
    {
        rectTransform = GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(Width, HeaderHeight + RowHeight * (MaxRows + 1) + 18f);
        AlignTo(hudRect);

        var group = GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var panel = gameObject.AddComponent<Image>();
        panel.color = PanelTint;
        panel.raycastTarget = false;

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 7, 7);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        headerText = CreateTextBlock(transform, "Header", "", HeaderHeight, 16, TextAnchor.MiddleLeft, TextColor, HeaderTint);
        contentRoot = CreateContentRoot(transform);
    }

    private static Transform CreateContentRoot(Transform parent)
    {
        var go = new GameObject("Rows", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return go.transform;
    }

    private static void CreateRow(Transform parent, string value, Color tint, Color? textColor = null)
    {
        var row = TerriasUiPool.AcquireComponent(
            "StarScoreHudTooltip.Row",
            parent,
            "Row",
            CreateRowTemplate);
        row.Bind(value, tint, textColor ?? TextColor);
    }

    private static TooltipRowView CreateRowTemplate(Transform parent, string name)
    {
        var text = CreateTextBlock(parent, name, "", RowHeight, 13, TextAnchor.MiddleLeft, TextColor, RowTint);
        var view = text.transform.parent.gameObject.AddComponent<TooltipRowView>();
        view.Initialize(text.transform.parent.GetComponent<Image>(), text);
        return view;
    }

    private static Text CreateTextBlock(
        Transform parent,
        string name,
        string value,
        float height,
        int fontSize,
        TextAnchor anchor,
        Color textColor,
        Color tint)
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
        textRect.offsetMin = new Vector2(8f, 0f);
        textRect.offsetMax = new Vector2(-8f, 0f);

        var text = textGo.AddComponent<Text>();
        text.text = value;
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = textColor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Math.Max(10, fontSize - 3);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        return text;
    }

    private static void ClearChildren(Transform parent)
    {
        TerriasUiPool.ReleaseOrDestroyChildren(parent, "StarScoreHudTooltip.ClearChildren", "[StarScoreHudTooltip]");
    }

    private sealed class TooltipRowView : TerriasPooledUiBehaviour
    {
        private Image? image;
        private Text? text;

        public void Initialize(Image image, Text text)
        {
            this.image = image;
            this.text = text;
        }

        public void Bind(string value, Color tint, Color textColor)
        {
            if (image != null)
            {
                image.color = tint;
            }

            if (text != null)
            {
                text.text = value;
                text.color = textColor;
            }
        }

        public override void ResetForPool()
        {
            if (text != null)
            {
                text.text = "";
            }
        }
    }
}
