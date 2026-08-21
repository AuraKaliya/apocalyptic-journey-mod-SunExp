using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Presentation;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayVideoExporter
{
    private static readonly Queue<MatchReplayExportJob> RecoveryQueue = new();
    private static MatchReplayExportJob? current;
    private static bool workerRunning;

    internal static MatchReplayExportJob? Current => current;

    internal static void Initialize()
    {
        RecoveryQueue.Clear();
        foreach (var job in MatchRecordStorage.Database.LoadRecoverableExportJobs()) RecoveryQueue.Enqueue(job);
        current = MatchRecordStorage.Database.LoadLatestExportJob();
        ReconcileOrphanPartials();
        ResumePending();
    }

    private static void ResumePending()
    {
        MatchReplayExportControlsPresenter.Refresh(current);
        if (workerRunning) return;
        if (RecoveryQueue.Count == 0)
        {
            AuraToolsMatchRecordsRuntime.ReleaseRuntimeDriver();
            return;
        }
        var job = RecoveryQueue.Dequeue();
        current = job;
        MatchReplayExportControlsPresenter.Show();
        workerRunning = true;
        var routine = job.State == MatchReplayExportStates.Planned
                      && !string.Equals(
                          job.ProfileId,
                          MatchReplayVideoEncodingPolicy.ImportedCodecProfileId,
                          StringComparison.Ordinal)
            ? Export(job, SettingsFromJob(job))
            : Recover(job);
        if (AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(routine) == null)
        {
            workerRunning = false;
            Fail(job, "worker-unavailable", "无法启动回放导出恢复任务。", deleteStaging: true);
            ResumePending();
        }
    }

    internal static bool TryStart(string recordId, Action closeOrigin, out string message)
    {
        if (workerRunning || current != null && !IsTerminal(current.State))
        {
            message = "已有视频导出任务正在运行。";
            return false;
        }

        ReplayEncoderDependency dependency;
        ReplayDocumentV10? document;
        try
        {
            dependency = ReplayEncoderDependency.LoadVerified();
            document = MatchRecordStorage.Database.LoadV10(recordId);
        }
        catch (Exception ex)
        {
            message = "无法开始视频导出：" + ex.Message;
            return false;
        }

        if (document == null)
        {
            message = "这条记录没有经过验证的 Replay Document v10。";
            return false;
        }

        var settings = SnapshotSettings();
        var dimensions = Dimensions(settings.Quality);
        var now = DateTime.UtcNow.ToString("O");
        var jobId = Guid.NewGuid().ToString("N");
        var mediaDirectory = Path.Combine(MatchRecordStorage.MediaDirectory, recordId);
        Directory.CreateDirectory(mediaDirectory);
        var target = Path.Combine(mediaDirectory, jobId + ".mp4");
        var job = new MatchReplayExportJob
        {
            JobId = jobId,
            RecordId = recordId,
            State = MatchReplayExportStates.Planned,
            CreatedUtc = now,
            UpdatedUtc = now,
            ProfileId = ProfileId(settings),
            Width = dimensions.width,
            Height = dimensions.height,
            FramesPerSecond = settings.FramesPerSecond,
            StagingPath = target + ".partial.mp4",
            TargetPath = target,
            Message = "任务已计划",
            EstimatedBytes = EstimateOutputBytes(
                document,
                dimensions.width,
                dimensions.height,
                settings.FramesPerSecond,
                settings.IncludeAudio)
        };
        try
        {
            MatchRecordStorage.Database.CreateExportJob(job);
            closeOrigin?.Invoke();
            current = job;
            workerRunning = true;
            MatchReplayExportControlsPresenter.Show();
            if (AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(Export(job, settings, dependency, document)) == null)
            {
                workerRunning = false;
                Fail(job, "worker-unavailable", "无法启动视频导出协程。", deleteStaging: true);
                message = job.Message;
                return false;
            }

            message = "已创建持久化 MP4 导出任务。";
            return true;
        }
        catch (Exception ex)
        {
            workerRunning = false;
            message = "无法创建视频导出任务：" + ex.Message;
            return false;
        }
    }

    internal static void CancelOrDismiss()
    {
        if (current == null || IsTerminal(current.State))
        {
            current = null;
            MatchReplayExportControlsPresenter.Close();
            return;
        }

        if (current.State == MatchReplayExportStates.Committing)
        {
            current.Message = "正在提交已验证文件，此阶段不能取消";
            Persist(current);
            return;
        }

        current.CancelRequested = true;
        current.Message = "正在取消";
        Persist(current);
    }

    private static IEnumerator Export(
        MatchReplayExportJob job,
        MatchReplayVideoSettings settings,
        ReplayEncoderDependency? dependency = null,
        ReplayDocumentV10? document = null)
    {
        Exception? failure = null;
        var core = ExportCore(job, settings, dependency, document);
        while (failure == null)
        {
            bool moved;
            try { moved = core.MoveNext(); }
            catch (Exception ex) { failure = ex; break; }
            if (!moved) break;
            yield return core.Current;
        }
        (core as IDisposable)?.Dispose();
        if (failure is OperationCanceledException)
        {
            job.CancelRequested = true;
            Transition(job, MatchReplayExportStates.Cancelled, job.Progress, "导出任务已取消");
            DeleteIfExists(job.StagingPath);
        }
        else if (failure != null)
        {
            var committing = job.State == MatchReplayExportStates.Committing;
            Fail(job, committing ? "commit-interrupted" : "export-failed", failure.Message, deleteStaging: !committing);
            AuraToolsLog.Warn("[MatchRecords] v10 video export failed: " + failure);
        }
        workerRunning = false;
        ResumePending();
    }

    private static IEnumerator ExportCore(
        MatchReplayExportJob job,
        MatchReplayVideoSettings settings,
        ReplayEncoderDependency? dependency,
        ReplayDocumentV10? document)
    {
        ReplaySceneInstance? scene = null;
        RenderTexture? target = null;
        Texture2D? reader = null;
        ReplayFramePipeline? pipeline = null;
        var audioPath = job.TargetPath + ".audio.partial.wav";
        try
        {
            dependency ??= ReplayEncoderDependency.LoadVerified();
            document ??= MatchRecordStorage.Database.LoadV10(job.RecordId)
                         ?? throw new InvalidDataException("找不到经过验证的 Replay Document v10。");
            job.AttemptCount++;
            DeleteIfExists(job.StagingPath);
            DeleteIfExists(audioPath);
            var frameCount = Math.Max(1L, (long)Math.Ceiling(
                document.Events.Count == 0
                    ? settings.FramesPerSecond
                    : (document.Events.Max(item => item.TimeTicks
                        + Math.Max(160_000L, item.Presentation.Count == 0
                            ? 160_000L
                            : item.Presentation.Max(cue => cue.StartOffsetTicks + cue.DurationTicks)))
                       / (double)ReplayProtocolV10.TimebaseTicksPerSecond * settings.FramesPerSecond)) + 1L);
            job.FrameCount = frameCount;
            if (!HasFreeSpace(job.EstimatedBytes, out var available))
            {
                throw new IOException("预计需要 " + FormatBytes(job.EstimatedBytes)
                                      + "，媒体目录仅剩 " + FormatBytes(available) + "。");
            }

            var audioSampleFrames = settings.IncludeAudio
                ? ReplayOfflineAudioMixer.MixToWave(
                    document,
                    frameCount,
                    settings.FramesPerSecond,
                    MatchRecordStorage.Database.ResolveReplayAsset,
                    audioPath)
                : 0L;
            job.AudioSampleFrames = audioSampleFrames;
            Transition(job, MatchReplayExportStates.Rendering, 0.02f,
                "固定时间步渲染 " + job.Width + "x" + job.Height + " / " + job.FramesPerSecond + " FPS");
            target = new RenderTexture(job.Width, job.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            reader = new Texture2D(job.Width, job.Height, TextureFormat.RGB24, mipChain: false);
            scene = ReplaySceneRuntime.CreateExportSession(document, target, settings.IncludeUi);
            pipeline = new ReplayFramePipeline(
                dependency,
                job.StagingPath,
                job.Width,
                job.Height,
                job.FramesPerSecond,
                settings.IncludeAudio ? audioPath : null);
            for (var frameIndex = 0L; frameIndex < frameCount; frameIndex++)
            {
                if (job.CancelRequested) throw new OperationCanceledException();
                var timeTicks = frameIndex * ReplayProtocolV10.TimebaseTicksPerSecond / job.FramesPerSecond;
                scene.SeekTime(timeTicks);
                scene.Camera.Render();
                var frame = CaptureFrame(target, reader, job.Width, job.Height);
                while (!pipeline.TryEnqueue(frame))
                {
                    if (job.CancelRequested) throw new OperationCanceledException();
                    if (pipeline.Completion.IsFaulted)
                    {
                        throw pipeline.Completion.Exception?.GetBaseException()
                              ?? new IOException("FFmpeg 编码管道失败。");
                    }
                    yield return null;
                }

                if (frameIndex % Math.Max(1, job.FramesPerSecond) == 0)
                {
                    job.Progress = 0.02f + 0.73f * (frameIndex + 1f) / frameCount;
                    job.Message = "已渲染 " + (frameIndex + 1) + "/" + frameCount + " 帧";
                    Persist(job);
                }
                yield return null;
            }

            pipeline.Complete();
            Transition(job, MatchReplayExportStates.Encoding, 0.78f, "受控 FFmpeg 正在完成 MP4 编码");
            while (!pipeline.Completion.IsCompleted)
            {
                if (job.CancelRequested) pipeline.Cancel();
                yield return null;
            }
            if (pipeline.Completion.IsFaulted)
            {
                throw pipeline.Completion.Exception?.GetBaseException() ?? new IOException("FFmpeg MP4 编码失败。");
            }
            if (job.CancelRequested) throw new OperationCanceledException();

            Transition(job, MatchReplayExportStates.Validating, 0.86f, "正在检查容器并完整解码所有音视频");
            var verifyTask = Task.Run(() => ReplayVideoVerifier.Verify(
                dependency,
                job.StagingPath,
                job.Width,
                job.Height,
                job.FramesPerSecond,
                frameCount,
                settings.IncludeAudio));
            while (!verifyTask.IsCompleted) yield return null;
            if (verifyTask.IsFaulted)
            {
                throw verifyTask.Exception?.GetBaseException() ?? new InvalidDataException("MP4 验证失败。");
            }

            var verification = verifyTask.Result;
            job.OutputSha256 = verification.Sha256;
            job.FileBytes = verification.FileBytes;
            job.FrameCount = verification.FrameCount;
            Transition(job, MatchReplayExportStates.Committing, 0.96f, "正在原子提交已验证 MP4");
            if (File.Exists(job.TargetPath)) throw new IOException("MP4 目标文件已经存在。");
            File.Move(job.StagingPath, job.TargetPath);
            var asset = new MatchMediaAsset
            {
                MediaId = job.JobId,
                RecordId = job.RecordId,
                Kind = "Video",
                Format = "MP4",
                FilePath = MatchReplayMediaStore.ToStoredPath(job.TargetPath),
                CreatedUtc = DateTime.UtcNow.ToString("O"),
                State = MatchMediaStates.Ready,
                DurationMilliseconds = verification.DurationMilliseconds,
                Width = job.Width,
                Height = job.Height,
                FramesPerSecond = job.FramesPerSecond,
                FileBytes = verification.FileBytes,
                Sha256 = verification.Sha256,
                TimelineJson = AuraSharedJson.SerializeCompact(BuildTimeline(document, job.FramesPerSecond))
            };
            job.Message = "已保存并验证 " + Path.GetFileName(job.TargetPath);
            if (!MatchRecordStorage.Database.CommitExportMedia(job, asset))
            {
                throw new IOException("已生成 MP4，但数据库提交发生并发冲突；启动恢复将继续登记。");
            }
            current = job;
        }
        finally
        {
            pipeline?.Dispose();
            scene?.Dispose();
            if (target != null)
            {
                target.Release();
                Object.Destroy(target);
            }
            if (reader != null) Object.Destroy(reader);
            DeleteIfExists(audioPath);
        }
    }

    private static IEnumerator Recover(MatchReplayExportJob job)
    {
        Exception? failure = null;
        var core = RecoverCore(job);
        while (failure == null)
        {
            bool moved;
            try { moved = core.MoveNext(); }
            catch (Exception ex) { failure = ex; break; }
            if (!moved) break;
            yield return core.Current;
        }
        (core as IDisposable)?.Dispose();
        if (failure != null)
        {
            Fail(job, "recovery-failed", failure.Message, deleteStaging: job.State != MatchReplayExportStates.Committing);
        }
        workerRunning = false;
        ResumePending();
    }

    private static IEnumerator RecoverCore(MatchReplayExportJob job)
    {
        var recoveryAction = ReplayExportRecoveryPolicy.Resolve(
            job.State,
            File.Exists(job.StagingPath),
            File.Exists(job.TargetPath));
        if (recoveryAction == ReplayExportRecoveryActions.FailAndDeletePartial)
        {
            Fail(job, "interrupted", "上次导出在渲染或编码时中断，可重新创建任务。", deleteStaging: true);
            yield break;
        }

        var dependency = ReplayEncoderDependency.LoadVerified();
        var candidate = File.Exists(job.TargetPath) ? job.TargetPath : job.StagingPath;
        if (!File.Exists(candidate)) throw new FileNotFoundException("恢复任务找不到部分输出。", candidate);
        var importedProfile = string.Equals(
            job.ProfileId,
            MatchReplayVideoEncodingPolicy.ImportedCodecProfileId,
            StringComparison.Ordinal);
        var verificationTask = Task.Run(() => importedProfile
            ? ReplayVideoVerifier.VerifyNormalized(dependency, candidate)
            : ReplayVideoVerifier.Verify(
                dependency,
                candidate,
                job.Width,
                job.Height,
                job.FramesPerSecond,
                job.FrameCount,
                job.AudioSampleFrames > 0));
        while (!verificationTask.IsCompleted) yield return null;
        if (verificationTask.IsFaulted)
        {
            throw verificationTask.Exception?.GetBaseException() ?? new InvalidDataException("恢复验证失败。");
        }
        var verification = verificationTask.Result;
        if (importedProfile)
        {
            job.Width = verification.Width;
            job.Height = verification.Height;
            job.FramesPerSecond = (int)Math.Round(verification.FramesPerSecond);
            job.FrameCount = verification.FrameCount;
            job.AudioSampleFrames = verification.HasAudio ? 1 : 0;
        }
        job.OutputSha256 = verification.Sha256;
        job.FileBytes = verification.FileBytes;
        if (job.State != MatchReplayExportStates.Committing)
        {
            Transition(job, MatchReplayExportStates.Committing, 0.96f, "恢复已验证输出的提交");
        }
        if (!File.Exists(job.TargetPath)) File.Move(job.StagingPath, job.TargetPath);
        var asset = new MatchMediaAsset
        {
            MediaId = job.JobId,
            RecordId = job.RecordId,
            Kind = "Video",
            Format = "MP4",
            FilePath = MatchReplayMediaStore.ToStoredPath(job.TargetPath),
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            State = MatchMediaStates.Ready,
            DurationMilliseconds = verification.DurationMilliseconds,
            Width = job.Width,
            Height = job.Height,
            FramesPerSecond = verification.FramesPerSecond,
            FileBytes = verification.FileBytes,
            Sha256 = verification.Sha256,
            TimelineJson = "[]"
        };
        job.Message = "启动恢复已完成 MP4 登记";
        if (!MatchRecordStorage.Database.CommitExportMedia(job, asset))
        {
            throw new IOException("恢复提交发生并发冲突。");
        }
        current = job;
    }

    private static byte[] CaptureFrame(RenderTexture target, Texture2D texture, int width, int height)
    {
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return (byte[])texture.GetRawTextureData().Clone();
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private static void Transition(MatchReplayExportJob job, string state, float progress, string message)
    {
        job.State = state;
        job.Progress = Math.Max(0f, Math.Min(1f, progress));
        job.Message = message ?? "";
        Persist(job);
        MatchReplayExportControlsPresenter.Refresh(job);
    }

    private static void Fail(MatchReplayExportJob job, string code, string message, bool deleteStaging)
    {
        if (deleteStaging)
        {
            DeleteIfExists(job.StagingPath);
            DeleteIfExists(job.TargetPath + ".audio.partial.wav");
        }
        job.State = MatchReplayExportStates.Failed;
        job.ErrorCode = code ?? "export-failed";
        job.Message = message ?? "视频导出失败。";
        Persist(job);
        current = job;
    }

    private static void Persist(MatchReplayExportJob job)
    {
        if (!MatchRecordStorage.Database.UpdateExportJob(job))
        {
            var latest = MatchRecordStorage.Database.LoadExportJob(job.JobId);
            if (latest != null) current = latest;
            throw new InvalidOperationException("视频导出任务状态发生并发冲突。");
        }
    }

    private static MatchReplayVideoSettings SnapshotSettings()
    {
        var source = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.Video;
        source.Normalize();
        return new MatchReplayVideoSettings
        {
            Quality = source.Quality,
            FramesPerSecond = source.FramesPerSecond,
            IncludeUi = source.IncludeUi,
            IncludeAudio = source.IncludeAudio
        };
    }

    private static MatchReplayVideoSettings SettingsFromJob(MatchReplayExportJob job)
    {
        return new MatchReplayVideoSettings
        {
            Quality = job.Width >= 1920 ? "1080p" : "720p",
            FramesPerSecond = Math.Max(1, job.FramesPerSecond),
            IncludeAudio = (job.ProfileId ?? "").IndexOf(".audio", StringComparison.Ordinal) >= 0,
            IncludeUi = (job.ProfileId ?? "").IndexOf(".hud", StringComparison.Ordinal) >= 0
        };
    }

    private static (int width, int height) Dimensions(string quality)
    {
        return string.Equals(quality, "1080p", StringComparison.OrdinalIgnoreCase) ? (1920, 1080) : (1280, 720);
    }

    private static string ProfileId(MatchReplayVideoSettings settings)
    {
        var dimensions = Dimensions(settings.Quality);
        return MatchReplayVideoEncodingPolicy.CodecProfileId + "." + dimensions.width + "x" + dimensions.height
               + "." + settings.FramesPerSecond + "fps"
               + (settings.IncludeAudio ? ".audio" : ".silent")
               + (settings.IncludeUi ? ".hud" : ".clean");
    }

    private static IReadOnlyList<MatchMediaTimelineEntry> BuildTimeline(ReplayDocumentV10 document, int fps)
    {
        return document.Events
            .Where(item => item.EventType == ReplayEventTypesV10.TurnChanged)
            .GroupBy(item => item.TurnIndex)
            .Select(group => group.First())
            .OrderBy(item => item.TurnIndex)
            .Select(item => new MatchMediaTimelineEntry
            {
                TurnIndex = item.TurnIndex,
                EventSequence = item.Sequence,
                VideoMilliseconds = item.TimeTicks * 1000L / ReplayProtocolV10.TimebaseTicksPerSecond
            })
            .ToList();
    }

    private static long EstimateOutputBytes(ReplayDocumentV10 document, int width, int height, int fps, bool includeAudio)
    {
        var durationSeconds = Math.Max(1d,
            document.Events.Count == 0 ? 1d : document.Events.Max(item => item.TimeTicks) / (double)ReplayProtocolV10.TimebaseTicksPerSecond + 1d);
        var bitsPerSecond = width >= 1920 ? 12_000_000L : 6_000_000L;
        var waveBytes = includeAudio
            ? (long)(durationSeconds * ReplayOfflineAudioMixer.SampleRate * ReplayOfflineAudioMixer.Channels * 2d)
            : 0L;
        return Math.Max(32L * 1024L * 1024L, (long)(durationSeconds * bitsPerSecond / 8d * 1.5d) + waveBytes);
    }

    private static bool HasFreeSpace(long estimatedBytes, out long available)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(MatchRecordStorage.MediaDirectory));
            available = string.IsNullOrWhiteSpace(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
            return available >= estimatedBytes + 256L * 1024L * 1024L;
        }
        catch
        {
            available = long.MaxValue;
            return true;
        }
    }

    private static bool IsTerminal(string state)
    {
        return state == MatchReplayExportStates.Ready
               || state == MatchReplayExportStates.Corrupt
               || state == MatchReplayExportStates.Failed
               || state == MatchReplayExportStates.Cancelled;
    }

    private static void ReconcileOrphanPartials()
    {
        try
        {
            var known = new HashSet<string>(
                MatchRecordStorage.Database.LoadRecoverableExportJobs()
                    .Select(item => Path.GetFullPath(item.StagingPath)),
                StringComparer.OrdinalIgnoreCase);
            var knownFinals = new HashSet<string>(
                MatchRecordStorage.Database.LoadAllMediaPaths()
                    .Select(MatchReplayMediaStore.ResolvePath)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);
            foreach (var job in MatchRecordStorage.Database.LoadRecoverableExportJobs())
            {
                if (!string.IsNullOrWhiteSpace(job.TargetPath)) knownFinals.Add(Path.GetFullPath(job.TargetPath));
            }
            var quarantine = Path.Combine(MatchRecordStorage.RootDirectory, "Quarantine", "ExportPartials");
            foreach (var path in Directory.GetFiles(MatchRecordStorage.MediaDirectory, "*.partial.mp4", SearchOption.AllDirectories))
            {
                if (known.Contains(Path.GetFullPath(path))) continue;
                Directory.CreateDirectory(quarantine);
                var target = Path.Combine(quarantine, Path.GetFileName(path) + ".orphan-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.Move(path, target);
            }
            foreach (var path in Directory.GetFiles(MatchRecordStorage.MediaDirectory, "*.mp4", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(path);
                if (knownFinals.Contains(full) || full.EndsWith(".partial.mp4", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(quarantine);
                var target = Path.Combine(quarantine, Path.GetFileName(path) + ".orphan-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.Move(path, target);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] partial output reconciliation failed: " + ex.Message);
        }
    }

    private static void DeleteIfExists(string path)
    {
        try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string FormatBytes(long value)
    {
        return value >= 1024L * 1024L * 1024L ? (value / (1024d * 1024d * 1024d)).ToString("0.0") + " GB"
            : value >= 1024L * 1024L ? (value / (1024d * 1024d)).ToString("0.0") + " MB"
            : (value / 1024d).ToString("0.0") + " KB";
    }
}
