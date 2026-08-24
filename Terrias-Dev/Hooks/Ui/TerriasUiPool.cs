using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Terrias.Dll.Hooks.Ui;

public abstract class TerriasPooledUiBehaviour : MonoBehaviour
{
    public virtual void ResetForPool()
    {
    }
}

public sealed class TerriasPooledUiItem : MonoBehaviour
{
    public string PoolKey = "";
}

public static class TerriasUiPool
{
    private const string PoolRootName = "Terrias_UiPool";
    private static readonly Dictionary<string, Stack<GameObject>> Pools = new(StringComparer.Ordinal);
    private static GameObject? poolRoot;
    private static bool rootFailureLogged;

    public static T AcquireComponent<T>(
        string key,
        Transform parent,
        string instanceName,
        Func<Transform, string, T> create)
        where T : UnityEngine.Component
    {
        if (parent == null)
        {
            throw new ArgumentNullException(nameof(parent));
        }

        var normalizedKey = NormalizeKey(key);
        GameObject? go = null;
        if (TerriasPerformanceSettings.UiPoolEnabled && Pools.TryGetValue(normalizedKey, out var stack))
        {
            while (stack.Count > 0 && go == null)
            {
                go = stack.Pop();
            }
        }

        T component;
        if (go != null)
        {
            TerriasPerformanceCounters.Record("UiPool.Acquire.Hit");
            go.name = instanceName;
            go.transform.SetParent(parent, false);
            RestoreReusableTree(go);
            go.SetActive(true);
            component = go.GetComponent<T>() ?? go.AddComponent<T>();
        }
        else
        {
            TerriasPerformanceCounters.Record("UiPool.Acquire.Miss");
            component = create(parent, instanceName);
            go = component.gameObject;
        }

        var item = go.GetComponent<TerriasPooledUiItem>() ?? go.AddComponent<TerriasPooledUiItem>();
        item.PoolKey = normalizedKey;
        return component;
    }

    public static T AcquireConfiguredComponent<T>(
        string key,
        Transform parent,
        string instanceName,
        Func<Transform, string, T> create,
        Action<T> configureBeforeActivation)
        where T : UnityEngine.Component
    {
        if (parent == null)
        {
            throw new ArgumentNullException(nameof(parent));
        }

        if (configureBeforeActivation == null)
        {
            throw new ArgumentNullException(nameof(configureBeforeActivation));
        }

        var normalizedKey = NormalizeKey(key);
        GameObject? go = null;
        if (TerriasPerformanceSettings.UiPoolEnabled && Pools.TryGetValue(normalizedKey, out var stack))
        {
            while (stack.Count > 0 && go == null)
            {
                go = stack.Pop();
            }
        }

        T component;
        if (go != null)
        {
            TerriasPerformanceCounters.Record("UiPool.Acquire.Hit");
            go.name = instanceName;
            go.transform.SetParent(parent, false);
            RestoreReusableTree(go);
            component = go.GetComponent<T>() ?? go.AddComponent<T>();
        }
        else
        {
            TerriasPerformanceCounters.Record("UiPool.Acquire.Miss");
            component = create(parent, instanceName);
            go = component.gameObject;
            go.SetActive(false);
        }

        var item = go.GetComponent<TerriasPooledUiItem>() ?? go.AddComponent<TerriasPooledUiItem>();
        item.PoolKey = normalizedKey;
        try
        {
            configureBeforeActivation(component);
            go.SetActive(true);
            return component;
        }
        catch
        {
            go.SetActive(false);
            throw;
        }
    }

    public static void ReleaseOrDestroyChildren(Transform? parent, string source, string logPrefix)
    {
        if (parent == null)
        {
            return;
        }

        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child == null)
            {
                continue;
            }

            var go = child.gameObject;
            if (go.TryGetComponent<TerriasPooledUiItem>(out var item) && !string.IsNullOrWhiteSpace(item.PoolKey))
            {
                Release(go, source, logPrefix);
            }
            else
            {
                UiRaycastSafeDestroyRuntime.DisableAndHide(go, source, TerriasLog.Debug);
                Object.Destroy(go);
            }
        }

        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(2, source + ":children", TerriasLog.Debug);
        TerriasLog.Debug(logPrefix + " released transient UI children from " + source + ".");
    }

    public static bool Release(GameObject? go, string source, string logPrefix)
    {
        if (go == null)
        {
            return false;
        }

        var item = go.GetComponent<TerriasPooledUiItem>();
        var key = NormalizeKey(item?.PoolKey ?? "");
        if (!TerriasPerformanceSettings.UiPoolEnabled || key.Length == 0)
        {
            UiRaycastSafeDestroyRuntime.DisableAndHide(go, source, TerriasLog.Debug);
            Object.Destroy(go);
            TerriasPerformanceCounters.Record("UiPool.Destroyed");
            return true;
        }

        foreach (var pooled in go.GetComponentsInChildren<TerriasPooledUiBehaviour>(true))
        {
            try
            {
                pooled.ResetForPool();
            }
            catch (Exception ex)
            {
                TerriasLog.Warn(logPrefix + " pooled UI reset failed from " + source + ": " + ex.Message);
            }
        }

        foreach (var button in go.GetComponentsInChildren<Button>(true))
        {
            button.onClick.RemoveAllListeners();
            button.interactable = false;
        }

        var root = EnsurePoolRoot();
        if (root == null || CountFor(key) >= TerriasPerformanceSettings.UiPoolCapacityPerKey)
        {
            UiRaycastSafeDestroyRuntime.DisableAndHide(go, source, TerriasLog.Debug);
            Object.Destroy(go);
            TerriasPerformanceCounters.Record("UiPool.Discarded");
            return true;
        }

        go.transform.SetParent(root.transform, false);
        go.SetActive(false);
        if (!Pools.TryGetValue(key, out var stack))
        {
            stack = new Stack<GameObject>();
            Pools[key] = stack;
        }

        stack.Push(go);
        TerriasPerformanceCounters.Record("UiPool.Released");
        return true;
    }

    public static void ClearAll(string source, string logPrefix)
    {
        var destroyed = 0;
        foreach (var stack in Pools.Values)
        {
            while (stack.Count > 0)
            {
                var go = stack.Pop();
                if (go == null)
                {
                    continue;
                }

                UiRaycastSafeDestroyRuntime.DisableAndHide(go, source, TerriasLog.Debug);
                Object.Destroy(go);
                destroyed++;
            }
        }

        Pools.Clear();
        if (poolRoot != null)
        {
            Object.Destroy(poolRoot);
            poolRoot = null;
        }

        TerriasPerformanceCounters.Record("UiPool.Cleared");
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(2, source + ":pool-clear", TerriasLog.Debug);
        TerriasLog.Debug(logPrefix + " cleared " + destroyed + " pooled UI object(s) from " + source + ".");
    }

    private static void RestoreReusableTree(GameObject go)
    {
        foreach (var graphic in go.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic != null)
            {
                graphic.enabled = true;
            }
        }
    }

    private static int CountFor(string key)
    {
        return Pools.TryGetValue(key, out var stack) ? stack.Count : 0;
    }

    private static GameObject? EnsurePoolRoot()
    {
        if (poolRoot != null)
        {
            return poolRoot;
        }

        try
        {
            poolRoot = new GameObject(PoolRootName);
            Object.DontDestroyOnLoad(poolRoot);
            return poolRoot;
        }
        catch (Exception ex)
        {
            if (!rootFailureLogged)
            {
                TerriasLog.Warn("[TerriasUiPool] unavailable; pooled UI will be destroyed instead: " + ex.Message);
                rootFailureLogged = true;
            }

            return null;
        }
    }

    private static string NormalizeKey(string? key)
    {
        return (key ?? "").Trim();
    }
}
