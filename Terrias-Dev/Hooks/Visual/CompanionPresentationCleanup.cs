using System;
using System.Collections.Generic;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

internal static class CompanionPresentationCleanup
{
    private const string ProjectionProxyPrefix = "Terrias_ProjectionVisualProxy:";
    private const string SpiritProxyPrefix = "Terrias_SpiritVisualProxy:";

    public static CompanionPresentationSuppression SuppressAll(string source)
    {
        var suppressedRoots = new HashSet<int>();
        var suppressedUi = new HashSet<int>();
        var result = new MutableSuppression();

        SuppressActors<ProjectionOtherObj>(suppressedRoots, suppressedUi, result, CompanionArtifactKind.Projection);
        SuppressActors<SpiritOtherObj>(suppressedRoots, suppressedUi, result, CompanionArtifactKind.Spirit);
        SuppressVisualProxies(suppressedRoots, result);

        return new CompanionPresentationSuppression(
            result.ActorRoots,
            result.ProxyRoots,
            result.Renderers,
            result.UiObjects,
            result.ProjectionRoots,
            result.SpiritRoots,
            result.TurnAnchors,
            result.ProjectionProxies,
            result.SpiritProxies);
    }

    private static void SuppressActors<T>(
        HashSet<int> suppressedRoots,
        HashSet<int> suppressedUi,
        MutableSuppression result,
        CompanionArtifactKind kind)
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
            if (SuppressRoot(root, suppressedRoots, result, isProxy: false))
            {
                CountActor(kind, result);
            }
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

            if (SuppressRoot(root, suppressedRoots, result, isProxy: true))
            {
                CountProxy(root.name, result);
            }
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

            if (SuppressRoot(root, suppressedRoots, result, isProxy: true))
            {
                CountProxy(root.name, result);
            }
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

    private static bool SuppressRoot(
        GameObject root,
        HashSet<int> suppressedRoots,
        MutableSuppression result,
        bool isProxy)
    {
        if (!suppressedRoots.Add(root.GetInstanceID()))
        {
            return false;
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

        return true;
    }

    private static void CountActor(CompanionArtifactKind kind, MutableSuppression result)
    {
        switch (kind)
        {
            case CompanionArtifactKind.Projection:
                result.ProjectionRoots++;
                break;
            case CompanionArtifactKind.Spirit:
                result.SpiritRoots++;
                break;
            case CompanionArtifactKind.TurnAnchor:
                result.TurnAnchors++;
                break;
        }
    }

    private static void CountProxy(string name, MutableSuppression result)
    {
        if (name.StartsWith(ProjectionProxyPrefix, StringComparison.Ordinal))
        {
            result.ProjectionProxies++;
        }
        else if (name.StartsWith(SpiritProxyPrefix, StringComparison.Ordinal))
        {
            result.SpiritProxies++;
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
        public int ProjectionRoots;
        public int SpiritRoots;
        public int TurnAnchors;
        public int ProjectionProxies;
        public int SpiritProxies;
    }

    private enum CompanionArtifactKind
    {
        Projection,
        Spirit,
        TurnAnchor
    }
}

internal readonly struct CompanionPresentationSuppression
{
    public CompanionPresentationSuppression(
        int actorRoots,
        int proxyRoots,
        int renderers,
        int uiObjects,
        int projectionRoots,
        int spiritRoots,
        int turnAnchors,
        int projectionProxies,
        int spiritProxies)
    {
        Available = true;
        ActorRoots = actorRoots;
        ProxyRoots = proxyRoots;
        Renderers = renderers;
        UiObjects = uiObjects;
        ProjectionRoots = projectionRoots;
        SpiritRoots = spiritRoots;
        TurnAnchors = turnAnchors;
        ProjectionProxies = projectionProxies;
        SpiritProxies = spiritProxies;
    }

    public bool Available { get; }

    public int ActorRoots { get; }

    public int ProxyRoots { get; }

    public int Renderers { get; }

    public int UiObjects { get; }

    public int ProjectionRoots { get; }

    public int SpiritRoots { get; }

    public int TurnAnchors { get; }

    public int ProjectionProxies { get; }

    public int SpiritProxies { get; }

    public int Total => ProjectionRoots + SpiritRoots + TurnAnchors + ProjectionProxies + SpiritProxies;
}
