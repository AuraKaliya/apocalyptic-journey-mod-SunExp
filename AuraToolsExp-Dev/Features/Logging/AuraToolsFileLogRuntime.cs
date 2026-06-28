using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraLog.Shared;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Logging;

public static class AuraToolsFileLogRuntime
{
    private static readonly object Gate = new();
    private static AuraLogFileWriter? writer;
    private static bool unityLogHooked;
    private static bool quittingHooked;

    public static void Initialize(ModConfig config)
    {
        AuraToolsConfigService.Changed += ApplyConfig;
        ApplyConfig();
    }

    public static void RecordCommand(string level, string? tag, string? message)
    {
        if (!AuraToolsConfigService.Root.Logging.Enabled
            || !AuraToolsConfigService.Logging.Enabled
            || !AuraToolsConfigService.Logging.MirrorCommandsLog)
        {
            return;
        }

        Enqueue(new AuraLogRecord(DateTime.Now, "Command", level, Normalize(tag), Normalize(message), null));
    }

    private static void ApplyConfig()
    {
        lock (Gate)
        {
            if (!AuraToolsConfigService.Root.Logging.Enabled || !AuraToolsConfigService.Logging.Enabled)
            {
                StopNoLock();
                return;
            }

            if (writer != null)
            {
                return;
            }

            try
            {
                writer = new AuraLogFileWriter(BuildLogFilePath());
                writer.Enqueue(new AuraLogRecord(DateTime.Now, "AuraTools", "Info", null, "File logging initialized. File: " + writer.FilePath, null));
                PruneOldLogFiles(writer.FilePath);
                if (!unityLogHooked)
                {
                    Application.logMessageReceivedThreaded += OnUnityLog;
                    unityLogHooked = true;
                }

                if (!quittingHooked)
                {
                    Application.quitting += Shutdown;
                    quittingHooked = true;
                }
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("File logging failed to initialize: " + ex.Message);
                StopNoLock();
            }
        }
    }

    private static void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        if (!AuraToolsConfigService.Root.Logging.Enabled
            || !AuraToolsConfigService.Logging.Enabled
            || !AuraToolsConfigService.Logging.MirrorUnityLog)
        {
            return;
        }

        Enqueue(new AuraLogRecord(DateTime.Now, "Unity", type.ToString(), null, Normalize(condition), Normalize(stackTrace)));
    }

    private static void Enqueue(AuraLogRecord record)
    {
        lock (Gate)
        {
            writer?.Enqueue(record);
        }
    }

    private static void Shutdown()
    {
        lock (Gate)
        {
            StopNoLock();
        }
    }

    private static void StopNoLock()
    {
        if (unityLogHooked)
        {
            Application.logMessageReceivedThreaded -= OnUnityLog;
            unityLogHooked = false;
        }

        if (quittingHooked)
        {
            Application.quitting -= Shutdown;
            quittingHooked = false;
        }

        var current = writer;
        writer = null;
        try
        {
            current?.Enqueue(new AuraLogRecord(DateTime.Now, "AuraTools", "Info", null, "File logging stopped.", null));
            current?.Dispose();
        }
        catch
        {
        }
    }

    private static string BuildLogFilePath()
    {
        var date = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = AuraToolsConfigService.Logging.FileNamePattern
            .Replace("{date}", date)
            .Replace("{mod}", "AuraTools");
        return AuraLogRuntime.OwnerLogPath("AuraToolsExp", fileName);
    }

    private static void PruneOldLogFiles(string currentFilePath)
    {
        try
        {
            var maximum = Math.Max(1, AuraToolsConfigService.Logging.MaxRetainedLogFiles);
            var current = Path.GetFullPath(currentFilePath);
            var files = AuraLogRuntime.Enumerate("AuraToolsExp", "*.log")
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderByDescending(file => string.Equals(file.FullName, current, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files.Skip(maximum))
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Log cleanup must never break runtime logging.
                }
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[Logging] old log cleanup failed: " + ex.Message);
        }
    }

    private static string Normalize(string? text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .TrimEnd();
    }
}
