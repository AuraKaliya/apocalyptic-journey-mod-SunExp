using System;
using System.Globalization;
using System.IO;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraShared.Core;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Logging;

public static class AuraToolsFileLogRuntime
{
    private static readonly object Gate = new();
    private static AuraToolsLogFileWriter? writer;
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

        Enqueue(new AuraToolsLogRecord(DateTime.Now, "Command", level, Normalize(tag), Normalize(message), null));
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
                writer = new AuraToolsLogFileWriter(BuildLogFilePath());
                writer.Enqueue(new AuraToolsLogRecord(DateTime.Now, "AuraTools", "Info", null, "File logging initialized. File: " + writer.FilePath, null));
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

        Enqueue(new AuraToolsLogRecord(DateTime.Now, "Unity", type.ToString(), null, Normalize(condition), Normalize(stackTrace)));
    }

    private static void Enqueue(AuraToolsLogRecord record)
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
            current?.Enqueue(new AuraToolsLogRecord(DateTime.Now, "AuraTools", "Info", null, "File logging stopped.", null));
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
        return AuraSharedLogStore.OwnerLogPath("AuraToolsExp", fileName);
    }

    private static string Normalize(string? text)
    {
        return (text ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .TrimEnd();
    }
}
