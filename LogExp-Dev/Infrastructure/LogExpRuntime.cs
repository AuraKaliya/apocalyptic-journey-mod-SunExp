using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using Witch.Mod;

namespace LogExp.Dll.Infrastructure;

public static class LogExpRuntime
{
    private static readonly object SyncRoot = new object();
    private static LogFileWriter? writer;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            if (initialized)
            {
                return;
            }

            LogFileWriter? newWriter = null;
            var logHooked = false;
            try
            {
                newWriter = new LogFileWriter(BuildLogFilePath(modConfig));
                newWriter.Enqueue(new LogRecord(DateTime.Now, "LogExp", "Info", null, "LogExp initialized. File: " + newWriter.FilePath, null));
                Application.logMessageReceivedThreaded += OnUnityLog;
                logHooked = true;
                Application.quitting += Shutdown;
                writer = newWriter;
                initialized = true;
                newWriter = null;
            }
            catch (Exception ex)
            {
                if (logHooked)
                {
                    try
                    {
                        Application.logMessageReceivedThreaded -= OnUnityLog;
                    }
                    catch
                    {
                    }
                }

                SafeDispose(newWriter);
                ReportInitializationFailure(ex);
            }
        }
    }

    public static void RecordCommand(string level, string? tag, string? message)
    {
        Enqueue(new LogRecord(DateTime.Now, "Command", level, Normalize(tag), Normalize(message), null));
    }

    private static void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        Enqueue(new LogRecord(DateTime.Now, "Unity", type.ToString(), null, Normalize(condition), Normalize(stackTrace)));
    }

    private static void Shutdown()
    {
        LogFileWriter? writerToDispose;
        lock (SyncRoot)
        {
            Application.logMessageReceivedThreaded -= OnUnityLog;
            Application.quitting -= Shutdown;
            writerToDispose = writer;
            writer = null;
            initialized = false;
        }

        writerToDispose?.Enqueue(new LogRecord(DateTime.Now, "LogExp", "Info", null, "LogExp shutdown.", null));
        writerToDispose?.Dispose();
    }

    private static void Enqueue(LogRecord record)
    {
        LogFileWriter? currentWriter;
        lock (SyncRoot)
        {
            currentWriter = writer;
        }

        currentWriter?.Enqueue(record);
    }

    private static string BuildLogFilePath(ModConfig modConfig)
    {
        var root = GetWritableRoot(modConfig);
        var fileName = "Witch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log";
        return Path.Combine(root, "Logs", fileName);
    }

    private static string GetWritableRoot(ModConfig modConfig)
    {
        if (!string.IsNullOrWhiteSpace(modConfig.DirectoryName))
        {
            return modConfig.DirectoryName;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(Application.persistentDataPath))
            {
                return Application.persistentDataPath;
            }
        }
        catch
        {
        }

        return Environment.CurrentDirectory;
    }

    private static string Normalize(string? text)
    {
        var value = text ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }

        return value.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
    }

    private static void ReportInitializationFailure(Exception ex)
    {
        try
        {
            Debug.LogWarning("LogExp failed to initialize file logging: " + ex.Message);
        }
        catch
        {
        }
    }

    private static void SafeDispose(LogFileWriter? writerToDispose)
    {
        try
        {
            writerToDispose?.Dispose();
        }
        catch
        {
        }
    }
}
