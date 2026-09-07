using System;
using System.Collections.Concurrent;
using System.IO;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static partial class MatchReplayVideoExporter
{
    private static readonly ConcurrentDictionary<string, Exception> PersistenceErrors = new(StringComparer.Ordinal);
    private static bool recoveryLoading;

    internal static bool TryStart(string recordId, MatchRecordLibraryViewState returnState, out string message)
    {
        if (recoveryLoading || workerRunning || current != null && !IsTerminal(current.State))
        { message = "已有视频任务正在准备或运行。"; return false; }
        if (!MatchRecordStorage.Ready) { message = MatchRecordStorage.Status; return false; }
        var settings = SnapshotSettings();
        var dimensions = Dimensions(settings.Quality);
        workerRunning = true;
        var accepted = ReplayBackgroundWork.Storage.Enqueue("PrepareVideoExport", () =>
        {
            var dependency = ReplayEncoderDependency.LoadVerified();
            var data = ReplayLoadedRecord.Read(recordId);
            var id = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(MatchRecordStorage.MediaDirectory, recordId);
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, id + ".mp4");
            var job = new MatchReplayExportJob
            {
                JobId = id, RecordId = recordId, State = MatchReplayExportStates.Planned,
                CreatedUtc = DateTime.UtcNow.ToString("O"), UpdatedUtc = DateTime.UtcNow.ToString("O"),
                ProfileId = ProfileId(settings), Width = dimensions.width, Height = dimensions.height,
                FramesPerSecond = settings.FramesPerSecond, StagingPath = target + ".partial.mp4", TargetPath = target,
                Message = "任务已计划", EstimatedBytes = EstimateOutputBytes(data.Envelope.Document,
                    dimensions.width, dimensions.height, settings.FramesPerSecond, settings.IncludeAudio)
            };
            MatchRecordStorage.Database.CreateExportJob(job);
            return new PreparedExport { Job = job, Data = data, Dependency = dependency };
        }, prepared =>
        {
            current = prepared.Job;
            if (!MatchReplayLaunchCoordinator.TryStartForExport(prepared.Data, returnState, () =>
            {
                MatchReplayExportControlsPresenter.Show();
                if (AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(Export(prepared.Job, settings, prepared.Dependency, prepared.Data.Envelope)) != null) return;
                workerRunning = false; Fail(prepared.Job, "worker-unavailable", "无法启动视频导出。", true);
            }, failure =>
            {
                workerRunning = false; Fail(prepared.Job, "replay-launch-failed", failure, true);
            }, out var detail))
            {
                workerRunning = false; Fail(prepared.Job, "replay-launch-failed", detail, true);
            }
        }, ex =>
        {
            workerRunning = false;
            AuraToolsLog.Warn("[MatchRecords] video preparation failed: " + ex.Message);
            Witch.UI.UIManager.Instance?.ShowModalWindow("AuraTools", "无法准备视频导出：" + ex.Message, null, 1f);
        });
        message = accepted ? "正在后台读取回放并准备视频任务…" : "视频任务未能进入后台队列。";
        return accepted;
    }

    private sealed class PreparedExport
    {
        internal MatchReplayExportJob Job = null!;
        internal ReplayLoadedRecord Data = null!;
        internal ReplayEncoderDependency Dependency = null!;
    }

    private static void Persist(MatchReplayExportJob job)
    {
        // This producer is the sole progress writer. Reserve consecutive
        // revisions on enqueue; the FIFO applies them in that same order.
        var snapshot = ReplayCanonicalJsonV17.Clone(job);
        job.Revision++;
        ReplayBackgroundWork.Storage.Enqueue("ExportProgress." + job.JobId, () =>
        {
            ThrowPersistenceFailure(job.JobId);
            try
            {
                if (!MatchRecordStorage.Database.UpdateExportJob(snapshot)) throw new IOException("视频任务状态发生并发冲突。");
                return true;
            }
            catch (Exception ex) { PersistenceErrors[job.JobId] = ex; throw; }
        }, _ => { }, ex =>
        {
            PersistenceErrors[job.JobId] = ex;
            AuraToolsLog.Warn("[MatchRecords] video state was not saved: " + ex.Message);
        });
    }

    private static void ThrowPersistenceFailure(string id)
    {
        if (PersistenceErrors.TryGetValue(id, out var error)) throw new IOException("视频状态保存失败。", error);
    }

    private static void ApplyCommittedJob(MatchReplayExportJob source, MatchReplayExportJob target)
    {
        target.Revision = source.Revision; target.State = source.State; target.Progress = source.Progress;
        target.OutputPath = source.OutputPath; target.TargetPath = source.TargetPath;
        target.UpdatedUtc = source.UpdatedUtc; target.Message = source.Message;
    }

    private static void QueueDelete(string path) => ReplayBackgroundWork.Storage.Enqueue("VideoCleanup",
        () => { DeleteIfExists(path); return true; }, _ => { }, ex => AuraToolsLog.Warn("[MatchRecords] video cleanup failed: " + ex.Message));
}
