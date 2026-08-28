using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritArtifactCellView : TerriasPooledUiBehaviour
{
    private Image? background;
    private Image? icon;
    private Text? stars;
    private Text? level;
    private GameObject? lockBadge;
    private GameObject? ownerBadge;
    private GameObject? presetBadge;
    private GameObject? batchBadge;
    private GameObject? batchFrame;
    private Image? ownerPortrait;
    private GameObject? ownerFallback;
    private SpiritArtifactCardMotion? motion;
    private Button? button;
    private SpiritArtifactHoverProbe? hover;
    private Action<string>? onClick;
    private string artifactUid = "";

    public string ArtifactUid => artifactUid;

    public void Initialize(
        Image nextBackground,
        Image nextIcon,
        Text nextStars,
        Text nextLevel,
        GameObject nextLockBadge,
        GameObject nextOwnerBadge,
        GameObject nextPresetBadge,
        GameObject nextBatchBadge,
        GameObject nextBatchFrame,
        Image nextOwnerPortrait,
        GameObject nextOwnerFallback,
        SpiritArtifactCardMotion nextMotion,
        Button nextButton,
        SpiritArtifactHoverProbe nextHover,
        Action<string> click)
    {
        background = nextBackground;
        icon = nextIcon;
        stars = nextStars;
        level = nextLevel;
        lockBadge = nextLockBadge;
        ownerBadge = nextOwnerBadge;
        presetBadge = nextPresetBadge;
        batchBadge = nextBatchBadge;
        batchFrame = nextBatchFrame;
        ownerPortrait = nextOwnerPortrait;
        ownerFallback = nextOwnerFallback;
        motion = nextMotion;
        button = nextButton;
        hover = nextHover;
        onClick = click;
        PrepareForReuse();
    }

    public void PrepareForReuse()
    {
        ResetForPool();
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }
        hover?.Configure(HandleEnter, HandleExit);
    }

    public void Bind(
        SpiritArtifactInstance artifact,
        Sprite? iconSprite,
        Sprite backgroundSprite,
        bool selected,
        bool batchSelected,
        bool presetProtected,
        bool equipped,
        Sprite? ownerSprite)
    {
        artifactUid = artifact?.ArtifactUid ?? "";
        if (background != null)
        {
            background.sprite = backgroundSprite;
            background.type = Image.Type.Simple;
            background.color = Color.white;
        }
        if (icon != null)
        {
            icon.sprite = iconSprite;
            icon.color = iconSprite == null ? new Color(1f, 1f, 1f, 0.12f) : Color.white;
        }
        if (stars != null) stars.text = new string('★', Math.Max(1, Math.Min(3, artifact?.Rarity ?? 1)));
        if (level != null) level.text = "Lv." + Math.Max(1, artifact?.Level ?? 1);
        if (lockBadge != null) lockBadge.SetActive(artifact?.Locked == true);
        if (ownerBadge != null) ownerBadge.SetActive(equipped);
        if (presetBadge != null) presetBadge.SetActive(presetProtected && artifact?.Locked != true);
        if (batchBadge != null) batchBadge.SetActive(batchSelected);
        if (batchFrame != null) batchFrame.SetActive(batchSelected);
        if (ownerPortrait != null)
        {
            ownerPortrait.sprite = ownerSprite;
            ownerPortrait.color = ownerSprite == null ? Color.clear : Color.white;
        }
        if (ownerFallback != null) ownerFallback.SetActive(equipped && ownerSprite == null);
        motion?.SetSelected(selected);
        if (button != null) button.interactable = true;
        gameObject.SetActive(true);
    }

    public override void ResetForPool()
    {
        artifactUid = "";
        if (icon != null) { icon.sprite = null; icon.color = Color.clear; }
        if (stars != null) stars.text = "";
        if (level != null) level.text = "";
        if (lockBadge != null) lockBadge.SetActive(false);
        if (ownerBadge != null) ownerBadge.SetActive(false);
        if (presetBadge != null) presetBadge.SetActive(false);
        if (batchBadge != null) batchBadge.SetActive(false);
        if (batchFrame != null) batchFrame.SetActive(false);
        if (ownerPortrait != null) { ownerPortrait.sprite = null; ownerPortrait.color = Color.clear; }
        if (ownerFallback != null) ownerFallback.SetActive(false);
        motion?.ResetVisual();
        if (button != null) button.interactable = false;
    }

    public void SetSelected(bool selected)
        => motion?.SetSelected(selected);

    public void SetSelection(bool selected, bool batchSelected)
    {
        motion?.SetSelected(selected);
        if (batchBadge != null) batchBadge.SetActive(batchSelected);
        if (batchFrame != null) batchFrame.SetActive(batchSelected);
    }

    private void HandleClick() { if (artifactUid.Length > 0) onClick?.Invoke(artifactUid); }
    private void HandleEnter()
    {
        motion?.SetHovered(true);
    }

    private void HandleExit()
    {
        motion?.SetHovered(false);
    }
}

internal sealed class SpiritArtifactHoverProbe : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Action? entered;
    private Action? exited;

    public void Configure(Action onEntered, Action onExited)
    {
        entered = onEntered;
        exited = onExited;
    }

    public void OnPointerEnter(PointerEventData eventData) => entered?.Invoke();
    public void OnPointerExit(PointerEventData eventData) => exited?.Invoke();
}

internal sealed class SpiritArtifactSelectionDismissSurface : MonoBehaviour, IPointerClickHandler
{
    private Action? dismissed;

    public void Configure(Action action)
    {
        dismissed = action;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button == PointerEventData.InputButton.Left)
            dismissed?.Invoke();
    }
}

internal sealed class SpiritArtifactPointerBlocker : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
    }
}

internal sealed class SpiritArtifactCardMotion : MonoBehaviour
{
    private RectTransform? visualRoot;
    private CanvasGroup? selectionHalo;
    private CanvasGroup? hoverFrame;
    private bool selected;
    private bool hovered;
    private float hoverProgress;
    private float selectionStarted;

    public void Configure(
        RectTransform rootTransform,
        CanvasGroup selection,
        CanvasGroup hover)
    {
        visualRoot = rootTransform;
        selectionHalo = selection;
        hoverFrame = hover;
        ResetVisual();
    }

    public void SetSelected(bool value)
    {
        var restart = SpiritArtifactCardMotionPolicy.ShouldRestartSelection(selected, value);
        selected = value;
        if (selected)
        {
            if (restart) selectionStarted = Time.unscaledTime;
            if (selectionHalo != null) selectionHalo.gameObject.SetActive(true);
        }
        else if (selectionHalo != null)
        {
            selectionHalo.alpha = 0f;
            selectionHalo.transform.localScale = Vector3.one;
            selectionHalo.gameObject.SetActive(false);
        }
        enabled = selected || hovered || hoverProgress > 0f;
    }

    public void SetHovered(bool value)
    {
        hovered = value;
        if (hoverFrame != null && value) hoverFrame.gameObject.SetActive(true);
        enabled = true;
    }

    public void ResetVisual()
    {
        selected = false;
        hovered = false;
        hoverProgress = 0f;
        if (visualRoot != null) visualRoot.localScale = Vector3.one;
        if (hoverFrame != null)
        {
            hoverFrame.alpha = 0f;
            hoverFrame.transform.localScale = Vector3.one;
            hoverFrame.gameObject.SetActive(false);
        }
        if (selectionHalo != null)
        {
            selectionHalo.alpha = 0f;
            selectionHalo.transform.localScale = Vector3.one;
            selectionHalo.gameObject.SetActive(false);
        }
        enabled = false;
    }

    private void Update()
    {
        var duration = hovered
            ? SpiritArtifactCardMotionPolicy.HoverEnterSeconds
            : SpiritArtifactCardMotionPolicy.HoverExitSeconds;
        hoverProgress = Mathf.MoveTowards(
            hoverProgress,
            hovered ? 1f : 0f,
            Time.unscaledDeltaTime / Math.Max(0.01f, duration));
        var hoverEase = hoverProgress * hoverProgress * (3f - 2f * hoverProgress);
        if (visualRoot != null)
        {
            var scale = Mathf.Lerp(1f, 1.03f, hoverEase);
            visualRoot.localScale = new Vector3(scale, scale, 1f);
        }
        if (hoverFrame != null)
        {
            hoverFrame.alpha = 0.95f * hoverEase;
            if (!hovered && hoverProgress <= 0f) hoverFrame.gameObject.SetActive(false);
        }
        if (selected && selectionHalo != null)
        {
            var pulse = SpiritArtifactCardMotionPolicy.SelectionPulse(Time.unscaledTime - selectionStarted);
            selectionHalo.alpha = pulse.Alpha;
            selectionHalo.transform.localScale = new Vector3(pulse.Scale, pulse.Scale, 1f);
        }
        if (!selected && !hovered && hoverProgress <= 0f) enabled = false;
    }
}

internal sealed class SpiritArtifactVirtualizedGridView : MonoBehaviour
{
    private const string DefaultPoolKey = "SpiritArtifact.InventoryCell";
    private const int MaximumColumns = 10;
    private readonly List<SpiritArtifactInstance> items = new();
    private readonly List<RowView> rows = new();
    private RectTransform? viewport;
    private RectTransform? content;
    private ScrollRect? scroll;
    private Func<Transform, string, SpiritArtifactCellView>? createCell;
    private Action<SpiritArtifactCellView, SpiritArtifactInstance>? bindCell;
    private Vector2 cellSize;
    private Vector2 spacing;
    private RectOffset padding = new();
    private int columns;
    private int minimumColumns = 5;
    private int maximumColumns = 10;
    private string poolKey = DefaultPoolKey;
    private int firstVisibleRow = -1;
    private int activeRowCount;
    private bool released;
    private bool pendingDimensionCheck;
    private bool pendingScrollCheck;
    private readonly SpiritVirtualGridRefreshState refreshState = new();

    private sealed class RowView
    {
        public RowView(RectTransform root, SpiritArtifactCellView[] cells)
        {
            Root = root;
            Cells = cells;
        }

        public RectTransform Root { get; }
        public SpiritArtifactCellView[] Cells { get; }
        public int BoundRow { get; set; } = -1;
    }

    public int ActiveCellCount => activeRowCount * columns;

    public void Configure(
        TerriasUiComponents.ScrollArea area,
        int columnCount,
        Vector2 nextCellSize,
        Vector2 nextSpacing,
        RectOffset nextPadding,
        Func<Transform, string, SpiritArtifactCellView> nextCreateCell,
        Action<SpiritArtifactCellView, SpiritArtifactInstance> nextBindCell,
        int nextMinimumColumns = 5,
        int nextMaximumColumns = 10,
        string nextPoolKey = DefaultPoolKey)
    {
        viewport = area.Viewport;
        content = area.Content;
        scroll = area.Scroll;
        columns = Math.Max(1, columnCount);
        minimumColumns = Math.Max(1, nextMinimumColumns);
        maximumColumns = Math.Max(minimumColumns, Math.Min(MaximumColumns, nextMaximumColumns));
        poolKey = string.IsNullOrWhiteSpace(nextPoolKey) ? DefaultPoolKey : nextPoolKey.Trim();
        cellSize = nextCellSize;
        spacing = nextSpacing;
        padding = nextPadding ?? new RectOffset();
        createCell = nextCreateCell;
        bindCell = nextBindCell;
        scroll.onValueChanged.AddListener(OnScroll);
        released = false;
        refreshState.Reset();
        UpdateResponsiveLayout();
    }

    public void SetItems(IEnumerable<SpiritArtifactInstance>? values, bool resetScroll)
    {
        refreshState.Cancel();
        pendingDimensionCheck = false;
        pendingScrollCheck = false;
        items.Clear();
        if (values != null) items.AddRange(values);
        UpdateResponsiveLayout();
        ResizeContent();
        EnsureRowPool();
        if (resetScroll && scroll != null) scroll.verticalNormalizedPosition = 1f;
        FullRebind(forceDataBind: true);
        TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Grid.Query");
    }

    public void SetSelectedUid(string? artifactUid)
        => SetSelectionState(artifactUid, null);

    public void SetSelectionState(string? artifactUid, IReadOnlyCollection<string>? batchSelectedUids)
    {
        var selectedUid = (artifactUid ?? "").Trim();
        var batch = batchSelectedUids == null
            ? null
            : new HashSet<string>(batchSelectedUids, StringComparer.Ordinal);
        for (var rowIndex = 0; rowIndex < activeRowCount; rowIndex++)
        {
            foreach (var cell in rows[rowIndex].Cells)
            {
                if (cell == null || !cell.gameObject.activeSelf) continue;
                cell.SetSelection(
                    string.Equals(cell.ArtifactUid, selectedUid, StringComparison.Ordinal),
                    batch?.Contains(cell.ArtifactUid) == true);
            }
        }
        TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Selection.VisibleCells");
    }

    public void RefreshVisible(bool force = false)
    {
        if (released || viewport == null || content == null) return;
        EnsureRowPool();
        if (force) FullRebind(forceDataBind: true);
        else RefreshRowsForScroll();
    }

    public void Release()
    {
        if (released) return;
        released = true;
        refreshState.Reset();
        if (scroll != null) scroll.onValueChanged.RemoveListener(OnScroll);
        foreach (var row in rows)
        {
            foreach (var cell in row.Cells)
                if (cell != null) TerriasUiPool.Release(cell.gameObject, "SpiritArtifactGrid.Release", "[SpiritArtifact]");
            if (row.Root != null) UnityEngine.Object.Destroy(row.Root.gameObject);
        }
        rows.Clear();
        items.Clear();
    }

    private void OnDestroy() => Release();
    private void OnRectTransformDimensionsChange()
    {
        if (released || viewport == null) return;
        var size = viewport.rect.size;
        if (!refreshState.ObserveViewport(size.x, size.y)) return;
        pendingDimensionCheck = true;
        ScheduleRefresh();
    }

    private void OnScroll(Vector2 _)
    {
        if (content == null) return;
        var nextRow = CurrentFirstVisibleRow();
        if (nextRow == firstVisibleRow) return;
        pendingScrollCheck = true;
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (released || viewport == null || content == null || !refreshState.Request(false)) return;
        var key = "SpiritArtifactGrid.RowRefresh." + GetInstanceID();
        if (TerriasFrameScheduler.RunOnceNextFrame(key, () =>
            {
                if (this == null || released) return;
                refreshState.Drain();
                var dimensionCheck = pendingDimensionCheck;
                var scrollCheck = pendingScrollCheck;
                pendingDimensionCheck = false;
                pendingScrollCheck = false;
                var layoutChanged = UpdateResponsiveLayout();
                var poolChanged = EnsureRowPool();
                if (layoutChanged)
                {
                    ResizeContent();
                    FullRebind(forceDataBind: true);
                    TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Grid.ViewportColumnsChanged");
                    return;
                }
                if (poolChanged)
                {
                    FullRebind(forceDataBind: false);
                    TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Grid.PoolRowsChanged");
                    return;
                }
                if (scrollCheck) RefreshRowsForScroll();
                else if (dimensionCheck) LayoutBoundRows();
            }))
        {
            return;
        }
        refreshState.Cancel();
        TerriasLog.WarnOnce(
            "SpiritArtifactGrid.RowRefresh.ScheduleRejected",
            "Spirit artifact row ring could not schedule its layout-safe next-frame refresh.");
    }

    private bool EnsureRowPool()
    {
        if (viewport == null || content == null || createCell == null) return false;
        var height = viewport.rect.height > 1f ? viewport.rect.height : 260f;
        var target = SpiritVirtualGridPolicy.RequiredCellCount(height, cellSize.y, spacing.y, 1);
        var changed = target != activeRowCount;
        while (rows.Count < target)
        {
            var rowIndex = rows.Count;
            var rowRoot = TerriasUiComponents.CreateRectTransform(
                "ArtifactRow-" + rowIndex,
                content,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, cellSize.y));
            var rowCells = new SpiritArtifactCellView[maximumColumns];
            for (var column = 0; column < maximumColumns; column++)
            {
                var cellIndex = rowIndex * maximumColumns + column;
                rowCells[column] = TerriasUiPool.AcquireConfiguredComponent(
                    poolKey,
                    rowRoot,
                    "ArtifactCell-" + cellIndex,
                    createCell,
                    cell => cell.PrepareForReuse());
                TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Grid.CellCreated");
            }
            rows.Add(new RowView(rowRoot, rowCells));
        }
        activeRowCount = target;
        for (var index = 0; index < rows.Count; index++)
            rows[index].Root.gameObject.SetActive(index < activeRowCount);
        return changed;
    }

    private void ResizeContent()
    {
        if (content == null) return;
        var height = SpiritVirtualGridPolicy.ContentHeight(items.Count, columns, cellSize.y, spacing.y, padding.top, padding.bottom);
        content.sizeDelta = new Vector2(0f, Math.Max(0f, height));
    }

    private bool UpdateResponsiveLayout()
    {
        if (viewport == null || viewport.rect.width <= 1f) return false;
        var width = viewport.rect.width;
        var nextColumns = SpiritArtifactCardStylePolicy.ColumnsForWidth(width, minimumColumns, maximumColumns);
        var horizontalPadding = SpiritArtifactCardStylePolicy.HorizontalPaddingForWidth(width, nextColumns);
        if (columns == nextColumns
            && padding.left == horizontalPadding
            && padding.right == horizontalPadding)
            return false;
        columns = nextColumns;
        padding.left = horizontalPadding;
        padding.right = horizontalPadding;
        return true;
    }

    private void FullRebind(bool forceDataBind)
    {
        if (content == null) return;
        firstVisibleRow = CurrentFirstVisibleRow();
        for (var index = 0; index < activeRowCount; index++)
            BindRow(rows[index], firstVisibleRow + index, forceDataBind);
        for (var index = activeRowCount; index < rows.Count; index++)
            rows[index].Root.gameObject.SetActive(false);
        TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Grid.FullRebind");
    }

    private void RefreshRowsForScroll()
    {
        if (activeRowCount <= 0) return;
        var nextFirstRow = CurrentFirstVisibleRow();
        if (nextFirstRow == firstVisibleRow) return;
        if (SpiritArtifactRowRingPolicy.RequiresFullRebind(
                firstVisibleRow,
                nextFirstRow,
                activeRowCount))
        {
            FullRebind(forceDataBind: false);
            return;
        }

        var delta = nextFirstRow - firstVisibleRow;
        if (delta > 0)
        {
            for (var step = 0; step < delta; step++)
            {
                var outgoing = rows[0];
                rows.RemoveAt(0);
                rows.Insert(activeRowCount - 1, outgoing);
            }
        }
        else
        {
            for (var step = 0; step < -delta; step++)
            {
                var outgoing = rows[activeRowCount - 1];
                rows.RemoveAt(activeRowCount - 1);
                rows.Insert(0, outgoing);
            }
        }

        firstVisibleRow = nextFirstRow;
        for (var index = 0; index < activeRowCount; index++)
        {
            var logicalRow = firstVisibleRow + index;
            if (rows[index].BoundRow != logicalRow)
                BindRow(rows[index], logicalRow, forceDataBind: false);
            else
                PositionRow(rows[index], logicalRow);
        }
        TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Grid.ScrollRowChanged");
    }

    private void BindRow(RowView row, int logicalRow, bool forceDataBind)
    {
        row.BoundRow = logicalRow;
        row.Root.gameObject.SetActive(true);
        PositionRow(row, logicalRow);
        var boundCells = 0;
        for (var column = 0; column < row.Cells.Length; column++)
        {
            var cell = row.Cells[column];
            if (column >= columns)
            {
                DeactivateCell(cell);
                continue;
            }
            PositionCell(cell.transform as RectTransform, column);
            var itemIndex = logicalRow * columns + column;
            if (itemIndex < 0 || itemIndex >= items.Count)
            {
                DeactivateCell(cell);
                continue;
            }
            var item = items[itemIndex];
            if (forceDataBind || !string.Equals(cell.ArtifactUid, item.ArtifactUid, StringComparison.Ordinal))
            {
                bindCell?.Invoke(cell, item);
                TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Grid.Bind");
                boundCells++;
            }
            else if (!cell.gameObject.activeSelf)
            {
                cell.gameObject.SetActive(true);
            }
        }
        if (boundCells > 0)
            TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Grid.IncomingRowBound");
    }

    private void LayoutBoundRows()
    {
        for (var index = 0; index < activeRowCount; index++)
        {
            var row = rows[index];
            if (row.BoundRow >= 0) PositionRow(row, row.BoundRow);
        }
    }

    private void PositionRow(RowView row, int logicalRow)
    {
        row.Root.anchorMin = new Vector2(0f, 1f);
        row.Root.anchorMax = new Vector2(0f, 1f);
        row.Root.pivot = new Vector2(0f, 1f);
        row.Root.sizeDelta = new Vector2(0f, cellSize.y);
        row.Root.anchoredPosition = new Vector2(
            0f,
            -(padding.top + logicalRow * (cellSize.y + spacing.y)));
    }

    private void PositionCell(RectTransform? rect, int column)
    {
        if (rect == null) return;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = cellSize;
        rect.anchoredPosition = new Vector2(
            padding.left + column * (cellSize.x + spacing.x),
            0f);
    }

    private int CurrentFirstVisibleRow()
    {
        if (content == null) return 0;
        return SpiritVirtualGridPolicy.FirstVisibleRow(
            content.anchoredPosition.y,
            padding.top,
            cellSize.y,
            spacing.y);
    }

    private static void DeactivateCell(SpiritArtifactCellView cell)
    {
        if (!cell.gameObject.activeSelf) return;
        cell.ResetForPool();
        cell.gameObject.SetActive(false);
    }
}
