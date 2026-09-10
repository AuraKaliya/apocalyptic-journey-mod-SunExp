using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class EndlessAbyssChoiceView
{
    private const string ShockArt = "Mods/Terrias/ModResource/Images/UI/无尽之渊UI/深渊震荡卡片";
    private const string MilestoneArt = "Mods/Terrias/ModResource/Images/UI/无尽之渊UI/里程碑卡片";
    internal static readonly Color Gold = new(0.94f, 0.8f, 0.49f);
    internal static readonly Color TextColor = new(0.94f, 0.94f, 0.9f);
    private readonly Dictionary<string, EndlessAbyssSelectionGlow> outlines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Text> selectionLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Button> refreshButtons = new();
    private readonly Dictionary<int, Text> refreshLabels = new();
    private readonly Text hint;
    private readonly Text subtitle;
    public GameObject Root { get; }
    public RectTransform Content { get; }
    public Button Confirm { get; }
    public Button Back { get; }

    public EndlessAbyssChoiceView(string panelName, Transform parent, string title)
    {
        Root = TerriasModalHost.CreateFullscreenRoot(panelName, parent, new Color(0f, 0f, 0f, 0.82f));
        var surface = TerriasUiBuilder.CreateRect("Surface", Root.transform, Vector2.one * 0.5f, Vector2.one * 0.5f,
            Vector2.one * 0.5f, new Vector2(1540f, 940f));
        Root.AddComponent<EndlessAbyssChoiceSizing>().Surface = surface;
        Root.GetComponent<EndlessAbyssChoiceSizing>().Fit();
        AddText(surface, "Title", title, 42, TextAnchor.MiddleCenter, Gold, new Vector2(0f, 414f), new Vector2(1400f, 62f));
        subtitle = AddText(surface, "Subtitle", "", 24, TextAnchor.MiddleCenter, TextColor, new Vector2(0f, 365f), new Vector2(1400f, 40f));
        Content = TerriasUiBuilder.CreateRect("Content", surface, Vector2.one * 0.5f, Vector2.one * 0.5f,
            Vector2.one * 0.5f, new Vector2(1460f, 700f));
        hint = AddText(surface, "Hint", "", 23, TextAnchor.MiddleCenter, TextColor, new Vector2(0f, -383f), new Vector2(1400f, 38f));
        Confirm = CreateButton(surface, "确定", new Vector2(220f, 62f), new Vector2(0f, -433f), () => { });
        Confirm.interactable = false;
        Back = CreateButton(surface, "返回", new Vector2(180f, 58f), new Vector2(-610f, -433f), () => { });
        Back.gameObject.SetActive(false);
    }

    public void SetSubtitle(string value) => subtitle.text = value;
    public void SetHint(string value) => hint.text = value;

    public void ClearContent()
    {
        outlines.Clear();
        selectionLabels.Clear();
        refreshButtons.Clear();
        refreshLabels.Clear();
        TerriasUiPool.ReleaseOrDestroyChildren(Content, "EndlessAbyssChoice.Clear", "[EndlessAbyssChoice]");
        Confirm.onClick.RemoveAllListeners();
    }

    public void ShowChoices(bool shock, EndlessAbyssChoiceState state, IReadOnlyList<EndlessAbyssChoiceOption> options,
        Action<string> select, Action<int> refresh)
    {
        ClearContent();
        Back.gameObject.SetActive(false);
        var sprite = TerriasResourceCache.Load<Sprite>(shock ? ShockArt : MilestoneArt, true, "abyss-choice");
        if (sprite == null) throw new InvalidOperationException("无尽之渊选项卡片素材加载失败。");
        var cardWidth = 600f * sprite.rect.width / sprite.rect.height;
        for (var slot = 0; slot < state.Offers.Count; slot++)
        {
            var index = slot;
            var option = options.First(item => item.Id == state.Offers[index]);
            var x = (index - 1) * 480f;
            var card = TerriasUiBuilder.CreateRect("Choice-" + option.Id, Content, Vector2.one * 0.5f,
                Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(cardWidth, 600f));
            card.anchoredPosition = new Vector2(x, 36f);
            var art = card.gameObject.AddComponent<Image>();
            art.sprite = sprite;
            art.preserveAspect = true;
            art.color = option.Available ? Color.white : new Color(0.55f, 0.55f, 0.55f);
            art.raycastTarget = true;
            var outline = card.gameObject.AddComponent<EndlessAbyssSelectionGlow>();
            outline.Bind(art);
            outlines[option.Id] = outline;
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = art;
            button.transition = Selectable.Transition.None;
            button.interactable = option.Available;
            button.onClick.AddListener(() => select(option.Id));

            var name = RelativeText(card, "Name", option.Name, 34, TextAnchor.MiddleCenter, Gold,
                shock ? new Vector2(0.23f, 0.62f) : new Vector2(0.18f, 0.47f),
                shock ? new Vector2(0.77f, 0.82f) : new Vector2(0.81f, 0.59f));
            name.resizeTextMinSize = 28;
            var body = RelativeText(card, "Description", option.Description, 27, TextAnchor.UpperLeft, TextColor,
                shock ? new Vector2(0.23f, 0.14f) : new Vector2(0.20f, 0.14f),
                shock ? new Vector2(0.77f, 0.49f) : new Vector2(0.80f, 0.44f));
            body.resizeTextMinSize = 23;
            selectionLabels[option.Id] = RelativeText(card, "Selection", "已选", 23, TextAnchor.MiddleCenter, Gold,
                new Vector2(0.2f, 0.065f), new Vector2(0.8f, 0.12f));
            if (!option.Available)
                RelativeText(card, "Unavailable", option.UnavailableReason, 21, TextAnchor.MiddleCenter, new Color(1f, 0.77f, 0.7f),
                    new Vector2(0.13f, 0.075f), new Vector2(0.87f, 0.15f));

            var refreshButton = CreateButton(Content, "", new Vector2(62f, 54f), new Vector2(x, -299f), () => refresh(index));
            var icon = TerriasUiBuilder.CreateRect("RefreshIcon", refreshButton.transform, Vector2.one * 0.5f,
                Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(31f, 31f));
            var graphic = icon.gameObject.AddComponent<EndlessAbyssRefreshIcon>();
            graphic.color = TextColor;
            graphic.raycastTarget = false;
            refreshButtons[index] = refreshButton;
            refreshLabels[index] = AddText(Content, "RefreshCount-" + index, "", 20, TextAnchor.MiddleCenter,
                TextColor, new Vector2(x, -343f), new Vector2(440f, 30f));
        }
    }

    public void RefreshSelection(EndlessAbyssChoiceState state, IReadOnlyList<EndlessAbyssChoiceOption> options, int required)
    {
        var ids = options.Select(option => option.Id).ToArray();
        bool Available(string id) => options.Any(option => option.Id == id && option.Available);
        foreach (var pair in outlines) pair.Value.SetSelected(state.Selected.Contains(pair.Key));
        foreach (var pair in selectionLabels) pair.Value.gameObject.SetActive(state.Selected.Contains(pair.Key));
        foreach (var pair in refreshButtons)
        {
            var canRefresh = state.CanRefresh(pair.Key, ids, Available);
            pair.Value.interactable = canRefresh;
            refreshLabels[pair.Key].text = state.Refreshed[pair.Key] ? "刷新已用完"
                : canRefresh ? "免费刷新 1 次" : "暂无其他可用选项 · 未消耗";
        }
        Confirm.interactable = state.IsReady(required, Available);
        SetHint("已选 " + state.Selected.Count + " / " + required);
    }

    internal static Button CreateButton(Transform parent, string label, Vector2 size, Vector2 position, Action action)
    {
        var rect = TerriasUiBuilder.CreateRect("Button-" + label, parent, Vector2.one * 0.5f, Vector2.one * 0.5f, Vector2.one * 0.5f, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = TerriasUiSprites.Button("[EndlessAbyssChoice]");
        image.type = Image.Type.Sliced;
        image.color = new Color(0.24f, 0.22f, 0.30f);
        rect.anchoredPosition = position;
        var button = rect.gameObject.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, Gold);
        button.onClick.AddListener(() => action());
        RelativeText(rect, "Label", label, 26, TextAnchor.MiddleCenter, TextColor, Vector2.zero, Vector2.one);
        return button;
    }

    internal static Text AddText(RectTransform parent, string name, string value, int size, TextAnchor anchor,
        Color color, Vector2 position, Vector2 bounds) => TerriasUiBuilder.AddText(
            parent, name, value, size, FontStyle.Normal, anchor, color, position, bounds, 2);

    private static Text RelativeText(RectTransform parent, string name, string value, int size, TextAnchor anchor,
        Color color, Vector2 min, Vector2 max)
    {
        var rect = TerriasUiBuilder.CreateRect(name, parent, min, max, Vector2.one * 0.5f, Vector2.zero);
        return TerriasUiComponents.ConfigureText(rect.gameObject, value, size, anchor, color);
    }
}

internal sealed class EndlessAbyssChoiceSizing : MonoBehaviour
{
    public RectTransform? Surface;
    private void OnRectTransformDimensionsChange() => Fit();
    public void Fit()
    {
        if (Surface == null || transform is not RectTransform root) return;
        var scale = Mathf.Min(1f, Mathf.Min(root.rect.width / 1920f, root.rect.height / 1080f));
        Surface.localScale = Vector3.one * Mathf.Max(0.1f, scale);
    }
}

internal sealed class EndlessAbyssRefreshIcon : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.36f;
        var center = rectTransform.rect.center;
        const int segments = 28;
        for (var i = 0; i <= segments; i++)
        {
            var angle = (35f + i * 290f / segments) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            mesh.AddVert(center + direction * (radius - 1.8f), color, Vector2.zero);
            mesh.AddVert(center + direction * (radius + 1.8f), color, Vector2.zero);
            if (i == 0) continue;
            var p = i * 2;
            mesh.AddTriangle(p - 2, p - 1, p);
            mesh.AddTriangle(p - 1, p + 1, p);
        }
        var end = 325f * Mathf.Deg2Rad;
        var tip = center + new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * radius;
        var tangent = new Vector2(-Mathf.Sin(end), Mathf.Cos(end));
        var normal = new Vector2(Mathf.Cos(end), Mathf.Sin(end));
        var start = mesh.currentVertCount;
        mesh.AddVert(tip + tangent * 5f, color, Vector2.zero);
        mesh.AddVert(tip - tangent * 5f + normal * 5f, color, Vector2.zero);
        mesh.AddVert(tip - tangent * 5f - normal * 5f, color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
    }
}
