using AuraToolsExp.Dll.Features.MatchRecords.Media;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayExportPolicy()
    {
        var arguments = MatchReplayVideoEncodingPolicy.BuildFfmpegArguments(
            1280,
            720,
            30,
            wavePath: null,
            outputPath: "replay.partial.mp4");
        Assert(arguments.Contains("-f rawvideo", StringComparison.Ordinal)
               && arguments.Contains("-pixel_format rgb24", StringComparison.Ordinal)
               && arguments.Contains("-video_size 1280x720", StringComparison.Ordinal)
               && arguments.Contains("-c:v mpeg4", StringComparison.Ordinal)
               && arguments.Contains("format=yuv420p", StringComparison.Ordinal)
               && arguments.Contains("-color_range tv", StringComparison.Ordinal)
               && arguments.Contains("-colorspace bt709", StringComparison.Ordinal)
               && arguments.Contains("-f mp4", StringComparison.Ordinal)
               && !arguments.Contains("image2pipe", StringComparison.Ordinal)
               && !arguments.Contains("mjpeg", StringComparison.OrdinalIgnoreCase)
               && !arguments.Contains("avi", StringComparison.OrdinalIgnoreCase),
            "the only encoder path consumes raw bounded frames and emits the fixed MP4 profile");
        Assert(ReplayExportRecoveryPolicy.Resolve(MatchReplayExportStates.Planned, false, false)
               == ReplayExportRecoveryActions.ResumeRendering
               && ReplayExportRecoveryPolicy.Resolve(MatchReplayExportStates.Rendering, true, false)
               == ReplayExportRecoveryActions.FailAndDeletePartial
               && ReplayExportRecoveryPolicy.Resolve(MatchReplayExportStates.Encoding, true, false)
               == ReplayExportRecoveryActions.FailAndDeletePartial
               && ReplayExportRecoveryPolicy.Resolve(MatchReplayExportStates.Validating, true, false)
               == ReplayExportRecoveryActions.ValidatePartial
               && ReplayExportRecoveryPolicy.Resolve(MatchReplayExportStates.Committing, false, true)
               == ReplayExportRecoveryActions.ResumeCommit
               && ReplayExportRecoveryPolicy.Resolve(MatchReplayExportStates.Ready, false, true)
               == ReplayExportRecoveryActions.VerifyReady
               && ReplayExportRecoveryPolicy.Resolve(MatchReplayExportStates.Cancelled, true, false)
               == ReplayExportRecoveryActions.CleanupTerminal,
            "every persistent export state has one deterministic startup recovery action");

        var root = Path.Combine(Path.GetTempPath(), "AuraTools-ReplayAudio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wave = Path.Combine(root, "audio.partial.wav");
            var samples = ReplayOfflineAudioMixer.MixToWave(
                ReplayV10Document("audio-test"),
                videoFrameCount: 60,
                framesPerSecond: 30,
                _ => "",
                wave);
            Assert(samples == 96_000
                   && File.Exists(wave)
                   && new FileInfo(wave).Length == 44 + samples * 2 * 2,
                "offline audio length is derived exactly from the fixed video frame clock");

            var source = Path.Combine(root, "source-44100-mono.wav");
            WritePcmWave(source, 44_100, 1, 4410, 4096);
            var mixed = Path.Combine(root, "mixed.wav");
            var document = ReplayV10Document("audio-cue-test");
            document.Events[0].Audio.Add(new ReplayAudioCueV10
            {
                AssetSha256 = "audio-test",
                StartSample = 4800,
                DurationSamples = 4800,
                GainQ16 = 65_536,
                PlaybackRateQ16 = 65_536,
                Bus = "Effect"
            });
            ReplayOfflineAudioMixer.MixToWave(document, 30, 30, _ => source, mixed);
            Assert(File.ReadAllBytes(mixed).Skip(44).Any(value => value != 0),
                "offline mixer resamples recorded PCM cues into the fixed 48 kHz stereo timeline");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WritePcmWave(
        string path,
        int sampleRate,
        int channels,
        int sampleFrames,
        short sample)
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
        for (var index = 0; index < sampleFrames * channels; index++) writer.Write(sample);
    }
}
