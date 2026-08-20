using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal sealed class ReplayVideoVerification
{
    internal string Sha256 { get; set; } = "";
    internal long FileBytes { get; set; }
    internal long FrameCount { get; set; }
    internal long DurationMilliseconds { get; set; }
    internal bool HasAudio { get; set; }
    internal int Width { get; set; }
    internal int Height { get; set; }
    internal double FramesPerSecond { get; set; }
}

internal static class ReplayVideoVerifier
{
    internal static ReplayVideoVerification Verify(
        ReplayEncoderDependency dependency,
        string path,
        int expectedWidth,
        int expectedHeight,
        int expectedFps,
        long expectedFrames,
        bool expectedAudio)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            throw new InvalidDataException("编码器没有产生 MP4 文件。");
        }

        var probe = Run(dependency.FfprobeExecutable, MatchReplayVideoEncodingPolicy.BuildFfprobeArguments(path));
        if (probe.ExitCode != 0) throw new InvalidDataException("ffprobe 验证失败：" + probe.Error);
        var json = JObject.Parse(probe.Output);
        var streams = json["streams"] as JArray ?? new JArray();
        var video = streams.OfType<JObject>().SingleOrDefault(item => (string?)item["codec_type"] == "video")
                    ?? throw new InvalidDataException("MP4 缺少唯一视频流。");
        var audioStreams = streams.OfType<JObject>().Where(item => (string?)item["codec_type"] == "audio").ToList();
        if (streams.Count != 1 + audioStreams.Count)
        {
            throw new InvalidDataException("MP4 包含 profile 未声明的额外流。");
        }
        if ((int?)video["width"] != expectedWidth || (int?)video["height"] != expectedHeight)
        {
            throw new InvalidDataException("MP4 分辨率与导出 profile 不一致。");
        }
        if (!string.Equals((string?)video["codec_name"], "mpeg4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("MP4 视频 codec 与固定 profile 不一致。");
        }
        if (!string.Equals((string?)video["pix_fmt"], "yuv420p", StringComparison.OrdinalIgnoreCase)
            || !string.Equals((string?)video["color_range"], "tv", StringComparison.OrdinalIgnoreCase)
            || !string.Equals((string?)video["color_space"], "bt709", StringComparison.OrdinalIgnoreCase)
            || !string.Equals((string?)video["color_transfer"], "bt709", StringComparison.OrdinalIgnoreCase)
            || !string.Equals((string?)video["color_primaries"], "bt709", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("MP4 像素格式或 BT.709 limited-range 元数据与固定 profile 不一致。");
        }

        var fps = ParseRate((string?)video["r_frame_rate"]);
        if (Math.Abs(fps - expectedFps) > 0.01d) throw new InvalidDataException("MP4 帧率与导出 profile 不一致。");
        var frames = long.TryParse((string?)video["nb_read_frames"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
        if (frames <= 0 || Math.Abs(frames - expectedFrames) > 1)
        {
            throw new InvalidDataException("MP4 完整帧数与渲染任务不一致。");
        }

        if (expectedAudio != (audioStreams.Count == 1))
        {
            throw new InvalidDataException("MP4 音轨数量与导出 profile 不一致。");
        }
        if (expectedAudio)
        {
            var audio = audioStreams[0];
            if (!string.Equals((string?)audio["codec_name"], "aac", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("MP4 音频 codec 与固定 profile 不一致。");
            }
            var sampleRate = int.TryParse((string?)audio["sample_rate"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRate)
                ? parsedRate
                : 0;
            var channels = (int?)audio["channels"] ?? 0;
            var audioDuration = double.TryParse((string?)audio["duration"], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedAudioDuration)
                ? parsedAudioDuration
                : expectedFrames / (double)Math.Max(1, expectedFps);
            var expectedDuration = expectedFrames / (double)Math.Max(1, expectedFps);
            if (sampleRate != ReplayOfflineAudioMixer.SampleRate
                || channels != ReplayOfflineAudioMixer.Channels
                || Math.Abs(audioDuration - expectedDuration) > 0.12d)
            {
                throw new InvalidDataException("MP4 音频采样率、声道或时长与离线混音轨道不一致。");
            }
        }

        var decode = Run(dependency.FfmpegExecutable, MatchReplayVideoEncodingPolicy.BuildDecodeArguments(path));
        if (decode.ExitCode != 0) throw new InvalidDataException("MP4 完整解码失败：" + decode.Error);
        var durationSeconds = double.TryParse(
            (string?)json["format"]?["duration"],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsedDuration)
            ? parsedDuration
            : expectedFrames / (double)Math.Max(1, expectedFps);
        return new ReplayVideoVerification
        {
            Sha256 = Sha256(path),
            FileBytes = new FileInfo(path).Length,
            FrameCount = frames,
            DurationMilliseconds = (long)Math.Round(durationSeconds * 1000d),
            HasAudio = audioStreams.Count == 1,
            Width = expectedWidth,
            Height = expectedHeight,
            FramesPerSecond = fps
        };
    }

    internal static ReplayVideoVerification VerifyImported(ReplayEncoderDependency dependency, string path)
    {
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("只允许导入 MP4 文件。");
        }

        var probe = Run(dependency.FfprobeExecutable, MatchReplayVideoEncodingPolicy.BuildFfprobeArguments(path));
        if (probe.ExitCode != 0) throw new InvalidDataException("ffprobe 验证失败：" + probe.Error);
        var json = JObject.Parse(probe.Output);
        var streams = json["streams"] as JArray ?? new JArray();
        var videos = streams.OfType<JObject>().Where(item => (string?)item["codec_type"] == "video").ToList();
        var audios = streams.OfType<JObject>().Where(item => (string?)item["codec_type"] == "audio").ToList();
        if (videos.Count != 1 || audios.Count > 1 || streams.Count != videos.Count + audios.Count)
        {
            throw new InvalidDataException("MP4 必须包含一个视频流、至多一个音频流且不得包含额外流。");
        }
        var video = videos[0];
        var width = (int?)video["width"] ?? 0;
        var height = (int?)video["height"] ?? 0;
        var fps = ParseRate((string?)video["r_frame_rate"]);
        var frames = long.TryParse((string?)video["nb_read_frames"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
        if (width <= 0 || height <= 0 || fps <= 0d || frames <= 0)
        {
            throw new InvalidDataException("MP4 视频元数据不完整。");
        }

        var decode = Run(dependency.FfmpegExecutable, MatchReplayVideoEncodingPolicy.BuildDecodeArguments(path));
        if (decode.ExitCode != 0) throw new InvalidDataException("MP4 完整解码失败：" + decode.Error);
        var durationSeconds = double.TryParse(
            (string?)json["format"]?["duration"],
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsedDuration)
            ? parsedDuration
            : frames / fps;
        return new ReplayVideoVerification
        {
            Sha256 = Sha256(path),
            FileBytes = new FileInfo(path).Length,
            FrameCount = frames,
            DurationMilliseconds = (long)Math.Round(durationSeconds * 1000d),
            HasAudio = audios.Count == 1,
            Width = width,
            Height = height,
            FramesPerSecond = fps
        };
    }

    private static (int ExitCode, string Output, string Error) Run(string executable, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private static double ParseRate(string? value)
    {
        var parts = (value ?? "0/1").Split('/');
        return parts.Length == 2
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
               && Math.Abs(denominator) > double.Epsilon
            ? numerator / denominator
            : 0d;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream).Select(item => item.ToString("x2")));
    }
}
