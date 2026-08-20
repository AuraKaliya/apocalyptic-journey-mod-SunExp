using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class ReplayMediaSourcePolicy
{
    private static readonly HashSet<string> ManualExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4"
    };

    private static readonly HashSet<string> LegacyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".mkv", ".mov", ".mp4", ".webm"
    };

    private static readonly HashSet<string> Containers = new(StringComparer.OrdinalIgnoreCase)
    {
        "avi", "matroska", "mov", "mp4", "webm"
    };

    private static readonly HashSet<string> VideoCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "av1", "h264", "hevc", "mjpeg", "mpeg4", "vp8", "vp9"
    };

    private static readonly HashSet<string> AudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac", "alac", "flac", "mp3", "opus", "pcm_f32le", "pcm_s16le",
        "pcm_s24le", "pcm_s32le", "vorbis"
    };

    internal static IReadOnlyCollection<string> SupportedContainers => Containers;

    internal static IReadOnlyCollection<string> SupportedVideoCodecs => VideoCodecs;

    internal static IReadOnlyCollection<string> SupportedAudioCodecs => AudioCodecs;

    internal static void ValidateManualPath(string path)
    {
        ValidateExtension(path, ManualExtensions, "手动导入只接受 MP4 文件。");
    }

    internal static void ValidateLegacyPath(string path)
    {
        ValidateExtension(path, LegacyExtensions, "旧媒体迁移只接受 AVI、MKV、MOV、MP4 或 WebM 文件。");
    }

    internal static void ValidateProbe(string path, string formatNames, string videoCodec, string? audioCodec)
    {
        var reported = new HashSet<string>(
            (formatNames ?? "").Split(',').Select(item => item.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var container = reported.FirstOrDefault(Containers.Contains);
        if (string.IsNullOrWhiteSpace(container))
        {
            throw new InvalidDataException("视频容器不在受控导入白名单中。");
        }
        var extension = Path.GetExtension(path) ?? "";
        bool extensionMatches;
        if (string.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase))
        {
            extensionMatches = reported.Contains("avi");
        }
        else if (string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase))
        {
            extensionMatches = reported.Contains("matroska") || reported.Contains("webm");
        }
        else if (string.Equals(extension, ".mov", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            extensionMatches = reported.Contains("mov") || reported.Contains("mp4");
        }
        else
        {
            extensionMatches = false;
        }
        if (!extensionMatches)
        {
            throw new InvalidDataException("视频扩展名与探测到的受控容器不一致。");
        }
        if (!VideoCodecs.Contains(videoCodec ?? ""))
        {
            throw new InvalidDataException("视频 codec 不在受控导入白名单中：" + videoCodec);
        }
        if (!string.IsNullOrWhiteSpace(audioCodec) && !AudioCodecs.Contains(audioCodec ?? ""))
        {
            throw new InvalidDataException("音频 codec 不在受控导入白名单中：" + audioCodec);
        }
    }

    private static void ValidateExtension(string path, HashSet<string> allowed, string message)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("视频文件不存在。", path);
        }
        if (!allowed.Contains(Path.GetExtension(path) ?? ""))
        {
            throw new InvalidDataException(message);
        }
    }
}
