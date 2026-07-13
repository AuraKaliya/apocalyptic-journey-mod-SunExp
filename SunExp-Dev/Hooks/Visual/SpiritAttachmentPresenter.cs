using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

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

            var active = owner.CurHp > 0 && owner.state != IStatusManager.State.Dead;
            state.Spirit.gameObject.SetActive(active);
            if (Proxies.TryGetValue(state.StatusId, out var proxy) && proxy != null)
            {
                proxy.SetActive(active);
            }

            SunExpLog.Debug("[SpiritAttachment] owner visibility=" + active + ", source=" + source);
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
                SunExpLog.Warn("[SpiritAttachment] visual proxy prerequisites unavailable: " + state.StatusId);
                return;
            }

            RemoveProxy(state.StatusId, true);
            var status = spirit.Status as StatusManager;
            status?.statusBarObj?.SetActive(false);
            proxy = new GameObject("SunExp_SpiritVisualProxy:" + state.StatusId);
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
                    output))
            {
                visual.RestoreSourcePresentation();
                UnityEngine.Object.Destroy(proxy);
                return;
            }

            Proxies[state.StatusId] = proxy;
            RefreshByOwner(owner, "Attach");
            SunExpPerformanceCounters.Record("SpiritAttachment.ProxyAttached");
        }
        catch (Exception ex)
        {
            if (proxy != null)
            {
                proxy.GetComponent<ProjectionVisualProxy>()?.RestoreSourcePresentation();
                UnityEngine.Object.Destroy(proxy);
            }

            SunExpLog.Warn("[SpiritAttachment] proxy attach failed: " + ex.Message);
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
            proxy.GetComponent<ProjectionVisualProxy>()?.RestoreSourcePresentation();
        }

        UnityEngine.Object.Destroy(proxy);
    }

    private static IStatusManager? StatusById(string statusId)
    {
        return FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true ? status : null;
    }
}
