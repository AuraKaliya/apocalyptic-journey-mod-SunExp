using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayRenderHostContract()
    {
        TestPixelPreflight();
        TestRendererFeaturePolicy();
        var initialSize = ReplayRenderSizePolicyV17.Resolve(1920, 1080, 1920, 1080);
        using var host = new ReplayRenderHostContractV17(initialSize);
        Assert(host.Phase == ReplayRenderHostPhaseV17.Prepared
               && host.CanRenderPreflight
               && !host.CanRenderInteractive
               && host.Generation == 1,
            "replay render ownership begins hidden and permits only explicit preflight rendering");

        host.MarkPreflightSucceeded();
        Assert(host.Phase == ReplayRenderHostPhaseV17.Preflighted
               && !host.CanRenderPreflight
               && host.CanConfirmFrameBarrier
               && !host.CanRenderInteractive,
            "successful preflight remains hidden until the launch coordinator commits activation");
        var earlyActivationRejected = false;
        try { host.Activate(); }
        catch (InvalidOperationException) { earlyActivationRejected = true; }
        Assert(earlyActivationRejected,
            "manual camera preflight alone cannot commit replay before the normal game render frame survives");
        host.ConfirmFrameBarrier();
        Assert(host.Phase == ReplayRenderHostPhaseV17.FrameBarrierConfirmed
               && !host.CanConfirmFrameBarrier
               && !host.CanRenderInteractive,
            "replay activation waits until the game's normal render loop survives the preflight frame");
        host.Activate();
        Assert(host.Phase == ReplayRenderHostPhaseV17.Active && host.CanRenderInteractive,
            "activation opens the interactive render path after preflight");

        var export = host.AcquireExport();
        Assert(host.HasExportLease
               && !host.CanRenderInteractive
               && host.CanRenderExport(export),
            "an export lease has exclusive ownership of the replay camera target");
        var duplicateAcquireRejected = false;
        try { host.AcquireExport(); }
        catch (InvalidOperationException) { duplicateAcquireRejected = true; }
        Assert(duplicateAcquireRejected,
            "a second export cannot overwrite the active render-target owner");
        var resizeDuringExportRejected = false;
        try { host.Resize(new ReplayRenderSizeV17(1280, 720)); }
        catch (InvalidOperationException) { resizeDuringExportRejected = true; }
        Assert(resizeDuringExportRejected,
            "interactive resize cannot invalidate a target while export owns the camera");

        Assert(host.ReleaseExport(export) == ReplayRenderLeaseReleaseV17.Released
               && host.CanRenderInteractive,
            "releasing export restores the interactive render path exactly once");
        Assert(host.ReleaseExport(export) == ReplayRenderLeaseReleaseV17.Duplicate,
            "duplicate export release is recognized without changing ownership");

        var secondExport = host.AcquireExport();
        Assert(host.ReleaseExport(export) == ReplayRenderLeaseReleaseV17.ForeignLease
               && host.CanRenderExport(secondExport),
            "an out-of-order old token cannot release a newer export lease");
        Assert(host.ReleaseExport(secondExport) == ReplayRenderLeaseReleaseV17.Released,
            "the current export token remains releasable after a rejected old token");

        var generationBeforeResize = host.Generation;
        Assert(host.Resize(new ReplayRenderSizeV17(1280, 720))
               && host.Generation == generationBeforeResize + 1
               && !host.Resize(new ReplayRenderSizeV17(1280, 720)),
            "interactive target replacement advances one generation only when dimensions change");
        Assert(host.ReleaseExport(secondExport) == ReplayRenderLeaseReleaseV17.StaleGeneration,
            "a token from a replaced render-target generation cannot mutate current ownership");

        using var foreignHost = new ReplayRenderHostContractV17(initialSize);
        foreignHost.MarkPreflightSucceeded();
        foreignHost.ConfirmFrameBarrier();
        foreignHost.Activate();
        var foreignToken = foreignHost.AcquireExport();
        Assert(host.ReleaseExport(foreignToken) == ReplayRenderLeaseReleaseV17.ForeignLease,
            "a lease token cannot cross replay render-host ownership boundaries");
        Assert(foreignHost.ReleaseExport(foreignToken) == ReplayRenderLeaseReleaseV17.Released,
            "the foreign host retains ownership after cross-host release rejection");

        var wide = ReplayRenderSizePolicyV17.Resolve(3440, 1440, 1920, 1080);
        var portrait = ReplayRenderSizePolicyV17.Resolve(900, 1600, 1080, 1920);
        var fallback = ReplayRenderSizePolicyV17.Resolve(0, 0, 0, 0);
        Assert(wide.Width == 2560 && wide.Height == 1440
               && portrait.Width % 2 == 0 && portrait.Height % 2 == 0
               && portrait.Width <= ReplayRenderSizePolicyV17.MaximumWidth
               && portrait.Height <= ReplayRenderSizePolicyV17.MaximumHeight
               && fallback.Width == 1920 && fallback.Height == 1080,
            "render-size policy preserves source shape within bounded even-sized targets and has a deterministic fallback");

        host.Dispose();
        host.Dispose();
        Assert(host.Phase == ReplayRenderHostPhaseV17.Disposed
               && !host.CanRenderInteractive
               && host.ReleaseExport(secondExport) == ReplayRenderLeaseReleaseV17.HostDisposed,
            "render-host teardown is idempotent and permanently invalidates prior leases");

        var rendererOwner = new ReplayRendererIsolationContractV17();
        var rendererLease = rendererOwner.Acquire(101);
        Assert(rendererOwner.HasActiveCamera && rendererOwner.Validate(rendererLease, 101),
            "a dedicated replay renderer binds exactly one camera token");
        var secondCameraRejected = false;
        try { rendererOwner.Acquire(202); }
        catch (InvalidOperationException) { secondCameraRejected = true; }
        Assert(secondCameraRejected && rendererOwner.Validate(rendererLease, 101),
            "a second camera cannot share or replace the active dedicated renderer owner");
        var otherRendererOwner = new ReplayRendererIsolationContractV17();
        var foreignRendererLease = otherRendererOwner.Acquire(303);
        Assert(rendererOwner.Release(foreignRendererLease) == ReplayRendererCameraReleaseV17.ForeignLease
               && rendererOwner.Validate(rendererLease, 101),
            "a token from another renderer slot cannot release the active camera");
        Assert(rendererOwner.Release(rendererLease) == ReplayRendererCameraReleaseV17.Released
               && rendererOwner.Release(rendererLease) == ReplayRendererCameraReleaseV17.Duplicate
               && !rendererOwner.HasActiveCamera,
            "dedicated renderer camera release is exact and duplicate-safe");
        Assert(otherRendererOwner.Release(foreignRendererLease) == ReplayRendererCameraReleaseV17.Released,
            "a rejected foreign release does not damage its real renderer owner");
    }

    private static void TestPixelPreflight()
    {
        Assert(ReplayRenderPixelContractV17.Validate(Enumerable.Range(0, 64)
                   .Select(_ => new ReplayRgbaSampleV17(0, 0, 0, 255)).ToArray())
                   .StartsWith("pixel-black", StringComparison.Ordinal),
            "first-frame preflight rejects an opaque black render instead of treating Camera.Render as success");
        var valid = Enumerable.Range(0, 64)
            .Select(index => index < 16
                ? new ReplayRgbaSampleV17(240, 210, 180, 255)
                : new ReplayRgbaSampleV17(8, 10, 18, 255))
            .ToArray();
        Assert(ReplayRenderPixelContractV17.Validate(valid).Length == 0,
            "first-frame preflight accepts a dark battle scene that still contains visible native HUD pixels");
    }

    private static void TestRendererFeaturePolicy()
    {
        var fullScreen = ReplayRendererFeaturePolicyV17.Decide(
            ReplayRendererFeaturePolicyV17.FullScreenPassRendererFeature,
            sourceActive: true);
        Assert(fullScreen.Disposition == ReplayRendererFeatureDispositionV17.RetainOwnedClone
               && fullScreen.RequiresIntermediateColor
               && fullScreen.Reason == "render-graph-full-screen-pass-with-owned-intermediate-color",
            "the native full-screen pass is retained only as an owned clone with an explicit intermediate-color contract");

        var blur = ReplayRendererFeaturePolicyV17.Decide(
            ReplayRendererFeaturePolicyV17.UiBlurGrabPassFeature,
            sourceActive: true);
        Assert(blur.Disposition == ReplayRendererFeatureDispositionV17.ExcludeFromReplay
               && !blur.RequiresIntermediateColor
               && blur.Reason == "main-camera-ui-blur-pass-has-no-render-graph-implementation",
            "the compatibility-only main-camera blur pass is explicitly excluded from RenderGraph replay");

        var unknown = ReplayRendererFeaturePolicyV17.Decide("Example.UnknownRendererFeature", sourceActive: true);
        var inactive = ReplayRendererFeaturePolicyV17.Decide("Example.UnknownRendererFeature", sourceActive: false);
        Assert(unknown.Disposition == ReplayRendererFeatureDispositionV17.RejectProfile
               && unknown.Reason == "unknown-active-renderer-feature"
               && inactive.Disposition == ReplayRendererFeatureDispositionV17.ExcludeFromReplay
               && inactive.Reason == "source-feature-inactive",
            "an unknown active renderer feature fails closed while an inactive source feature is harmlessly omitted");
    }
}
