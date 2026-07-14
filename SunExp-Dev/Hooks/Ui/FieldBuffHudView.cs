using System;
using AuraUi.Shared;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public sealed class FieldBuffHudView : MonoBehaviour
{
    public const string RootName = "SunExp_FieldBuffHud";

    private const float RootWidth = 164f;
    private const float RootHeight = 154f;
    private const float IconFrameSize = 76f;
    private const float IconSize = 64f;
    private const float MultiplayerAvoidanceAt1080 = 150f;
    private static readonly Color PanelTint = new(0.08f, 0.07f, 0.05f, 0.78f);
    private static readonly Color IconFrameTint = new(0.12f, 0.08f, 0.045f, 0.94f);
    private static readonly Color StackTint = new(0.18f, 0.11f, 0.045f, 0.92f);
    private static readonly Color TextColor = new(1f, 0.96f, 0.86f, 1f);
    private static readonly Color MutedText = new(0.95f, 0.78f, 0.42f, 1f);

    private RectTransform? rectTransform;
    private Image? icon;
    private TMP_Text? nameText;
    private TMP_Text? stackText;
    private FieldBuffHudTooltipView? tooltip;
    private FieldBuffSnapshot currentSnapshot = FieldBuffSnapshot.Empty;
    private bool pointerInside;

    public static FieldBuffHudView Create(Transform parent)
    {
        var go = new GameObject(RootName, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);

        var view = go.AddComponent<FieldBuffHudView>();
        view.Build();
        return view;
    }

    public void ApplySnapshot(FieldBuffSnapshot snapshot)
    {
        currentSnapshot = snapshot ?? FieldBuffSnapshot.Empty;
        if (!currentSnapshot.IsActive)
        {
            pointerInside = false;
            tooltip?.Hide();
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        if (nameText != null)
        {
            nameText.text = DisplayName(currentSnapshot);
        }

        if (stackText != null)
        {
            stackText.text = currentSnapshot.Stacks + "/" + currentSnapshot.MaxStacks;
        }

        if (icon != null)
        {
            var sprite = LoadIcon(currentSnapshot);
            icon.sprite = sprite;
            icon.color = sprite == null ? new Color(1f, 0.64f, 0.26f, 0.26f) : Color.white;
        }

        RefreshTooltip();
    }

    public void Close(string source)
    {
        CloseTooltip(source + ":tooltip");
        SunExpUiSafety.CloseTransient(gameObject, source, "[FieldBuffHud]");
    }

    internal void HandlePointerEntered()
    {
        pointerInside = true;
        RefreshTooltip();
    }

    internal void HandlePointerExited()
    {
        pointerInside = false;
        tooltip?.Hide();
    }

    private void Build()
    {
        rectTransform = GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 1f);
        rectTransform.anchorMax = new Vector2(0.5f, 1f);
        rectTransform.pivot = new Vector2(0.5f, 1f);
        rectTransform.sizeDelta = new Vector2(RootWidth, RootHeight);
        RefreshResponsivePosition();

        var group = GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        SunExpUiBuilder.ApplyPanelImage(gameObject, SunExpUiSprites.Panel("[FieldBuffHud]"), PanelTint);
        CreateHitArea();
        CreateIcon();
        CreateStackBar();
        CreateName();
        EnsureTooltip();
    }

    private void CreateHitArea()
    {
        var hitRect = SunExpUiBuilder.CreateRect(
            "HitArea",
            transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        hitRect.offsetMin = Vector2.zero;
        hitRect.offsetMax = Vector2.zero;
        hitRect.SetAsLastSibling();

        var image = hitRect.gameObject.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.01f);
        image.raycastTarget = true;

        var probe = hitRect.gameObject.AddComponent<FieldBuffHudHoverProbe>();
        probe.Configure(this);
    }

    private void CreateIcon()
    {
        var frameRect = SunExpUiBuilder.CreateRect(
            "IconFrame",
            transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(IconFrameSize, IconFrameSize));
        frameRect.anchoredPosition = new Vector2(0f, -14f);
        var frameImage = SunExpUiBuilder.ApplyPanelImage(frameRect.gameObject, SunExpUiSprites.Panel("[FieldBuffHud.Icon]"), IconFrameTint);
        frameImage.raycastTarget = false;

        var iconRect = SunExpUiBuilder.CreateRect(
            "Icon",
            frameRect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(IconSize, IconSize));
        icon = iconRect.gameObject.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;
    }

    private void CreateStackBar()
    {
        var stackRect = SunExpUiBuilder.CreateRect(
            "StackBar",
            transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(96f, 24f));
        stackRect.anchoredPosition = new Vector2(0f, -94f);
        var image = SunExpUiBuilder.ApplyLabelImage(stackRect.gameObject, SunExpUiSprites.Label("[FieldBuffHud.Stack]"), StackTint);
        image.raycastTarget = false;

        stackText = CreateHudText(stackRect, "Stacks", 17f, MutedText, new Vector2(90f, 22f));
    }

    private void CreateName()
    {
        var nameRect = SunExpUiBuilder.CreateRect(
            "NameBar",
            transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(146f, 32f));
        nameRect.anchoredPosition = new Vector2(0f, -122f);
        var image = SunExpUiBuilder.ApplyLabelImage(nameRect.gameObject, SunExpUiSprites.Label("[FieldBuffHud.Name]"), StackTint);
        image.raycastTarget = false;

        nameText = CreateHudText(nameRect, "Name", 18f, TextColor, new Vector2(138f, 28f));
    }

    private static TMP_Text CreateHudText(RectTransform parent, string name, float fontSize, Color color, Vector2 size)
    {
        var textRect = SunExpUiBuilder.CreateRect(
            name,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            size);
        var text = AuraUiComponents.ConfigureTmpText(
            textRect.gameObject,
            "",
            fontSize,
            Math.Max(12f, fontSize - 4f),
            TextAnchor.MiddleCenter,
            color,
            true,
            SunExpUiTheme.Current);
        text.fontStyle = FontStyles.Bold;
        return text;
    }

    private void EnsureTooltip()
    {
        if (tooltip != null || transform.parent == null || rectTransform == null)
        {
            return;
        }

        tooltip = FieldBuffHudTooltipView.Create(transform.parent, rectTransform);
    }

    private void RefreshTooltip()
    {
        if (!pointerInside || tooltip == null || rectTransform == null || !currentSnapshot.IsActive)
        {
            tooltip?.Hide();
            return;
        }

        tooltip.AlignTo(rectTransform);
        tooltip.Show(currentSnapshot);
    }

    private void OnDisable()
    {
        pointerInside = false;
        tooltip?.Hide();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (rectTransform != null)
        {
            RefreshResponsivePosition();
        }
    }

    private void RefreshResponsivePosition()
    {
        if (rectTransform == null)
        {
            return;
        }

        var host = transform.parent as RectTransform;
        var hostHeight = host != null && host.rect.height > 1f ? host.rect.height : Screen.height;
        var avoidance = hostHeight * MultiplayerAvoidanceAt1080 / 1080f;
        rectTransform.anchoredPosition = new Vector2(0f, -avoidance);
    }

    private void OnDestroy()
    {
        CloseTooltip("FieldBuffHudView.OnDestroy");
    }

    private void CloseTooltip(string source)
    {
        if (tooltip == null)
        {
            return;
        }

        var root = tooltip.gameObject;
        tooltip = null;
        SunExpUiSafety.CloseTransient(root, source, "[FieldBuffHud]");
    }

    private static string DisplayName(FieldBuffSnapshot snapshot)
    {
        return FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).DisplayName;
    }

    private static Sprite? LoadIcon(FieldBuffSnapshot snapshot)
    {
        try
        {
            var iconPath = FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).IconPath;
            return string.IsNullOrWhiteSpace(iconPath)
                ? null
                : SunExpResourceCache.Load<Sprite>(iconPath, true, "field.buff.hud");
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[FieldBuffHud] icon fallback: " + snapshot.BuffId + ", error=" + ex.Message);
            return null;
        }
    }
}
