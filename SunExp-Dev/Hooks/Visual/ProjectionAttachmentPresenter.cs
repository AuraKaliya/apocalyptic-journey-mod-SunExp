using System;
using System.Collections;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public static class ProjectionAttachmentPresenter
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
        ProjectionStateStore.Registered += Attach;
        ProjectionStateStore.Retired += Detach;
        ProjectionStateStore.ActionPresented += PlayActionFocus;
    }

    public static void RefreshByOwner(IStatusManager? owner, string source)
    {
        if (owner == null)
        {
            return;
        }

        foreach (var state in ProjectionStateStore.Active())
        {
            if (!string.Equals(state.OwnerStatusId, owner.InstanceId, StringComparison.Ordinal))
            {
                continue;
            }

            var active = owner.CurHp > 0 && owner.state != IStatusManager.State.Dead;
            state.SetSuspended(!active);
            if (state.Projection?.gameObject != null)
            {
                state.Projection.gameObject.SetActive(active);
            }

            if (Proxies.TryGetValue(state.StatusId, out var proxy) && proxy != null)
            {
                proxy.SetActive(active);
            }

            SunExpLog.Debug("[ProjectionAttachment] owner visibility=" + active
                + ", owner=" + owner.InstanceId + ", source=" + source);
        }
    }

    private static void Attach(ProjectionState state)
    {
        GameObject? proxy = null;
        try
        {
            var owner = StatusById(state.OwnerStatusId);
            var projection = state.Projection;
            if (owner?.transform == null || projection?.transform == null)
            {
                return;
            }

            RemoveProxy(state.StatusId, restoreSource: true);
            var ownerRenderer = owner.transform.Find("body")?.GetComponent<SpriteRenderer>();
            var sourceRenderer = projection.transform.Find("body")?.GetComponent<SpriteRenderer>();
            var ownerCollider = owner.transform.GetComponent<BoxCollider>();
            var projectionCollider = projection.transform.GetComponent<BoxCollider>();
            if (ownerRenderer == null || sourceRenderer == null || projectionCollider == null)
            {
                SunExpLog.Warn("[ProjectionAttachment] visual proxy prerequisites unavailable: status="
                    + state.StatusId);
                return;
            }

            var status = projection.Status as StatusManager;
            status?.statusBarObj?.SetActive(false);

            proxy = new GameObject("SunExp_ProjectionVisualProxy:" + state.StatusId);
            CompanionSceneApi.MoveToOwnerScene(proxy, owner.transform.gameObject, "ProjectionAttachment.Attach");
            proxy.transform.position = Vector3.zero;
            proxy.transform.rotation = Quaternion.identity;
            proxy.transform.localScale = Vector3.one;
            proxy.layer = ownerRenderer.gameObject.layer;
            var proxyRenderer = proxy.AddComponent<SpriteRenderer>();
            var visualProxy = proxy.AddComponent<ProjectionVisualProxy>();
            if (!visualProxy.Configure(
                    owner.transform,
                    ownerCollider,
                    ownerRenderer,
                    projection.transform,
                    projectionCollider,
                    sourceRenderer,
                    projection.transform.Find("Reflection")?.gameObject,
                    projection.transform.Find("bottom")?.gameObject,
                    status,
                    proxyRenderer))
            {
                visualProxy.RestoreSourcePresentation();
                UnityEngine.Object.Destroy(proxy);
                return;
            }

            Proxies[state.StatusId] = proxy;
            RefreshByOwner(owner, "Attach");
            SunExpPerformanceCounters.Record("ProjectionAttachment.ProxyAttached");
        }
        catch (Exception ex)
        {
            Proxies.Remove(state.StatusId);
            if (proxy != null)
            {
                proxy.GetComponent<ProjectionVisualProxy>()?.RestoreSourcePresentation();
                UnityEngine.Object.Destroy(proxy);
            }

            SunExpLog.Warn("[ProjectionAttachment] proxy attach failed: " + ex.Message);
        }
    }

    private static void Detach(ProjectionState state)
    {
        if (state != null)
        {
            RemoveProxy(state.StatusId, restoreSource: false);
        }
    }

    private static void RemoveProxy(string statusId, bool restoreSource)
    {
        if (string.IsNullOrWhiteSpace(statusId)
            || !Proxies.TryGetValue(statusId, out var proxy))
        {
            return;
        }

        Proxies.Remove(statusId);
        if (proxy == null)
        {
            return;
        }

        if (restoreSource)
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
                    || !proxy.name.StartsWith("SunExp_ProjectionVisualProxy:", StringComparison.Ordinal))
                {
                    continue;
                }

                proxy.SetActive(false);
                UnityEngine.Object.Destroy(proxy);
            }
        }

        SunExpLog.Debug("[ProjectionAttachment] cleared from " + source + ": count=" + proxies.Count);
    }

    private static void PlayActionFocus(ProjectionState state)
    {
        if (state != null
            && Proxies.TryGetValue(state.StatusId, out var proxy)
            && proxy != null)
        {
            proxy.GetComponent<ProjectionVisualProxy>()?.PlayActionFocus(state.StatusId);
        }
    }

    private static IStatusManager? StatusById(string statusId)
    {
        return !string.IsNullOrWhiteSpace(statusId)
            && FightManager.Instance?.statuses?.TryGetValue(statusId, out var status) == true
                ? status
                : null;
    }
}

internal sealed class ProjectionVisualProxy : MonoBehaviour
{
    private const float ProjectionHeightAt1080 = 120f;
    private const float HorizontalOverlapRatio = 1f / 3f;
    private const float IntentGapAt1080 = 14f;
    private const float IntentIconScale = 0.60f;
    private const float MinimumDisplayScale = 0.02f;
    private const float MaximumDisplayScale = 4f;
    private const float AttackFocusTravelAt1080 = 70f;
    private const float InterferenceFocusTravelAt1080 = 45f;
    private const float SupportFocusTravelAt1080 = 12f;

    private Transform? ownerRoot;
    private BoxCollider? ownerCollider;
    private SpriteRenderer? ownerRenderer;
    private Transform? sourceRoot;
    private BoxCollider? sourceCollider;
    private SpriteRenderer? sourceRenderer;
    private GameObject? sourceReflection;
    private GameObject? sourceBottom;
    private StatusManager? projectionStatus;
    private SpriteRenderer? proxyRenderer;
    private bool sourceRendererWasEnabled;
    private bool sourceColliderWasEnabled;
    private bool sourceReflectionWasActive;
    private bool sourceBottomWasActive;
    private bool sourcePresentationRestored;
    private bool hasLocalAabb;
    private Vector3 localAabbCenter;
    private Vector3 localAabbSize;
    private Vector3 localBodyPosition;
    private bool hasOwnerBounds;
    private Bounds lastOwnerBounds;
    private Sprite? synchronizedSprite;
    private Material? synchronizedMaterial;
    private Camera? layoutCamera;
    private GameObject? scaledActionContent;
    private Vector3 actionContentBaseScale = Vector3.one;
    private RectTransform? actionRect;
    private RectTransform? actionParentRect;
    private Canvas? actionCanvas;
    private Coroutine? actionPulse;
    private float pulseMultiplier = 1f;
    private Vector2 focusDirection = Vector2.up;
    private float focusTravelAt1080;
    private float focusProgress;
    private float focusPeakScale = 1.08f;
    private Vector2 lastDisplayedScreenCenter;
    private bool hasDisplayedScreenCenter;
    private bool warnedInvalidLayout;
    private bool hasValidLayout;
    private bool hasLayoutSnapshot;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private Matrix4x4 lastWorldToCameraMatrix;
    private Matrix4x4 lastProjectionMatrix;
    private float lastPulseMultiplier;
    private float lastFocusProgress;
    private float lastSourceXDirection;
    private float lastSourceYDirection;
    private GameObject? lastActionContent;
    private bool lastActionContentActive;
    private int lastSortingLayerId;
    private int lastSortingOrder;

    public bool Configure(
        Transform owner,
        BoxCollider? ownerBoundsCollider,
        SpriteRenderer ownerBody,
        Transform projection,
        BoxCollider projectionBoundsCollider,
        SpriteRenderer projectionBody,
        GameObject? reflection,
        GameObject? bottom,
        StatusManager? status,
        SpriteRenderer outputRenderer)
    {
        ownerRoot = owner;
        ownerCollider = ownerBoundsCollider;
        ownerRenderer = ownerBody;
        sourceRoot = projection;
        sourceCollider = projectionBoundsCollider;
        sourceRenderer = projectionBody;
        sourceReflection = reflection;
        sourceBottom = bottom;
        projectionStatus = status;
        proxyRenderer = outputRenderer;
        proxyRenderer.enabled = false;
        layoutCamera = Camera.main;

        sourceRendererWasEnabled = projectionBody.enabled;
        sourceColliderWasEnabled = projectionBoundsCollider.enabled;
        sourceReflectionWasActive = reflection != null && reflection.activeSelf;
        sourceBottomWasActive = bottom != null && bottom.activeSelf;
        sourcePresentationRestored = false;
        RefreshLocalAabb();
        if (!hasLocalAabb)
        {
            SunExpLog.Warn("[ProjectionAttachment] projection local AABB unavailable for visual proxy");
            return false;
        }

        HideSourcePresentation();
        SynchronizeVisual();
        ApplyLayout();
        return true;
    }

    public void PlayActionFocus(string statusId)
    {
        StopActionPulse();
        var plan = CompanionBattleStateStore.Find(statusId)?.CurrentPlan;
        var battleState = CompanionBattleStateStore.Find(statusId);
        var intentType = CompanionIntentResolver.IntentType(battleState, CompanionIntentResolver.Find(battleState, plan?.IntentId ?? ""));
        switch (intentType)
        {
            case CompanionIntentType.Attack:
                focusTravelAt1080 = AttackFocusTravelAt1080;
                focusPeakScale = 1.12f;
                focusDirection = ResolveFocusDirection(plan, Vector2.right);
                break;
            case CompanionIntentType.Interference:
                focusTravelAt1080 = InterferenceFocusTravelAt1080;
                focusPeakScale = 1.07f;
                focusDirection = ResolveFocusDirection(plan, Vector2.right);
                break;
            default:
                focusTravelAt1080 = SupportFocusTravelAt1080;
                focusPeakScale = 1.08f;
                focusDirection = Vector2.up;
                break;
        }

        if (isActiveAndEnabled)
        {
            actionPulse = StartCoroutine(Pulse());
        }
    }

    public void RestoreSourcePresentation()
    {
        if (sourcePresentationRestored)
        {
            return;
        }

        sourcePresentationRestored = true;
        StopActionPulse();
        if (sourceRenderer != null)
        {
            sourceRenderer.enabled = sourceRendererWasEnabled;
        }

        if (sourceCollider != null)
        {
            sourceCollider.enabled = sourceColliderWasEnabled;
        }

        if (sourceReflection != null)
        {
            sourceReflection.SetActive(sourceReflectionWasActive);
        }

        if (sourceBottom != null)
        {
            sourceBottom.SetActive(sourceBottomWasActive);
        }
    }

    private void LateUpdate()
    {
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (sourcePresentationRestored
            || ownerRoot == null
            || ownerRenderer == null
            || sourceRoot == null
            || sourceRenderer == null
            || proxyRenderer == null)
        {
            return;
        }

        HideSourcePresentation();
        SynchronizeVisual();
        var localAabbChanged = RefreshLocalAabb();
        if (!hasLocalAabb
            || !sourceRoot.gameObject.activeInHierarchy
            || !ownerRoot.gameObject.activeInHierarchy)
        {
            proxyRenderer.enabled = false;
            hasLayoutSnapshot = false;
            return;
        }

        if (synchronizedSprite == null)
        {
            proxyRenderer.enabled = false;
            hasLayoutSnapshot = false;
            return;
        }

        var ownerBoundsChanged = false;
        if (TryOwnerBounds(out var currentOwnerBounds))
        {
            ownerBoundsChanged = !hasOwnerBounds || !Approximately(lastOwnerBounds, currentOwnerBounds);
            lastOwnerBounds = currentOwnerBounds;
            hasOwnerBounds = true;
        }

        var camera = layoutCamera ?? Camera.main;
        layoutCamera = camera;
        var sourceXDirection = sourceRenderer.transform.localScale.x < 0f ? -1f : 1f;
        var sourceYDirection = sourceRenderer.transform.localScale.y < 0f ? -1f : 1f;
        var actionContent = projectionStatus?.actionContent;
        var actionContentActive = actionContent != null && actionContent.activeInHierarchy;
        var layoutChanged = !hasLayoutSnapshot
                            || localAabbChanged
                            || ownerBoundsChanged
                            || camera == null
                            || camera.worldToCameraMatrix != lastWorldToCameraMatrix
                            || camera.projectionMatrix != lastProjectionMatrix
                            || Screen.width != lastScreenWidth
                            || Screen.height != lastScreenHeight
                            || !Mathf.Approximately(pulseMultiplier, lastPulseMultiplier)
                            || !Mathf.Approximately(focusProgress, lastFocusProgress)
                            || !Mathf.Approximately(sourceXDirection, lastSourceXDirection)
                            || !Mathf.Approximately(sourceYDirection, lastSourceYDirection)
                            || actionContent != lastActionContent
                            || actionContentActive != lastActionContentActive
                            || ownerRenderer.sortingLayerID != lastSortingLayerId
                            || ownerRenderer.sortingOrder != lastSortingOrder;
        if (!layoutChanged)
        {
            proxyRenderer.enabled = hasValidLayout;
            return;
        }

        if (!hasOwnerBounds
            || camera == null
            || Screen.height <= 0
            || !TryScreenRect(lastOwnerBounds, camera, out var ownerScreen)
            || !TryReferencePixelsToWorldY(
                ProjectionHeightAt1080,
                lastOwnerBounds.center,
                camera,
                out var targetWorldHeight))
        {
            RejectInvalidLayout("owner AABB or camera unavailable");
            return;
        }

        var baseScale = targetWorldHeight / localAabbSize.y;
        if (!IsFinite(baseScale)
            || baseScale < MinimumDisplayScale
            || baseScale > MaximumDisplayScale)
        {
            RejectInvalidLayout("proxy scale outside safe range");
            return;
        }

        var displayScale = baseScale * pulseMultiplier;
        var targetScreenHeight = Screen.height * ProjectionHeightAt1080 / 1080f * pulseMultiplier;
        var targetScreenWidth = targetScreenHeight * localAabbSize.x / localAabbSize.y;
        var desiredScreenCenter = new Vector2(
            ownerScreen.xMax - targetScreenWidth * HorizontalOverlapRatio,
            ownerScreen.yMax + targetScreenHeight * 0.5f);
        desiredScreenCenter += focusDirection
            * (Screen.height * focusTravelAt1080 / 1080f * focusProgress);
        var ownerScreenCenter = camera.WorldToScreenPoint(lastOwnerBounds.center);
        var desiredScreenPoint = new Vector3(
            desiredScreenCenter.x,
            desiredScreenCenter.y,
            ownerScreenCenter.z);
        if (!IsFinite(desiredScreenPoint) || desiredScreenPoint.z <= 0f)
        {
            RejectInvalidLayout("proxy screen center invalid");
            return;
        }

        var desiredAabbCenter = camera.ScreenToWorldPoint(desiredScreenPoint);
        var localCenterOffset = localAabbCenter - localBodyPosition;
        var desiredBodyOrigin = desiredAabbCenter - new Vector3(
            localCenterOffset.x * displayScale,
            localCenterOffset.y * displayScale,
            0f);
        if (!IsFinite(desiredAabbCenter) || !IsFinite(desiredBodyOrigin))
        {
            RejectInvalidLayout("proxy world center invalid");
            return;
        }

        transform.position = desiredBodyOrigin;
        transform.localScale = new Vector3(
            displayScale * sourceXDirection,
            displayScale * sourceYDirection,
            1f);
        proxyRenderer.sortingLayerID = ownerRenderer.sortingLayerID;
        proxyRenderer.sortingOrder = ownerRenderer.sortingOrder - 1;

        var displayedBounds = new Bounds(
            desiredAabbCenter,
            new Vector3(
                localAabbSize.x * displayScale,
                localAabbSize.y * displayScale,
                0f));
        var displayedScreen = Rect.MinMaxRect(
            desiredScreenCenter.x - targetScreenWidth * 0.5f,
            desiredScreenCenter.y - targetScreenHeight * 0.5f,
            desiredScreenCenter.x + targetScreenWidth * 0.5f,
            desiredScreenCenter.y + targetScreenHeight * 0.5f);
        warnedInvalidLayout = false;
        hasValidLayout = true;
        lastDisplayedScreenCenter = desiredScreenCenter;
        hasDisplayedScreenCenter = true;
        proxyRenderer.enabled = true;
        AnchorIntent(displayedBounds, displayedScreen);
        CaptureLayoutSnapshot(
            camera,
            sourceXDirection,
            sourceYDirection,
            actionContent,
            actionContentActive);
    }

    private void HideSourcePresentation()
    {
        if (sourceRenderer != null && sourceRenderer.enabled)
        {
            sourceRenderer.enabled = false;
        }

        if (sourceCollider != null && sourceCollider.enabled)
        {
            sourceCollider.enabled = false;
        }

        if (sourceReflection != null && sourceReflection.activeSelf)
        {
            sourceReflection.SetActive(false);
        }

        if (sourceBottom != null && sourceBottom.activeSelf)
        {
            sourceBottom.SetActive(false);
        }

        if (projectionStatus?.statusBarObj?.activeSelf == true)
        {
            projectionStatus.statusBarObj.SetActive(false);
        }
    }

    private void SynchronizeVisual()
    {
        if (sourceRenderer == null || proxyRenderer == null)
        {
            return;
        }

        if (synchronizedSprite != sourceRenderer.sprite)
        {
            synchronizedSprite = sourceRenderer.sprite;
            proxyRenderer.sprite = synchronizedSprite;
            SunExpPerformanceCounters.Record("ProjectionAttachment.ProxySpriteChanged");
        }

        if (synchronizedMaterial != sourceRenderer.sharedMaterial)
        {
            synchronizedMaterial = sourceRenderer.sharedMaterial;
            proxyRenderer.sharedMaterial = synchronizedMaterial;
        }

        proxyRenderer.color = sourceRenderer.color;
        proxyRenderer.flipX = sourceRenderer.flipX;
        proxyRenderer.flipY = sourceRenderer.flipY;
    }

    private bool RefreshLocalAabb()
    {
        if (sourceCollider == null || sourceRenderer == null)
        {
            return false;
        }

        var center = sourceCollider.center;
        var size = sourceCollider.size;
        var bodyPosition = sourceRenderer.transform.localPosition;
        if (!IsFinite(center)
            || !IsFinite(size)
            || !IsFinite(bodyPosition)
            || size.x <= 0.001f
            || size.y <= 0.001f)
        {
            return false;
        }

        var changed = !hasLocalAabb
                      || !Approximately(localAabbCenter, center)
                      || !Approximately(localAabbSize, size)
                      || !Approximately(localBodyPosition, bodyPosition);
        localAabbCenter = center;
        localAabbSize = size;
        localBodyPosition = bodyPosition;
        hasLocalAabb = true;
        return changed;
    }

    private void CaptureLayoutSnapshot(
        Camera camera,
        float sourceXDirection,
        float sourceYDirection,
        GameObject? actionContent,
        bool actionContentActive)
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastWorldToCameraMatrix = camera.worldToCameraMatrix;
        lastProjectionMatrix = camera.projectionMatrix;
        lastPulseMultiplier = pulseMultiplier;
        lastFocusProgress = focusProgress;
        lastSourceXDirection = sourceXDirection;
        lastSourceYDirection = sourceYDirection;
        lastActionContent = actionContent;
        lastActionContentActive = actionContentActive;
        lastSortingLayerId = ownerRenderer?.sortingLayerID ?? 0;
        lastSortingOrder = ownerRenderer?.sortingOrder ?? 0;
        hasLayoutSnapshot = true;
        SunExpPerformanceCounters.Record("ProjectionAttachment.ProxyLayoutApplied");
    }

    private bool TryOwnerBounds(out Bounds bounds)
    {
        bounds = default;
        if (ownerCollider != null && ownerCollider.enabled && ownerCollider.gameObject.activeInHierarchy)
        {
            bounds = ownerCollider.bounds;
        }
        else if (ownerRenderer != null)
        {
            bounds = ownerRenderer.bounds;
        }

        return IsUsableBounds(bounds);
    }

    private void AnchorIntent(Bounds displayedBounds, Rect displayedScreen)
    {
        var actionContent = projectionStatus?.actionContent;
        if (actionContent == null || !actionContent.activeInHierarchy)
        {
            return;
        }

        if (scaledActionContent != actionContent)
        {
            scaledActionContent = actionContent;
            actionContentBaseScale = actionContent.transform.localScale;
            actionRect = actionContent.transform as RectTransform;
            actionParentRect = actionRect?.parent as RectTransform;
            actionCanvas = actionRect?.GetComponentInParent<Canvas>();
        }

        var desiredScale = actionContentBaseScale * IntentIconScale;
        if ((actionContent.transform.localScale - desiredScale).sqrMagnitude > 0.000001f)
        {
            actionContent.transform.localScale = desiredScale;
        }

        var worldCamera = layoutCamera ?? Camera.main;
        layoutCamera = worldCamera;
        if (worldCamera == null || actionRect == null || actionParentRect == null || actionCanvas == null)
        {
            return;
        }

        var screenPoint = new Vector3(
            displayedScreen.center.x,
            displayedScreen.yMax + Screen.height * IntentGapAt1080 / 1080f,
            worldCamera.WorldToScreenPoint(displayedBounds.center).z);
        if (!IsFinite(screenPoint) || screenPoint.z <= 0f)
        {
            return;
        }

        var uiCamera = actionCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : actionCanvas.worldCamera ?? worldCamera;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                actionParentRect,
                screenPoint,
                uiCamera,
                out var targetUiWorld)
            && IsFinite(targetUiWorld)
            && (actionRect.position - targetUiWorld).sqrMagnitude > 0.000001f)
        {
            actionRect.position = targetUiWorld;
        }
    }

    private IEnumerator Pulse()
    {
        const float enterDuration = 0.12f;
        const float holdDuration = 0.10f;
        const float returnDuration = 0.18f;
        var elapsed = 0f;
        while (elapsed < enterDuration)
        {
            elapsed += Time.deltaTime;
            var progress = Mathf.Clamp01(elapsed / enterDuration);
            focusProgress = progress;
            pulseMultiplier = Mathf.Lerp(1f, focusPeakScale, progress);
            yield return null;
        }

        focusProgress = 1f;
        pulseMultiplier = focusPeakScale;
        elapsed = 0f;
        while (elapsed < holdDuration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            var progress = Mathf.Clamp01(elapsed / returnDuration);
            focusProgress = Mathf.Lerp(1f, 0f, progress);
            pulseMultiplier = Mathf.Lerp(focusPeakScale, 1f, progress);
            yield return null;
        }

        focusProgress = 0f;
        pulseMultiplier = 1f;
        actionPulse = null;
    }

    private Vector2 ResolveFocusDirection(CompanionIntentPlan? plan, Vector2 fallback)
    {
        if (plan == null || !hasDisplayedScreenCenter)
        {
            return fallback;
        }

        string? targetId = null;
        foreach (var effect in plan.ResolvedEffects)
        {
            if (effect.TargetIds != null && effect.TargetIds.Count > 0)
            {
                targetId = effect.TargetIds[0];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(targetId) && plan.OrderedTargetIds.Count > 0)
        {
            targetId = plan.OrderedTargetIds[0];
        }

        var camera = layoutCamera ?? Camera.main;
        if (camera == null
            || string.IsNullOrWhiteSpace(targetId)
            || FightManager.Instance?.statuses?.TryGetValue(targetId, out var target) != true
            || target?.transform == null)
        {
            return fallback;
        }

        var targetCollider = target.transform.GetComponent<BoxCollider>();
        var targetRenderer = target.transform.Find("body")?.GetComponent<SpriteRenderer>();
        var targetCenter = targetCollider != null && targetCollider.enabled
            ? targetCollider.bounds.center
            : targetRenderer != null
                ? targetRenderer.bounds.center
                : target.transform.position;
        var targetScreen = camera.WorldToScreenPoint(targetCenter);
        var delta = new Vector2(targetScreen.x, targetScreen.y) - lastDisplayedScreenCenter;
        return IsFinite(targetScreen) && targetScreen.z > 0f && delta.sqrMagnitude > 0.01f
            ? delta.normalized
            : fallback;
    }

    private void OnDisable()
    {
        StopActionPulse();
    }

    private void StopActionPulse()
    {
        if (actionPulse != null)
        {
            StopCoroutine(actionPulse);
            actionPulse = null;
        }

        pulseMultiplier = 1f;
        focusProgress = 0f;
    }

    private void RejectInvalidLayout(string reason)
    {
        if (proxyRenderer != null)
        {
            proxyRenderer.enabled = hasValidLayout && synchronizedSprite != null;
        }

        if (!warnedInvalidLayout)
        {
            warnedInvalidLayout = true;
            SunExpLog.Warn("[ProjectionAttachment] visual proxy layout skipped: " + reason);
            SunExpPerformanceCounters.Record("ProjectionAttachment.ProxyLayoutSkipped");
        }
    }

    private static bool TryScreenRect(Bounds bounds, Camera camera, out Rect screenRect)
    {
        screenRect = default;
        var depth = bounds.center.z;
        var first = camera.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.min.y, depth));
        if (!IsFinite(first) || first.z <= 0f)
        {
            return false;
        }

        var xMin = first.x;
        var xMax = first.x;
        var yMin = first.y;
        var yMax = first.y;
        if (!TryExtendScreenRect(
                camera.WorldToScreenPoint(new Vector3(bounds.min.x, bounds.max.y, depth)),
                ref xMin,
                ref xMax,
                ref yMin,
                ref yMax)
            || !TryExtendScreenRect(
                camera.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.min.y, depth)),
                ref xMin,
                ref xMax,
                ref yMin,
                ref yMax)
            || !TryExtendScreenRect(
                camera.WorldToScreenPoint(new Vector3(bounds.max.x, bounds.max.y, depth)),
                ref xMin,
                ref xMax,
                ref yMin,
                ref yMax))
        {
            return false;
        }

        screenRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return IsFinite(screenRect) && screenRect.width > 0.01f && screenRect.height > 0.01f;
    }

    private static bool TryExtendScreenRect(
        Vector3 point,
        ref float xMin,
        ref float xMax,
        ref float yMin,
        ref float yMax)
    {
        if (!IsFinite(point) || point.z <= 0f)
        {
            return false;
        }

        xMin = Mathf.Min(xMin, point.x);
        xMax = Mathf.Max(xMax, point.x);
        yMin = Mathf.Min(yMin, point.y);
        yMax = Mathf.Max(yMax, point.y);
        return true;
    }

    private static bool TryReferencePixelsToWorldY(
        float referencePixels,
        Vector3 worldPoint,
        Camera camera,
        out float worldHeight)
    {
        worldHeight = 0f;
        if (Screen.height <= 0 || !IsFinite(worldPoint))
        {
            return false;
        }

        var screenPoint = camera.WorldToScreenPoint(worldPoint);
        if (!IsFinite(screenPoint) || screenPoint.z <= 0f)
        {
            return false;
        }

        var scaledPixels = Screen.height * referencePixels / 1080f;
        var lower = camera.ScreenToWorldPoint(screenPoint);
        var upper = camera.ScreenToWorldPoint(screenPoint + new Vector3(0f, scaledPixels, 0f));
        worldHeight = Mathf.Abs(upper.y - lower.y);
        return IsFinite(lower)
            && IsFinite(upper)
            && IsFinite(worldHeight)
            && worldHeight > 0.0001f;
    }

    private static bool IsUsableBounds(Bounds bounds)
    {
        return IsFinite(bounds.center)
            && IsFinite(bounds.size)
            && bounds.size.x > 0.001f
            && bounds.size.y > 0.001f;
    }

    private static bool Approximately(Bounds left, Bounds right)
    {
        return Approximately(left.center, right.center)
               && Approximately(left.size, right.size);
    }

    private static bool Approximately(Vector3 left, Vector3 right)
    {
        return (left - right).sqrMagnitude <= 0.000001f;
    }

    private static bool IsFinite(Rect value)
    {
        return IsFinite(value.xMin)
            && IsFinite(value.xMax)
            && IsFinite(value.yMin)
            && IsFinite(value.yMax);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
