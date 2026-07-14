using System;
using System.Collections.Generic;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

internal static class CompanionPresentationCleanup
{
    private const string ProjectionProxyPrefix = "SunExp_ProjectionVisualProxy:";
    private const string SpiritProxyPrefix = "SunExp_SpiritVisualProxy:";

    public static CompanionPresentationSuppression SuppressAll(string source)
    {
        var suppressedRoots = new HashSet<int>();
        var suppressedUi = new HashSet<int>();
        var result = new MutableSuppression();

        SuppressActors<ProjectionOtherObj>(suppressedRoots, suppressedUi, result);
        SuppressActors<SpiritOtherObj>(suppressedRoots, suppressedUi, result);
        SuppressActors<ProjectionTurnAnchorObj>(suppressedRoots, suppressedUi, result);
        SuppressVisualProxies(suppressedRoots, result);

        return new CompanionPresentationSuppression(
            result.ActorRoots,
            result.ProxyRoots,
            result.Renderers,
            result.UiObjects);
    }

    private static void SuppressActors<T>(
        HashSet<int> suppressedRoots,
        HashSet<int> suppressedUi,
        MutableSuppression result)
        where T : UnityEngine.Component
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<T>())
        {
            var root = component?.gameObject;
            if (root == null || !root.scene.IsValid())
            {
                continue;
            }

            SuppressStatusUi(root.GetComponent<StatusManager>(), suppressedUi, result);
            SuppressRoot(root, suppressedRoots, result, isProxy: false);
        }
    }

    private static void SuppressVisualProxies(HashSet<int> suppressedRoots, MutableSuppression result)
    {
        foreach (var visual in Resources.FindObjectsOfTypeAll<ProjectionVisualProxy>())
        {
            var root = visual?.gameObject;
            if (root == null || !root.scene.IsValid() || !IsCompanionProxy(root.name))
            {
                continue;
            }

            SuppressRoot(root, suppressedRoots, result, isProxy: true);
        }

        // Retain a name-based fallback for a proxy whose marker component was
        // already destroyed while its renderer survived until the frame end.
        foreach (var renderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            var root = renderer?.gameObject;
            if (root == null || !root.scene.IsValid() || !IsCompanionProxy(root.name))
            {
                continue;
            }

            SuppressRoot(root, suppressedRoots, result, isProxy: true);
        }
    }

    private static void SuppressStatusUi(
        StatusManager? status,
        HashSet<int> suppressedUi,
        MutableSuppression result)
    {
        if (status == null)
        {
            return;
        }

        SuppressUiObject(status.statusBarObj, suppressedUi, result);
        SuppressUiObject(status.actionContent, suppressedUi, result);
        SuppressUiObject(status.effectListObj, suppressedUi, result);
        SuppressUiObject(status.selfUI, suppressedUi, result);
    }

    private static void SuppressUiObject(
        GameObject? instance,
        HashSet<int> suppressedUi,
        MutableSuppression result)
    {
        if (instance == null || !suppressedUi.Add(instance.GetInstanceID()))
        {
            return;
        }

        if (instance.activeSelf)
        {
            instance.SetActive(false);
            result.UiObjects++;
        }
    }

    private static void SuppressRoot(
        GameObject root,
        HashSet<int> suppressedRoots,
        MutableSuppression result,
        bool isProxy)
    {
        if (!suppressedRoots.Add(root.GetInstanceID()))
        {
            return;
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null && renderer.enabled)
            {
                renderer.enabled = false;
                result.Renderers++;
            }
        }

        root.SetActive(false);
        if (isProxy)
        {
            result.ProxyRoots++;
        }
        else
        {
            result.ActorRoots++;
        }
    }

    private static bool IsCompanionProxy(string name)
    {
        return !string.IsNullOrEmpty(name)
               && (name.StartsWith(ProjectionProxyPrefix, StringComparison.Ordinal)
                   || name.StartsWith(SpiritProxyPrefix, StringComparison.Ordinal));
    }

    private sealed class MutableSuppression
    {
        public int ActorRoots;
        public int ProxyRoots;
        public int Renderers;
        public int UiObjects;
    }
}

internal readonly struct CompanionPresentationSuppression
{
    public CompanionPresentationSuppression(int actorRoots, int proxyRoots, int renderers, int uiObjects)
    {
        ActorRoots = actorRoots;
        ProxyRoots = proxyRoots;
        Renderers = renderers;
        UiObjects = uiObjects;
    }

    public int ActorRoots { get; }

    public int ProxyRoots { get; }

    public int Renderers { get; }

    public int UiObjects { get; }
}
