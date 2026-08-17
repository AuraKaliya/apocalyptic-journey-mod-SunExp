using System;
using System.Collections;
using System.Collections.Generic;
using AuraShared.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraUi.Shared;

public sealed class AuraUiStableId : MonoBehaviour
{
    public string Value { get; private set; } = "";

    public void Set(string value)
    {
        Value = (value ?? "").Trim();
    }

    public static AuraUiStableId Assign(GameObject target, string value)
    {
        var marker = target.GetComponent<AuraUiStableId>()
                     ?? target.AddComponent<AuraUiStableId>();
        marker.Set(value);
        return marker;
    }
}

public sealed class AuraUiViewStateSnapshot
{
    public string FocusedId { get; set; } = "";

    public string AnchorId { get; set; } = "";

    public float AnchorOffsetY { get; set; }

    public float NormalizedFallback { get; set; } = 1f;
}

public static class AuraUiViewState
{
    public static AuraUiViewStateSnapshot? CaptureForContent(Transform content)
    {
        var scroll = ResolveScroll(content);
        return scroll == null ? null : Capture(scroll);
    }

    public static AuraUiViewStateSnapshot Capture(ScrollRect scroll)
    {
        var snapshot = new AuraUiViewStateSnapshot
        {
            NormalizedFallback = scroll == null
                ? 1f
                : scroll.verticalNormalizedPosition
        };
        if (scroll == null || scroll.content == null || scroll.viewport == null)
        {
            return snapshot;
        }

        snapshot.FocusedId = ResolveStableId(
            EventSystem.current?.currentSelectedGameObject?.transform);

        var viewportRect = scroll.viewport.rect;
        var bestTop = float.NegativeInfinity;
        foreach (var marker in scroll.content.GetComponentsInChildren<AuraUiStableId>(true))
        {
            if (marker == null
                || string.IsNullOrWhiteSpace(marker.Value)
                || marker.transform is not RectTransform rect
                || !marker.gameObject.activeInHierarchy)
            {
                continue;
            }

            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                scroll.viewport,
                rect);
            if (bounds.max.y < viewportRect.yMin || bounds.min.y > viewportRect.yMax)
            {
                continue;
            }

            if (bounds.max.y <= bestTop)
            {
                continue;
            }

            bestTop = bounds.max.y;
            snapshot.AnchorId = marker.Value;
            snapshot.AnchorOffsetY = viewportRect.yMax - bounds.max.y;
        }

        return snapshot;
    }

    public static void RestoreAfterLayout(
        Transform content,
        AuraUiViewStateSnapshot? snapshot,
        string source = "AuraUi.ViewState")
    {
        var scroll = ResolveScroll(content);
        if (scroll == null || snapshot == null)
        {
            return;
        }

        AuraSharedFrameScheduler.StartCoroutine(
            source,
            RestoreNextFrame(scroll, snapshot));
    }

    public static IEnumerator RestoreNextFrame(
        ScrollRect scroll,
        AuraUiViewStateSnapshot snapshot)
    {
        yield return null;
        if (scroll == null || scroll.content == null || scroll.viewport == null)
        {
            yield break;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.StopMovement();

        var anchor = FindByStableId(scroll.content, snapshot.AnchorId);
        if (anchor != null && anchor.transform is RectTransform anchorRect)
        {
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                scroll.viewport,
                anchorRect);
            var desiredTop = scroll.viewport.rect.yMax - snapshot.AnchorOffsetY;
            var delta = desiredTop - bounds.max.y;
            scroll.content.anchoredPosition += new Vector2(0f, delta);
        }
        else
        {
            scroll.verticalNormalizedPosition = Mathf.Clamp01(
                snapshot.NormalizedFallback);
        }

        Canvas.ForceUpdateCanvases();
        var focused = FindByStableId(scroll.content, snapshot.FocusedId);
        var selectable = focused == null
            ? null
            : focused.GetComponent<Selectable>()
              ?? focused.GetComponentInChildren<Selectable>(true);
        if (selectable != null
            && selectable.gameObject.activeInHierarchy
            && selectable.interactable)
        {
            EventSystem.current?.SetSelectedGameObject(selectable.gameObject);
        }
    }

    public static ScrollRect? ResolveScroll(Transform? content)
    {
        var current = content;
        while (current != null)
        {
            var scroll = current.GetComponent<ScrollRect>();
            if (scroll != null && scroll.content != null)
            {
                return scroll;
            }

            current = current.parent;
        }

        return null;
    }

    private static string ResolveStableId(Transform? current)
    {
        while (current != null)
        {
            var marker = current.GetComponent<AuraUiStableId>();
            if (marker != null && !string.IsNullOrWhiteSpace(marker.Value))
            {
                return marker.Value;
            }

            current = current.parent;
        }

        return "";
    }

    private static GameObject? FindByStableId(Transform root, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var marker in root.GetComponentsInChildren<AuraUiStableId>(true))
        {
            if (marker != null
                && string.Equals(marker.Value, value, StringComparison.Ordinal))
            {
                return marker.gameObject;
            }
        }

        return null;
    }
}

public sealed class AuraUiKeyedListReconciler<TKey, TModel>
{
    private readonly Transform parent;
    private readonly Dictionary<TKey, GameObject> rows;
    private readonly Func<TModel, TKey> keySelector;
    private readonly Func<TModel, GameObject> create;
    private readonly Action<GameObject, TModel> update;

    public AuraUiKeyedListReconciler(
        Transform parent,
        IEqualityComparer<TKey> comparer,
        Func<TModel, TKey> keySelector,
        Func<TModel, GameObject> create,
        Action<GameObject, TModel> update)
    {
        this.parent = parent ?? throw new ArgumentNullException(nameof(parent));
        rows = new Dictionary<TKey, GameObject>(
            comparer ?? EqualityComparer<TKey>.Default);
        this.keySelector = keySelector
                           ?? throw new ArgumentNullException(nameof(keySelector));
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        this.update = update ?? throw new ArgumentNullException(nameof(update));
    }

    public void Reconcile(IReadOnlyList<TModel> models, bool preserveView = true)
    {
        models ??= Array.Empty<TModel>();
        var snapshot = preserveView
            ? AuraUiViewState.CaptureForContent(parent)
            : null;
        var desired = new HashSet<TKey>();

        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            var key = keySelector(model);
            if (!desired.Add(key))
            {
                throw new InvalidOperationException(
                    "Aura UI keyed list contains a duplicate key: " + key);
            }

            if (!rows.TryGetValue(key, out var row) || row == null)
            {
                row = create(model);
                rows[key] = row;
            }

            row.transform.SetSiblingIndex(index);
            row.SetActive(true);
            update(row, model);
        }

        var removed = new List<TKey>();
        foreach (var pair in rows)
        {
            if (desired.Contains(pair.Key))
            {
                continue;
            }

            if (pair.Value != null)
            {
                pair.Value.SetActive(false);
                UnityEngine.Object.Destroy(pair.Value);
            }
            removed.Add(pair.Key);
        }

        foreach (var key in removed)
        {
            rows.Remove(key);
        }

        if (snapshot != null)
        {
            AuraUiViewState.RestoreAfterLayout(
                parent,
                snapshot,
                "AuraUi.KeyedList.Reconcile");
        }
    }
}
