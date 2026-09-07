using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static partial class MatchReplayVideoExporter
{
    private static readonly Queue<MatchReplayExportJob> RecoveryQueue = new();
    private static MatchReplayExportJob? current;
    private static bool workerRunning;

    internal static MatchReplayExportJob? Current => current;

    internal static void Initialize()
    {
        RecoveryQueue.Clear();
        recoveryLoading = true;
        ReplayBackgroundWork.Storage.Enqueue("RecoverVideoJobs", () =>
        {
            var store = MatchRecordStorage.Database;
            var jobs = store.LoadRecoverableExportJobs(); var latest = store.LoadLatestExportJob();
            ReconcileOrphanPartials(); return (jobs, latest);
        }, result =>
        {
            recoveryLoading = false;
            foreach (var job in result.jobs) RecoveryQueue.Enqueue(job);
            current = result.latest; ResumePending();
        }, ex => { recoveryLoading = false; AuraToolsLog.Warn("[MatchRecords] video recovery failed: " + ex.Message); });
    }

    private static void ResumePending()
    {
        MatchReplayExportControlsPresenter.Refresh(current);
        if (workerRunning) return;
        if (RecoveryQueue.Count == 0)
        {
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
            MatchReplayExportControlsPresenter.Refresh(current);
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
        ReplayDocumentEnvelopeV17? envelope = null)
    {
        Exception? failure = null;
        var core = ExportCore(job, settings, dependency, envelope);
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
            QueueDelete(job.StagingPath);
        }
        else if (failure != null)
        {
            var committing = job.State == MatchReplayExportStates.Committing;
            Fail(job, committing ? "commit-interrupted" : "export-failed", failure.Message, deleteStaging: !committing);
            AuraToolsLog.Warn("[MatchRecords] v17 perspective replay video export failed: " + failure);
        }
        if (MatchReplayPlayer.IsActive)
        {
            var completed = failure == null
                            && string.Equals(
                                job.State,
                                MatchReplayExportStates.Ready,
                                StringComparison.Ordinal);
            MatchReplayPlayer.StopAfterExport(
                completed,
                completed ? job.Message : failure?.Message ?? job.Message);
        }
        workerRunning = false;
        ResumePending();
    }

    private static IEnumerator ExportCore(
        MatchReplayExportJob job,
        MatchReplayVideoSettings settings,
        ReplayEncoderDependency? dependency,
        ReplayDocumentEnvelopeV17? envelope)
    {
        ReplayRenderSurfaceV17? surface = null;
        RenderTexture? target = null;
        Texture2D? reader = null;
        ReplayFramePipeline? pipeline = null;
        var audioPath = job.TargetPath + ".audio.partial.wav";
        var previousCaptureFramerate = Time.captureFramerate;
        try
        {
            ReplayLoadedRecord? sourceData = null;
            if (dependency == null || envelope == null || !MatchReplayPlayer.IsActive)
            {
                var load = new ReplayIoOperation<(ReplayEncoderDependency Dependency, ReplayLoadedRecord Data)>("ReadVideoReplay",
                    () => (ReplayEncoderDependency.LoadVerified(), ReplayLoadedRecord.Read(job.RecordId)));
                yield return load;
                dependency = load.Result.Dependency; sourceData = load.Result.Data; envelope = sourceData.Envelope;
            }
            if (!MatchReplayPlayer.IsActive
                && !MatchReplayPlayer.TryStartForExport(sourceData!, out var startMessage))
                throw new InvalidOperationException(startMessage);
            var readyFrames = 0;
            while (MatchReplayPlayer.IsActive && !MatchReplayPlayer.IsReadyForExport && readyFrames++ < 900)
                yield return null;
            if (!MatchReplayPlayer.IsReadyForExport)
                throw new TimeoutException("独立回放场景准备超时：" + MatchReplayPlayer.PreparationStatus);
            job.AttemptCount++;
            var frameCount = Math.Max(1L, (long)Math.Ceiling(
                Math.Max(1000L, MatchReplayPlayer.DurationMilliseconds)
                / 1000d * settings.FramesPerSecond) + 1L);
            job.FrameCount = frameCount;
            var sourceDocument = envelope.Document;
            var prepareMedia = new ReplayIoOperation<long>("PrepareVideoMedia", () =>
            {
                DeleteIfExists(job.StagingPath); DeleteIfExists(audioPath);
                if (!HasFreeSpace(job.EstimatedBytes, out var available))
                    throw new IOException("预计需要 " + FormatBytes(job.EstimatedBytes) + "，媒体目录仅剩 " + FormatBytes(available) + "。");
                return settings.IncludeAudio ? ReplayOfflineAudioMixer.MixToWave(sourceDocument, frameCount,
                    settings.FramesPerSecond, MatchRecordStorage.Database.ResolveReplayAsset, audioPath) : 0L;
            });
            yield return prepareMedia;
            var audioSampleFrames = prepareMedia.Result;
            job.AudioSampleFrames = audioSampleFrames;
            Transition(job, MatchReplayExportStates.Rendering, 0.02f,
                "固定时间步渲染 " + job.Width + "x" + job.Height + " / " + job.FramesPerSecond + " FPS");
            target = new RenderTexture(job.Width, job.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            reader = new Texture2D(job.Width, job.Height, TextureFormat.RGB24, mipChain: false);
            Time.captureFramerate = job.FramesPerSecond;
            surface = new ReplayRenderSurfaceV17(target, settings.IncludeUi);
            pipeline = new ReplayFramePipeline(
                dependency,
                job.StagingPath,
                job.Width,
                job.Height,
                job.FramesPerSecond,
                settings.IncludeAudio ? audioPath : null);
            for (var frameIndex = 0L; frameIndex < frameCount; frameIndex++)
            {
                ThrowPersistenceFailure(job.JobId);
                if (job.CancelRequested) throw new OperationCanceledException();
                if (frameIndex > 0)
                {
                    MatchReplayPlayer.AdvanceExportClock(1000f / job.FramesPerSecond);
                    if (MatchReplayPlayer.HasBlockingError)
                    {
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(MatchReplayPlayer.PlaybackIssue)
                                ? "独立回放投影在视频导出期间失败。"
                                : MatchReplayPlayer.PlaybackIssue);
                    }
                }
                if (!MatchReplayPlayer.IsActive)
                {
                    throw new InvalidOperationException("独立回放会话在视频导出完成前退出。");
                }
                yield return new WaitForEndOfFrame();
                surface.Render();
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
            var snapshot = ReplayCanonicalJsonV17.Clone(job);
            var document = envelope.Document;
            var commit = new ReplayIoOperation<MatchReplayExportJob>("CommitVideo", () =>
            {
                ThrowPersistenceFailure(snapshot.JobId);
                if (File.Exists(snapshot.TargetPath)) throw new IOException("MP4 目标文件已经存在。");
                AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, snapshot.StagingPath, snapshot.TargetPath);
                var asset = new MatchMediaAsset
                {
                    MediaId = snapshot.JobId, RecordId = snapshot.RecordId, Kind = "Video", Format = "MP4",
                    FilePath = MatchReplayMediaStore.ToStoredPath(snapshot.TargetPath), CreatedUtc = DateTime.UtcNow.ToString("O"),
                    State = MatchMediaStates.Ready, DurationMilliseconds = verification.DurationMilliseconds,
                    Width = snapshot.Width, Height = snapshot.Height, FramesPerSecond = snapshot.FramesPerSecond,
                    FileBytes = verification.FileBytes, Sha256 = verification.Sha256,
                    TimelineJson = AuraSharedJson.SerializeCompact(BuildTimeline(document, snapshot.FramesPerSecond))
                };
                snapshot.Message = "已保存并验证 " + Path.GetFileName(snapshot.TargetPath);
                if (!MatchRecordStorage.Database.CommitExportMedia(snapshot, asset))
                    throw new IOException("已生成 MP4，但数据库提交发生并发冲突；启动恢复将继续登记。");
                return snapshot;
            });
            yield return commit;
            ApplyCommittedJob(commit.Result, job);
            current = job;
        }
        finally
        {
            try
            {
                pipeline?.Dispose();
            }
            finally
            {
                try
                {
                    surface?.Dispose();
                }
                finally
                {
                    Time.captureFramerate = previousCaptureFramerate;
                    if (target != null)
                    {
                        target.Release();
                        Object.Destroy(target);
                    }
                    if (reader != null) Object.Destroy(reader);
                    QueueDelete(audioPath);
                }
            }
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
        var existence = new ReplayIoOperation<(bool Staging, bool Target)>("InspectVideoRecovery",
            () => (File.Exists(job.StagingPath), File.Exists(job.TargetPath)));
        yield return existence;
        var recoveryAction = ReplayExportRecoveryPolicy.Resolve(
            job.State,
            existence.Result.Staging,
            existence.Result.Target);
        if (recoveryAction == ReplayExportRecoveryActions.FailAndDeletePartial)
        {
            Fail(job, "interrupted", "上次导出在渲染或编码时中断，可重新创建任务。", deleteStaging: true);
            yield break;
        }

        var load = new ReplayIoOperation<ReplayEncoderDependency>("VerifyVideoDependency", ReplayEncoderDependency.LoadVerified);
        yield return load;
        var dependency = load.Result;
        var candidate = existence.Result.Target ? job.TargetPath : job.StagingPath;
        if (!existence.Result.Target && !existence.Result.Staging) throw new FileNotFoundException("恢复任务找不到部分输出。", candidate);
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
        var snapshot = ReplayCanonicalJsonV17.Clone(job);
        var commit = new ReplayIoOperation<MatchReplayExportJob>("CommitRecoveredVideo", () =>
        {
            ThrowPersistenceFailure(snapshot.JobId);
            if (!File.Exists(snapshot.TargetPath)) AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, snapshot.StagingPath, snapshot.TargetPath);
            if (!MatchRecordStorage.Database.CommitExportMedia(snapshot, asset)) throw new IOException("恢复提交发生并发冲突。");
            return snapshot;
        });
        yield return commit;
        ApplyCommittedJob(commit.Result, job);
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
            QueueDelete(job.StagingPath);
            QueueDelete(job.TargetPath + ".audio.partial.wav");
        }
        job.State = MatchReplayExportStates.Failed;
        job.ErrorCode = code ?? "export-failed";
        job.Message = message ?? "视频导出失败。";
        Persist(job);
        current = job;
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

    private static IReadOnlyList<MatchMediaTimelineEntry> BuildTimeline(ReplayDocumentV17 document, int fps)
    {
        return document.TruthEvents
            .Where(item => item.EventType == ReplayEventTypesV17.RoundStarted)
            .GroupBy(item => item.RoundSequence)
            .Select(group => group.First())
            .OrderBy(item => item.RoundSequence)
            .Select(item => new MatchMediaTimelineEntry
            {
                TurnIndex = item.RoundSequence,
                EventSequence = item.Sequence,
                VideoMilliseconds = item.TimeTicks * 1000L / ReplayProtocolV17.TimebaseTicksPerSecond
            })
            .ToList();
    }

    private static long EstimateOutputBytes(ReplayDocumentV17 document, int width, int height, int fps, bool includeAudio)
    {
        var events = document.TruthEvents.Concat(document.PresentationEvents).ToList();
        var durationSeconds = Math.Max(1d,
            events.Count == 0 ? 1d : events.Max(item => item.TimeTicks) / (double)ReplayProtocolV17.TimebaseTicksPerSecond + 1d);
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
                AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, path, target);
            }
            foreach (var path in Directory.GetFiles(MatchRecordStorage.MediaDirectory, "*.mp4", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(path);
                if (knownFinals.Contains(full) || full.EndsWith(".partial.mp4", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(quarantine);
                var target = Path.Combine(quarantine, Path.GetFileName(path) + ".orphan-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, path, target);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] partial output reconciliation failed: " + ex.Message);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }

    private static string FormatBytes(long value)
    {
        return value >= 1024L * 1024L * 1024L ? (value / (1024d * 1024d * 1024d)).ToString("0.0") + " GB"
            : value >= 1024L * 1024L ? (value / (1024d * 1024d)).ToString("0.0") + " MB"
            : (value / 1024d).ToString("0.0") + " KB";
    }
}
