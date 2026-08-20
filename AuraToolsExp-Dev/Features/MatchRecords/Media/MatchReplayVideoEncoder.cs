using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal sealed class ReplayFramePipeline : IDisposable
{
    private readonly BlockingCollection<byte[]> frames;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task completion;
    private Process? process;
    private bool disposed;

    internal ReplayFramePipeline(
        ReplayEncoderDependency dependency,
        string outputPath,
        int width,
        int height,
        int framesPerSecond,
        string? wavePath,
        int capacity = 4)
    {
        frames = new BlockingCollection<byte[]>(Math.Max(2, Math.Min(8, capacity)));
        completion = Task.Run(() => Encode(
            dependency,
            outputPath,
            width,
            height,
            framesPerSecond,
            wavePath,
            cancellation.Token));
    }

    internal Task Completion => completion;

    internal bool TryEnqueue(byte[] frame)
    {
        if (disposed || frame == null || frames.IsAddingCompleted) return false;
        return frames.TryAdd(frame);
    }

    internal void Complete()
    {
        if (!frames.IsAddingCompleted) frames.CompleteAdding();
    }

    internal void Cancel()
    {
        cancellation.Cancel();
        try
        {
            if (process != null && !process.HasExited) process.Kill();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Cancel();
        Complete();
        frames.Dispose();
        cancellation.Dispose();
        process?.Dispose();
        process = null;
    }

    private void Encode(
        ReplayEncoderDependency dependency,
        string outputPath,
        int width,
        int height,
        int framesPerSecond,
        string? wavePath,
        CancellationToken token)
    {
        var arguments = MatchReplayVideoEncodingPolicy.BuildFfmpegArguments(
            width,
            height,
            framesPerSecond,
            wavePath,
            outputPath);
        process = new Process
        {
            StartInfo = new ProcessStartInfo(dependency.FfmpegExecutable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start()) throw new IOException("无法启动受控 FFmpeg 编码器。");
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            foreach (var frame in frames.GetConsumingEnumerable(token))
            {
                token.ThrowIfCancellationRequested();
                process.StandardInput.BaseStream.Write(frame, 0, frame.Length);
            }
            process.StandardInput.Close();
            process.WaitForExit();
            var error = errors.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new IOException("FFmpeg MP4 编码失败：" + error);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(); } catch { }
            }
        }
    }
}
