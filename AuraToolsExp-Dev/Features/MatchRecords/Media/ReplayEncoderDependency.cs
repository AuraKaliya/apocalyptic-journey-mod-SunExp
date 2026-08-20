using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal sealed class ReplayEncoderManifest
{
    public string Version { get; set; } = "";

    public string License { get; set; } = "";

    public string FfmpegSha256 { get; set; } = "";

    public string FfprobeSha256 { get; set; } = "";

    public string CodecProfile { get; set; } = "";
}

internal sealed class ReplayEncoderDependency
{
    internal string FfmpegExecutable { get; private set; } = "";

    internal string FfprobeExecutable { get; private set; } = "";

    internal ReplayEncoderManifest Manifest { get; private set; } = new();

    internal static ReplayEncoderDependency LoadVerified()
    {
        var directory = Path.Combine(AuraToolsConfigService.ModDirectory, "Runtime", "ffmpeg", "win-x64");
        var manifestPath = Path.Combine(directory, "manifest.json");
        var ffmpeg = Path.Combine(directory, "ffmpeg.exe");
        var ffprobe = Path.Combine(directory, "ffprobe.exe");
        if (!File.Exists(manifestPath) || !File.Exists(ffmpeg) || !File.Exists(ffprobe))
        {
            throw new FileNotFoundException("受控 FFmpeg 运行时不完整。请重新安装 AuraToolsExp。", manifestPath);
        }

        var manifest = AuraSharedJson.Deserialize<ReplayEncoderManifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException("FFmpeg 运行时清单无法读取。");
        if (!string.Equals(manifest.License, "LGPL-3.0-or-later", StringComparison.Ordinal)
            || !string.Equals(manifest.CodecProfile, MatchReplayVideoEncodingPolicy.CodecProfileId, StringComparison.Ordinal)
            || !string.Equals(Sha256(ffmpeg), manifest.FfmpegSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Sha256(ffprobe), manifest.FfprobeSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("FFmpeg 运行时版本、许可证、编码 profile 或文件哈希不匹配。");
        }

        return new ReplayEncoderDependency
        {
            FfmpegExecutable = ffmpeg,
            FfprobeExecutable = ffprobe,
            Manifest = manifest
        };
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream).Select(item => item.ToString("x2")));
    }
}
