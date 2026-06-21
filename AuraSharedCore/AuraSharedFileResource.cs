using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AuraShared.Core;

public static class AuraSharedFileResource
{
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

    public static string EnsureDirectory(params string[] segments)
    {
        var path = Path.Combine(segments);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string SafeFolderName(string id, string fallback = "unknown")
    {
        return AuraSharedIdentity.SafeId(id, fallback);
    }

    public static bool IsSupportedAudioFile(string path)
    {
        return HasSupportedExtension(path, AudioExtensions);
    }

    public static bool IsSupportedImageFile(string path)
    {
        return HasSupportedExtension(path, ImageExtensions);
    }

    public static bool HasSupportedExtension(string path, params string[] supportedExtensions)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        foreach (var supported in supportedExtensions)
        {
            if (string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void OpenDirectory(
        string directory,
        Action<string>? info = null,
        Action<string>? warn = null,
        string threadName = "AuraShared.OpenDirectory")
    {
        string fullPath;
        try
        {
            Directory.CreateDirectory(directory);
            fullPath = Path.GetFullPath(directory);
        }
        catch (Exception ex)
        {
            warn?.Invoke("Failed to open directory " + directory + ": " + ex.Message);
            return;
        }

        info?.Invoke("Opening directory: " + fullPath);
        var thread = new Thread(() => OpenDirectoryOnWorker(fullPath, warn))
        {
            IsBackground = true,
            Name = threadName
        };
        thread.Start();
    }

    private static void OpenDirectoryOnWorker(string directory, Action<string>? warn)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = QuoteArgument(directory),
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            warn?.Invoke("Failed to open directory " + directory + ": " + ex.Message);
        }
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
