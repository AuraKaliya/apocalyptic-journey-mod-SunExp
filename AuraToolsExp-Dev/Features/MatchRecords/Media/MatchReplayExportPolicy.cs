using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal readonly struct MatchReplayCaptureColorPolicy
{
    private MatchReplayCaptureColorPolicy(bool useSrgbRenderTarget, bool enableSrgbWrite)
    {
        UseSrgbRenderTarget = useSrgbRenderTarget;
        EnableSrgbWrite = enableSrgbWrite;
    }

    internal bool UseSrgbRenderTarget { get; }
    internal bool EnableSrgbWrite { get; }

    internal static MatchReplayCaptureColorPolicy PreserveDisplayPixels(bool sourceIsDataSrgb)
    {
        // ScreenCapture can return display-referred bytes in a texture marked
        // as linear. Matching the render target/write state to the source flag
        // preserves those bytes instead of applying a second gamma encoding.
        return new MatchReplayCaptureColorPolicy(sourceIsDataSrgb, sourceIsDataSrgb);
    }
}

internal readonly struct MatchReplayExportReadinessState
{
    internal MatchReplayExportReadinessState(
        bool replayReady,
        bool fightUiReady,
        int settingUiCount,
        int originOverlayCount)
    {
        ReplayReady = replayReady;
        FightUiReady = fightUiReady;
        SettingUiCount = Math.Max(0, settingUiCount);
        OriginOverlayCount = Math.Max(0, originOverlayCount);
    }

    internal bool ReplayReady { get; }
    internal bool FightUiReady { get; }
    internal int SettingUiCount { get; }
    internal int OriginOverlayCount { get; }

    internal bool CanCapture => ReplayReady
                                && FightUiReady
                                && SettingUiCount == 0
                                && OriginOverlayCount == 0;
}

/// <summary>
/// Maps the Unity DSP clock to a constant-rate video timeline. When rendering
/// misses a frame, the caller can duplicate the newest battle frame instead of
/// letting the audio and video durations diverge.
/// </summary>
internal sealed class MatchReplayExportFrameClock
{
    private readonly int framesPerSecond;
    private long emittedFrames;
    private double startedAt;
    private bool started;

    internal MatchReplayExportFrameClock(int framesPerSecond)
    {
        this.framesPerSecond = Math.Max(1, Math.Min(120, framesPerSecond));
    }

    internal void Start(double dspTime)
    {
        startedAt = Math.Max(0d, dspTime);
        emittedFrames = 0;
        started = true;
    }

    internal int DueFrames(double dspTime, int maximumCatchUpFrames = 8)
    {
        if (!started) throw new InvalidOperationException("The export clock has not started.");
        var elapsed = Math.Max(0d, dspTime - startedAt);
        var desiredFrames = (long)Math.Floor(elapsed * framesPerSecond + 0.000001d) + 1L;
        var due = Math.Max(0L, desiredFrames - emittedFrames);
        var result = (int)Math.Min(Math.Max(1, maximumCatchUpFrames), due);
        emittedFrames += result;
        return result;
    }

    internal double ElapsedMilliseconds(double dspTime)
    {
        if (!started) return 0d;
        return Math.Max(0d, dspTime - startedAt) * 1000d;
    }

    internal static long ExpectedPcmSampleFrames(int videoFrameCount, int framesPerSecond, int sampleRate)
    {
        if (videoFrameCount <= 0) return 0;
        var fps = Math.Max(1, framesPerSecond);
        var rate = Math.Max(1, sampleRate);
        return Math.Max(0L, (long)Math.Round(
            videoFrameCount / (double)fps * rate,
            MidpointRounding.AwayFromZero));
    }
}
