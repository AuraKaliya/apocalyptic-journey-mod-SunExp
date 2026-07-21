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
    private const float RootHeight = 128f;
    private const float IconSize = 64f;
    private const float NameSectionHeight = 32f;
    private const float NameSectionInset = 3f;
    private const float DividerInset = 12f;
    private const float DividerHeight = 1f;
    private const float MultiplayerAvoidanceAt1080 = 150f;
    private static readonly Color PanelTint = new(0.055f, 0.045f, 0.03f, 0.84f);
    private static readonly Color NameSectionTint = new(0.018f, 0.014f, 0.01f, 0.78f);
    private static readonly Color DividerTint = new(0.88f, 0.66f, 0.3f, 0.62f);
    private static readonly Color StackTextColor = new(0.95f, 0.78f, 0.42f, 1f);
    private static readonly Color NameTextColor = new(1f, 0.96f, 0.86f, 1f);
    private static readonly Color StackOutlineColor = new(0.04f, 0.025f, 0.01f, 0.96f);

    private RectTransform? rectTransform;
    private Image? icon;
    private TMP_Text? stackText;
    private TMP_Text? nameText;
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

        var panel = SunExpUiBuilder.ApplyPanelImage(gameObject, SunExpUiSprites.Panel("[FieldBuffHud]"), PanelTint);
        panel.raycastTarget = false;
        CreateIcon();
        CreateStackText();
        CreateNameSection();
        CreateHitArea();
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
        var iconRect = SunExpUiBuilder.CreateRect(
            "Icon",
            transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(IconSize, IconSize));
        iconRect.anchoredPosition = new Vector2(0f, -6f);
        icon = iconRect.gameObject.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;
    }

    private void CreateStackText()
    {
        var stackRect = SunExpUiBuilder.CreateRect(
            "Stacks",
            transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(80f, 22f));
        stackRect.anchoredPosition = new Vector2(0f, -72f);
        stackText = AuraUiComponents.ConfigureTmpText(
            stackRect.gameObject,
            "",
            17f,
            13f,
            TextAnchor.MiddleCenter,
            StackTextColor,
            true,
            SunExpUiTheme.Current);
        stackText.fontStyle = FontStyles.Bold;
        stackText.outlineColor = StackOutlineColor;
        stackText.outlineWidth = 0.24f;
        stackText.raycastTarget = false;
    }

    private void CreateNameSection()
    {
        var sectionRect = SunExpUiBuilder.CreateRect(
            "NameSection",
            transform,
            Vector2.zero,
            new Vector2(1f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(-NameSectionInset * 2f, NameSectionHeight - NameSectionInset));
        sectionRect.anchoredPosition = new Vector2(0f, NameSectionInset);

        var sectionImage = sectionRect.gameObject.AddComponent<Image>();
        sectionImage.color = NameSectionTint;
        sectionImage.raycastTarget = false;

        var dividerRect = SunExpUiBuilder.CreateRect(
            "Divider",
            sectionRect,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(RootWidth - DividerInset * 2f, DividerHeight));
        var dividerImage = dividerRect.gameObject.AddComponent<Image>();
        dividerImage.color = DividerTint;
        dividerImage.raycastTarget = false;

        var nameRect = SunExpUiBuilder.CreateRect(
            "Name",
            sectionRect,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        nameRect.offsetMin = new Vector2(8f, 2f);
        nameRect.offsetMax = new Vector2(-8f, -2f);
        nameText = AuraUiComponents.ConfigureTmpText(
            nameRect.gameObject,
            "",
            18f,
            13f,
            TextAnchor.MiddleCenter,
            NameTextColor,
            true,
            SunExpUiTheme.Current);
        nameText.fontStyle = FontStyles.Bold;
        nameText.outlineColor = StackOutlineColor;
        nameText.outlineWidth = 0.12f;
        nameText.raycastTarget = false;
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
            var iconPath = FieldEffectRegistry.RuntimeSpecFor(snapshot.Field).HudIconPathForStacks(snapshot.Stacks);
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
