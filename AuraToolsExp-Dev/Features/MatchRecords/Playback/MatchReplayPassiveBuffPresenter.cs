using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Witch.Core;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Draws the recorded Buff HUD without creating BuffItem components. BuffItem.Init executes
/// ApplyScript and registers listeners in the current game build, which is forbidden in a
/// projection-only replay view.
/// </summary>
internal static class MatchReplayPassiveBuffPresenter
{
    private static readonly Dictionary<string, Dictionary<string, BuffVisual>> Visuals =
        new(StringComparer.Ordinal);
    private static readonly HashSet<int> InitializedBars = new();

    internal static void Project(StatusManager status, IReadOnlyList<MatchReplayBuffState>? expected)
    {
        var bar = status?.statusBarUI?.buffBarObj?.GetComponent<BuffBarUI>();
        if (status == null || bar == null)
        {
            return;
        }

        if (InitializeBar(bar))
        {
            status.effectList?.Clear();
            status.UpdateEffectList();
        }
        var statusId = status.InstanceId ?? "";
        if (!Visuals.TryGetValue(statusId, out var visuals))
        {
            visuals = new Dictionary<string, BuffVisual>(StringComparer.Ordinal);
            Visuals[statusId] = visuals;
        }

        var states = (expected ?? Array.Empty<MatchReplayBuffState>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.BuffId) && item.Level > 0)
            .GroupBy(item => item.BuffId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(item => item.BuffId, StringComparer.Ordinal)
            .ToList();
        var expectedIds = new HashSet<string>(states.Select(item => item.BuffId), StringComparer.Ordinal);
        foreach (var obsolete in visuals.Keys.Where(id => !expectedIds.Contains(id)).ToList())
        {
            if (visuals[obsolete].Root != null)
            {
                UnityEngine.Object.Destroy(visuals[obsolete].Root);
            }

            visuals.Remove(obsolete);
        }

        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            if (!visuals.TryGetValue(state.BuffId, out var visual) || visual.Root == null)
            {
                visual = Create(bar, state.BuffId);
                visuals[state.BuffId] = visual;
            }

            if (visual.Level != null)
            {
                visual.Level.text = state.Level.ToString();
            }

            if (visual.Root != null)
            {
                visual.Root.transform.SetSiblingIndex(index);
                var sorting = visual.Root.GetComponent<SortingGroup>();
                if (sorting != null)
                {
                    sorting.sortingOrder = -index;
                }
            }
        }

        bar.BuffDic.Clear();
        bar.isDirty = false;
    }

    internal static void Reset()
    {
        foreach (var visual in Visuals.Values.SelectMany(items => items.Values))
        {
            if (visual.Root != null)
            {
                UnityEngine.Object.Destroy(visual.Root);
            }
        }

        Visuals.Clear();
        InitializedBars.Clear();
    }

    private static bool InitializeBar(BuffBarUI bar)
    {
        var key = bar.GetInstanceID();
        if (!InitializedBars.Add(key))
        {
            return false;
        }

        var content = bar.content ?? bar.transform.Find("Content");
        if (content != null)
        {
            for (var index = content.childCount - 1; index >= 0; index--)
            {
                var child = content.GetChild(index);
                if (child != null)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        // Do not call ClearBuff: its implementation executes ClearScript and queues commands.
        bar.BuffDic.Clear();
        return true;
    }

    private static BuffVisual Create(BuffBarUI bar, string buffId)
    {
        var content = bar.content ?? bar.transform.Find("Content")
                      ?? throw new InvalidOperationException("Buff HUD content is unavailable.");
        var prefab = ResourceLoader.Load<GameObject>("UI/BuffItem")
                     ?? throw new InvalidOperationException("Buff HUD prefab is unavailable.");
        var root = UnityEngine.Object.Instantiate(prefab, content);
        root.name = "ReplayBuff_" + buffId;
        var level = root.transform.Find("Content/Level")?.GetComponent<TMP_Text>();
        try
        {
            var config = new DataConfig(buffId, DataType.Buff);
            var iconPath = Value(config.data, "Icon");
            var icon = root.transform.Find("Content/Image")?.GetComponent<SpriteRenderer>();
            if (icon != null && !string.IsNullOrWhiteSpace(iconPath))
            {
                icon.sprite = ResourceLoader.Load<Sprite>(iconPath);
                icon.material = ResourceLoader.Load<Material>("Material/BuffIcon");
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay Buff icon unavailable: id=" + buffId + ", error=" + ex.Message);
        }

        var contentTransform = root.transform.Find("Content");
        if (contentTransform != null)
        {
            contentTransform.localScale = Vector3.one;
        }

        return new BuffVisual { Root = root, Level = level };
    }

    private static string Value(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private sealed class BuffVisual
    {
        internal GameObject? Root { get; set; }
        internal TMP_Text? Level { get; set; }
    }
}
