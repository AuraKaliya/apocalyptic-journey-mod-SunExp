using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UiRaycastSafetyShared;

public static class UiRaycastSafeDestroyRuntime
{
    private const string RunnerName = "UiRaycastSafety.GlobalRunner";
    private const int MaxDetailsPerScrub = 12;
    private static ScrubRunner? runner;

    public static int DisableRaycasts(GameObject? root, string source, Action<string>? log = null)
    {
        if (root == null)
        {
            return 0;
        }

        var disabled = 0;
        foreach (var group in root.GetComponentsInChildren<CanvasGroup>(true))
        {
            if (group == null)
            {
                continue;
            }

            group.blocksRaycasts = false;
            group.interactable = false;
        }

        foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
        {
            if (selectable == null)
            {
                continue;
            }

            selectable.interactable = false;
        }

        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == null)
            {
                continue;
            }

            TryUnregisterGraphic(graphic);
            try
            {
                graphic.raycastTarget = false;
                graphic.enabled = false;
                disabled++;
            }
            catch
            {
                // A partially destroyed Graphic can throw here; the important part is that
                // callers continue clearing the rest of the UI tree.
            }
        }

        if (disabled > 0)
        {
            log?.Invoke("UI raycasts disabled. root=" + root.name + ", source=" + source + ", graphics=" + disabled);
        }

        return disabled;
    }

    public static void DisableAndHide(GameObject? root, string source, Action<string>? log = null)
    {
        if (root == null)
        {
            return;
        }

        DisableRaycasts(root, source, log);
        try
        {
            root.SetActive(false);
        }
        catch
        {
        }
    }

    public static void DisableAndDestroyAfterFrame(
        MonoBehaviour owner,
        GameObject? root,
        string source,
        Action<string>? log = null)
    {
        if (root == null)
        {
            return;
        }

        DisableAndHide(root, source, log);
        if (owner != null && owner.isActiveAndEnabled)
        {
            owner.StartCoroutine(DestroyAfterFrame(root, source, log));
            return;
        }

        UnityEngine.Object.Destroy(root);
    }

    public static int ScrubGraphicRegistry(string source, Action<string>? log = null)
    {
        var scanned = 0;
        var removed = 0;
        var details = new List<string>();
        Canvas[] canvases;
        try
        {
            canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        }
        catch
        {
            return 0;
        }

        foreach (var canvas in canvases)
        {
            if (canvas == null)
            {
                continue;
            }

            removed += ScrubCanvas(canvas, source, ref scanned, details);
        }

        if (removed > 0)
        {
            log?.Invoke(
                "UI raycast registry scrubbed. source=" + source
                + ", removed=" + removed
                + ", scanned=" + scanned);
            LogDetails(details, log);
        }

        return removed;
    }

    public static int ScrubGraphicRegistryForRaycaster(GraphicRaycaster? raycaster, string source, Action<string>? log = null)
    {
        if (raycaster == null)
        {
            return 0;
        }

        Canvas? canvas = null;
        try
        {
            canvas = raycaster.GetComponent<Canvas>();
        }
        catch
        {
        }

        if (canvas == null)
        {
            try
            {
                canvas = raycaster.GetComponentInParent<Canvas>();
            }
            catch
            {
            }
        }

        if (canvas == null)
        {
            return 0;
        }

        var removed = ScrubGraphicRegistryForCanvas(canvas, source, log);
        return removed;
    }

    public static int ScrubGraphicRegistryForCanvas(Canvas? canvas, string source, Action<string>? log = null)
    {
        if (canvas == null)
        {
            return 0;
        }

        var scanned = 0;
        var details = new List<string>();
        var removed = ScrubCanvas(canvas, source, ref scanned, details);
        if (removed > 0)
        {
            log?.Invoke(
                "UI raycast registry scrubbed for canvas. source=" + source
                + ", canvas=" + DescribeCanvas(canvas)
                + ", removed=" + removed
                + ", scanned=" + scanned);
            LogDetails(details, log);
        }

        return removed;
    }

    public static void ScrubGraphicRegistryForFrames(int frameCount, string source, Action<string>? log = null)
    {
        var owner = EnsureRunner();
        if (owner == null)
        {
            ScrubGraphicRegistry(source + ":fallback", log);
            return;
        }

        owner.StartCoroutine(ScrubForFrames(Math.Max(1, frameCount), source, log));
    }

    private static IEnumerator DestroyAfterFrame(GameObject root, string source, Action<string>? log)
    {
        yield return null;
        if (root == null)
        {
            yield break;
        }

        DisableAndHide(root, source + ":destroy", log);
        UnityEngine.Object.Destroy(root);
    }

    private static void TryUnregisterGraphic(Graphic graphic)
    {
        try
        {
            var canvas = graphic.canvas;
            if (canvas == null)
            {
                return;
            }

            TryUnregisterGraphic(canvas, graphic);
        }
        catch
        {
        }
    }

    private static void TryUnregisterGraphic(Canvas canvas, Graphic graphic)
    {
        try
        {
            GraphicRegistry.UnregisterRaycastGraphicForCanvas(canvas, graphic);
        }
        catch
        {
        }

        try
        {
            GraphicRegistry.UnregisterGraphicForCanvas(canvas, graphic);
        }
        catch
        {
        }

        try
        {
            GraphicRegistry.DisableRaycastGraphicForCanvas(canvas, graphic);
        }
        catch
        {
        }

        try
        {
            GraphicRegistry.DisableGraphicForCanvas(canvas, graphic);
        }
        catch
        {
        }
    }

    private static int ScrubCanvas(Canvas canvas, string source, ref int scanned, List<string> details)
    {
        var visited = new HashSet<int>();
        var removed = ScrubCanvasList(
            canvas,
            source,
            "raycastable",
            allowInactiveCleanup: true,
            ref scanned,
            details,
            visited);
        removed += ScrubCanvasList(
            canvas,
            source,
            "graphics",
            allowInactiveCleanup: false,
            ref scanned,
            details,
            visited);
        return removed;
    }

    private static int ScrubCanvasList(
        Canvas canvas,
        string source,
        string registry,
        bool allowInactiveCleanup,
        ref int scanned,
        List<string> details,
        HashSet<int> visited)
    {
        IList<Graphic>? graphics;
        try
        {
            graphics = string.Equals(registry, "graphics", StringComparison.Ordinal)
                ? GraphicRegistry.GetGraphicsForCanvas(canvas)
                : GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
        }
        catch
        {
            return 0;
        }

        if (graphics == null)
        {
            return 0;
        }

        var removed = 0;
        for (var i = graphics.Count - 1; i >= 0; i--)
        {
            Graphic? graphic;
            try
            {
                graphic = graphics[i];
            }
            catch
            {
                continue;
            }

            if (ReferenceEquals(graphic, null))
            {
                continue;
            }

            var key = StableObjectKey(graphic);
            if (key != 0 && !visited.Add(key))
            {
                continue;
            }

            scanned++;
            if (!TryGetRaycastRegistryIssue(graphic, allowInactiveCleanup, out var reason))
            {
                continue;
            }

            AddDetail(details, canvas, graphic, registry + ":" + reason, source);
            TryUnregisterGraphic(canvas, graphic);
            removed++;
        }

        return removed;
    }

    private static bool TryGetRaycastRegistryIssue(Graphic graphic, bool allowInactiveCleanup, out string reason)
    {
        reason = "";
        try
        {
            if (graphic == null)
            {
                reason = "graphic-null";
                return true;
            }

            if (!graphic.raycastTarget)
            {
                if (!allowInactiveCleanup)
                {
                    return false;
                }

                reason = "raycast-target-false";
                return true;
            }

            if (!graphic.isActiveAndEnabled)
            {
                if (!allowInactiveCleanup)
                {
                    return false;
                }

                reason = "inactive-or-disabled";
                return true;
            }

            var renderer = graphic.canvasRenderer;
            if (renderer == null)
            {
                reason = "canvas-renderer-null";
                return true;
            }

            _ = renderer.cull;
            return false;
        }
        catch (Exception ex)
        {
            reason = "canvas-renderer-cull-failed:" + ex.GetType().Name;
            return true;
        }
    }

    private static int StableObjectKey(UnityEngine.Object target)
    {
        try
        {
            return target == null ? 0 : target.GetInstanceID();
        }
        catch
        {
            return 0;
        }
    }

    private static void AddDetail(List<string> details, Canvas canvas, Graphic graphic, string reason, string source)
    {
        if (details.Count >= MaxDetailsPerScrub)
        {
            return;
        }

        details.Add(
            "UI raycast stale graphic removed. source=" + source
            + ", reason=" + reason
            + ", canvas=" + DescribeCanvas(canvas)
            + ", graphic=" + DescribeGraphic(graphic)
            + ", path=" + TransformPath(graphic == null ? null : graphic.transform));
    }

    private static void LogDetails(List<string> details, Action<string>? log)
    {
        if (log == null)
        {
            return;
        }

        foreach (var detail in details)
        {
            log(detail);
        }
    }

    private static string DescribeCanvas(Canvas canvas)
    {
        try
        {
            return SafeName(canvas)
                   + ", active=" + canvas.isActiveAndEnabled
                   + ", renderMode=" + canvas.renderMode
                   + ", sortingLayer=" + canvas.sortingLayerName
                   + ", sortingOrder=" + canvas.sortingOrder;
        }
        catch
        {
            return SafeName(canvas);
        }
    }

    private static string DescribeGraphic(Graphic? graphic)
    {
        if (graphic == null)
        {
            return "<null>";
        }

        try
        {
            return graphic.GetType().Name
                   + "(" + SafeName(graphic) + ")"
                   + ", active=" + graphic.isActiveAndEnabled
                   + ", raycastTarget=" + graphic.raycastTarget
                   + ", depth=" + graphic.depth;
        }
        catch
        {
            return graphic.GetType().Name + "(" + SafeName(graphic) + ")";
        }
    }

    private static string SafeName(UnityEngine.Object? target)
    {
        try
        {
            return target == null ? "<null>" : target.name;
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static string TransformPath(Transform? transform)
    {
        if (transform == null)
        {
            return "<null>";
        }

        try
        {
            var parts = new List<string>();
            var current = transform;
            while (current != null && parts.Count < 32)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static IEnumerator ScrubForFrames(int frameCount, string source, Action<string>? log)
    {
        for (var i = 0; i < frameCount; i++)
        {
            ScrubGraphicRegistry(source + ":frame" + i, log);
            yield return null;
        }
    }

    private static ScrubRunner? EnsureRunner()
    {
        if (runner != null)
        {
            return runner;
        }

        try
        {
            var existing = GameObject.Find(RunnerName);
            var gameObject = existing != null ? existing : new GameObject(RunnerName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.GetComponent<ScrubRunner>() ?? gameObject.AddComponent<ScrubRunner>();
            return runner;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ScrubRunner : MonoBehaviour
    {
    }
}
