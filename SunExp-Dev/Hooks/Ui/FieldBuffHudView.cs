using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public sealed class FieldBuffHudView : MonoBehaviour
{
    public const string RootName = "SunExp_FieldBuffHud";

    private const float RootWidth = 136f;
    private const float RootHeight = 128f;
    private const float IconFrameSize = 62f;
    private const float IconSize = 52f;
    private static readonly Color PanelTint = new(0.08f, 0.07f, 0.05f, 0.78f);
    private static readonly Color IconFrameTint = new(0.12f, 0.08f, 0.045f, 0.94f);
    private static readonly Color StackTint = new(0.18f, 0.11f, 0.045f, 0.92f);
    private static readonly Color TextColor = new(1f, 0.96f, 0.86f, 1f);
    private static readonly Color MutedText = new(0.95f, 0.78f, 0.42f, 1f);

    private RectTransform? rectTransform;
    private Image? icon;
    private Text? nameText;
    private Text? stackText;
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
        rectTransform.anchoredPosition = new Vector2(0f, -72f);

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
        frameRect.anchoredPosition = new Vector2(0f, -12f);
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
            new Vector2(78f, 20f));
        stackRect.anchoredPosition = new Vector2(0f, -78f);
        var image = SunExpUiBuilder.ApplyLabelImage(stackRect.gameObject, SunExpUiSprites.Label("[FieldBuffHud.Stack]"), StackTint);
        image.raycastTarget = false;

        stackText = SunExpUiBuilder.AddText(
            stackRect,
            "Stacks",
            "",
            14,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            MutedText,
            Vector2.zero,
            new Vector2(72f, 18f),
            4);
    }

    private void CreateName()
    {
        var nameRect = SunExpUiBuilder.CreateRect(
            "NameBar",
            transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(120f, 26f));
        nameRect.anchoredPosition = new Vector2(0f, -98f);
        var image = SunExpUiBuilder.ApplyLabelImage(nameRect.gameObject, SunExpUiSprites.Label("[FieldBuffHud.Name]"), StackTint);
        image.raycastTarget = false;

        nameText = SunExpUiBuilder.AddText(
            nameRect,
            "Name",
            "",
            15,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            TextColor,
            Vector2.zero,
            new Vector2(112f, 22f),
            5);
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
