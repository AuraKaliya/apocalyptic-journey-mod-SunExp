using System;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class AuraToolsDamageMeterUi
{
    private const string RootName = "AuraToolsDamageMeterCanvas";
    private const string PanelName = "AuraToolsDamageMeterPanel";
    private const string DetailName = "AuraToolsDamageMeterDetails";
    private static GameObject? root;
    private static GameObject? panel;
    private static Transform? rows;
    private static Text? title;
    private static Text? footer;

    public static void EnsureDriver()
    {
        EnsureRoot();
        if (root != null && root.GetComponent<AuraToolsDamageMeterDriver>() == null)
        {
            root.AddComponent<AuraToolsDamageMeterDriver>();
        }
    }

    public static void SetVisible(bool visible)
    {
        EnsurePanel();
        panel?.SetActive(visible);
    }

    public static void Refresh(AuraToolsDamageMeterState state, DamageMeterSettings settings)
    {
        EnsurePanel();
        if (panel == null || rows == null)
        {
            return;
        }

        var isVisible = AuraToolsDamageMeterRuntime.Visible
                        && AuraToolsDamageMeterRuntime.Enabled
                        && state.InFight;
        panel.SetActive(isVisible);
        if (!isVisible)
        {
            return;
        }

        title!.text = "DPS统计  回合 " + state.RoundIndex;
        ClearChildren(rows);
        var visibleRows = state.VisibleRows(settings);
        var maxDamage = Math.Max(1, state.MaxFightDamage(settings));
        if (visibleRows.Count == 0)
        {
            AddText(rows, "暂无伤害记录", 15, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 34f, 1f);
        }
        else
        {
            foreach (var stat in visibleRows)
            {
                CreateStatRow(rows, stat, maxDamage);
            }
        }

        var total = visibleRows.Sum(stat => stat.FightDamage);
        footer!.text = "本场合计 " + total + "  /  " + settings.Hotkey + " 显示隐藏";
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
            root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            root.AddComponent<GraphicRaycaster>();
        }
    }

    private static void EnsurePanel()
    {
        EnsureRoot();
        if (root == null || panel != null)
        {
            return;
        }

        panel = CreateRect(PanelName, root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(360f, 330f));
        var rect = panel.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(18f, -92f);
        AddPanel(panel, new Color(0.035f, 0.032f, 0.05f, 0.92f));

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
        title = AddText(header.transform, "DPS统计", 17, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 30f, 1f);
        AddButton(header.transform, "X", () => AuraToolsDamageMeterRuntime.SetVisible(false), 34f, 30f);

        rows = CreateLayout("Rows", panel.transform).transform;
        var rowsLayout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        rowsLayout.spacing = 4f;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;
        rows.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

        footer = AddText(panel.transform, "", 13, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 28f, 1f);
        panel.SetActive(false);
    }

    private static void CreateStatRow(Transform parent, CombatantStat stat, int maxDamage)
    {
        var row = CreateLayout("Row-" + stat.InstanceId, parent);
        SetHeight(row, 48f);
        AddPanel(row, stat.IsFriendly ? new Color(0.08f, 0.11f, 0.08f, 0.86f) : new Color(0.12f, 0.07f, 0.07f, 0.86f));
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(7, 7, 4, 4);
        layout.spacing = 7f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        AddText(row.transform, stat.IsFriendly ? "友" : "敌", 13, TextAnchor.MiddleCenter, stat.IsFriendly ? AuraToolsUi.SuccessText : AuraToolsUi.WarningText, 32f, 0f, 28f);
        AddText(row.transform, TrimName(stat.DisplayName), 14, TextAnchor.MiddleLeft, stat.IsDead ? AuraToolsUi.MutedText : AuraToolsUi.Text, 32f, 1f);
        AddText(row.transform, "回 " + stat.RoundDamage, 13, TextAnchor.MiddleRight, AuraToolsUi.Text, 32f, 0f, 62f);
        AddText(row.transform, "场 " + stat.FightDamage, 13, TextAnchor.MiddleRight, AuraToolsUi.Accent, 32f, 0f, 66f);
        AddProgress(row.transform, (float)stat.FightDamage / maxDamage);
        AddButton(row.transform, "明细", () => ShowDetails(stat), 58f, 32f);
    }

    private static void ShowDetails(CombatantStat stat)
    {
        EnsureRoot();
        if (root == null)
        {
            return;
        }

        CloseDetails();
        var overlay = CreateRect(DetailName, root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AddPanel(overlay, new Color(0f, 0f, 0f, 0.35f));
        var blocker = overlay.AddComponent<Button>();
        blocker.targetGraphic = overlay.GetComponent<Image>();
        blocker.onClick.AddListener(CloseDetails);

        var window = CreateRect("Window", overlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(430f, 360f));
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
        AddText(header.transform, stat.DisplayName + " 伤害明细", 16, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 32f, 1f);
        AddButton(header.transform, "关闭", CloseDetails, 74f, 32f);

        var content = CreateLayout("Content", window.transform);
        content.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 4f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        foreach (var detail in stat.Details.OrderByDescending(pair => pair.Value).Take(12))
        {
            var row = CreateLayout("Detail-" + detail.Key, content.transform);
            SetHeight(row, 32f);
            AddPanel(row, AuraToolsUi.Row);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(8, 8, 2, 2);
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            AddText(row.transform, detail.Key, 14, TextAnchor.MiddleLeft, AuraToolsUi.Text, 28f, 1f);
            AddText(row.transform, detail.Value.ToString(), 14, TextAnchor.MiddleRight, AuraToolsUi.Accent, 28f, 0f, 86f);
        }
    }

    private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
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

    private static void AddPanel(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
    }

    private static void AddProgress(Transform parent, float value)
    {
        var root = CreateLayout("Progress", parent);
        var element = root.AddComponent<LayoutElement>();
        element.minWidth = 48f;
        element.preferredWidth = 48f;
        element.minHeight = 10f;
        element.preferredHeight = 10f;
        AddPanel(root, new Color(0.01f, 0.01f, 0.02f, 0.85f));
        var fill = CreateRect("Fill", root.transform, Vector2.zero, new Vector2(Mathf.Clamp01(value), 1f), Vector2.zero, Vector2.zero);
        AddPanel(fill, AuraToolsUi.Accent);
    }

    private static Text AddText(Transform parent, string value, int fontSize, TextAnchor anchor, Color color, float height, float flexibleWidth, float width = 0f)
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

        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
        AddPanel(go, new Color(0.16f, 0.13f, 0.21f, 0.98f));
        var button = go.AddComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        button.onClick.AddListener(() => action());
        var text = AddText(go.transform, label, 13, TextAnchor.MiddleCenter, AuraToolsUi.Text, height, 1f);
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    private static void SetHeight(GameObject go, float height)
    {
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = height;
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
    }

    private static void ClearChildren(Transform transform)
    {
        for (var i = transform.childCount - 1; i >= 0; i--)
        {
            Object.Destroy(transform.GetChild(i).gameObject);
        }
    }

    private static string TrimName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "未知单位" : value.Trim();
        return value.Length <= 10 ? value : value.Substring(0, 10);
    }
}

internal sealed class AuraToolsDamageMeterDriver : MonoBehaviour
{
    private void Update()
    {
        AuraToolsDamageMeterRuntime.Tick();
    }
}
