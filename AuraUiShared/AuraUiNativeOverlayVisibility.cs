using System;
using UnityEngine;

namespace AuraUi.Shared;

/// <summary>
/// Verifies that a game-native global overlay is active on the same root
/// Canvas as its anchor and renders above the anchor's top-level UI branch.
/// This avoids treating pointer callbacks or Show() returns as proof that an
/// overlay is actually visible.
/// </summary>
public static class AuraUiNativeOverlayVisibility
{
    public static bool SharesRootCanvas(
        Transform? anchor,
        Transform? overlay,
        out string diagnostic)
    {
        if (anchor == null || overlay == null)
        {
            diagnostic = "anchorOrOverlay=missing";
            return false;
        }

        var anchorRoot = anchor.GetComponentInParent<Canvas>()?.rootCanvas;
        var overlayRoot = overlay.GetComponentInParent<Canvas>()?.rootCanvas;
        var sameRootCanvas = anchorRoot != null
                             && overlayRoot != null
                             && anchorRoot == overlayRoot;
        diagnostic = "sameRootCanvas=" + sameRootCanvas
                     + ", anchorCanvas=" + NameOf(anchorRoot)
                     + ", overlayCanvas=" + NameOf(overlayRoot);
        return sameRootCanvas;
    }

    public static bool IsVisibleAbove(
        Transform? anchor,
        GameObject? overlay,
        out string diagnostic)
    {
        if (anchor == null || overlay == null)
        {
            diagnostic = "anchorOrOverlay=missing";
            return false;
        }

        var anchorCanvas = anchor.GetComponentInParent<Canvas>();
        var overlayCanvas = overlay.GetComponentInParent<Canvas>();
        var anchorRoot = anchorCanvas == null ? null : anchorCanvas.rootCanvas;
        var overlayRoot = overlayCanvas == null ? null : overlayCanvas.rootCanvas;
        var sameRootCanvas = anchorRoot != null
                             && overlayRoot != null
                             && anchorRoot == overlayRoot;

        var anchorBranch = sameRootCanvas
            ? DirectChildOf(anchorRoot!.transform, anchor)
            : null;
        var overlayBranch = sameRootCanvas
            ? DirectChildOf(anchorRoot!.transform, overlay.transform)
            : null;
        var aboveAnchor = anchorBranch != null
                          && overlayBranch != null
                          && anchorBranch != overlayBranch
                          && overlayBranch.GetSiblingIndex() > anchorBranch.GetSiblingIndex();
        var effectiveAlpha = EffectiveAlpha(overlay.transform, overlayRoot?.transform);
        var scale = overlay.transform.lossyScale;
        var hasVisibleScale = Mathf.Abs(scale.x) > 0.001f
                              && Mathf.Abs(scale.y) > 0.001f;
        var active = anchor.gameObject.activeInHierarchy && overlay.activeInHierarchy;
        var visible = active
                      && sameRootCanvas
                      && aboveAnchor
                      && effectiveAlpha > 0.001f
                      && hasVisibleScale;

        diagnostic = "active=" + active
                     + ", sameRootCanvas=" + sameRootCanvas
                     + ", aboveAnchor=" + aboveAnchor
                     + ", effectiveAlpha=" + effectiveAlpha.ToString("0.###")
                     + ", visibleScale=" + hasVisibleScale
                     + ", anchorCanvas=" + NameOf(anchorRoot)
                     + ", overlayCanvas=" + NameOf(overlayRoot)
                     + ", anchorSibling=" + SiblingOf(anchorBranch)
                     + ", overlaySibling=" + SiblingOf(overlayBranch);
        return visible;
    }

    private static Transform? DirectChildOf(Transform root, Transform node)
    {
        var current = node;
        while (current.parent != null && current.parent != root)
        {
            current = current.parent;
        }

        return current.parent == root ? current : null;
    }

    private static float EffectiveAlpha(Transform node, Transform? stop)
    {
        var alpha = 1f;
        var current = node;
        while (current != null)
        {
            foreach (var group in current.GetComponents<CanvasGroup>())
            {
                alpha *= group.alpha;
            }

            if (current == stop)
            {
                break;
            }

            current = current.parent;
        }

        return alpha;
    }

    private static string NameOf(Canvas? canvas)
    {
        return canvas == null ? "<none>" : canvas.name;
    }

    private static int SiblingOf(Transform? transform)
    {
        return transform == null ? -1 : transform.GetSiblingIndex();
    }
}
