using System;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class EndlessAbyssCardGrid : MonoBehaviour
{
    private readonly List<EndlessAbyssCardCell> cells = new();
    private const string PoolKey = "EndlessAbyss.DictionaryCard";

    public void Show(RectTransform parent, IReadOnlyList<EndlessAbyssCardOption> options, Action<EndlessAbyssCardOption> select)
    {
        var prefab = TerriasResourceCache.Load<GameObject>("UI/DictionaryUI", false, "abyss-native-preview");
        var dictionary = prefab != null ? prefab.GetComponent<DictionaryUI>() : null;
        var templateRoot = dictionary?.CardList?.parent?.parent?.Find("TempList");
        var template = templateRoot != null && templateRoot.childCount > 0
            ? templateRoot.GetChild(0).GetComponent<DictionaryShowItem>() : null;
        if (template == null) throw new InvalidOperationException("游戏卡牌图鉴模板加载失败，请返回重试。");

        var area = TerriasUiComponents.CreateUniformGridScrollArea(parent, "AbyssCards", 660f, 1f, 5,
            new Vector2(272f, 375f), new Vector2(14f, 22f), new RectOffset(16, 16, 12, 12), 38f, new Color(0.06f, 0.07f, 0.10f, 0.6f));
        var rect = (RectTransform)area.Root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        foreach (var option in options)
        {
            var cell = TerriasUiPool.AcquireComponent<EndlessAbyssCardCell>(PoolKey, area.Content,
                "Card-" + option.InstanceId, (host, name) => EndlessAbyssCardCell.Create(host, name, template));
            cells.Add(cell);
            cell.Bind(option, () => select(option));
        }
    }

    public void Select(string instanceId)
    {
        foreach (var cell in cells) cell.SetSelected(cell.InstanceId == instanceId);
    }

    public void Release()
    {
        foreach (var cell in cells)
            if (cell != null) TerriasUiPool.Release(cell.gameObject, "EndlessAbyssCardGrid.Release", "[EndlessAbyss]");
        cells.Clear();
    }

    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}

internal sealed class EndlessAbyssCardCell : TerriasPooledUiBehaviour
{
    private DictionaryShowItem? view;
    private Button? button;
    private Image? background;
    private EndlessAbyssSelectionGlow? selectionGlow;
    private DataConfig? snapshot;
    public string InstanceId { get; private set; } = "";

    public static EndlessAbyssCardCell Create(Transform parent, string name, DictionaryShowItem template)
    {
        var root = TerriasUiBuilder.CreateRect(name, parent, Vector2.zero, Vector2.zero,
            Vector2.one * 0.5f, new Vector2(272f, 375f));
        var cell = root.gameObject.AddComponent<EndlessAbyssCardCell>();
        cell.background = root.gameObject.AddComponent<Image>();
        cell.background.color = Color.clear;
        cell.background.raycastTarget = true;
        cell.button = root.gameObject.AddComponent<Button>();
        cell.button.targetGraphic = cell.background;
        cell.button.transition = Selectable.Transition.None;
        cell.selectionGlow = root.gameObject.AddComponent<EndlessAbyssSelectionGlow>();
        cell.view = UnityEngine.Object.Instantiate(template, root, false);
        cell.view.name = "DictionaryCard";
        var rect = (RectTransform)cell.view.transform;
        var bounds = rect.rect.size;
        if (bounds.x <= 0f || bounds.y <= 0f) throw new InvalidOperationException("游戏图鉴卡牌尺寸无效。");
        rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
        rect.pivot = Vector2.one * 0.5f;
        rect.sizeDelta = bounds;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one * Mathf.Min(256f / bounds.x, 359f / bounds.y);
        return cell;
    }

    public void Bind(EndlessAbyssCardOption option, Action action)
    {
        ResetForPool();
        InstanceId = option.InstanceId;
        snapshot = EndlessAbyssCardPreviewApi.Snapshot(option.Card);
        view!.ItemType = "Card";
        view.Init(snapshot);
        view.gameObject.SetActive(true);
        selectionGlow!.BindNativeCard(view.transform);
        foreach (var graphic in view.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        button!.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => action());
        button.interactable = true;
        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (background != null) background.color = Color.clear;
        selectionGlow?.SetSelected(selected);
    }

    public override void ResetForPool()
    {
        selectionGlow?.Clear();
        if (view != null && snapshot != null)
            AuraCardPresentationRuntime.RequestReset(new AuraCardPresentationContext
            {
                Root = view.transform, Config = snapshot, Surface = AuraCardPresentationSurface.Dictionary,
                ResetKind = AuraCardPresentationResetKind.Rebind, Source = "EndlessAbyss.CardPicker.Release"
            });
        snapshot?.scriptExecutor?.Clear();
        snapshot = null;
        InstanceId = "";
        if (button != null) { button.onClick.RemoveAllListeners(); button.interactable = false; }
        if (view != null) { view.dataConfig = null; view.gameObject.SetActive(false); }
    }
}
