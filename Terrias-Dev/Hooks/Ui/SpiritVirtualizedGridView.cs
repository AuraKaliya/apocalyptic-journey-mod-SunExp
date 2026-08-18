using System;
using System.Collections.Generic;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritManagementCellView : TerriasPooledUiBehaviour
{
    private Image? background;
    private Image? portrait;
    private Text? nameText;
    private Text? starText;
    private Text? levelText;
    private Text? aptitudeText;
    private Text? markerText;
    private Outline? outline;
    private GameObject? activeStamp;
    private Button? button;
    private Action<string>? onClick;
    private string spiritUid = "";

    public void Initialize(
        Image nextBackground,
        Image nextPortrait,
        Text nextNameText,
        Text nextStarText,
        Text nextLevelText,
        Text nextAptitudeText,
        Text nextMarkerText,
        Outline nextOutline,
        GameObject nextActiveStamp,
        Button nextButton,
        Action<string> nextOnClick)
    {
        background = nextBackground;
        portrait = nextPortrait;
        nameText = nextNameText;
        starText = nextStarText;
        levelText = nextLevelText;
        aptitudeText = nextAptitudeText;
        markerText = nextMarkerText;
        outline = nextOutline;
        activeStamp = nextActiveStamp;
        button = nextButton;
        onClick = nextOnClick;
        button.onClick.RemoveListener(HandleClick);
        button.onClick.AddListener(HandleClick);
    }

    public void Bind(
        SpiritInstance item,
        Sprite? portraitSprite,
        string displayName,
        string stars,
        string aptitude,
        string marker,
        Color backgroundColor,
        Color nameColor,
        Color starColor,
        Color primaryColor,
        Color markerColor,
        Color outlineColor,
        bool outlined,
        bool active,
        bool interactable)
    {
        spiritUid = item?.SpiritUid ?? "";
        if (background != null) background.color = backgroundColor;
        if (portrait != null)
        {
            portrait.sprite = portraitSprite;
            portrait.color = portraitSprite == null ? new Color(0.18f, 0.20f, 0.24f, 1f) : Color.white;
        }
        if (nameText != null) { nameText.text = displayName ?? ""; nameText.color = nameColor; }
        if (starText != null) { starText.text = stars ?? ""; starText.color = starColor; }
        if (levelText != null) { levelText.text = "Lv." + Math.Max(1, item?.Level ?? 1); levelText.color = primaryColor; }
        if (aptitudeText != null) { aptitudeText.text = aptitude ?? ""; aptitudeText.color = nameColor; }
        if (markerText != null) { markerText.text = marker ?? ""; markerText.color = markerColor; }
        if (outline != null) { outline.enabled = outlined; outline.effectColor = outlineColor; }
        if (activeStamp != null) activeStamp.SetActive(active);
        if (button != null) button.interactable = interactable;
        gameObject.SetActive(true);
    }

    public override void ResetForPool()
    {
        spiritUid = "";
        if (portrait != null) portrait.sprite = null;
        if (outline != null) outline.enabled = false;
        if (activeStamp != null) activeStamp.SetActive(false);
        if (button != null) button.interactable = false;
    }

    private void HandleClick()
    {
        if (spiritUid.Length > 0) onClick?.Invoke(spiritUid);
    }
}

internal sealed class SpiritVirtualizedGridView : MonoBehaviour
{
    private const string PoolKey = "SpiritManagement.RosterCell";
    private readonly List<SpiritInstance> items = new();
    private readonly List<SpiritManagementCellView> cells = new();
    private RectTransform? viewport;
    private RectTransform? content;
    private ScrollRect? scroll;
    private Func<Transform, string, SpiritManagementCellView>? createCell;
    private Action<SpiritManagementCellView, SpiritInstance>? bindCell;
    private Vector2 cellSize;
    private Vector2 spacing;
    private RectOffset padding = new();
    private int columns;
    private int firstVisibleRow = -1;
    private bool released;

    public int ActiveCellCount => cells.Count;

    public void Configure(
        TerriasUiComponents.ScrollArea area,
        int columnCount,
        Vector2 nextCellSize,
        Vector2 nextSpacing,
        RectOffset nextPadding,
        Func<Transform, string, SpiritManagementCellView> nextCreateCell,
        Action<SpiritManagementCellView, SpiritInstance> nextBindCell)
    {
        viewport = area.Viewport;
        content = area.Content;
        scroll = area.Scroll;
        columns = Math.Max(1, columnCount);
        cellSize = nextCellSize;
        spacing = nextSpacing;
        padding = nextPadding ?? new RectOffset();
        createCell = nextCreateCell;
        bindCell = nextBindCell;
        scroll.onValueChanged.AddListener(OnScroll);
        released = false;
    }

    public void SetItems(IEnumerable<SpiritInstance>? values, bool resetScroll)
    {
        items.Clear();
        if (values != null) items.AddRange(values);
        ResizeContent();
        EnsurePool();
        if (resetScroll && scroll != null) scroll.verticalNormalizedPosition = 1f;
        firstVisibleRow = -1;
        RefreshVisible(force: true);
        TerriasPerformanceCounters.Record("SpiritUi.VirtualGrid.Query");
    }

    public void RefreshVisible(bool force = false)
    {
        if (released || viewport == null || content == null) return;
        EnsurePool();
        var first = SpiritVirtualGridPolicy.FirstVisibleRow(
            content.anchoredPosition.y,
            padding.top,
            cellSize.y,
            spacing.y);
        if (!force && first == firstVisibleRow) return;
        firstVisibleRow = first;
        var firstIndex = first * columns;
        for (var poolIndex = 0; poolIndex < cells.Count; poolIndex++)
        {
            var itemIndex = firstIndex + poolIndex;
            var cell = cells[poolIndex];
            if (itemIndex < 0 || itemIndex >= items.Count)
            {
                cell.gameObject.SetActive(false);
                continue;
            }
            Position(cell.transform as RectTransform, itemIndex);
            bindCell?.Invoke(cell, items[itemIndex]);
            TerriasPerformanceCounters.Record("SpiritUi.VirtualGrid.Bind");
        }
    }

    public void Release()
    {
        if (released) return;
        released = true;
        if (scroll != null) scroll.onValueChanged.RemoveListener(OnScroll);
        foreach (var cell in cells)
        {
            if (cell != null) TerriasUiPool.Release(cell.gameObject, "SpiritVirtualizedGridView.Release", "[SpiritManagement]");
        }
        cells.Clear();
        items.Clear();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (released) return;
        EnsurePool();
        firstVisibleRow = -1;
        RefreshVisible(force: true);
    }

    private void OnDestroy() => Release();

    private void OnScroll(Vector2 _)
    {
        var key = "SpiritVirtualGrid.Scroll." + GetInstanceID();
        if (!TerriasFrameScheduler.RunOnceNextFrame(key, () =>
            {
                if (this != null && !released) RefreshVisible();
            }))
        {
            RefreshVisible();
        }
    }

    private void EnsurePool()
    {
        if (viewport == null || content == null || createCell == null) return;
        var visibleHeight = viewport.rect.height > 1f ? viewport.rect.height : 260f;
        var target = SpiritVirtualGridPolicy.RequiredCellCount(visibleHeight, cellSize.y, spacing.y, columns);
        while (cells.Count < target)
        {
            var index = cells.Count;
            var view = TerriasUiPool.AcquireConfiguredComponent(
                PoolKey,
                content,
                "SpiritCell-" + index,
                createCell,
                cell => cell.ResetForPool());
            cells.Add(view);
            TerriasPerformanceCounters.Record("SpiritUi.VirtualGrid.CellCreated");
        }
    }

    private void ResizeContent()
    {
        if (content == null) return;
        var height = SpiritVirtualGridPolicy.ContentHeight(
            items.Count,
            columns,
            cellSize.y,
            spacing.y,
            padding.top,
            padding.bottom);
        content.sizeDelta = new Vector2(0f, Math.Max(0f, height));
    }

    private void Position(RectTransform? rect, int itemIndex)
    {
        if (rect == null) return;
        var row = itemIndex / columns;
        var column = itemIndex % columns;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = cellSize;
        rect.anchoredPosition = new Vector2(
            padding.left + column * (cellSize.x + spacing.x),
            -(padding.top + row * (cellSize.y + spacing.y)));
    }
}
