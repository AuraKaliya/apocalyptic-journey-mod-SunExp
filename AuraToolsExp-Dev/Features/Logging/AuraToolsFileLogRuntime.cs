using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using AuraLog.Shared;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Logging;

public static class AuraToolsFileLogRuntime
{
    private const int MaxStackTraceChars = 4096;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, MirrorWindow> MirrorWindows = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DateTime> LastMirrorByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AuraToolsMirrorDeduplicator CrossSourceDeduplicator = new();
    private static AuraLogFileWriter? writer;
    private static bool unityLogHooked;
    private static bool quittingHooked;

    public static void Initialize(ModConfig config)
    {
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.FileLogging,
            ApplyConfig);
        ApplyConfig();
    }

    public static void RecordCommand(string level, string? tag, string? message)
    {
        if (!AuraToolsConfigService.Logging.Enabled
            || !AuraToolsConfigService.Logging.MirrorCommandsLog
            || !ShouldWrite("Command", NormalizeCommandLevel(level), tag))
        {
            return;
        }

        var normalizedLevel = NormalizeCommandLevel(level);
        var normalizedTag = Normalize(tag);
        var normalizedMessage = Normalize(message);
        if (!AllowMirroredRecord("Command", normalizedLevel, normalizedTag, normalizedMessage))
        {
            return;
        }

        Enqueue(new AuraLogRecord(DateTime.Now, "Command", normalizedLevel, normalizedTag, normalizedMessage, null));
    }

    private static void ApplyConfig()
    {
        lock (Gate)
        {
            if (!AuraToolsConfigService.Logging.Enabled)
            {
                StopNoLock();
                return;
            }

            if (writer != null)
            {
                SyncHooksNoLock();
                return;
            }

            try
            {
                writer = new AuraLogFileWriter(
                    BuildLogFilePath(),
                    AuraToolsConfigService.Logging.MaxQueueLength,
                    AuraToolsConfigService.Logging.FlushIntervalMs);
                if (ShouldWrite("AuraTools", "Info", null))
                {
                    writer.Enqueue(new AuraLogRecord(DateTime.Now, "AuraTools", "Info", null, "File logging initialized. File: " + writer.FilePath, null));
                }

                PruneOldLogFiles(writer.FilePath);
                SyncHooksNoLock();
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
        var level = NormalizeUnityLevel(type);
        if (!AuraToolsConfigService.Logging.Enabled
            || !AuraToolsConfigService.Logging.MirrorUnityLog
            || !UnityTypeAllowed(type)
            || !ShouldWrite("Unity", level, null))
        {
            return;
        }

        var normalizedCondition = Normalize(condition);
        if (!AllowMirroredRecord("Unity", level, type.ToString(), normalizedCondition))
        {
            return;
        }

        Enqueue(new AuraLogRecord(
            DateTime.Now,
            "Unity",
            level,
            type.ToString(),
            normalizedCondition,
            ShouldIncludeStackTrace(level) ? NormalizeStackTrace(stackTrace) : null));
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
        MirrorWindows.Clear();
        LastMirrorByKey.Clear();
        CrossSourceDeduplicator.Clear();
        try
        {
            if (current != null && ShouldWrite("AuraTools", "Info", null))
            {
                current.Enqueue(new AuraLogRecord(DateTime.Now, "AuraTools", "Info", null, "File logging stopped.", null));
            }

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

    private static void SyncHooksNoLock()
    {
        var shouldHookUnity = AuraToolsConfigService.Logging.Enabled
                              && AuraToolsConfigService.Logging.MirrorUnityLog;
        if (shouldHookUnity && !unityLogHooked)
        {
            Application.logMessageReceivedThreaded += OnUnityLog;
            unityLogHooked = true;
        }
        else if (!shouldHookUnity && unityLogHooked)
        {
            Application.logMessageReceivedThreaded -= OnUnityLog;
            unityLogHooked = false;
        }

        if (!quittingHooked)
        {
            Application.quitting += Shutdown;
            quittingHooked = true;
        }
    }

    private static string Normalize(string? text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .TrimEnd();
    }

    private static bool ShouldWrite(string source, string level, string? tag)
    {
        var settings = AuraToolsConfigService.Logging;
        return SourceAllowed(settings, source)
               && LevelRank(level) >= LevelRank(settings.MinimumLevel)
               && (!string.Equals(source, "Command", StringComparison.OrdinalIgnoreCase)
                   || TagAllowed(settings, tag));
    }

    private static bool SourceAllowed(AuraToolsLoggingSettings settings, string source)
    {
        var normalized = source?.Trim() ?? "";
        return settings.EnabledSources.Count == 0
               || settings.EnabledSources.Any(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TagAllowed(AuraToolsLoggingSettings settings, string? tag)
    {
        var normalized = tag?.Trim() ?? "";
        if (settings.IncludedCommandTags.Count > 0
            && !settings.IncludedCommandTags.Any(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return settings.ExcludedCommandTags.Count == 0
               || !settings.ExcludedCommandTags.Any(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool UnityTypeAllowed(LogType type)
    {
        return AuraToolsConfigService.Logging.UnityLogTypes.Any(value =>
            string.Equals(value, type.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldIncludeStackTrace(string level)
    {
        var mode = AuraToolsConfigService.Logging.StackTraceMode;
        if (string.Equals(mode, LoggingStackTraceModes.Off, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(mode, LoggingStackTraceModes.All, StringComparison.OrdinalIgnoreCase)
               || LevelRank(level) >= LevelRank(LoggingLevelNames.Error);
    }

    private static string NormalizeCommandLevel(string? level)
    {
        var text = level?.Trim() ?? "";
        if (string.Equals(text, "Warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Warn", StringComparison.OrdinalIgnoreCase))
        {
            return LoggingLevelNames.Warning;
        }

        if (string.Equals(text, "Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Exception", StringComparison.OrdinalIgnoreCase))
        {
            return LoggingLevelNames.Error;
        }

        if (string.Equals(text, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            return LoggingLevelNames.Debug;
        }

        return LoggingLevelNames.Info;
    }

    private static string NormalizeUnityLevel(LogType type)
    {
        return type switch
        {
            LogType.Warning => LoggingLevelNames.Warning,
            LogType.Error or LogType.Assert or LogType.Exception => LoggingLevelNames.Error,
            _ => LoggingLevelNames.Info
        };
    }

    private static int LevelRank(string? level)
    {
        var normalized = LoggingLevelNames.Normalize(level);
        if (string.Equals(normalized, LoggingLevelNames.Debug, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(normalized, LoggingLevelNames.Warning, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return string.Equals(normalized, LoggingLevelNames.Error, StringComparison.OrdinalIgnoreCase) ? 3 : 1;
    }

    private static bool AllowMirroredRecord(string source, string level, string? tag, string message)
    {
        var now = DateTime.UtcNow;
        lock (Gate)
        {
            if (!CrossSourceDeduplicator.Allow(source, level, tag, message, now))
            {
                return false;
            }

            var sourceKey = (source ?? "").Trim();
            if (!MirrorWindows.TryGetValue(sourceKey, out var window))
            {
                window = new MirrorWindow(now);
                MirrorWindows[sourceKey] = window;
            }

            if ((now - window.StartedUtc).TotalMilliseconds >= 1000d)
            {
                window.StartedUtc = now;
                window.Count = 0;
            }

            if (window.Count >= MirrorLimitPerSecond(level))
            {
                return false;
            }

            var duplicateKey = sourceKey
                               + "|"
                               + LoggingLevelNames.Normalize(level)
                               + "|"
                               + (tag ?? "")
                               + "|"
                               + StableMessageKey(message);
            if (LastMirrorByKey.TryGetValue(duplicateKey, out var previous)
                && (now - previous).TotalMilliseconds < DuplicateWindowMs(level))
            {
                return false;
            }

            LastMirrorByKey[duplicateKey] = now;
            if (LastMirrorByKey.Count > 1024)
            {
                PruneDuplicateKeys(now);
            }

            window.Count++;
            return true;
        }
    }

    private static int MirrorLimitPerSecond(string level)
    {
        return LevelRank(level) >= LevelRank(LoggingLevelNames.Error)
            ? 120
            : LevelRank(level) >= LevelRank(LoggingLevelNames.Warning)
                ? 80
                : 60;
    }

    private static double DuplicateWindowMs(string level)
    {
        return LevelRank(level) >= LevelRank(LoggingLevelNames.Error) ? 1000d : 1500d;
    }

    private static string StableMessageKey(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        return message.Length <= 256 ? message : message.Substring(0, 256);
    }

    private static string NormalizeStackTrace(string value)
    {
        var normalized = Normalize(value);
        return normalized.Length <= MaxStackTraceChars
            ? normalized
            : normalized.Substring(0, MaxStackTraceChars) + "\n... <stack trace truncated>";
    }

    private static void PruneDuplicateKeys(DateTime now)
    {
        foreach (var key in LastMirrorByKey
                     .Where(pair => (now - pair.Value).TotalSeconds > 10d)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            LastMirrorByKey.Remove(key);
        }
    }

    private sealed class MirrorWindow
    {
        public MirrorWindow(DateTime startedUtc)
        {
            StartedUtc = startedUtc;
        }

        public DateTime StartedUtc { get; set; }

        public int Count { get; set; }
    }
}
