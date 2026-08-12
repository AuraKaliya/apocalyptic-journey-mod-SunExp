using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayVideoExporter
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int FramesPerSecond = 30;
    private const int JpegQuality = 72;
    private static MatchReplayExportJob? current;
    private static bool cancelRequested;

    internal static MatchReplayExportJob? Current => current;

    internal static bool TryStart(string recordId, out string message)
    {
        message = "";
        if (current != null && !IsTerminal(current.State))
        {
            message = "已有视频导出任务正在运行。";
            return false;
        }

        if (!MatchReplayPlayer.TryStartForExport(recordId, out message))
        {
            return false;
        }

        cancelRequested = false;
        current = new MatchReplayExportJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            RecordId = recordId,
            State = MatchReplayExportStates.Preparing,
            Message = "初始化回放场景"
        };
        MatchReplayExportControlsPresenter.Show();
        if (AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(Capture(current)) == null)
        {
            MatchReplayPlayer.Stop();
            current.State = MatchReplayExportStates.Failed;
            current.Message = "无法启动导出协程。";
            message = current.Message;
            return false;
        }

        message = "视频导出已开始。";
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
    }

    private static IEnumerator Capture(MatchReplayExportJob job)
    {
        var context = new ExportContext(job);
        Exception? failure = null;
        var core = CaptureCore(context);
        while (failure == null)
        {
            bool moved;
            try
            {
                moved = core.MoveNext();
            }
            catch (Exception ex)
            {
                failure = ex;
                break;
            }

            if (!moved)
            {
                break;
            }

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
        if (failure != null && !(failure is OperationCanceledException))
        {
            AuraToolsLog.Warn("[MatchRecords] video export failed: " + failure);
        }
    }

    private static IEnumerator CaptureCore(ExportContext context)
    {
        Directory.CreateDirectory(context.FramesDirectory);
        while (MatchReplayPlayer.IsActive && !MatchReplayPlayer.IsReadyForExport && !cancelRequested)
        {
            yield return null;
        }

        if (cancelRequested || !MatchReplayPlayer.IsActive) throw new OperationCanceledException();
        var listener = Object.FindAnyObjectByType<AudioListener>();
        if (listener != null)
        {
            context.AudioCapture = listener.gameObject.AddComponent<ReplayWaveCapture>();
            context.AudioCapture.BeginCapture();
            context.WaveWriter = new ReplayWaveWriter(context.WavePath, context.AudioCapture.SampleRate, 2);
        }

        Time.captureFramerate = FramesPerSecond;
        context.Job.State = MatchReplayExportStates.Rendering;
        context.Job.Message = "录制 720p / 30 FPS";
        var lastTurn = 0;
        var tailFrames = 0;
        while (!cancelRequested && MatchReplayPlayer.IsActive)
        {
            if (!MatchReplayPlayer.IsFinished)
            {
                MatchReplayPlayer.AdvanceExportClock(1000f / FramesPerSecond);
            }
            else if (++tailFrames > FramesPerSecond / 2)
            {
                break;
            }

            MatchReplayExportControlsPresenter.SetCaptured(true);
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(context.FramesDirectory, "frame-" + context.FramePaths.Count.ToString("D7") + ".jpg");
            var bytes = CaptureFrame();
            File.WriteAllBytes(path, bytes);
            context.FrameBytes += bytes.Length;
            context.FramePaths.Add(path);
            MatchReplayExportControlsPresenter.SetCaptured(false);
            if (context.AudioCapture != null && context.WaveWriter != null)
            {
                context.AudioCapture.DrainTo(context.WaveWriter);
            }

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
                    VideoMilliseconds = context.FramePaths.Count * 1000L / FramesPerSecond
                });
            }

            context.Job.Progress = Math.Min(0.8f, MatchReplayPlayer.Progress * 0.8f);
            if (context.FrameBytes > 1750L * 1024L * 1024L)
            {
                throw new IOException("视频帧接近 AVI 安全大小上限，导出已停止。");
            }

            yield return null;
        }

        if (cancelRequested) throw new OperationCanceledException();
        context.AudioCapture?.EndCapture();
        if (context.AudioCapture != null && context.WaveWriter != null)
        {
            context.AudioCapture.DrainTo(context.WaveWriter);
        }

        context.WaveWriter?.Dispose();
        context.WaveWriter = null;
        MatchReplayPlayer.Stop();
        Time.captureFramerate = context.PreviousCaptureFrameRate;

        context.Job.State = MatchReplayExportStates.Encoding;
        context.Job.Progress = 0.82f;
        context.Job.Message = "封装 MJPEG/PCM AVI";
        var mediaDirectory = Path.Combine(MatchRecordStorage.MediaDirectory, context.Job.RecordId);
        Directory.CreateDirectory(mediaDirectory);
        var baseName = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-replay";
        var output = NextAvailablePath(Path.Combine(mediaDirectory, baseName + ".avi"));
        baseName = Path.GetFileNameWithoutExtension(output);
        var audio = File.Exists(context.WavePath) && new FileInfo(context.WavePath).Length > 44 ? context.WavePath : null;
        var encoding = Task.Run(() => MjpegAviWriter.Write(
            output, context.FramePaths, Width, Height, FramesPerSecond, audio, () => cancelRequested));
        while (!encoding.IsCompleted)
        {
            if (cancelRequested) context.Job.Message = "正在停止编码";
            yield return null;
        }

        if (encoding.IsFaulted) throw encoding.Exception?.GetBaseException() ?? new IOException("视频编码失败。");
        if (cancelRequested) throw new OperationCanceledException();
        var asset = MatchReplayMediaStore.RegisterGenerated(
            context.Job.RecordId,
            output,
            context.FramePaths.Count * 1000L / FramesPerSecond,
            Width,
            Height,
            FramesPerSecond,
            context.Timeline);
        if (audio != null)
        {
            File.Copy(audio, Path.Combine(mediaDirectory, baseName + ".wav"), overwrite: false);
        }

        context.Job.OutputPath = asset.FilePath;
        context.Job.State = MatchReplayExportStates.Completed;
        context.Job.Progress = 1f;
        context.Job.Message = "已保存 " + Path.GetFileName(asset.FilePath);
    }

    private static void Cleanup(ExportContext context)
    {
        MatchReplayExportControlsPresenter.SetCaptured(false);
        Time.captureFramerate = context.PreviousCaptureFrameRate;
        context.AudioCapture?.EndCapture();
        if (context.AudioCapture != null && context.WaveWriter != null)
        {
            context.AudioCapture.DrainTo(context.WaveWriter);
        }

        context.WaveWriter?.Dispose();
        if (context.AudioCapture != null) Object.Destroy(context.AudioCapture);
        if (MatchReplayPlayer.IsActive) MatchReplayPlayer.Stop();
        TryDeleteDirectory(context.TemporaryDirectory);
    }

    private sealed class ExportContext
    {
        internal ExportContext(MatchReplayExportJob job)
        {
            Job = job;
            TemporaryDirectory = Path.Combine(MatchRecordStorage.TemporaryDirectory, job.JobId);
            FramesDirectory = Path.Combine(TemporaryDirectory, "frames");
            WavePath = Path.Combine(TemporaryDirectory, "audio.wav");
            PreviousCaptureFrameRate = Time.captureFramerate;
        }

        internal MatchReplayExportJob Job { get; }
        internal string TemporaryDirectory { get; }
        internal string FramesDirectory { get; }
        internal string WavePath { get; }
        internal int PreviousCaptureFrameRate { get; }
        internal List<string> FramePaths { get; } = new();
        internal List<MatchMediaTimelineEntry> Timeline { get; } = new();
        internal long FrameBytes { get; set; }
        internal ReplayWaveCapture? AudioCapture { get; set; }
        internal ReplayWaveWriter? WaveWriter { get; set; }
    }

    private static byte[] CaptureFrame()
    {
        var source = ScreenCapture.CaptureScreenshotAsTexture();
        if (source == null) throw new InvalidOperationException("无法读取当前游戏画面。");
        var temporary = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D? resized = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            resized = new Texture2D(Width, Height, TextureFormat.RGB24, mipChain: false);
            resized.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, recalculateMipMaps: false);
            resized.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return resized.EncodeToJPG(JpegQuality);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            Object.Destroy(source);
            if (resized != null) Object.Destroy(resized);
        }
    }

    private static bool IsTerminal(string state)
    {
        return state == MatchReplayExportStates.Completed
               || state == MatchReplayExportStates.Failed
               || state == MatchReplayExportStates.Cancelled;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static string NextAvailablePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, name + "-" + index + extension);
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N") + extension);
    }
}
