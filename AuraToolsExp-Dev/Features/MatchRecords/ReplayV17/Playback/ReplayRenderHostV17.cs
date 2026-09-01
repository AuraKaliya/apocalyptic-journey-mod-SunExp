using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

/// <summary>
/// Owns every replay render target. The replay camera is permanently manual and
/// can render only into a validated RenderTexture; it never writes to the game backbuffer.
/// </summary>
internal sealed class ReplayRenderHostV17 : IDisposable
{
    private const int ReplayLayer = 30;
    private const int DisplaySortingOrder = 31_000;
    private static readonly Vector3 CaptureIsolationOffset = new(10_000f, 10_000f, 0f);

    private readonly ReplaySceneDescriptorV17 scene;
    private readonly ReplayRenderHostContractV17 contract;
    private readonly GameObject captureRootObject;
    private readonly GameObject displayRootObject;
    private readonly Canvas captureCanvas;
    private readonly Canvas displayCanvas;
    private readonly RawImage displayImage;
    private readonly AspectRatioFitter displayAspect;
    private readonly Camera camera;
    private readonly ReplayUrpRendererIsolationApi.ReplayUrpRendererIsolationLease rendererLease;
    private RenderTexture interactiveTarget;
    private bool disposed;

    internal ReplayRenderHostV17(Transform sceneRoot, ReplaySceneDescriptorV17 scene)
    {
        if (sceneRoot == null) throw new ArgumentNullException(nameof(sceneRoot));
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));

        var size = ResolveInteractiveSize();
        var createdContract = new ReplayRenderHostContractV17(size);
        RenderTexture? createdTarget = null;
        GameObject? createdCaptureRoot = null;
        GameObject? createdDisplayRoot = null;
        Camera? createdCamera = null;
        ReplayUrpRendererIsolationApi.ReplayUrpRendererIsolationLease? createdRendererLease = null;
        Canvas? createdCaptureCanvas = null;
        Canvas? createdDisplayCanvas = null;
        RawImage? createdDisplayImage = null;
        AspectRatioFitter? createdDisplayAspect = null;
        try
        {
            createdTarget = CreateInteractiveTarget(size, createdContract.Generation);
            createdCaptureRoot = new GameObject("ReplayCaptureRoot");
            createdCaptureRoot.transform.SetParent(sceneRoot, false);
            createdCaptureRoot.transform.localPosition = CaptureIsolationOffset;
            createdCaptureRoot.layer = ReplayLayer;
            createdCamera = CreateManualCamera(
                createdCaptureRoot.transform,
                scene,
                createdTarget,
                out createdRendererLease);
            createdCaptureCanvas = CreateCaptureCanvas(createdCaptureRoot.transform, createdCamera, scene);
            (createdDisplayRoot, createdDisplayCanvas, createdDisplayImage, createdDisplayAspect) =
                CreateDisplaySurface(sceneRoot, createdTarget, size);
            createdDisplayRoot.SetActive(false);
        }
        catch (Exception creationFailure)
        {
            var failures = new List<Exception> { creationFailure };
            void Cleanup(Action action)
            {
                try { action(); }
                catch (Exception ex) { failures.Add(ex); }
            }
            Cleanup(() => { if (createdDisplayImage != null) createdDisplayImage.texture = null; });
            Cleanup(() =>
            {
                if (createdCamera == null) return;
                createdCamera.enabled = false;
                createdCamera.targetTexture = null;
            });
            Cleanup(() => createdRendererLease?.Dispose());
            Cleanup(() =>
            {
                if (createdTarget == null) return;
                if (createdTarget.IsCreated()) createdTarget.Release();
                Object.Destroy(createdTarget);
            });
            Cleanup(() => { if (createdDisplayRoot != null) Object.Destroy(createdDisplayRoot); });
            Cleanup(() => { if (createdCaptureRoot != null) Object.Destroy(createdCaptureRoot); });
            Cleanup(createdContract.Dispose);
            if (failures.Count > 1)
                throw new AggregateException("Replay render host creation and cleanup failed.", failures);
            throw;
        }

        contract = createdContract;
        interactiveTarget = createdTarget;
        captureRootObject = createdCaptureRoot;
        displayRootObject = createdDisplayRoot;
        camera = createdCamera;
        rendererLease = createdRendererLease;
        captureCanvas = createdCaptureCanvas;
        displayCanvas = createdDisplayCanvas;
        displayImage = createdDisplayImage;
        displayAspect = createdDisplayAspect;
        try
        {
            var sentinel = sceneRoot.gameObject.GetComponent<ReplayRenderHostDestroySentinelV17>()
                           ?? sceneRoot.gameObject.AddComponent<ReplayRenderHostDestroySentinelV17>();
            sentinel.Bind(Dispose);
            Log("prepared", "target=interactive");
        }
        catch (Exception initializationFailure)
        {
            try { Dispose(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Replay render host initialization and cleanup both failed.",
                    initializationFailure,
                    cleanupFailure);
            }
            throw;
        }
    }

    internal Transform CaptureRoot => captureRootObject.transform;
    internal Canvas CaptureCanvas => captureCanvas;
    internal Camera Camera => camera;
    internal bool IsPreflighted => contract.Phase is ReplayRenderHostPhaseV17.Preflighted
        or ReplayRenderHostPhaseV17.FrameBarrierConfirmed
        or ReplayRenderHostPhaseV17.Active;
    internal bool IsActivationReady => contract.Phase is ReplayRenderHostPhaseV17.FrameBarrierConfirmed
        or ReplayRenderHostPhaseV17.Active;
    internal bool IsDisplayActive => !disposed && displayRootObject.activeSelf;

    internal void PreflightRender()
    {
        ThrowIfDisposed();
        if (!contract.CanRenderPreflight)
            throw new InvalidOperationException("Replay render host is not eligible for first-frame preflight.");
        RenderTo(interactiveTarget, "preflight");
        ValidateRenderedPixels(interactiveTarget);
        contract.MarkPreflightSucceeded();
        Log("preflighted", "target=interactive");
    }

    internal void ConfirmFrameBarrier()
    {
        ThrowIfDisposed();
        if (!contract.CanConfirmFrameBarrier)
            throw new InvalidOperationException(
                "Replay render host is not waiting for the game frame barrier.");
        rendererLease.Validate(camera);
        contract.ConfirmFrameBarrier();
        Log("frame-barrier-confirmed", "target=interactive");
    }

    internal void ActivateDisplay(bool visible)
    {
        ThrowIfDisposed();
        contract.Activate();
        displayRootObject.SetActive(visible);
        Log("active", "display=" + visible.ToString().ToLowerInvariant());
    }

    internal bool RenderInteractive(bool contentDirty)
    {
        ThrowIfDisposed();
        if (!contract.CanRenderInteractive)
            return false;
        var resized = EnsureInteractiveSize();
        if (!contentDirty && !resized) return false;
        RenderTo(interactiveTarget, resized ? "interactive-resize" : "interactive");
        return true;
    }

    internal ReplayRenderExportLeaseV17 AcquireExportTarget(RenderTexture target)
    {
        ThrowIfDisposed();
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (!target.IsCreated())
            throw new InvalidOperationException("Replay export RenderTexture has not been created.");
        if (target.width <= 0 || target.height <= 0)
            throw new InvalidOperationException("Replay export RenderTexture has invalid dimensions.");

        var token = contract.AcquireExport();
        try
        {
            camera.enabled = false;
            camera.targetTexture = target;
            EnsureManualTarget(target, "export-acquire");
            Log("export-acquired", "lease=" + token.LeaseId + ", target=" + TargetIdentity(target));
            return new ReplayRenderExportLeaseV17(this, token, target);
        }
        catch
        {
            contract.ReleaseExport(token);
            camera.enabled = false;
            camera.targetTexture = interactiveTarget;
            throw;
        }
    }

    internal void RenderExport(ReplayRenderLeaseTokenV17 token, RenderTexture target)
    {
        ThrowIfDisposed();
        if (!contract.CanRenderExport(token))
            throw new InvalidOperationException("Replay export render lease is no longer the active target owner.");
        if (target == null || !target.IsCreated())
            throw new InvalidOperationException("Replay export target was released before rendering completed.");
        if (!ReferenceEquals(camera.targetTexture, target))
            throw new InvalidOperationException("Replay camera target ownership changed during export.");
        RenderTo(target, "export");
    }

    internal void ReleaseExport(ReplayRenderLeaseTokenV17 token)
    {
        var release = contract.ReleaseExport(token);
        if (release == ReplayRenderLeaseReleaseV17.HostDisposed) return;
        if (release == ReplayRenderLeaseReleaseV17.Duplicate) return;
        if (release != ReplayRenderLeaseReleaseV17.Released)
            throw new InvalidOperationException("Replay export lease release rejected: " + release + ".");

        camera.enabled = false;
        camera.targetTexture = interactiveTarget;
        EnsureManualTarget(interactiveTarget, "export-release");
        Log("export-released", "lease=" + token.LeaseId + ", target=interactive");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Exception? cleanupFailure = null;
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception ex) { cleanupFailure ??= ex; }
        }

        Cleanup(() => { if (displayRootObject != null) displayRootObject.SetActive(false); });
        Cleanup(() => { if (displayImage != null) displayImage.texture = null; });
        Cleanup(() => { if (displayCanvas != null) displayCanvas.enabled = false; });
        Cleanup(() => { if (captureCanvas != null) captureCanvas.enabled = false; });
        Cleanup(() =>
        {
            if (camera == null) return;
            camera.enabled = false;
            camera.targetTexture = null;
            if (camera.gameObject != null) camera.gameObject.SetActive(false);
        });
        Cleanup(rendererLease.Dispose);
        Cleanup(contract.Dispose);
        Cleanup(() =>
        {
            if (interactiveTarget == null) return;
            if (interactiveTarget.IsCreated()) interactiveTarget.Release();
            Object.Destroy(interactiveTarget);
        });
        Cleanup(() => { if (displayRootObject != null) Object.Destroy(displayRootObject); });
        Cleanup(() => { if (captureRootObject != null) Object.Destroy(captureRootObject); });
        AuraToolsLog.Info("[MatchRecords] replay render host disposed: phase=Disposed, camera-enabled=false, target=none.");
        if (cleanupFailure != null)
            throw new InvalidOperationException(
                "Replay render host teardown was incomplete.",
                cleanupFailure);
    }

    private bool EnsureInteractiveSize()
    {
        var size = ResolveInteractiveSize();
        if (size.Equals(contract.Size)) return false;
        contract.Resize(size);

        var replacement = CreateInteractiveTarget(size, contract.Generation);
        var previous = interactiveTarget;
        camera.enabled = false;
        camera.targetTexture = replacement;
        interactiveTarget = replacement;
        displayImage.texture = replacement;
        displayAspect.aspectRatio = size.Width / (float)size.Height;
        camera.aspect = displayAspect.aspectRatio;
        EnsureManualTarget(replacement, "interactive-resize");

        if (previous != null)
        {
            if (previous.IsCreated()) previous.Release();
            Object.Destroy(previous);
        }
        Log("resized", "target=interactive");
        return true;
    }

    private ReplayRenderSizeV17 ResolveInteractiveSize() => ReplayRenderSizePolicyV17.Resolve(
        Screen.width,
        Screen.height,
        scene.ReferenceWidth,
        scene.ReferenceHeight);

    private void RenderTo(RenderTexture target, string operation)
    {
        EnsureManualTarget(target, operation);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        EnsureManualTarget(target, operation + "-complete");
    }

    private void EnsureManualTarget(RenderTexture target, string operation)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ReplayRenderHostV17));
        if (camera == null || camera.gameObject == null)
            throw new InvalidOperationException("Replay camera was destroyed during " + operation + ".");
        if (camera.enabled)
            throw new InvalidOperationException("Replay camera became automatically enabled during " + operation + ".");
        if (target == null || !target.IsCreated())
            throw new InvalidOperationException("Replay target is unavailable during " + operation + ".");
        if (!ReferenceEquals(camera.targetTexture, target))
            throw new InvalidOperationException("Replay camera has no owned target during " + operation + ".");
        rendererLease.Validate(camera);
    }

    private static void ValidateRenderedPixels(RenderTexture target)
    {
        const int sampleWidth = 64;
        const int sampleHeight = 36;
        var previous = RenderTexture.active;
        var sample = RenderTexture.GetTemporary(
            sampleWidth,
            sampleHeight,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        var readback = new Texture2D(sampleWidth, sampleHeight, TextureFormat.RGBA32, false, false)
        {
            hideFlags = HideFlags.DontSave
        };
        try
        {
            Graphics.Blit(target, sample);
            RenderTexture.active = sample;
            readback.ReadPixels(new Rect(0, 0, sampleWidth, sampleHeight), 0, 0, false);
            readback.Apply(false, false);
            var pixels = readback.GetPixels32();
            var samples = pixels.Select(pixel => new ReplayRgbaSampleV17(
                pixel.r,
                pixel.g,
                pixel.b,
                pixel.a)).ToArray();
            var error = ReplayRenderPixelContractV17.Validate(samples);
            if (error.Length > 0)
                throw new InvalidOperationException(
                    "Replay first-frame pixel preflight rejected an empty, black, or flat render: " + error + ".");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(sample);
            Object.Destroy(readback);
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ReplayRenderHostV17));
    }

    private void Log(string operation, string detail)
    {
        AuraToolsLog.Info("[MatchRecords] replay render host " + operation
                          + ": phase=" + contract.Phase
                          + ", generation=" + contract.Generation
                          + ", size=" + contract.Size
                          + ", camera-enabled=" + camera.enabled.ToString().ToLowerInvariant()
                          + ", renderer-slot=" + rendererLease.RendererSlot
                          + ", " + detail + ".");
    }

    private static RenderTexture CreateInteractiveTarget(ReplayRenderSizeV17 size, int generation)
    {
        var target = new RenderTexture(
            size.Width,
            size.Height,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB)
        {
            name = "AuraToolsReplayInteractiveV17-g" + generation,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            useDynamicScale = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        if (!target.Create() || !target.IsCreated())
        {
            Object.Destroy(target);
            throw new InvalidOperationException("Unable to create the replay interactive RenderTexture.");
        }
        return target;
    }

    private static Camera CreateManualCamera(
        Transform parent,
        ReplaySceneDescriptorV17 scene,
        RenderTexture target,
        out ReplayUrpRendererIsolationApi.ReplayUrpRendererIsolationLease rendererLease)
    {
        var cameraObject = new GameObject("ReplayCamera");
        cameraObject.SetActive(false);
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.localPosition = new Vector3(
            ReplayPresentationPrimitivesV17.FromQ16(scene.CameraPosition.X),
            ReplayPresentationPrimitivesV17.FromQ16(scene.CameraPosition.Y),
            ReplayPresentationPrimitivesV17.FromQ16(scene.CameraPosition.Z));
        cameraObject.transform.localEulerAngles = new Vector3(
            ReplayPresentationPrimitivesV17.FromQ16(scene.CameraRotation.X),
            ReplayPresentationPrimitivesV17.FromQ16(scene.CameraRotation.Y),
            ReplayPresentationPrimitivesV17.FromQ16(scene.CameraRotation.Z));
        cameraObject.layer = ReplayLayer;
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = scene.CameraOrthographic;
        camera.orthographicSize = Math.Max(1f, ReplayPresentationPrimitivesV17.FromQ16(scene.CameraOrthographicSizeQ16));
        camera.fieldOfView = Math.Max(1f, ReplayPresentationPrimitivesV17.FromQ16(scene.CameraFieldOfViewQ16));
        camera.aspect = target.width / (float)Math.Max(1, target.height);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = ReplayPresentationPrimitivesV17.Color(scene.ClearColor);
        camera.depth = -1000f;
        camera.cullingMask = 1 << ReplayLayer;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.allowDynamicResolution = false;
        camera.useOcclusionCulling = false;
        camera.forceIntoRenderTexture = true;
        camera.targetTexture = target;
        var acquired = ReplayUrpRendererIsolationApi.Acquire(camera);
        try
        {
            cameraObject.SetActive(true);
            camera.enabled = false;
            acquired.Validate(camera);
            rendererLease = acquired;
            return camera;
        }
        catch (Exception activationFailure)
        {
            try { acquired.Dispose(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Replay camera activation and renderer cleanup both failed.",
                    activationFailure,
                    cleanupFailure);
            }
            throw;
        }
    }

    private static Canvas CreateCaptureCanvas(
        Transform parent,
        Camera camera,
        ReplaySceneDescriptorV17 scene)
    {
        var value = new GameObject("ReplayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        value.transform.SetParent(parent, false);
        value.layer = ReplayLayer;
        var canvas = value.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = 1000;
        var scaler = value.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(
            Math.Max(320, scene.ReferenceWidth),
            Math.Max(180, scene.ReferenceHeight));
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static (GameObject Root, Canvas Canvas, RawImage Image, AspectRatioFitter Aspect)
        CreateDisplaySurface(Transform parent, RenderTexture target, ReplayRenderSizeV17 size)
    {
        var root = new GameObject("ReplayDisplay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(parent, false);
        root.layer = 0;
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = DisplaySortingOrder;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        var backgroundObject = CreateFullScreenRect("Background", root.transform);
        var background = backgroundObject.AddComponent<Image>();
        background.color = Color.black;
        background.raycastTarget = false;

        var imageObject = CreateFullScreenRect("RenderTexture", root.transform);
        var image = imageObject.AddComponent<RawImage>();
        image.texture = target;
        image.color = Color.white;
        image.raycastTarget = false;
        var aspect = imageObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = size.Width / (float)size.Height;
        return (root, canvas, image, aspect);
    }

    private static GameObject CreateFullScreenRect(string name, Transform parent)
    {
        var value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        value.layer = 0;
        var rect = value.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        return value;
    }

    private static string TargetIdentity(RenderTexture target) =>
        target.name + "#" + target.GetInstanceID() + "@" + target.width + "x" + target.height;
}

internal sealed class ReplayRenderHostDestroySentinelV17 : MonoBehaviour
{
    private Action? destroy;

    internal void Bind(Action callback) => destroy = callback ?? throw new ArgumentNullException(nameof(callback));

    private void OnDestroy()
    {
        var callback = destroy;
        destroy = null;
        if (callback == null) return;
        try { callback(); }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[MatchRecords] replay render host sentinel cleanup failed", ex);
        }
    }
}

internal sealed class ReplayRenderExportLeaseV17 : IDisposable
{
    private ReplayRenderHostV17? owner;
    private readonly ReplayRenderLeaseTokenV17 token;
    private readonly RenderTexture target;

    internal ReplayRenderExportLeaseV17(
        ReplayRenderHostV17 owner,
        ReplayRenderLeaseTokenV17 token,
        RenderTexture target)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.token = token;
        this.target = target ?? throw new ArgumentNullException(nameof(target));
    }

    internal void Render()
    {
        var currentOwner = owner ?? throw new ObjectDisposedException(nameof(ReplayRenderExportLeaseV17));
        currentOwner.RenderExport(token, target);
    }

    public void Dispose()
    {
        var currentOwner = owner;
        if (currentOwner == null) return;
        owner = null;
        currentOwner.ReleaseExport(token);
    }
}
