using System;
using System.Diagnostics;
using System.IO;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class ReplayLegacyMediaTranscoder
{
    internal static MatchMediaAsset TranscodeOrValidateAndImport(string recordId, string source)
    {
        if (string.Equals(Path.GetExtension(source), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return MatchReplayMediaStore.ImportFile(recordId, source);
        }
        var dependency = ReplayEncoderDependency.LoadVerified();
        var temporary = Path.Combine(
            MatchRecordStorage.TemporaryDirectory,
            "legacy-media-" + Guid.NewGuid().ToString("N") + ".partial.mp4");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(
                    dependency.FfmpegExecutable,
                    MatchReplayVideoEncodingPolicy.BuildLegacyTranscodeArguments(source, temporary))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(temporary))
            {
                throw new IOException("旧媒体转码失败：" + error.GetAwaiter().GetResult());
            }
            return MatchReplayMediaStore.ImportFile(recordId, temporary);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
