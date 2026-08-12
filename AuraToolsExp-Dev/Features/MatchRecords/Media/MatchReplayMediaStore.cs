using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayMediaStore
{
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".mp4", ".mov", ".m4v", ".webm"
    };

    internal static MatchMediaAsset RegisterGenerated(
        string recordId,
        string path,
        long durationMilliseconds,
        int width,
        int height,
        double framesPerSecond,
        IReadOnlyList<MatchMediaTimelineEntry>? timeline)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("生成的视频文件不存在。", path);
        }

        var asset = new MatchMediaAsset
        {
            MediaId = Guid.NewGuid().ToString("N"),
            RecordId = recordId,
            Kind = "Video",
            Format = info.Extension.TrimStart('.').ToUpperInvariant(),
            FilePath = info.FullName,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            State = MatchMediaStates.Ready,
            DurationMilliseconds = Math.Max(0, durationMilliseconds),
            Width = Math.Max(0, width),
            Height = Math.Max(0, height),
            FramesPerSecond = Math.Max(0d, framesPerSecond),
            FileBytes = info.Length,
            Sha256 = Sha256(info.FullName),
            TimelineJson = AuraSharedJson.SerializeCompact(timeline ?? Array.Empty<MatchMediaTimelineEntry>())
        };
        MatchRecordStorage.Database.SaveMedia(asset);
        return asset;
    }

    internal static int ImportInbox(string recordId, out string message)
    {
        var files = Directory.GetFiles(MatchRecordStorage.ImportsDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedVideoExtensions.Contains(Path.GetExtension(path)))
            .ToList();
        var imported = 0;
        var failed = new List<string>();
        foreach (var source in files)
        {
            try
            {
                var directory = Path.Combine(MatchRecordStorage.MediaDirectory, recordId);
                Directory.CreateDirectory(directory);
                var target = UniquePath(Path.Combine(directory, Path.GetFileName(source)));
                File.Copy(source, target, overwrite: false);
                RegisterGenerated(recordId, target, 0, 0, 0, 0d, Array.Empty<MatchMediaTimelineEntry>());
                var completed = Path.Combine(MatchRecordStorage.ImportsDirectory, "Imported");
                Directory.CreateDirectory(completed);
                File.Move(source, UniquePath(Path.Combine(completed, Path.GetFileName(source))));
                imported++;
            }
            catch (Exception ex)
            {
                failed.Add(Path.GetFileName(source) + "：" + ex.Message);
            }
        }

        message = imported > 0 ? "已为本对局导入 " + imported + " 个视频。" : "导入目录中没有支持的视频。";
        if (failed.Count > 0)
        {
            message += " 失败 " + failed.Count + " 个。";
        }

        return imported;
    }

    internal static MatchMediaAsset ImportFile(string recordId, string source)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            throw new FileNotFoundException("视频文件不存在。", source);
        }

        if (!SupportedVideoExtensions.Contains(Path.GetExtension(source)))
        {
            throw new InvalidDataException("仅支持 mp4、avi、mov、m4v 和 webm 视频。");
        }

        var directory = Path.Combine(MatchRecordStorage.MediaDirectory, recordId);
        Directory.CreateDirectory(directory);
        var target = UniquePath(Path.Combine(directory, Path.GetFileName(source)));
        File.Copy(source, target, overwrite: false);
        return RegisterGenerated(recordId, target, 0, 0, 0, 0d, Array.Empty<MatchMediaTimelineEntry>());
    }

    internal static bool Delete(string mediaId)
    {
        var asset = MatchRecordStorage.Database.DeleteMedia(mediaId);
        if (asset == null)
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(asset.FilePath);
            var mediaRoot = Path.GetFullPath(MatchRecordStorage.MediaDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                File.Delete(fullPath);
                var wave = Path.ChangeExtension(fullPath, ".wav");
                if (File.Exists(wave)) File.Delete(wave);
            }
        }
        catch
        {
        }

        return true;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension);
    }
}
