using System;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks.Ui;

/// <summary>
/// Owns persistent SunExp battle HUDs inside FightUI, below its transient overlays.
/// </summary>
public static class BattleHudHost
{
    public const string RootName = "SunExp_BattleHudHost";

    private static RectTransform? activeHost;

    public static bool TryGet(out Transform host)
    {
        host = null!;
        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null || fightUi.transform == null || !fightUi.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (activeHost == null || activeHost.parent != fightUi.transform)
        {
            activeHost = FindExisting(fightUi.transform) ?? Create(fightUi.transform);
        }

        PlaceBelowTransientChildren(activeHost);
        host = activeHost;
        return true;
    }

    private static RectTransform? FindExisting(Transform fightUi)
    {
        var existing = fightUi.Find(RootName);
        return existing == null ? null : existing as RectTransform;
    }

    private static RectTransform Create(Transform fightUi)
    {
        var go = new GameObject(RootName, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(fightUi, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void PlaceBelowTransientChildren(RectTransform host)
    {
        var parent = host.parent;
        if (parent == null)
        {
            return;
        }

        // FightUI creates selection/discard overlays dynamically as "SelectCard".
        // If creation happens while one is open, keep the persistent host below it.
        var hostIndex = host.GetSiblingIndex();
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child != host && string.Equals(child.name, "SelectCard", StringComparison.OrdinalIgnoreCase))
            {
                if (hostIndex > i)
                {
                    host.SetSiblingIndex(i);
                }

                break;
            }
        }
    }
}
