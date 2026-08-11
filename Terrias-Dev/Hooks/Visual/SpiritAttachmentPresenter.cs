using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks.Visual;

public static class SpiritAttachmentPresenter
{
    private static readonly Dictionary<string, GameObject> Proxies = new(StringComparer.Ordinal);
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        SpiritStateStore.Registered += Attach;
        SpiritStateStore.Retired += Detach;
        SpiritStateStore.ActionPresented += PlayActionFocus;
    }

    public static void RefreshByOwner(IStatusManager? owner, string source)
    {
        if (owner == null)
        {
            return;
        }

        foreach (var state in SpiritStateStore.Active())
        {
            if (!string.Equals(state.OwnerStatusId, owner.InstanceId, StringComparison.Ordinal))
            {
                continue;
            }

            var spirit = state.Spirit;
            var spiritStatus = spirit?.Status;
            var active = spiritStatus != null
                         && spiritStatus.CurHp > 0
                         && spiritStatus.state != IStatusManager.State.Dead;
            if (spirit != null)
            {
                spirit.gameObject.SetActive(active);
            }
            if (Proxies.TryGetValue(state.StatusId, out var proxy) && proxy != null)
            {
                proxy.SetActive(active);
            }

            TerriasLog.Debug("[SpiritAttachment] spirit visibility=" + active + ", owner=" + owner.InstanceId + ", source=" + source);
        }
    }

    private static void Attach(SpiritState state)
    {
        GameObject? proxy = null;
        try
        {
            var owner = StatusById(state.OwnerStatusId);
            var spirit = state.Spirit;
            var ownerRenderer = owner?.transform?.Find("body")?.GetComponent<SpriteRenderer>();
            var sourceRenderer = spirit?.transform?.Find("body")?.GetComponent<SpriteRenderer>();
            var sourceCollider = spirit?.transform?.GetComponent<BoxCollider>();
            if (owner?.transform == null || spirit?.transform == null || ownerRenderer == null || sourceRenderer == null || sourceCollider == null)
            {
                TerriasLog.Warn("[SpiritAttachment] visual proxy prerequisites unavailable: " + state.StatusId);
                return;
            }

            RemoveProxy(state.StatusId, true);
            var status = spirit.Status as StatusManager;
            proxy = new GameObject("Terrias_SpiritVisualProxy:" + state.StatusId);
            CompanionSceneApi.MoveToOwnerScene(proxy, owner.transform.gameObject, "SpiritAttachment.Attach");
            proxy.layer = ownerRenderer.gameObject.layer;
            var output = proxy.AddComponent<SpriteRenderer>();
            var visual = proxy.AddComponent<ProjectionVisualProxy>();
            if (!visual.Configure(
                    owner.transform,
                    owner.transform.GetComponent<BoxCollider>(),
                    ownerRenderer,
                    spirit.transform,
                    sourceCollider,
                    sourceRenderer,
                    spirit.transform.Find("Reflection")?.gameObject,
                    spirit.transform.Find("bottom")?.gameObject,
                    status,
                    output,
                    allowInactiveOwner: true))
            {
                visual.RestoreSourcePresentation();
                UnityEngine.Object.Destroy(proxy);
                return;
            }

            proxy.AddComponent<SpiritDetachedStatusBarPresenter>().Configure(status, output);
            proxy.AddComponent<SpiritStatusHoverRelay>().Configure(status, output);

            Proxies[state.StatusId] = proxy;
            RefreshByOwner(owner, "Attach");
            TerriasPerformanceCounters.Record("SpiritAttachment.ProxyAttached");
        }
        catch (Exception ex)
        {
            if (proxy != null)
            {
                proxy.GetComponent<ProjectionVisualProxy>()?.RestoreSourcePresentation();
                UnityEngine.Object.Destroy(proxy);
            }

            TerriasLog.Warn("[SpiritAttachment] proxy attach failed: " + ex.Message);
        }
    }

    private static void Detach(SpiritState state) => RemoveProxy(state?.StatusId ?? "", false);

    private static void PlayActionFocus(SpiritState state)
    {
        if (state != null && Proxies.TryGetValue(state.StatusId, out var proxy) && proxy != null)
        {
            proxy.GetComponent<ProjectionVisualProxy>()?.PlayActionFocus(state.StatusId);
        }
    }

    private static void RemoveProxy(string statusId, bool restore)
    {
        if (!Proxies.TryGetValue(statusId, out var proxy))
        {
            return;
        }

        Proxies.Remove(statusId);
        if (proxy == null)
        {
            return;
        }

        if (restore)
        {
            proxy.GetComponent<SpiritDetachedStatusBarPresenter>()?.RestorePresentation();
            proxy.GetComponent<ProjectionVisualProxy>()?.RestoreSourcePresentation();
        }

        proxy.SetActive(false);
        UnityEngine.Object.Destroy(proxy);
    }

    public static void ClearAll(string source, bool sweepOrphans = true)
    {
        var proxies = new List<GameObject>(Proxies.Values);
        Proxies.Clear();
        foreach (var proxy in proxies)
        {
            if (proxy == null)
            {
                continue;
            }

            proxy.SetActive(false);
            UnityEngine.Object.Destroy(proxy);
        }

        if (sweepOrphans)
        {
            foreach (var visual in Resources.FindObjectsOfTypeAll<ProjectionVisualProxy>())
            {
                var proxy = visual?.gameObject;
                if (proxy == null
                    || !proxy.scene.IsValid()
                    || !proxy.name.StartsWith("Terrias_SpiritVisualProxy:", StringComparison.Ordinal))
                {
                    continue;
                }

                proxy.SetActive(false);
                UnityEngine.Object.Destroy(proxy);
            }
        }

        TerriasLog.Debug("[SpiritAttachment] cleared from " + source + ": count=" + proxies.Count);
    }

    private static IStatusManager? StatusById(string statusId)
    {
        return FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true ? status : null;
    }
}

internal sealed class SpiritStatusHoverRelay : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private StatusManager? status;
    private SpriteRenderer? renderer;
    private BoxCollider? hitBox;
    private KeywordDisplay? sourceDisplay;
    private KeywordDisplay? proxyDisplay;

    public bool IsHovered { get; private set; }

    public void Configure(StatusManager? nextStatus, SpriteRenderer nextRenderer)
    {
        status = nextStatus;
        renderer = nextRenderer;
        hitBox = gameObject.GetComponent<BoxCollider>() ?? gameObject.AddComponent<BoxCollider>();
        sourceDisplay = nextStatus?.GetComponent<KeywordDisplay>();
        proxyDisplay = gameObject.GetComponent<KeywordDisplay>() ?? gameObject.AddComponent<KeywordDisplay>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        IsHovered = true;
        status?.UpdateStatus(false);
        if (sourceDisplay != null && proxyDisplay != null)
        {
            proxyDisplay.SetText(
                sourceDisplay.title,
                sourceDisplay.text,
                sourceDisplay.keyWords,
                sourceDisplay.msg,
                sourceDisplay.icon,
                sourceDisplay.type);
            proxyDisplay.OnPointerEnter(eventData);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        proxyDisplay?.OnPointerExit(eventData);
        IsHovered = false;
    }

    private void LateUpdate()
    {
        if (renderer == null || hitBox == null || renderer.sprite == null)
        {
            return;
        }
        var bounds = renderer.bounds;
        hitBox.center = transform.InverseTransformPoint(bounds.center);
        var localSize = transform.InverseTransformVector(bounds.size);
        hitBox.size = new Vector3(
            Mathf.Abs(localSize.x),
            Mathf.Abs(localSize.y),
            Mathf.Max(0.1f, Mathf.Abs(localSize.z)));

    }

    private void OnDisable()
    {
        IsHovered = false;
        proxyDisplay?.OnPointerExit(null!);
    }
}
