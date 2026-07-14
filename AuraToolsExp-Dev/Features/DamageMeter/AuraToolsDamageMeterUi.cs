using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string ButtonSpritePath = "Mods/AuraToolsExp/ModResource/Images/UI/button-九宫格.png";
    private const string PanelSpritePath = "Mods/AuraToolsExp/ModResource/Images/UI/background-九宫格.png";
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
    private static Sprite? buttonSprite;
    private static Sprite? panelSprite;
    private static bool buttonSpriteLoadAttempted;
    private static bool panelSpriteLoadAttempted;
    private static Vector2 savedButtonPosition = new(-24f, 88f);
    private static float lastPanelHeight = -1f;

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
        var inFight = ledger.InFight;
        var height = inFight
            ? Math.Min(720f, 132f + Math.Max(1, settings.MaxRows) * 48f)
            : 250f;
        if (Math.Abs(lastPanelHeight - height) > 0.1f
            || Math.Abs(panelRect.sizeDelta.x - PanelWidth) > 0.1f)
        {
            panelRect.sizeDelta = new Vector2(PanelWidth, height);
            lastPanelHeight = height;
            UpdatePanelPosition();
        }

        SetTextIfChanged(
            title,
            inFight
                ? "DPS统计（按回合/DPT）  回合 " + ledger.CurrentRoundIndex
                : "DPS统计（世界推演）");
        if (historyButton != null)
        {
            var hasHistory = history.Records.Count > 0;
            if (historyButton.interactable != hasHistory)
            {
                historyButton.interactable = hasHistory;
            }
        }

        SetActiveIfChanged(columns, inFight);
        SetActiveIfChanged(rows.gameObject, inFight);
        SetActiveIfChanged(emptyState, !inFight);

        if (!inFight)
        {
            HideAllRows();
            var emptyMessage = history.Records.Count > 0
                ? "当前没有进行中的战斗。\n可通过“查看历史”回顾本轮冒险的输出记录。"
                : "等待下一场战斗开始。\n悬浮球会在世界推演的备战、地图和战斗界面保持可用。";
            var idleRunTotal = runAggregate.DisplayGrandTotal(
                settings.CountShieldLoss,
                settings.FriendlyOnly,
                settings.IncludeUnknownTeam);
            if (runAggregate.HasDamage)
            {
                emptyMessage = "本轮冒险累计伤害 " + idleRunTotal
                               + "\n战斗 " + runAggregate.EncounterCount
                               + " 场 / 回合 " + runAggregate.TotalRounds;
            }

            SetTextIfChanged(emptyText, emptyMessage);
            SetTextIfChanged(footer, networkStatus + "  /  拖动悬浮球可调整位置");
            return;
        }

        var visibleRows = ledger.VisibleRows(
            settings.FriendlyOnly,
            settings.IncludeUnknownTeam,
            settings.CountShieldLoss,
            settings.MaxRows);
        EnsureRows(settings.MaxRows);
        var grandTotal = ledger.DisplayGrandTotal(
            settings.CountShieldLoss,
            settings.FriendlyOnly,
            settings.IncludeUnknownTeam);
        for (var i = 0; i < RowPool.Count; i++)
        {
            if (i >= visibleRows.Count || i >= settings.MaxRows)
            {
                RowPool[i].SetVisible(false);
                continue;
            }

            RowPool[i].Bind(
                visibleRows[i],
                ledger,
                settings,
                grandTotal,
                ShowDetails);
        }

        SetTextIfChanged(
            footer,
            "本场合计 " + grandTotal
            + "  /  Run total " + runAggregate.DisplayGrandTotal(
                settings.CountShieldLoss,
                settings.FriendlyOnly,
                settings.IncludeUnknownTeam)
            + "  /  已完成 " + ledger.CompletedRoundCount + " 回合"
            + "  /  " + networkStatus
            + "  /  拖动悬浮球可调整位置");
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

    private static void EnsureRoot()
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
        historyButton = AddButton(
            header.transform,
            "查看历史",
            () => ShowHistory(
                AuraToolsDamageMeterRuntime.History,
                AuraToolsConfigService.MatchExperience.DamageMeter),
            82f,
            30f);
        historyButton.interactable = false;
        AddButton(header.transform, "收起", () => AuraToolsDamageMeterRuntime.SetVisible(false), 58f, 30f);

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

    private static void ShowHistory(DamageHistoryStore history, DamageMeterSettings settings)
    {
        EnsureRoot();
        if (root == null || history.Records.Count == 0)
        {
            return;
        }

        CloseHistory();
        var overlay = CreateRect(HistoryName, root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddPanel(overlay, new Color(0f, 0f, 0f, 0.42f));
        var blocker = overlay.AddComponent<Button>();
        blocker.targetGraphic = overlay.GetComponent<Image>();
        blocker.onClick.AddListener(CloseHistory);

        var window = CreateRect(
            "Window",
            overlay.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1040f, 620f));
        ApplyPanelImage(window, new Color(0.04f, 0.035f, 0.06f, 0.99f));
        var windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(12, 12, 10, 10);
        windowLayout.spacing = 8f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        var header = CreateLayout("Header", window.transform);
        SetHeight(header, 38f);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        AddText(header.transform, "本轮冒险输出历史", 17, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 34f, 1f);
        AddButton(header.transform, "关闭", CloseHistory, 72f, 32f);

        var body = CreateLayout("Body", window.transform);
        body.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 10f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = false;
        bodyLayout.childForceExpandHeight = true;

        var listViewport = CreateLayout("FightList", body.transform);
        var listElement = listViewport.AddComponent<LayoutElement>();
        listElement.minWidth = 240f;
        listElement.preferredWidth = 240f;
        listElement.flexibleWidth = 0f;
        AddPanel(listViewport, AuraToolsUi.Panel);
        var listContent = CreateScrollContent(listViewport);

        var details = CreateLayout("FightDetails", body.transform);
        details.AddComponent<LayoutElement>().flexibleWidth = 1f;
        AddPanel(details, new Color(0.055f, 0.05f, 0.075f, 0.82f));
        var detailsLayout = details.AddComponent<VerticalLayoutGroup>();
        detailsLayout.padding = new RectOffset(10, 10, 8, 8);
        detailsLayout.spacing = 5f;
        detailsLayout.childControlWidth = true;
        detailsLayout.childControlHeight = true;
        detailsLayout.childForceExpandWidth = true;
        detailsLayout.childForceExpandHeight = false;

        var ordered = history.Records.OrderByDescending(record => record.Sequence).ToList();
        foreach (var record in ordered)
        {
            var label = "第 " + record.Sequence + " 场  " + ResultLabel(record.Result)
                        + "  " + record.Snapshot.CompletedRoundCount + "回合";
            AddButton(listContent, label, () => RenderHistoryRecord(details.transform, record, settings), 216f, 34f);
        }

        RenderHistoryRecord(details.transform, ordered[0], settings);
        overlay.transform.SetAsLastSibling();
    }

    public static void ShowOutOfRunHistory(OutOfRunDamageHistoryStore history)
    {
        EnsureRoot();
        if (root == null || history == null)
        {
            return;
        }

        CloseHistory();
        var overlay = CreateRect(HistoryName, root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddPanel(overlay, new Color(0f, 0f, 0f, 0.42f));
        var blocker = overlay.AddComponent<Button>();
        blocker.targetGraphic = overlay.GetComponent<Image>();
        blocker.onClick.AddListener(CloseHistory);

        var window = CreateRect(
            "Window",
            overlay.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1180f, 640f));
        ApplyPanelImage(window, new Color(0.04f, 0.035f, 0.06f, 0.99f));
        var windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(12, 12, 10, 10);
        windowLayout.spacing = 8f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        var header = CreateLayout("Header", window.transform);
        SetHeight(header, 38f);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        AddText(header.transform, "局外历史记录", 17, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 34f, 1f);
        AddButton(
            header.transform,
            "清空",
            () =>
            {
                AuraToolsDamageMeterRuntime.ClearOutOfRunHistory();
                ShowOutOfRunHistory(AuraToolsDamageMeterRuntime.OutOfRunHistory);
            },
            72f,
            32f);
        AddButton(header.transform, "关闭", CloseHistory, 72f, 32f);

        var viewport = CreateLayout("HistoryList", window.transform);
        viewport.AddComponent<LayoutElement>().flexibleHeight = 1f;
        AddPanel(viewport, new Color(0.055f, 0.05f, 0.075f, 0.82f));
        var content = CreateScrollContent(viewport);

        RenderOutOfRunHeader(content);
        var ordered = history.Records.OrderByDescending(record => record.Sequence).ToList();
        if (ordered.Count == 0)
        {
            AddText(content, "暂无局外历史记录", 14, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 64f, 1f);
        }
        else
        {
            foreach (var record in ordered)
            {
                RenderOutOfRunRow(content, record);
            }
        }

        overlay.transform.SetAsLastSibling();
    }

    private static void RenderOutOfRunHeader(Transform parent)
    {
        var row = CreateLayout("Columns", parent);
        SetHeight(row, 28f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 3);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        AddText(row.transform, "游玩模式", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 0f, 120f);
        AddText(row.transform, "状态", 12, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 22f, 0f, 58f);
        AddText(row.transform, "队伍成员", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 0f, 460f);
        AddText(row.transform, "最强一击", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 0f, 178f);
        AddText(row.transform, "队伍DPS", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 0f, 112f);
        AddText(row.transform, "MVP", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 1f);
    }

    private static void RenderOutOfRunRow(Transform parent, OutOfRunDamageHistoryRecord record)
    {
        var row = CreateLayout("OutOfRun-" + record.Sequence, parent);
        SetHeight(row, 52f);
        AddPanel(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 5, 5);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        AddText(row.transform, string.IsNullOrWhiteSpace(record.ModeDisplayName) ? record.ModeId : record.ModeDisplayName, 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 40f, 0f, 120f);
        AddText(row.transform, record.Status ?? "", 13, TextAnchor.MiddleCenter, AuraToolsUi.Text, 40f, 0f, 58f);

        var members = CreateLayout("Members", row.transform);
        var memberElement = members.AddComponent<LayoutElement>();
        memberElement.minWidth = 460f;
        memberElement.preferredWidth = 460f;
        memberElement.flexibleWidth = 0f;
        var memberLayout = members.AddComponent<HorizontalLayoutGroup>();
        memberLayout.spacing = 5f;
        memberLayout.childControlWidth = true;
        memberLayout.childControlHeight = true;
        memberLayout.childForceExpandWidth = false;
        for (var index = 0; index < DamageMeterProtocol.MaxTeamMembers; index++)
        {
            var member = record.TeamMembers != null && index < record.TeamMembers.Count
                ? record.TeamMembers[index]
                : null;
            AddMemberCell(members.transform, member);
        }

        AddText(row.transform, BestHitLabel(record.BestHit), 12, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 40f, 0f, 178f);
        AddText(row.transform, DamageMeterFormatters.FormatScientific(record.TeamDps), 12, TextAnchor.MiddleLeft, AuraToolsUi.Text, 40f, 0f, 112f);
        AddText(row.transform, DamageMeterFormatters.TrimDisplayName(record.Mvp?.DisplayName ?? ""), 12, TextAnchor.MiddleLeft, AuraToolsUi.Text, 40f, 1f);
    }

    private static void AddMemberCell(Transform parent, OutOfRunTeamMemberSnapshot? member)
    {
        var cell = CreateLayout("Member", parent);
        var element = cell.AddComponent<LayoutElement>();
        element.minWidth = 110f;
        element.preferredWidth = 110f;
        element.flexibleWidth = 0f;
        var layout = cell.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        var avatar = CreateLayout("Avatar", cell.transform);
        var avatarElement = avatar.AddComponent<LayoutElement>();
        avatarElement.minWidth = 32f;
        avatarElement.preferredWidth = 32f;
        avatarElement.minHeight = 32f;
        avatarElement.preferredHeight = 32f;
        avatarElement.flexibleWidth = 0f;
        var avatarImage = AddPanel(avatar, new Color(0.08f, 0.075f, 0.105f, 0.95f));
        var sprite = TryLoadAvatarSprite(member?.AvatarPngBase64);
        if (sprite != null)
        {
            avatarImage.sprite = sprite;
            avatarImage.type = Image.Type.Simple;
            avatarImage.preserveAspect = true;
            avatarImage.color = Color.white;
        }

        var memberName = AddText(
            cell.transform,
            MemberDisplayName(member),
            12,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            32f,
            0f,
            74f);
        memberName.horizontalOverflow = HorizontalWrapMode.Overflow;
        memberName.verticalOverflow = VerticalWrapMode.Truncate;
    }

    private static string MemberDisplayName(OutOfRunTeamMemberSnapshot? member)
    {
        if (member == null)
        {
            return "";
        }

        var displayName = string.IsNullOrWhiteSpace(member.PlayerDisplayName)
            ? member.DisplayName
            : member.PlayerDisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = member.PlayerId;
        }

        return DamageMeterFormatters.TrimDisplayName(displayName ?? "");
    }

    private static string BestHitLabel(DamageBestHitRecord? bestHit)
    {
        if (bestHit == null || bestHit.Damage <= 0)
        {
            return "0.000 E+00 (-)";
        }

        return DamageMeterFormatters.FormatScientific(bestHit.Damage)
               + " ("
               + DamageMeterFormatters.TrimDisplayName(bestHit.SourceDisplayName)
               + ")";
    }

    private static string BestHitValueForStat(DamageBestHitRecord? bestHit, string instanceId)
    {
        if (bestHit == null
            || bestHit.Damage <= 0
            || !string.Equals(bestHit.SourceInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        return DamageMeterFormatters.FormatScientific(bestHit.Damage);
    }

    private static Sprite? TryLoadAvatarSprite(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadImageIntoTexture(texture, bytes))
            {
                Object.Destroy(texture);
                return null;
            }

            texture.filterMode = FilterMode.Bilinear;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
        }
        catch
        {
            return null;
        }
    }

    private static bool LoadImageIntoTexture(Texture2D texture, byte[] bytes)
    {
        try
        {
            var imageConversion = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEngine.ImageConversion"))
                .FirstOrDefault(type => type != null);
            var method = imageConversion?.GetMethod(
                "LoadImage",
                new[] { typeof(Texture2D), typeof(byte[]) });
            return method?.Invoke(null, new object[] { texture, bytes }) is true;
        }
        catch
        {
            return false;
        }
    }

    private static Transform CreateScrollContent(GameObject viewport)
    {
        var viewportRect = viewport.GetComponent<RectTransform>();
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var content = CreateRect(
            "Content",
            viewport.transform,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.offsetMin = new Vector2(6f, 0f);
        contentRect.offsetMax = new Vector2(-6f, 0f);
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 6, 6);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        return content.transform;
    }

    private static void RenderHistoryRecord(
        Transform parent,
        DamageFightRecord record,
        DamageMeterSettings settings)
    {
        ClearChildren(parent);
        var ledger = new DamageLedger();
        if (!ledger.ApplySnapshot(record.Snapshot))
        {
            AddText(parent, "历史记录无法读取。", 14, TextAnchor.MiddleCenter, AuraToolsUi.WarningText, 80f, 1f);
            return;
        }

        var grandTotal = ledger.DisplayGrandTotal(
            settings.CountShieldLoss,
            settings.FriendlyOnly,
            settings.IncludeUnknownTeam);
        AddText(
            parent,
            "第 " + record.Sequence + " 场  " + ResultLabel(record.Result)
            + "  /  " + ledger.CompletedRoundCount + " 回合"
            + "  /  合计 " + grandTotal,
            15,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Accent,
            34f,
            1f);

        AddText(
            parent,
            "最强一击 " + BestHitLabel(record.Snapshot.BestHit),
            13,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            28f,
            1f);

        var columns = CreateLayout("Columns", parent);
        SetHeight(columns, 24f);
        var columnsLayout = columns.AddComponent<HorizontalLayoutGroup>();
        columnsLayout.spacing = 6f;
        columnsLayout.childControlWidth = true;
        columnsLayout.childControlHeight = true;
        columnsLayout.childForceExpandWidth = false;
        AddText(columns.transform, "队", 11, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 22f, 0f, 28f);
        AddText(columns.transform, "角色", 11, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 1f);
        AddText(columns.transform, "总计", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 22f, 0f, 78f);
        AddText(columns.transform, "最强一击", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 22f, 0f, 112f);
        AddText(columns.transform, "平均", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 22f, 0f, 72f);
        AddText(columns.transform, "占比", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 22f, 0f, 58f);
        AddText(columns.transform, "", 11, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 22f, 0f, 58f);

        var visibleRows = ledger.VisibleRows(
            settings.FriendlyOnly,
            settings.IncludeUnknownTeam,
            settings.CountShieldLoss,
            Math.Max(settings.MaxRows, 12));
        foreach (var stat in visibleRows)
        {
            var row = CreateLayout("History-" + stat.InstanceId, parent);
            SetHeight(row, 40f);
            AddPanel(row, AuraToolsUi.Row);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 3, 3);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            AddText(row.transform, TeamLabel(stat.Team), 12, TextAnchor.MiddleCenter, AuraToolsUi.Text, 30f, 0f, 28f);
            AddText(row.transform, TrimName(stat.DisplayName), 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 30f, 1f);
            AddText(row.transform, stat.DisplayTotal(settings.CountShieldLoss).ToString(), 13, TextAnchor.MiddleRight, AuraToolsUi.Accent, 30f, 0f, 78f);
            AddText(
                row.transform,
                BestHitValueForStat(record.Snapshot.BestHit, stat.InstanceId),
                12,
                TextAnchor.MiddleRight,
                AuraToolsUi.Accent,
                30f,
                0f,
                112f);
            AddText(
                row.transform,
                stat.AveragePerCompletedRound(
                    settings.CountShieldLoss,
                    Math.Max(1, ledger.CompletedRoundCount)).ToString("0.0"),
                12,
                TextAnchor.MiddleRight,
                AuraToolsUi.Text,
                30f,
                0f,
                72f);
            AddText(
                row.transform,
                grandTotal <= 0
                    ? "0%"
                    : ((double)stat.DisplayTotal(settings.CountShieldLoss) / grandTotal).ToString("P0"),
                12,
                TextAnchor.MiddleRight,
                AuraToolsUi.Text,
                30f,
                0f,
                58f);
            AddButton(row.transform, "明细", () => ShowDetails(stat.InstanceId, ledger, settings), 58f, 30f);
        }
    }

    private static string ResultLabel(string result)
    {
        return result switch
        {
            "Win" => "胜利",
            "Escape" => "撤退",
            "Loss" => "失败",
            _ => "已结束"
        };
    }

    private static void ClearChildren(Transform parent)
    {
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            Object.Destroy(parent.GetChild(index).gameObject);
        }
    }

    private static void ShowDetails(string instanceId, DamageLedger ledger, DamageMeterSettings settings)
    {
        EnsureRoot();
        var stat = ledger.Combatants.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        if (root == null || stat == null)
        {
            return;
        }

        CloseDetails();
        var overlay = CreateRect(DetailName, root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddPanel(overlay, new Color(0f, 0f, 0f, 0.35f));
        var blocker = overlay.AddComponent<Button>();
        blocker.targetGraphic = overlay.GetComponent<Image>();
        blocker.onClick.AddListener(CloseDetails);

        var window = CreateRect(
            "Window",
            overlay.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(500f, 440f));
        AddPanel(window, new Color(0.04f, 0.035f, 0.06f, 0.98f));
        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CreateLayout("Header", window.transform);
        SetHeight(header, 36f);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        AddText(
            header.transform,
            stat.DisplayName + " 伤害明细",
            16,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Accent,
            32f,
            1f);
        AddButton(header.transform, "关闭", CloseDetails, 74f, 32f);

        var summary = "本回合 " + stat.DisplayCurrentRound(settings.CountShieldLoss)
                      + "　 本场 " + stat.DisplayTotal(settings.CountShieldLoss)
                      + "　 平均DPT " + stat.AveragePerCompletedRound(
                          settings.CountShieldLoss,
                          Math.Max(1, ledger.AveragingRoundCount)).ToString("0.0")
                      + "\nHP伤害 " + stat.TotalHpDamage
                      + "　 护盾伤害 " + stat.TotalShieldDamage
                      + "　 最高单回合 " + stat.HighestRound(settings.CountShieldLoss);
        AddText(window.transform, summary, 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 48f, 1f);

        var content = CreateLayout("Content", window.transform);
        content.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 4f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        foreach (var detail in stat.Details.Values
                     .OrderByDescending(item => item.HpDamage + (settings.CountShieldLoss ? item.ShieldDamage : 0))
                     .Take(12))
        {
            var detailRow = CreateLayout("Detail-" + detail.Key, content.transform);
            SetHeight(detailRow, 32f);
            AddPanel(detailRow, AuraToolsUi.Row);
            var detailLayout = detailRow.AddComponent<HorizontalLayoutGroup>();
            detailLayout.padding = new RectOffset(8, 8, 2, 2);
            detailLayout.spacing = 8f;
            detailLayout.childControlWidth = true;
            detailLayout.childControlHeight = true;
            AddText(detailRow.transform, detail.Label, 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 28f, 1f);
            AddText(
                detailRow.transform,
                ConfidenceLabel(detail.Confidence),
                11,
                TextAnchor.MiddleCenter,
                AuraToolsUi.MutedText,
                28f,
                0f,
                60f);
            AddText(
                detailRow.transform,
                (detail.HpDamage + (settings.CountShieldLoss ? detail.ShieldDamage : 0)).ToString(),
                13,
                TextAnchor.MiddleRight,
                AuraToolsUi.Accent,
                28f,
                0f,
                86f);
        }
    }

    private static string ConfidenceLabel(DamageAttributionConfidence confidence)
    {
        return confidence switch
        {
            DamageAttributionConfidence.Exact => "精确",
            DamageAttributionConfidence.Derived => "推导",
            DamageAttributionConfidence.Mixed => "混合",
            _ => "未知"
        };
    }

    private static GameObject CreateRect(
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

    private static GameObject CreateLayout(string name, Transform parent)
    {
        return CreateRect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
    }

    private static Image AddPanel(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text AddFillText(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var go = CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text AddText(
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

    private static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
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

    private static Button AddButton(Transform parent, string label, Action action, float width, float height)
    {
        var go = CreateLayout("Button-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = width;
        element.preferredWidth = width;
        element.minHeight = height;
        element.preferredHeight = height;
        var image = ApplyButtonImage(go, new Color(0.16f, 0.13f, 0.21f, 0.98f));
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => action());
        var text = AddFillText(go.transform, label, 13, TextAnchor.MiddleCenter, AuraToolsUi.Text);
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    private static Image ApplyButtonImage(GameObject target, Color fallbackTint)
    {
        var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
        image.sprite = GetButtonSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? Color.white : fallbackTint;
        return image;
    }

    private static Image ApplyPanelImage(GameObject target, Color fallbackOrTint)
    {
        var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
        image.sprite = GetPanelSprite();
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = image.sprite != null ? new Color(1f, 1f, 1f, fallbackOrTint.a) : fallbackOrTint;
        if (image.sprite != null)
        {
            AddPanelTint(target, fallbackOrTint);
        }

        return image;
    }

    private static Sprite? GetButtonSprite()
    {
        if (buttonSprite != null)
        {
            return buttonSprite;
        }

        if (buttonSpriteLoadAttempted)
        {
            return null;
        }

        buttonSpriteLoadAttempted = true;
        buttonSprite = TryLoadNineSliceSprite(ButtonSpritePath, new Vector4(14f, 14f, 14f, 14f), new Rect(17f, 16f, 135f, 49f));
        return buttonSprite;
    }

    private static Sprite? GetPanelSprite()
    {
        if (panelSprite != null)
        {
            return panelSprite;
        }

        if (panelSpriteLoadAttempted)
        {
            return null;
        }

        panelSpriteLoadAttempted = true;
        panelSprite = TryLoadNineSliceSprite(PanelSpritePath, new Vector4(4f, 4f, 4f, 4f), null);
        return panelSprite;
    }

    private static Sprite? TryLoadNineSliceSprite(string path, Vector4 fallbackBorder, Rect? sourceCrop)
    {
        try
        {
            var source = AuraToolsResourceCache.Load<Sprite>(path, true);
            if (source == null || source.texture == null)
            {
                return null;
            }

            var texture = source.texture;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var rect = sourceCrop.HasValue ? ResolveSpriteRect(source, sourceCrop.Value) : source.rect;
            var border = source.border.sqrMagnitude > 0.01f ? source.border : fallbackBorder;
            return Sprite.Create(
                texture,
                rect,
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
        }
        catch
        {
            return null;
        }
    }

    private static Rect ResolveSpriteRect(Sprite source, Rect crop)
    {
        var x = Mathf.Clamp(source.rect.x + crop.x, source.rect.x, source.rect.xMax);
        var y = Mathf.Clamp(source.rect.y + crop.y, source.rect.y, source.rect.yMax);
        var width = Mathf.Clamp(crop.width, 1f, source.rect.xMax - x);
        var height = Mathf.Clamp(crop.height, 1f, source.rect.yMax - y);
        return new Rect(x, y, width, height);
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

    private static void SetHeight(GameObject go, float height)
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

    private static string TrimName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "未知单位" : value.Trim();
        return value.Length <= 14 ? value : value.Substring(0, 14);
    }

    private static string TeamLabel(DamageTeam team)
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

internal sealed class AuraToolsDamageMeterDriver : MonoBehaviour
{
    private void Update()
    {
        AuraToolsDamageMeterRuntime.Tick();
    }
}
