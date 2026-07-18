using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.Core;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class AuraToolsDamageMeterUi
{
    private const string RootName = "AuraToolsDamageMeterCanvas-v3";
    private const string ToggleName = "AuraToolsDamageMeterToggle";
    private const string PanelName = "AuraToolsDamageMeterPanel";
    private const string DetailName = "AuraToolsDamageMeterDetails";
    private const string HistoryName = "AuraToolsDamageMeterHistory";
    private const float ToggleSize = 54f;
    private const float PanelWidth = 520f;
    private const float EdgeMargin = 8f;
    private static readonly List<DamageMeterRowView> RowPool = new();
    private static GameObject? root;
    private static GameObject? toggleButton;
    private static RectTransform? toggleRect;
    private static GameObject? panel;
    private static RectTransform? panelRect;
    private static CanvasGroup? panelCanvasGroup;
    private static GameObject? columns;
    private static Transform? rows;
    private static GameObject? emptyState;
    private static Text? emptyText;
    private static Text? title;
    private static Text? footer;
    private static Button? historyButton;
    private static Vector2 savedButtonPosition = new(-24f, 88f);
    private static float lastPanelHeight = -1f;

    internal static GameObject? Root => root;

    public static void EnsureDriver()
    {
        EnsureRoot();
        if (root != null && root.GetComponent<AuraToolsDamageMeterDriver>() == null)
        {
            root.AddComponent<AuraToolsDamageMeterDriver>();
        }
    }

    public static void ReleaseDriver()
    {
        if (root == null)
        {
            return;
        }

        var driver = root.GetComponent<AuraToolsDamageMeterDriver>();
        if (driver != null)
        {
            Object.Destroy(driver);
        }
    }

    public static void SetAvailable(bool available)
    {
        EnsureShell();
        if (toggleButton != null)
        {
            SetActiveIfChanged(toggleButton, available);
            if (available)
            {
                toggleButton.transform.SetAsLastSibling();
            }
        }

        if (!available)
        {
            SetActiveIfChanged(panel, false);
            CloseDetails();
            CloseHistory();
        }

        ApplyExpandedState();
    }

    public static void SetVisible(bool visible)
    {
        EnsureShell();
        ApplyExpandedState();
    }

    public static void Refresh(
        DamageLedger ledger,
        DamageRunLedger runAggregate,
        DamageHistoryStore history,
        DamageMeterSettings settings,
        string networkStatus)
    {
        EnsureShell();
        if (panel == null
            || panelRect == null
            || rows == null
            || columns == null
            || emptyState == null
            || emptyText == null
            || title == null
            || footer == null)
        {
            return;
        }

        ApplyExpandedState();
        if (!AuraToolsDamageMeterRuntime.Available || !AuraToolsDamageMeterRuntime.Visible)
        {
            return;
        }

        panel.transform.SetAsLastSibling();
        var presentation = DamageMeterHudPresenter.Build(ledger, runAggregate, history, settings, networkStatus);
        if (Math.Abs(lastPanelHeight - presentation.Height) > 0.1f
            || Math.Abs(panelRect.sizeDelta.x - PanelWidth) > 0.1f)
        {
            panelRect.sizeDelta = new Vector2(PanelWidth, presentation.Height);
            lastPanelHeight = presentation.Height;
            UpdatePanelPosition();
        }

        SetTextIfChanged(title, presentation.Title);
        if (historyButton != null)
        {
            if (historyButton.interactable != presentation.HasHistory)
            {
                historyButton.interactable = presentation.HasHistory;
            }
        }

        SetActiveIfChanged(columns, presentation.InFight);
        SetActiveIfChanged(rows.gameObject, presentation.InFight);
        SetActiveIfChanged(emptyState, !presentation.InFight);

        if (!presentation.InFight)
        {
            HideAllRows();
            SetTextIfChanged(emptyText, presentation.EmptyMessage);
            SetTextIfChanged(footer, presentation.Footer);
            return;
        }

        EnsureRows(settings.MaxRows);
        for (var i = 0; i < RowPool.Count; i++)
        {
            if (i >= presentation.VisibleRows.Count || i >= settings.MaxRows)
            {
                RowPool[i].SetVisible(false);
                continue;
            }

            RowPool[i].Bind(
                presentation.VisibleRows[i],
                ledger,
                settings,
                presentation.GrandTotal,
                ShowDetails);
        }

        SetTextIfChanged(footer, presentation.Footer);
    }

    public static void CloseDetails()
    {
        if (root == null)
        {
            return;
        }

        var existing = root.transform.Find(DetailName);
        if (existing != null)
        {
            Object.Destroy(existing.gameObject);
        }
    }

    public static void CloseHistory()
    {
        if (root == null)
        {
            return;
        }

        var existing = root.transform.Find(HistoryName);
        if (existing != null)
        {
            Object.Destroy(existing.gameObject);
        }

        CloseDetails();
    }

    private static void EnsureShell()
    {
        EnsureRoot();
        EnsureToggle();
        EnsurePanel();
    }

    internal static void EnsureRoot()
    {
        if (root != null)
        {
            return;
        }

        root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName, typeof(RectTransform));
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            root.AddComponent<GraphicRaycaster>();
        }
    }

    private static void EnsureToggle()
    {
        EnsureRoot();
        if (root == null || toggleButton != null)
        {
            return;
        }

        toggleButton = CreateRect(
            ToggleName,
            root.transform,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(ToggleSize, ToggleSize));
        toggleRect = toggleButton.GetComponent<RectTransform>();
        toggleRect.anchoredPosition = ClampToParent(savedButtonPosition, toggleRect, new Vector2(ToggleSize, ToggleSize));
        var image = ApplyButtonImage(toggleButton, new Color(0.14f, 0.11f, 0.18f, 0.96f));
        image.raycastTarget = true;

        var label = AddFillText(toggleButton.transform, "DPS", 16, TextAnchor.MiddleCenter, AuraToolsUi.Accent);
        label.fontStyle = FontStyle.Bold;
        var dragHandle = toggleButton.AddComponent<AuraToolsDamageMeterDragHandle>();
        dragHandle.Initialize(toggleRect, OnToggleDragged, () =>
        {
            AuraToolsDamageMeterRuntime.SetVisible(!AuraToolsDamageMeterRuntime.Visible);
        });
        toggleButton.SetActive(false);
    }

    private static void EnsurePanel()
    {
        EnsureRoot();
        if (root == null || panel != null)
        {
            return;
        }

        panel = CreateRect(
            PanelName,
            root.transform,
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 0f),
            new Vector2(PanelWidth, 360f));
        panelRect = panel.GetComponent<RectTransform>();
        panelCanvasGroup = panel.AddComponent<CanvasGroup>();
        ApplyPanelImage(panel, new Color(0.035f, 0.032f, 0.05f, 0.94f));

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CreateLayout("Header", panel.transform);
        SetHeight(header, 34f);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        title = AddText(
            header.transform,
            "DPS统计（世界推演）",
            17,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Accent,
            30f,
            1f);
        var headerActions = CreateLayout("HeaderActions", header.transform);
        var headerActionsElement = headerActions.AddComponent<LayoutElement>();
        headerActionsElement.minWidth = 146f;
        headerActionsElement.preferredWidth = 146f;
        headerActionsElement.minHeight = 30f;
        headerActionsElement.preferredHeight = 30f;
        headerActionsElement.flexibleWidth = 0f;
        var headerActionsLayout = headerActions.AddComponent<HorizontalLayoutGroup>();
        headerActionsLayout.spacing = AuraToolsUi.Theme.Metrics.SmallSpacing;
        headerActionsLayout.childControlWidth = true;
        headerActionsLayout.childControlHeight = true;
        headerActionsLayout.childForceExpandWidth = false;
        headerActionsLayout.childForceExpandHeight = true;

        historyButton = AddButton(
            headerActions.transform,
            "查看历史",
            () => FightDamageHistoryPresenter.Show(
                AuraToolsDamageMeterRuntime.History,
                AuraToolsConfigService.MatchExperience.DamageMeter),
            82f,
            30f);
        historyButton.interactable = false;
        AddButton(headerActions.transform, "收起", () => AuraToolsDamageMeterRuntime.SetVisible(false), 58f, 30f);

        columns = CreateLayout("Columns", panel.transform);
        SetHeight(columns, 22f);
        var columnLayout = columns.AddComponent<HorizontalLayoutGroup>();
        columnLayout.spacing = 6f;
        columnLayout.childControlWidth = true;
        columnLayout.childControlHeight = true;
        columnLayout.childForceExpandWidth = false;
        AddText(columns.transform, "队", 11, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 20f, 0f, 28f);
        AddText(columns.transform, "角色", 11, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 20f, 1f);
        AddText(columns.transform, "本回合", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 20f, 0f, 62f);
        AddText(columns.transform, "本场", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 20f, 0f, 66f);
        AddText(columns.transform, "平均", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 20f, 0f, 66f);
        AddText(columns.transform, "占比", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 20f, 0f, 50f);
        AddText(columns.transform, "", 11, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 20f, 0f, 58f);

        rows = CreateLayout("Rows", panel.transform).transform;
        var rowsLayout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        rowsLayout.spacing = 4f;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;
        rows.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

        emptyState = CreateLayout("EmptyState", panel.transform);
        emptyState.AddComponent<LayoutElement>().flexibleHeight = 1f;
        AddPanel(emptyState, new Color(0.06f, 0.055f, 0.085f, 0.72f));
        emptyText = AddText(
            emptyState.transform,
            "",
            14,
            TextAnchor.MiddleCenter,
            AuraToolsUi.Text,
            120f,
            1f);
        emptyText.rectTransform.offsetMin = new Vector2(12f, 8f);
        emptyText.rectTransform.offsetMax = new Vector2(-12f, -8f);

        footer = AddText(panel.transform, "", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 28f, 1f);
        panel.SetActive(false);
    }

    private static void ApplyExpandedState()
    {
        var available = AuraToolsDamageMeterRuntime.Available && AuraToolsDamageMeterRuntime.Enabled;
        var panelVisible = available && AuraToolsDamageMeterRuntime.Visible;
        if (toggleButton != null)
        {
            SetActiveIfChanged(toggleButton, available);
            if (available)
            {
                toggleButton.transform.SetAsLastSibling();
            }
        }

        if (panel != null)
        {
            SetActiveIfChanged(panel, panelVisible);
            if (panelVisible)
            {
                panel.transform.SetAsLastSibling();
            }
        }

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = panelVisible ? 1f : 0f;
            panelCanvasGroup.interactable = panelVisible;
            panelCanvasGroup.blocksRaycasts = panelVisible;
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
        if (panelRect == null || toggleRect == null)
        {
            return;
        }

        var abovePosition = savedButtonPosition + new Vector2(0f, ToggleSize + 12f);
        panelRect.anchoredPosition = ClampToParent(abovePosition, panelRect, new Vector2(PanelWidth, panelRect.sizeDelta.y));
    }

    private static void SetActiveIfChanged(GameObject? value, bool active)
    {
        if (value != null && value.activeSelf != active)
        {
            value.SetActive(active);
        }
    }

    private static void SetTextIfChanged(Text? value, string text)
    {
        if (value != null && !string.Equals(value.text, text, StringComparison.Ordinal))
        {
            value.text = text;
        }
    }

    private static void SetColorIfChanged(Graphic? value, Color color)
    {
        if (value != null && value.color != color)
        {
            value.color = color;
        }
    }

    private static void EnsureRows(int count)
    {
        if (rows == null)
        {
            return;
        }

        count = Math.Max(1, Math.Min(12, count));
        while (RowPool.Count < count)
        {
            RowPool.Add(CreateStatRow(rows));
        }
    }

    private static void HideAllRows()
    {
        foreach (var row in RowPool)
        {
            row.SetVisible(false);
        }
    }

    private static DamageMeterRowView CreateStatRow(Transform parent)
    {
        var row = CreateLayout("DamageMeterRow-" + RowPool.Count, parent);
        SetHeight(row, 44f);
        var background = row.AddComponent<Image>();
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 3, 3);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        var team = AddText(row.transform, "", 12, TextAnchor.MiddleCenter, AuraToolsUi.Text, 30f, 0f, 28f);
        var name = AddText(row.transform, "", 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 30f, 1f);
        var round = AddText(row.transform, "", 12, TextAnchor.MiddleRight, AuraToolsUi.Text, 30f, 0f, 62f);
        var total = AddText(row.transform, "", 12, TextAnchor.MiddleRight, AuraToolsUi.Accent, 30f, 0f, 66f);
        var average = AddText(row.transform, "", 12, TextAnchor.MiddleRight, AuraToolsUi.Text, 30f, 0f, 66f);
        var share = AddText(row.transform, "", 12, TextAnchor.MiddleRight, AuraToolsUi.Text, 30f, 0f, 50f);
        var details = AddButton(row.transform, "明细", () => { }, 58f, 30f);
        row.SetActive(false);
        return new DamageMeterRowView(row, background, team, name, round, total, average, share, details);
    }

    internal static void ShowDetails(string instanceId, DamageLedger ledger, DamageMeterSettings settings)
    {
        DamageDetailsPresenter.Show(instanceId, ledger, settings);
    }

    internal static GameObject CreateRect(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = Vector2.zero;
        return go;
    }

    internal static GameObject CreateLayout(string name, Transform parent)
    {
        return CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
    }

    internal static Image AddPanel(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    internal static Text AddFillText(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var go = CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return ConfigureText(go, value, fontSize, anchor, color);
    }

    internal static Text AddText(
        Transform parent,
        string value,
        int fontSize,
        TextAnchor anchor,
        Color color,
        float height,
        float flexibleWidth,
        float width = 0f)
    {
        var go = CreateLayout("Text", parent);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
        element.flexibleWidth = flexibleWidth;
        if (width > 0f)
        {
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
        }

        return ConfigureText(go, value, fontSize, anchor, color);
    }

    internal static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var text = go.AddComponent<Text>();
        text.font = AuraUiNativeBridge.ResolveLegacyFont();
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    internal static Button AddButton(Transform parent, string label, Action action, float width, float height)
    {
        var go = CreateLayout("Button-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = width;
        element.preferredWidth = width;
        element.minHeight = height;
        element.preferredHeight = height;
        var image = ApplyButtonImage(go, new Color(0.16f, 0.13f, 0.21f, 0.98f));
        var button = go.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, AuraToolsUi.Accent);
        button.onClick.AddListener(() => action());
        var text = AddFillText(go.transform, label, 13, TextAnchor.MiddleCenter, AuraToolsUi.Text);
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    internal static Image ApplyButtonImage(GameObject target, Color fallbackTint)
    {
        var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
        image.sprite = DamageMeterUiAssets.GetButtonSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? Color.white : fallbackTint;
        return image;
    }

    internal static Image ApplyPanelImage(GameObject target, Color fallbackOrTint)
    {
        var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
        image.sprite = DamageMeterUiAssets.GetPanelSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, fallbackOrTint.a) : fallbackOrTint;
        if (image.sprite != null)
        {
            AddPanelTint(target, fallbackOrTint);
        }

        return image;
    }


    private static void AddPanelTint(GameObject target, Color color)
    {
        var tint = new GameObject("PanelTint", typeof(RectTransform));
        tint.transform.SetParent(target.transform, false);
        tint.transform.SetAsFirstSibling();
        var rect = tint.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);
        var layout = tint.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        var image = tint.AddComponent<Image>();
        image.color = new Color(color.r, color.g, color.b, Mathf.Min(0.62f, color.a));
        image.raycastTarget = false;
    }

    internal static void SetHeight(GameObject go, float height)
    {
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
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

    internal static string TrimName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "未知单位" : value.Trim();
        return value.Length <= 14 ? value : value.Substring(0, 14);
    }

    internal static string TeamLabel(DamageTeam team)
    {
        return team switch
        {
            DamageTeam.Friendly => "友",
            DamageTeam.Enemy => "敌",
            _ => "?"
        };
    }

    private sealed class DamageMeterRowView
    {
        private readonly Image background;
        private readonly Text team;
        private readonly Text name;
        private readonly Text round;
        private readonly Text total;
        private readonly Text average;
        private readonly Text share;
        private readonly Button details;
        private string currentInstanceId = "";
        private DamageLedger? currentLedger;
        private DamageMeterSettings? currentSettings;
        private Action<string, DamageLedger, DamageMeterSettings>? currentShowDetails;

        public DamageMeterRowView(
            GameObject root,
            Image background,
            Text team,
            Text name,
            Text round,
            Text total,
            Text average,
            Text share,
            Button details)
        {
            Root = root;
            this.background = background;
            this.team = team;
            this.name = name;
            this.round = round;
            this.total = total;
            this.average = average;
            this.share = share;
            this.details = details;
            this.details.onClick.RemoveAllListeners();
            this.details.onClick.AddListener(ShowCurrentDetails);
        }

        public GameObject Root { get; }

        public void SetVisible(bool visible)
        {
            SetActiveIfChanged(Root, visible);
        }

        public void Bind(
            CombatantDamageStat stat,
            DamageLedger ledger,
            DamageMeterSettings settings,
            long grandTotal,
            Action<string, DamageLedger, DamageMeterSettings> showDetails)
        {
            SetVisible(true);
            SetColorIfChanged(
                background,
                stat.Team switch
                {
                    DamageTeam.Friendly => new Color(0.08f, 0.11f, 0.08f, 0.86f),
                    DamageTeam.Enemy => new Color(0.12f, 0.07f, 0.07f, 0.86f),
                    _ => new Color(0.10f, 0.09f, 0.12f, 0.86f)
                });
            SetTextIfChanged(
                team,
                stat.Team switch
                {
                    DamageTeam.Friendly => "友",
                    DamageTeam.Enemy => "敌",
                    _ => "?"
                });
            SetColorIfChanged(
                team,
                stat.Team switch
                {
                    DamageTeam.Friendly => AuraToolsUi.SuccessText,
                    DamageTeam.Enemy => AuraToolsUi.WarningText,
                    _ => AuraToolsUi.MutedText
                });
            SetTextIfChanged(name, TrimName(stat.DisplayName));
            SetColorIfChanged(name, stat.IsDead ? AuraToolsUi.MutedText : AuraToolsUi.Text);
            SetTextIfChanged(round, stat.DisplayCurrentRound(settings.CountShieldLoss).ToString());
            var totalValue = stat.DisplayTotal(settings.CountShieldLoss);
            SetTextIfChanged(total, totalValue.ToString());
            SetTextIfChanged(
                average,
                settings.ShowAverageDpt
                    ? stat.AveragePerCompletedRound(
                        settings.CountShieldLoss,
                        Math.Max(1, ledger.AveragingRoundCount)).ToString("0.0")
                    : "-");
            SetTextIfChanged(
                share,
                settings.ShowTeamShare
                    ? grandTotal <= 0 ? "0%" : ((double)totalValue / grandTotal).ToString("P0")
                    : "-");
            currentInstanceId = stat.InstanceId;
            currentLedger = ledger;
            currentSettings = settings;
            currentShowDetails = showDetails;
        }

        private void ShowCurrentDetails()
        {
            if (!string.IsNullOrWhiteSpace(currentInstanceId)
                && currentLedger != null
                && currentSettings != null)
            {
                currentShowDetails?.Invoke(currentInstanceId, currentLedger, currentSettings);
            }
        }
    }

    private sealed class AuraToolsDamageMeterDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private const float ClickMoveThresholdSqr = 36f;
        private RectTransform? target;
        private Action<Vector2>? onDragged;
        private Action? onClicked;
        private Vector2 dragStartPosition;
        private Vector2 pointerStartPosition;
        private Vector2 pointerDownPosition;
        private bool dragged;
        private bool pointerDownSeen;
        private bool suppressNextClick;

        public void Initialize(RectTransform dragTarget, Action<Vector2> dragCallback, Action clickCallback)
        {
            target = dragTarget;
            onDragged = dragCallback;
            onClicked = clickCallback;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pointerDownSeen = true;
            pointerDownPosition = eventData.position;
            dragged = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            var clickStart = pointerDownSeen ? pointerDownPosition : eventData.pressPosition;
            var clickDelta = eventData.position - clickStart;
            if (suppressNextClick || dragged || clickDelta.sqrMagnitude > ClickMoveThresholdSqr)
            {
                suppressNextClick = false;
                dragged = false;
                pointerDownSeen = false;
                eventData.Use();
                return;
            }

            dragged = false;
            pointerDownSeen = false;
            onClicked?.Invoke();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target == null)
            {
                return;
            }

            dragged = false;
            suppressNextClick = false;
            dragStartPosition = target.anchoredPosition;
            pointerStartPosition = eventData.position;
            pointerDownPosition = eventData.position;
            pointerDownSeen = true;
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
                suppressNextClick = true;
                eventData.Use();
            }
        }
    }
}
