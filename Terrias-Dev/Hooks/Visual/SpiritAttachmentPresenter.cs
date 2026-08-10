using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;

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

            var active = owner.CurHp > 0 && owner.state != IStatusManager.State.Dead;
            state.Spirit.gameObject.SetActive(active);
            if (Proxies.TryGetValue(state.StatusId, out var proxy) && proxy != null)
            {
                proxy.SetActive(active);
            }

            TerriasLog.Debug("[SpiritAttachment] owner visibility=" + active + ", source=" + source);
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
            status?.statusBarObj?.SetActive(false);
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
                    output))
            {
                visual.RestoreSourcePresentation();
                UnityEngine.Object.Destroy(proxy);
                return;
            }

            proxy.AddComponent<SpiritAttachedHealthBar>().Configure(status, output);
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

internal sealed class SpiritAttachedHealthBar : MonoBehaviour
{
    private static Sprite? whiteSprite;
    private StatusManager? status;
    private SpriteRenderer? actorRenderer;
    private SpriteRenderer? background;
    private SpriteRenderer? fill;
    private SpiritStatusHoverRelay? hoverRelay;

    public void Configure(StatusManager? nextStatus, SpriteRenderer renderer)
    {
        status = nextStatus;
        actorRenderer = renderer;
        whiteSprite ??= Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        background = CreatePart("SpiritHealthBackground", new Color(0.08f, 0.09f, 0.11f, 0.92f));
        fill = CreatePart("SpiritHealthFill", new Color(0.31f, 0.86f, 0.48f, 1f));
    }

    private SpriteRenderer CreatePart(string name, Color color)
    {
        var child = new GameObject(name);
        CompanionSceneApi.MoveToOwnerScene(child, gameObject, "SpiritAttachment.HealthBar");
        child.transform.SetParent(transform, true);
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = whiteSprite;
        renderer.color = color;
        return renderer;
    }

    private void LateUpdate()
    {
        if (status == null || actorRenderer == null || background == null || fill == null)
        {
            return;
        }

        hoverRelay ??= GetComponent<SpiritStatusHoverRelay>();
        var showNativeHover = hoverRelay?.IsHovered == true;
        if (status.statusBarObj != null
            && status.statusBarObj.activeSelf != showNativeHover)
        {
            status.statusBarObj.SetActive(showNativeHover);
        }
        if (status.effectListObj?.activeSelf == true)
        {
            status.effectListObj.SetActive(false);
        }
        if (!actorRenderer.enabled || actorRenderer.sprite == null)
        {
            background.enabled = false;
            fill.enabled = false;
            return;
        }

        var bounds = actorRenderer.bounds;
        var width = Mathf.Max(0.025f, bounds.size.x * 0.055f);
        var height = Mathf.Max(0.15f, bounds.size.y * 0.86f);
        var x = bounds.max.x + width * 1.8f;
        var ratio = status.MaxHp <= 0
            ? 0f
            : Mathf.Clamp01((float)status.CurHp / status.MaxHp);
        var fillHeight = Mathf.Max(0.001f, height * ratio);

        Apply(background, new Vector3(x, bounds.center.y, bounds.center.z), width, height, 20);
        Apply(
            fill,
            new Vector3(x, bounds.min.y + fillHeight * 0.5f, bounds.center.z - 0.001f),
            width * 0.62f,
            fillHeight,
            21);
        background.enabled = true;
        fill.enabled = ratio > 0f;
    }

    private void Apply(
        SpriteRenderer renderer,
        Vector3 position,
        float width,
        float height,
        int sortingOffset)
    {
        renderer.transform.position = position;
        var parentScale = transform.lossyScale;
        renderer.transform.localScale = new Vector3(
            width / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            height / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            1f);
        renderer.sortingLayerID = actorRenderer?.sortingLayerID ?? 0;
        renderer.sortingOrder = (actorRenderer?.sortingOrder ?? 0) + sortingOffset;
    }
}

internal sealed class SpiritStatusHoverRelay : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private StatusManager? status;
    private SpriteRenderer? renderer;
    private BoxCollider? hitBox;

    public bool IsHovered { get; private set; }

    public void Configure(StatusManager? nextStatus, SpriteRenderer nextRenderer)
    {
        status = nextStatus;
        renderer = nextRenderer;
        hitBox = gameObject.GetComponent<BoxCollider>() ?? gameObject.AddComponent<BoxCollider>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        IsHovered = true;
        status?.UpdateStatus(false);
        status?.statusBarObj?.SetActive(true);
        if (status is IPointerEnterHandler handler)
        {
            handler.OnPointerEnter(eventData);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (status is IPointerExitHandler handler)
        {
            handler.OnPointerExit(eventData);
        }
        IsHovered = false;
        status?.statusBarObj?.SetActive(false);
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

        if (IsHovered && status?.statusBarObj != null && Camera.main != null)
        {
            var canvas = GameObject.Find("Canvas")?.GetComponent<RectTransform>();
            if (canvas != null)
            {
                var anchor = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                status.statusBarObj.transform.localPosition =
                    PositionUtility.ScreenPointToCanvasPoint(
                        canvas,
                        Camera.main.WorldToScreenPoint(anchor));
            }
        }
    }

    private void OnDisable()
    {
        IsHovered = false;
        status?.statusBarObj?.SetActive(false);
        if (status is IPointerExitHandler handler)
        {
            handler.OnPointerExit(null!);
        }
    }
}
