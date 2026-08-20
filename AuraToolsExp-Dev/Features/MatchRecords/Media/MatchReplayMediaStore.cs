using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayMediaStore
{
    internal static int ImportInbox(string recordId, out string message)
    {
        var files = Directory.GetFiles(MatchRecordStorage.ImportsDirectory, "*.mp4", SearchOption.TopDirectoryOnly)
            .ToList();
        var imported = 0;
        var failures = new List<string>();
        foreach (var source in files)
        {
            try
            {
                ImportFile(recordId, source);
                var completed = Path.Combine(MatchRecordStorage.ImportsDirectory, "Imported");
                Directory.CreateDirectory(completed);
                File.Move(source, UniquePath(Path.Combine(completed, Path.GetFileName(source))));
                imported++;
            }
            catch (Exception ex)
            {
                failures.Add(Path.GetFileName(source) + "：" + ex.Message);
            }
        }

        message = imported > 0 ? "已验证并导入 " + imported + " 个 MP4。" : "导入目录中没有 MP4。";
        if (failures.Count > 0) message += " 失败 " + failures.Count + " 个。";
        return imported;
    }

    internal static MatchMediaAsset ImportFile(string recordId, string source)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            throw new FileNotFoundException("视频文件不存在。", source);
        }

        if (!string.Equals(Path.GetExtension(source), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("媒体库只接受 MP4；旧格式由 v8/v9 迁移器统一转码。");
        }

        var dependency = ReplayEncoderDependency.LoadVerified();
        var directory = Path.Combine(MatchRecordStorage.MediaDirectory, recordId);
        Directory.CreateDirectory(directory);
        var jobId = Guid.NewGuid().ToString("N");
        var target = Path.Combine(directory, jobId + ".mp4");
        var staging = target + ".partial.mp4";
        var now = DateTime.UtcNow.ToString("O");
        var job = new MatchReplayExportJob
        {
            JobId = jobId,
            RecordId = recordId,
            State = MatchReplayExportStates.Planned,
            CreatedUtc = now,
            UpdatedUtc = now,
            ProfileId = "imported-mp4.v1",
            StagingPath = staging,
            TargetPath = target,
            Message = "正在验证导入 MP4"
        };
        MatchRecordStorage.Database.CreateExportJob(job);
        try
        {
            File.Copy(source, staging, overwrite: false);
            var verification = ReplayVideoVerifier.VerifyImported(dependency, staging);
            job.Width = verification.Width;
            job.Height = verification.Height;
            job.FramesPerSecond = (int)Math.Round(verification.FramesPerSecond);
            job.FrameCount = verification.FrameCount;
            job.FileBytes = verification.FileBytes;
            job.OutputSha256 = verification.Sha256;
            job.AudioSampleFrames = verification.HasAudio ? 1 : 0;
            job.State = MatchReplayExportStates.Validating;
            job.Progress = 0.9f;
            job.Message = "MP4 已完整解码验证";
            if (!MatchRecordStorage.Database.UpdateExportJob(job))
            {
                throw new IOException("导入验证状态发生并发冲突。");
            }
            job.State = MatchReplayExportStates.Committing;
            job.Progress = 0.96f;
            job.Message = "正在提交已验证导入 MP4";
            if (!MatchRecordStorage.Database.UpdateExportJob(job))
            {
                throw new IOException("导入任务状态发生并发冲突。");
            }
            File.Move(staging, target);
            var asset = new MatchMediaAsset
            {
                MediaId = job.JobId,
                RecordId = recordId,
                Kind = "Video",
                Format = "MP4",
                FilePath = ToStoredPath(target),
                CreatedUtc = now,
                State = MatchMediaStates.Ready,
                DurationMilliseconds = verification.DurationMilliseconds,
                Width = verification.Width,
                Height = verification.Height,
                FramesPerSecond = verification.FramesPerSecond,
                FileBytes = verification.FileBytes,
                Sha256 = verification.Sha256,
                TimelineJson = "[]"
            };
            job.Message = "MP4 已验证并导入";
            if (!MatchRecordStorage.Database.CommitExportMedia(job, asset))
            {
                throw new IOException("导入 MP4 的数据库提交失败；启动恢复将继续登记。");
            }
            return asset;
        }
        catch
        {
            if (job.State != MatchReplayExportStates.Committing)
            {
                TryDelete(staging);
                job.State = MatchReplayExportStates.Failed;
                job.ErrorCode = "import-failed";
                job.Message = "MP4 导入失败";
                MatchRecordStorage.Database.UpdateExportJob(job);
            }
            throw;
        }
    }

    internal static bool Delete(string mediaId)
    {
        var assets = MatchRecordStorage.Database.LoadMediaForDeletion(mediaId);
        if (assets == null) return false;
        var fullPath = ResolvePath(assets.FilePath);
        var staged = fullPath + ".delete.partial";
        var moved = false;
        try
        {
            if (File.Exists(fullPath))
            {
                if (File.Exists(staged)) File.Delete(staged);
                File.Move(fullPath, staged);
                moved = true;
            }
            var deleted = MatchRecordStorage.Database.DeleteMedia(mediaId) != null;
            if (!deleted)
            {
                if (moved) File.Move(staged, fullPath);
                return false;
            }
            TryDelete(staged);
            return true;
        }
        catch
        {
            if (moved && File.Exists(staged) && !File.Exists(fullPath))
            {
                try { File.Move(staged, fullPath); } catch { }
            }
            throw;
        }
    }

    internal static string ResolvePath(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return "";
        if (Path.IsPathRooted(storedPath)) return Path.GetFullPath(storedPath);
        var root = Path.GetFullPath(MatchRecordStorage.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, storedPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("媒体相对路径超出对局记录目录。");
        }
        return resolved;
    }

    internal static string ToStoredPath(string fullPath)
    {
        var root = Path.GetFullPath(MatchRecordStorage.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(fullPath);
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("媒体路径超出对局记录目录。");
        }
        return resolved.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        return Path.Combine(
            Path.GetDirectoryName(path) ?? ".",
            Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(path));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
