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
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayVideoExporter
{
    private const float PreparationTimeoutMilliseconds = 6000f;
    private const int StablePreparationFrames = 2;
    private const double TailSeconds = 0.5d;
    private static MatchReplayExportJob? current;
    private static bool cancelRequested;
    private static bool captureColorPolicyLogged;

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

    internal static bool TryStart(string recordId, Action closeOrigin, out string message)
    {
        message = "";
        if (current != null && !IsTerminal(current.State))
        {
            message = "已有视频导出任务正在运行。";
            return false;
        }

        var settings = SnapshotSettings();
        cancelRequested = false;
        var job = new MatchReplayExportJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            RecordId = recordId,
            State = MatchReplayExportStates.Preparing,
            Message = "关闭原界面并等待战斗场景",
            EstimatedBytes = 0
        };
        current = job;
        Persist(job);
        MatchReplayExportControlsPresenter.Show();

        var accepted = MatchReplayLaunchCoordinator.TryStartForExport(
            recordId,
            closeOrigin,
            () => BeginCapture(job, settings),
            result => FailBeforeCapture(job, result),
            out var launchMessage);
        if (!accepted)
        {
            FailBeforeCapture(job, launchMessage);
            MatchReplayExportControlsPresenter.Close();
            message = launchMessage;
            return false;
        }

        message = launchMessage;
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

    private static void BeginCapture(MatchReplayExportJob job, MatchReplayVideoSettings settings)
    {
        if (current != job || IsTerminal(job.State))
        {
            if (MatchReplayPlayer.IsActive) MatchReplayPlayer.Stop();
            return;
        }

        if (cancelRequested)
        {
            job.State = MatchReplayExportStates.Cancelled;
            job.Message = "导出任务已取消";
            Persist(job);
            if (MatchReplayPlayer.IsActive) MatchReplayPlayer.Stop();
            return;
        }

        var dimensions = Dimensions(settings.Quality);
        var estimatedBytes = EstimateBytes(
            Math.Max(5000L, MatchReplayPlayer.DurationMilliseconds + 1000L),
            dimensions.width,
            dimensions.height,
            settings.FramesPerSecond,
            settings.IncludeAudio);
        job.EstimatedBytes = estimatedBytes;
        if (!HasFreeSpace(estimatedBytes, out var available))
        {
            FailBeforeCapture(job,
                "预计需要 " + FormatBytes(estimatedBytes) + "，临时目录仅剩 " + FormatBytes(available) + "。");
            MatchReplayPlayer.Stop();
            return;
        }

        job.Message = "等待纯净战斗画面，预计临时空间 " + FormatBytes(estimatedBytes);
        Persist(job);
        if (AuraToolsMatchRecordsRuntime.StartRuntimeCoroutine(Capture(job, settings)) == null)
        {
            FailBeforeCapture(job, "无法启动导出协程。");
            MatchReplayPlayer.Stop();
        }
    }

    private static void FailBeforeCapture(MatchReplayExportJob job, string reason)
    {
        if (IsTerminal(job.State)) return;
        job.State = MatchReplayExportStates.Failed;
        job.Message = string.IsNullOrWhiteSpace(reason) ? "无法开始视频导出。" : reason;
        Persist(job);
        AuraToolsLog.Warn("[MatchRecords] video export launch failed: " + job.Message);
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

        var preparationElapsed = 0f;
        var stableFrames = 0;
        MatchReplayExportReadinessState readiness = default;
        while (!cancelRequested && MatchReplayPlayer.IsActive)
        {
            yield return new WaitForEndOfFrame();
            readiness = ReadReadiness();
            stableFrames = readiness.CanCapture ? stableFrames + 1 : 0;
            if (stableFrames >= StablePreparationFrames) break;
            preparationElapsed += Math.Max(1f, Time.unscaledDeltaTime * 1000f);
            if (preparationElapsed >= PreparationTimeoutMilliseconds)
            {
                throw new TimeoutException("战斗画面准备超时：" + DescribeReadiness(readiness));
            }
        }

        if (cancelRequested) throw new OperationCanceledException();
        if (!MatchReplayPlayer.IsActive)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(MatchReplayPlayer.LastStartFailure)
                    ? "回放环境在视频导出前意外停止。"
                    : MatchReplayPlayer.LastStartFailure);
        }

        context.Visibility = new BattleCaptureVisibility(context.Settings.IncludeUi);
        var listener = context.Settings.IncludeAudio ? Object.FindAnyObjectByType<AudioListener>() : null;
        if (listener != null)
        {
            context.AudioCapture = listener.gameObject.AddComponent<ReplayWaveCapture>();
            context.WaveWriter = new ReplayWaveWriter(context.WavePath, AudioSettings.outputSampleRate, 2);
            context.AudioCapture.BeginCapture();
            context.RealtimeAudio = true;
        }
        else if (context.Settings.IncludeAudio)
        {
            AuraToolsLog.Warn("[MatchRecords] no AudioListener was available; export continues without audio.");
        }

        if (!context.RealtimeAudio)
        {
            Time.captureFramerate = context.FramesPerSecond;
        }

        context.Job.State = MatchReplayExportStates.Rendering;
        context.Job.Message = "仅录制战斗画面 " + context.Width + "x" + context.Height + " / "
                              + context.FramesPerSecond + " FPS"
                              + (context.RealtimeAudio ? " / DSP 实时音频" : " / 无音频快速模式");
        Persist(context.Job);

        var lastTurn = 0;
        var tailFrames = 0;
        var frameClock = new MatchReplayExportFrameClock(context.FramesPerSecond);
        var startedAtDsp = AudioSettings.dspTime;
        var lastDsp = startedAtDsp;
        var tailEndsAtDsp = -1d;
        frameClock.Start(startedAtDsp);

        while (!cancelRequested && MatchReplayPlayer.IsActive)
        {
            if (MatchReplayPlayer.HasBlockingError)
            {
                throw new InvalidDataException("回放已失步，视频导出停止：" + MatchReplayPlayer.PlaybackIssue);
            }

            var framesDue = 1;
            if (context.RealtimeAudio)
            {
                var now = AudioSettings.dspTime;
                if (!MatchReplayPlayer.IsFinished)
                {
                    MatchReplayPlayer.AdvanceExportClock((float)Math.Max(0d, now - lastDsp) * 1000f);
                }
                lastDsp = now;
                if (MatchReplayPlayer.IsFinished)
                {
                    if (tailEndsAtDsp < 0d) tailEndsAtDsp = now + TailSeconds;
                    if (now >= tailEndsAtDsp) break;
                }

                framesDue = frameClock.DueFrames(now);
                context.AudioCapture?.DrainTo(context.WaveWriter!);
                if (framesDue <= 0)
                {
                    yield return null;
                    continue;
                }
            }
            else if (!MatchReplayPlayer.IsFinished)
            {
                MatchReplayPlayer.AdvanceExportClock(1000f / context.FramesPerSecond);
            }
            else if (++tailFrames > Math.Max(1, context.FramesPerSecond / 2))
            {
                break;
            }

            byte[] bytes;
            MatchReplayExportControlsPresenter.SetCaptured(true);
            context.Visibility.HideForCapture();
            try
            {
                yield return new WaitForEndOfFrame();
                bytes = CaptureFrame(context.Width, context.Height, context.JpegQuality);
            }
            finally
            {
                context.Visibility.RestoreAfterCapture();
                MatchReplayExportControlsPresenter.SetCaptured(false);
            }

            for (var duplicate = 0; duplicate < framesDue; duplicate++)
            {
                context.FrameSpool.Enqueue(bytes);
            }
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
        if (context.WaveWriter != null)
        {
            var sampleFrames = MatchReplayExportFrameClock.ExpectedPcmSampleFrames(
                context.FrameSpool.FrameCount,
                context.FramesPerSecond,
                context.WaveWriter.SampleRate);
            context.WaveWriter.NormalizeLength(sampleFrames);
            context.WaveWriter.Dispose();
            context.WaveWriter = null;
        }
        context.FrameSpool.Complete();
        MatchReplayPlayer.Stop();
        Time.captureFramerate = context.PreviousCaptureFrameRate;
        context.Visibility.Dispose();
        context.Visibility = null;

        context.Job.State = MatchReplayExportStates.Encoding;
        context.Job.Progress = 0.82f;
        context.Job.Message = context.Settings.PreferMp4
            ? "后台编码 MP4（FFmpeg 不可用时生成单文件 AVI）"
            : "后台封装交织式 MJPEG/PCM AVI";
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

        context.Job.OutputPath = asset.FilePath;
        context.Job.State = MatchReplayExportStates.Completed;
        context.Job.Progress = 1f;
        context.Job.Message = "已保存单个视频文件 " + Path.GetFileName(output);
        Persist(context.Job);
    }

    private static MatchReplayExportReadinessState ReadReadiness()
    {
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        return new MatchReplayExportReadinessState(
            MatchReplayPlayer.IsReadyForExport,
            fightUi != null && fightUi.gameObject != null && fightUi.gameObject.activeInHierarchy,
            MatchReplayUiLifecycle.SettingUiCount,
            MatchReplayUiLifecycle.SnapshotOriginTransitionRoots().Count);
    }

    private static string DescribeReadiness(MatchReplayExportReadinessState state)
    {
        return "replay=" + state.ReplayReady
               + ",fightUI=" + state.FightUiReady
               + ",settings=" + state.SettingUiCount
               + ",originOverlays=" + state.OriginOverlayCount;
    }

    private static void Cleanup(ExportContext context)
    {
        MatchReplayExportControlsPresenter.SetCaptured(false);
        Time.captureFramerate = context.PreviousCaptureFrameRate;
        context.Visibility?.RestoreAfterCapture();
        context.Visibility?.Dispose();
        context.Visibility = null;
        context.AudioCapture?.EndCapture();
        if (context.AudioCapture != null && context.WaveWriter != null) context.AudioCapture.DrainTo(context.WaveWriter);
        context.WaveWriter?.Dispose();
        context.WaveWriter = null;
        context.FrameSpool?.Dispose();
        if (context.AudioCapture != null) Object.Destroy(context.AudioCapture);
        if (MatchReplayPlayer.IsActive) MatchReplayPlayer.Stop();
        TryDeleteDirectory(context.TemporaryDirectory);
    }

    private sealed class ExportContext
    {
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
        internal BattleCaptureVisibility? Visibility { get; set; }
        internal bool RealtimeAudio { get; set; }
    }

    private sealed class BattleCaptureVisibility : IDisposable
    {
        private readonly List<CanvasGroupState> groups = new();
        private bool hidden;

        internal BattleCaptureVisibility(bool includeBattleHud)
        {
            var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
            var manager = WitchUiManager.Instance;
            var roots = (manager?.GetAllUI() ?? Array.Empty<Witch.UI.UIBase>())
                .Where(item => item != null && item.gameObject != null && item.gameObject.activeInHierarchy)
                .Select(item => item.gameObject)
                .Concat(Object.FindObjectsByType<AuraToolsOwnedOverlay>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .Where(item => item != null && item.gameObject != null && item.gameObject.activeInHierarchy)
                    .Select(item => item.gameObject))
                .Where(root => root != null)
                .Distinct()
                .Where(root => !includeBattleHud || fightUi == null || root != fightUi.gameObject);
            foreach (var root in roots)
            {
                var group = root.GetComponent<CanvasGroup>();
                var added = group == null;
                group ??= root.AddComponent<CanvasGroup>();
                groups.Add(new CanvasGroupState(group, added));
            }
        }

        internal void HideForCapture()
        {
            if (hidden) return;
            hidden = true;
            foreach (var state in groups) state.Hide();
        }

        internal void RestoreAfterCapture()
        {
            if (!hidden) return;
            foreach (var state in groups) state.Restore();
            hidden = false;
        }

        public void Dispose()
        {
            RestoreAfterCapture();
            foreach (var state in groups) state.Dispose();
            groups.Clear();
        }

        private sealed class CanvasGroupState : IDisposable
        {
            private readonly CanvasGroup group;
            private readonly bool added;
            private readonly float alpha;
            private readonly bool interactable;
            private readonly bool blocksRaycasts;

            internal CanvasGroupState(CanvasGroup group, bool added)
            {
                this.group = group;
                this.added = added;
                alpha = group.alpha;
                interactable = group.interactable;
                blocksRaycasts = group.blocksRaycasts;
            }

            internal void Hide()
            {
                if (group == null) return;
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            internal void Restore()
            {
                if (group == null) return;
                group.alpha = alpha;
                group.interactable = interactable;
                group.blocksRaycasts = blocksRaycasts;
            }

            public void Dispose()
            {
                Restore();
                if (added && group != null) Object.Destroy(group);
            }
        }
    }

    private static byte[] CaptureFrame(int width, int height, int quality)
    {
        var source = ScreenCapture.CaptureScreenshotAsTexture();
        if (source == null) throw new InvalidOperationException("无法读取当前游戏画面。");
        var colorPolicy = MatchReplayCaptureColorPolicy.PreserveDisplayPixels(source.isDataSRGB);
        var readWrite = colorPolicy.UseSrgbRenderTarget
            ? RenderTextureReadWrite.sRGB
            : RenderTextureReadWrite.Linear;
        var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, readWrite);
        var previous = RenderTexture.active;
        var previousSrgbWrite = GL.sRGBWrite;
        Texture2D? resized = null;
        try
        {
            GL.sRGBWrite = colorPolicy.EnableSrgbWrite;
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            resized = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            resized.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, recalculateMipMaps: false);
            resized.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            if (!captureColorPolicyLogged)
            {
                captureColorPolicyLogged = true;
                AuraToolsLog.Info("[MatchRecords] capture color policy: active="
                                  + QualitySettings.activeColorSpace
                                  + ",sourceSrgb=" + source.isDataSRGB
                                  + ",targetSrgb=" + temporary.sRGB
                                  + ",srgbWrite=" + colorPolicy.EnableSrgbWrite + ".");
            }
            return resized.EncodeToJPG(quality);
        }
        finally
        {
            GL.sRGBWrite = previousSrgbWrite;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            Object.Destroy(source);
            if (resized != null) Object.Destroy(resized);
        }
    }

    private static MatchReplayVideoSettings SnapshotSettings()
    {
        var source = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.Video;
        source.Normalize();
        var result = new MatchReplayVideoSettings
        {
            Quality = source.Quality,
            FramesPerSecond = source.FramesPerSecond,
            IncludeUi = source.IncludeUi,
            IncludeAudio = source.IncludeAudio,
            PreferMp4 = source.PreferMp4,
            FfmpegPath = source.FfmpegPath
        };
        result.Normalize();
        return result;
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
            if (!File.Exists(candidate + ".mp4") && !File.Exists(candidate + ".avi")) return candidate;
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
