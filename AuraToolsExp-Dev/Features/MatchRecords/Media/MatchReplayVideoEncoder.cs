using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayVideoEncoder
{
    internal static string Encode(
        ReplayFrameSpool spool,
        string outputBase,
        int width,
        int height,
        int framesPerSecond,
        string? wavePath,
        MatchReplayVideoSettings settings,
        Func<bool> cancelled)
    {
        spool.Complete();
        var ffmpeg = settings.PreferMp4 ? FindFfmpeg(settings.FfmpegPath) : null;
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            var mp4 = outputBase + ".mp4";
            try
            {
                EncodeMp4(ffmpeg!, spool.Path, mp4, framesPerSecond, wavePath, cancelled);
                return mp4;
            }
            catch when (!cancelled())
            {
                TryDelete(mp4);
            }
        }

        var avi = outputBase + ".avi";
        MjpegAviWriter.WriteFromSpool(
            avi,
            spool.Path,
            spool.FrameCount,
            spool.MaximumFrameBytes,
            spool.PayloadBytes,
            width,
            height,
            framesPerSecond,
            wavePath,
            cancelled);
        return avi;
    }

    internal static string? FindFfmpeg(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return Path.GetFullPath(configuredPath);
        var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
            }
        }

        return null;
    }

    private static void EncodeMp4(
        string ffmpeg,
        string spoolPath,
        string output,
        int framesPerSecond,
        string? wavePath,
        Func<bool> cancelled)
    {
        var temporary = output + ".tmp.mp4";
        TryDelete(temporary);
        var audio = !string.IsNullOrWhiteSpace(wavePath) && File.Exists(wavePath);
        var arguments = "-hide_banner -loglevel error -y -f image2pipe -vcodec mjpeg -framerate "
                        + framesPerSecond.ToString(CultureInfo.InvariantCulture) + " -i pipe:0 "
                        + (audio ? "-i \"" + wavePath + "\" -shortest -c:a aac -b:a 160k " : "-an ")
                        + "-c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -movflags +faststart \"" + temporary + "\"";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            foreach (var frame in ReplayFrameSpool.Read(spoolPath))
            {
                if (cancelled()) throw new OperationCanceledException();
                process.StandardInput.BaseStream.Write(frame, 0, frame.Length);
            }
            process.StandardInput.Close();
            process.WaitForExit();
            var error = errors.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !File.Exists(temporary))
            {
                throw new IOException("FFmpeg 编码失败：" + error);
            }
            File.Move(temporary, output);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(); } catch { }
            }
            process.Dispose();
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
