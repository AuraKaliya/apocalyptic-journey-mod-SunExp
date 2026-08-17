using AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayExportPolicy()
    {
        var blocked = new MatchReplayExportReadinessState(
            replayReady: true,
            fightUiReady: true,
            settingUiCount: 1,
            originOverlayCount: 0);
        var ready = new MatchReplayExportReadinessState(
            replayReady: true,
            fightUiReady: true,
            settingUiCount: 0,
            originOverlayCount: 0);
        Assert(!blocked.CanCapture && ready.CanCapture,
            "video capture waits until origin settings and overlays are actually gone");

        var clock = new MatchReplayExportFrameClock(30);
        clock.Start(10d);
        Assert(clock.DueFrames(10d) == 1,
            "DSP export clock emits a clean first frame at the audio epoch");
        Assert(clock.DueFrames(10d + 4d / 30d) == 4,
            "DSP export clock requests duplicate frames after a renderer stall to preserve duration");
        Assert(MatchReplayExportFrameClock.ExpectedPcmSampleFrames(900, 30, 48000) == 1_440_000,
            "PCM normalization derives its exact sample count from the final video clock");

        var displayReferred = MatchReplayCaptureColorPolicy.PreserveDisplayPixels(sourceIsDataSrgb: false);
        Assert(!displayReferred.UseSrgbRenderTarget && !displayReferred.EnableSrgbWrite,
            "display-referred screenshot bytes are copied through a linear target without a second gamma encode");
        var srgbTexture = MatchReplayCaptureColorPolicy.PreserveDisplayPixels(sourceIsDataSrgb: true);
        Assert(srgbTexture.UseSrgbRenderTarget && srgbTexture.EnableSrgbWrite,
            "sRGB screenshots keep matching source, target, and write conversion semantics");

        var encodingArguments = MatchReplayVideoEncodingPolicy.BuildFfmpegArguments(
            30,
            wavePath: null,
            outputPath: "replay.mp4");
        Assert(encodingArguments.Contains("scale=in_range=pc:out_range=tv:out_color_matrix=bt709", StringComparison.Ordinal)
               && encodingArguments.Contains("format=yuv420p", StringComparison.Ordinal)
               && encodingArguments.Contains("-color_range tv", StringComparison.Ordinal)
               && encodingArguments.Contains("-colorspace bt709", StringComparison.Ordinal)
               && encodingArguments.Contains("-color_primaries bt709", StringComparison.Ordinal)
               && encodingArguments.Contains("-color_trc bt709", StringComparison.Ordinal)
               && encodingArguments.Contains("colorprim=bt709:transfer=bt709:colormatrix=bt709:fullrange=off", StringComparison.Ordinal),
            "MP4 encoding normalizes JPEG frames to limited-range BT.709 instead of leaving ambiguous full-range metadata");
    }

    private static void WriteTestPcmWave(string path, int sampleRate, int channels, int sampleFrames)
    {
        var dataBytes = sampleFrames * channels * 2;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
    }
}
