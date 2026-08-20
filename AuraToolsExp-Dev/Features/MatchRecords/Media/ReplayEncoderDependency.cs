using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal sealed class ReplayEncoderManifest
{
    public int SchemaVersion { get; set; }

    public string Version { get; set; } = "";

    public string Platform { get; set; } = "";

    public string License { get; set; } = "";

    public string SourceRevision { get; set; } = "";

    public string SourceArchiveSha256 { get; set; } = "";

    public long SourceDateEpoch { get; set; }

    public string BuildProfile { get; set; } = "";

    public string CodecProfile { get; set; } = "";

    public List<ReplayEncoderFileManifest> Files { get; set; } = new();
}

internal sealed class ReplayEncoderFileManifest
{
    public string Path { get; set; } = "";

    public long Bytes { get; set; }

    public string Sha256 { get; set; } = "";
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
        var licensePath = Path.Combine(directory, "LICENSE.txt");
        var noticePath = Path.Combine(directory, "NOTICE.md");
        if (!File.Exists(manifestPath) || !File.Exists(licensePath) || !File.Exists(noticePath))
        {
            throw new FileNotFoundException("受控 FFmpeg 运行时不完整。请重新安装 AuraToolsExp。", manifestPath);
        }

        var manifest = AuraSharedJson.Deserialize<ReplayEncoderManifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidDataException("FFmpeg 运行时清单无法读取。");
        if (manifest.SchemaVersion != 2
            || !string.Equals(manifest.Version, "9.0.1-aura-minimal.1", StringComparison.Ordinal)
            || !string.Equals(manifest.Platform, "win-x64", StringComparison.Ordinal)
            || !string.Equals(manifest.License, "LGPL-3.0-or-later", StringComparison.Ordinal)
            || !string.Equals(manifest.BuildProfile, "aura-replay-win-x64-minimal-shared.v1", StringComparison.Ordinal)
            || !string.Equals(manifest.CodecProfile, MatchReplayVideoEncodingPolicy.CodecProfileId, StringComparison.Ordinal)
            || !string.Equals(manifest.SourceRevision, "9d4ca21220", StringComparison.Ordinal)
            || !string.Equals(
                manifest.SourceArchiveSha256,
                "6d9f8e49d1b6c561b6abb007fa872e844be181fb2516fd6e77741d0dd838bfa4",
                StringComparison.OrdinalIgnoreCase)
            || manifest.SourceDateEpoch != 1786990956)
        {
            throw new InvalidDataException("FFmpeg 运行时版本、平台、许可证或构建 profile 不匹配。");
        }

        var declared = new Dictionary<string, ReplayEncoderFileManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files ?? new List<ReplayEncoderFileManifest>())
        {
            var name = file.Path ?? "";
            if (string.IsNullOrWhiteSpace(name)
                || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
                || file.Bytes <= 0
                || string.IsNullOrWhiteSpace(file.Sha256)
                || declared.ContainsKey(name))
            {
                throw new InvalidDataException("FFmpeg 运行时文件清单包含非法或重复路径。");
            }
            declared.Add(name, file);
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)
                || new FileInfo(path).Length != file.Bytes
                || !string.Equals(Sha256(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("FFmpeg 运行时文件缺失、大小错误或哈希不匹配：" + name);
            }
        }

        var actualPayload = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (actualPayload.Count != declared.Count || actualPayload.Any(name => !declared.ContainsKey(name!))
            || !declared.ContainsKey("ffmpeg.exe") || !declared.ContainsKey("ffprobe.exe")
            || !declared.Keys.Any(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("FFmpeg 运行时文件与受控共享运行时清单不一致。");
        }

        var ffmpeg = Path.Combine(directory, "ffmpeg.exe");
        var ffprobe = Path.Combine(directory, "ffprobe.exe");

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
