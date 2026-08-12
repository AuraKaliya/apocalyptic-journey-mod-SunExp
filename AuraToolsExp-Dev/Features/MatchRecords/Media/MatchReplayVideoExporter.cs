using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayVideoExporter
{
    private static MatchReplayExportJob? current;
    private static bool cancelRequested;

    internal static MatchReplayExportJob? Current => current;

    internal static void Initialize()
    {
        CleanupAbandonedWorkDirectories();
        current = MatchReplayExportJobStore.Load();
        if (current != null && !IsTerminal(current.State))
        {
            current.State = MatchReplayExportStates.Interrupted;
            current.Message = "上次导出因游戏退出而中断，临时文件已清理。";
            MatchReplayExportJobStore.Save(current);
        }
    }

    internal static bool TryStart(string recordId, out string message)
    {
        message = "";
        if (current != null && !IsTerminal(current.State))
        {
            message = "已有视频导出任务正在运行。";
            return false;
        }

        if (!MatchReplayPlayer.TryStartForExport(recordId, out message)) return false;
        var settings = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.Video;
        settings.Normalize();
        var dimensions = Dimensions(settings.Quality);
        var estimatedBytes = EstimateBytes(
            Math.Max(5000L, MatchReplayPlayer.DurationMilliseconds + 1000L),
            dimensions.width,
            dimensions.height,
            settings.FramesPerSecond,
            settings.IncludeAudio);
        if (!HasFreeSpace(estimatedBytes, out var available))
        {
            MatchReplayPlayer.Stop();
            message = "预计需要 " + FormatBytes(estimatedBytes) + "，临时目录仅剩 " + FormatBytes(available) + "。";
            return false;
        }

        cancelRequested = false;
        current = new MatchReplayExportJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            RecordId = recordId,
            State = MatchReplayExportStates.Preparing,
            Message = "初始化回放场景",
            EstimatedBytes = estimatedBytes
        };
        Persist(current);
        MatchReplayExportControlsPresenter.Show();
        if (AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(Capture(current, settings)) == null)
        {
            MatchReplayPlayer.Stop();
            current.State = MatchReplayExportStates.Failed;
            current.Message = "无法启动导出协程。";
            Persist(current);
            message = current.Message;
            return false;
        }

        message = "视频导出已开始，预计临时空间 " + FormatBytes(estimatedBytes) + "。";
        return true;
    }

    internal static void Tick()
    {
        MatchReplayExportControlsPresenter.Refresh(current);
    }

    internal static void CancelOrDismiss()
    {
        if (current == null || IsTerminal(current.State))
        {
            current = null;
            MatchReplayExportControlsPresenter.Close();
            return;
        }

        cancelRequested = true;
        current.Message = "正在取消";
        Persist(current);
    }

    private static IEnumerator Capture(MatchReplayExportJob job, MatchReplayVideoSettings settings)
    {
        var context = new ExportContext(job, settings);
        Exception? failure = null;
        var core = CaptureCore(context);
        while (failure == null)
        {
            bool moved;
            try { moved = core.MoveNext(); }
            catch (Exception ex) { failure = ex; break; }
            if (!moved) break;
            yield return core.Current;
        }

        if (failure is OperationCanceledException)
        {
            job.State = MatchReplayExportStates.Cancelled;
            job.Message = "导出任务已取消";
        }
        else if (failure != null)
        {
            job.State = MatchReplayExportStates.Failed;
            job.Message = failure.Message;
        }

        Cleanup(context);
        Persist(job);
        if (failure != null && !(failure is OperationCanceledException))
        {
            AuraToolsLog.Warn("[MatchRecords] video export failed: " + failure);
        }
    }

    private static IEnumerator CaptureCore(ExportContext context)
    {
        Directory.CreateDirectory(context.TemporaryDirectory);
        context.FrameSpool = new ReplayFrameSpool(context.FrameSpoolPath);
        while (MatchReplayPlayer.IsActive && !MatchReplayPlayer.IsReadyForExport && !cancelRequested)
        {
            yield return null;
        }

        if (cancelRequested) throw new OperationCanceledException();
        if (!MatchReplayPlayer.IsActive)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(MatchReplayPlayer.LastStartFailure)
                    ? "回放环境在视频导出前意外停止。"
                    : MatchReplayPlayer.LastStartFailure);
        }
        if (!context.Settings.IncludeUi) context.HideUi();
        var listener = context.Settings.IncludeAudio ? Object.FindAnyObjectByType<AudioListener>() : null;
        if (listener != null)
        {
            context.AudioCapture = listener.gameObject.AddComponent<ReplayWaveCapture>();
            context.AudioCapture.BeginCapture();
            context.WaveWriter = new ReplayWaveWriter(context.WavePath, context.AudioCapture.SampleRate, 2);
        }

        Time.captureFramerate = context.FramesPerSecond;
        context.Job.State = MatchReplayExportStates.Rendering;
        context.Job.Message = "录制 " + context.Width + "x" + context.Height + " / " + context.FramesPerSecond + " FPS";
        Persist(context.Job);
        var lastTurn = 0;
        var tailFrames = 0;
        while (!cancelRequested && MatchReplayPlayer.IsActive)
        {
            if (MatchReplayPlayer.HasBlockingError)
            {
                throw new InvalidDataException("回放已失步，视频导出停止：" + MatchReplayPlayer.PlaybackIssue);
            }

            if (!MatchReplayPlayer.IsFinished)
            {
                MatchReplayPlayer.AdvanceExportClock(1000f / context.FramesPerSecond);
            }
            else if (++tailFrames > context.FramesPerSecond / 2)
            {
                break;
            }

            MatchReplayExportControlsPresenter.SetCaptured(true);
            yield return new WaitForEndOfFrame();
            var bytes = CaptureFrame(context.Width, context.Height, context.JpegQuality);
            context.FrameSpool.Enqueue(bytes);
            MatchReplayExportControlsPresenter.SetCaptured(false);
            context.AudioCapture?.DrainTo(context.WaveWriter!);

            var turn = MatchReplayPlayer.CurrentTurn;
            if (turn != lastTurn)
            {
                lastTurn = turn;
                var eventSequence = MatchReplayPlayer.EventIndex <= 0 || MatchReplayPlayer.Events.Count == 0
                    ? 0
                    : MatchReplayPlayer.Events[Math.Min(MatchReplayPlayer.Events.Count - 1, MatchReplayPlayer.EventIndex - 1)].Sequence;
                context.Timeline.Add(new MatchMediaTimelineEntry
                {
                    TurnIndex = turn,
                    EventSequence = eventSequence,
                    VideoMilliseconds = context.FrameSpool.FrameCount * 1000L / context.FramesPerSecond
                });
            }

            context.Job.Progress = Math.Min(0.8f, MatchReplayPlayer.Progress * 0.8f);
            if (context.FrameSpool.PayloadBytes > context.Job.EstimatedBytes * 3L / 2L)
            {
                throw new IOException("实际帧数据已明显超过导出预估，导出已停止以保护磁盘空间。");
            }

            yield return null;
        }

        if (cancelRequested) throw new OperationCanceledException();
        context.AudioCapture?.EndCapture();
        context.AudioCapture?.DrainTo(context.WaveWriter!);
        context.WaveWriter?.Dispose();
        context.WaveWriter = null;
        context.FrameSpool.Complete();
        MatchReplayPlayer.Stop();
        Time.captureFramerate = context.PreviousCaptureFrameRate;
        context.RestoreUi();

        context.Job.State = MatchReplayExportStates.Encoding;
        context.Job.Progress = 0.82f;
        context.Job.Message = context.Settings.PreferMp4 ? "后台编码 MP4（不可用时回退 AVI）" : "后台封装 MJPEG/PCM AVI";
        Persist(context.Job);
        var mediaDirectory = Path.Combine(MatchRecordStorage.MediaDirectory, context.Job.RecordId);
        Directory.CreateDirectory(mediaDirectory);
        var outputBase = NextAvailableBase(Path.Combine(mediaDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-replay"));
        var audio = File.Exists(context.WavePath) && new FileInfo(context.WavePath).Length > 44 ? context.WavePath : null;
        var encoding = Task.Run(() => MatchReplayVideoEncoder.Encode(
            context.FrameSpool,
            outputBase,
            context.Width,
            context.Height,
            context.FramesPerSecond,
            audio,
            context.Settings,
            () => cancelRequested));
        while (!encoding.IsCompleted)
        {
            if (cancelRequested) context.Job.Message = "正在停止编码";
            yield return null;
        }

        if (encoding.IsFaulted) throw encoding.Exception?.GetBaseException() ?? new IOException("视频编码失败。");
        if (cancelRequested) throw new OperationCanceledException();
        var output = encoding.Result;
        var duration = context.FrameSpool.FrameCount * 1000L / context.FramesPerSecond;
        var asset = MatchReplayMediaStore.RegisterGenerated(
            context.Job.RecordId, output, duration, context.Width, context.Height, context.FramesPerSecond, context.Timeline);
        if (audio != null)
        {
            var waveOutput = outputBase + ".wav";
            if (!File.Exists(waveOutput)) File.Copy(audio, waveOutput, overwrite: false);
        }

        context.Job.OutputPath = asset.FilePath;
        context.Job.State = MatchReplayExportStates.Completed;
        context.Job.Progress = 1f;
        context.Job.Message = "已保存 " + Path.GetFileName(output);
        Persist(context.Job);
    }

    private static void Cleanup(ExportContext context)
    {
        MatchReplayExportControlsPresenter.SetCaptured(false);
        Time.captureFramerate = context.PreviousCaptureFrameRate;
        context.RestoreUi();
        context.AudioCapture?.EndCapture();
        if (context.AudioCapture != null && context.WaveWriter != null) context.AudioCapture.DrainTo(context.WaveWriter);
        context.WaveWriter?.Dispose();
        context.FrameSpool?.Dispose();
        if (context.AudioCapture != null) Object.Destroy(context.AudioCapture);
        if (MatchReplayPlayer.IsActive) MatchReplayPlayer.Stop();
        TryDeleteDirectory(context.TemporaryDirectory);
    }

    private sealed class ExportContext
    {
        private readonly List<Canvas> hiddenCanvases = new();

        internal ExportContext(MatchReplayExportJob job, MatchReplayVideoSettings settings)
        {
            Job = job;
            Settings = settings;
            TemporaryDirectory = Path.Combine(MatchRecordStorage.TemporaryDirectory, "export-" + job.JobId);
            FrameSpoolPath = Path.Combine(TemporaryDirectory, "frames.spool");
            WavePath = Path.Combine(TemporaryDirectory, "audio.wav");
            PreviousCaptureFrameRate = Time.captureFramerate;
            (Width, Height) = Dimensions(settings.Quality);
            FramesPerSecond = settings.FramesPerSecond;
            JpegQuality = settings.Quality == "1080p" ? 80 : 74;
        }

        internal MatchReplayExportJob Job { get; }
        internal MatchReplayVideoSettings Settings { get; }
        internal string TemporaryDirectory { get; }
        internal string FrameSpoolPath { get; }
        internal string WavePath { get; }
        internal int PreviousCaptureFrameRate { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal int FramesPerSecond { get; }
        internal int JpegQuality { get; }
        internal ReplayFrameSpool FrameSpool { get; set; } = null!;
        internal List<MatchMediaTimelineEntry> Timeline { get; } = new();
        internal ReplayWaveCapture? AudioCapture { get; set; }
        internal ReplayWaveWriter? WaveWriter { get; set; }

        internal void HideUi()
        {
            foreach (var item in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(item => item.enabled))
            {
                hiddenCanvases.Add(item);
                item.enabled = false;
            }
        }

        internal void RestoreUi()
        {
            foreach (var item in hiddenCanvases.Where(item => item != null)) item.enabled = true;
            hiddenCanvases.Clear();
        }
    }

    private static byte[] CaptureFrame(int width, int height, int quality)
    {
        var source = ScreenCapture.CaptureScreenshotAsTexture();
        if (source == null) throw new InvalidOperationException("无法读取当前游戏画面。");
        var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D? resized = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            resized = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            resized.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
            resized.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return resized.EncodeToJPG(quality);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            Object.Destroy(source);
            if (resized != null) Object.Destroy(resized);
        }
    }

    private static (int width, int height) Dimensions(string quality)
    {
        return string.Equals(quality, "1080p", StringComparison.OrdinalIgnoreCase) ? (1920, 1080) : (1280, 720);
    }

    private static long EstimateBytes(long durationMilliseconds, int width, int height, int fps, bool audio)
    {
        var frames = Math.Max(1L, (long)Math.Ceiling(durationMilliseconds / 1000d * fps));
        var jpegBytes = width >= 1920 ? 340L * 1024L : 150L * 1024L;
        var audioBytes = audio ? durationMilliseconds * 192L : 0L;
        return Math.Max(32L * 1024L * 1024L, frames * jpegBytes + audioBytes + 16L * 1024L * 1024L);
    }

    private static bool HasFreeSpace(long estimatedBytes, out long available)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(MatchRecordStorage.TemporaryDirectory));
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
        return state == MatchReplayExportStates.Completed
               || state == MatchReplayExportStates.Failed
               || state == MatchReplayExportStates.Cancelled
               || state == MatchReplayExportStates.Interrupted;
    }

    private static void Persist(MatchReplayExportJob job)
    {
        try { MatchReplayExportJobStore.Save(job); }
        catch (Exception ex) { AuraToolsLog.Warn("[MatchRecords] export job state could not be saved: " + ex.Message); }
    }

    private static void CleanupAbandonedWorkDirectories()
    {
        try
        {
            var root = MatchRecordStorage.TemporaryDirectory;
            foreach (var directory in Directory.GetDirectories(root, "export-*", SearchOption.TopDirectoryOnly))
            {
                TryDeleteDirectory(directory);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static string NextAvailableBase(string path)
    {
        for (var index = 1; index < 1000; index++)
        {
            var candidate = index == 1 ? path : path + "-" + index;
            if (!File.Exists(candidate + ".mp4") && !File.Exists(candidate + ".avi") && !File.Exists(candidate + ".wav")) return candidate;
        }

        return path + "-" + Guid.NewGuid().ToString("N");
    }

    private static string FormatBytes(long value)
    {
        return value >= 1024L * 1024L * 1024L ? (value / (1024d * 1024d * 1024d)).ToString("0.0") + " GB"
            : value >= 1024L * 1024L ? (value / (1024d * 1024d)).ToString("0.0") + " MB"
            : (value / 1024d).ToString("0.0") + " KB";
    }
}
