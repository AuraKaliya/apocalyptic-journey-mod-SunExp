using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Infrastructure;

public static class FileResourceUtil
{
    private static readonly string[] SupportedAudioExtensions = { ".mp3", ".wav", ".ogg" };
    private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg" };

    public static string EnsureDirectory(params string[] segments)
    {
        var path = Path.Combine(segments);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string SafeFolderName(string id)
    {
        var value = string.IsNullOrWhiteSpace(id) ? "unknown" : id.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }

    public static string RoleAudioDirectory(string roleId)
    {
        return EnsureDirectory(
            AuraToolsConfigService.ResourceDirectory,
            "Audio",
            "Roles",
            SafeFolderName(roleId));
    }

    public static string RoleSkillCgDirectory(string roleId)
    {
        return EnsureDirectory(
            AuraToolsConfigService.ResourceDirectory,
            "SkillCG",
            "Roles",
            SafeFolderName(roleId));
    }

    public static string CommonAudioDirectory()
    {
        return EnsureDirectory(
            AuraToolsConfigService.ResourceDirectory,
            "Audio",
            "Common");
    }

    public static void OpenDirectory(string directory)
    {
        string fullPath;
        try
        {
            Directory.CreateDirectory(directory);
            fullPath = Path.GetFullPath(directory);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Failed to open directory " + directory + ": " + ex.Message);
            return;
        }

        AuraToolsLog.Info("[FileResource] opening directory: " + fullPath);
        var thread = new Thread(() => OpenDirectoryOnWorker(fullPath))
        {
            IsBackground = true,
            Name = "AuraTools.OpenDirectory"
        };
        thread.Start();
    }

    public static bool IsSupportedAudioFile(string path)
    {
        return HasSupportedExtension(path, SupportedAudioExtensions);
    }

    public static bool IsSupportedImageFile(string path)
    {
        return HasSupportedExtension(path, SupportedImageExtensions);
    }

    public static string ImportAudioPath(string inputPath, string targetDirectory, string targetBaseName, out string message)
    {
        return ImportPath(
            inputPath,
            targetDirectory,
            targetBaseName,
            IsSupportedAudioFile,
            "音频",
            "仅支持 mp3、wav、ogg 音频文件。",
            out message);
    }

    public static string ImportImagePath(string inputPath, string targetDirectory, string targetBaseName, out string message)
    {
        return ImportPath(
            inputPath,
            targetDirectory,
            targetBaseName,
            IsSupportedImageFile,
            "图片",
            "仅支持 png、jpg、jpeg 图片文件。",
            out message);
    }

    public static string CopyIntoMod(string sourcePath, string targetDirectory, string targetBaseName)
    {
        return CopyIntoData(sourcePath, targetDirectory, targetBaseName);
    }

    public static string CopyIntoData(string sourcePath, string targetDirectory, string targetBaseName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return "";
        }

        Directory.CreateDirectory(targetDirectory);
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".dat";
        }

        var targetPath = Path.Combine(targetDirectory, targetBaseName + extension);
        if (!AuraToolsPaths.IsSamePath(sourcePath, targetPath))
        {
            File.Copy(sourcePath, targetPath, true);
        }

        return AuraToolsConfigService.ToDataRelativePath(targetPath);
    }

    private static string ImportPath(
        string inputPath,
        string targetDirectory,
        string targetBaseName,
        Func<string, bool> supported,
        string label,
        string unsupportedMessage,
        out string message)
    {
        message = "";
        var candidate = NormalizePathInput(inputPath);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            message = label + "路径为空。";
            return "";
        }

        string fullPath;
        try
        {
            fullPath = ResolveInputPath(candidate);
        }
        catch (Exception ex)
        {
            message = label + "路径无效：" + ex.Message;
            return "";
        }

        if (!File.Exists(fullPath))
        {
            message = label + "文件不存在：" + candidate;
            return "";
        }

        if (!supported(fullPath))
        {
            message = unsupportedMessage;
            return "";
        }

        if (AuraToolsPaths.IsInsideDataRoot(fullPath))
        {
            message = "已使用 ModsData 内" + label + "。";
            return AuraToolsConfigService.ToDataRelativePath(fullPath);
        }

        try
        {
            var relative = CopyIntoData(fullPath, targetDirectory, targetBaseName);
            message = string.IsNullOrWhiteSpace(relative) ? "复制" + label + "失败。" : "已复制" + label + "。";
            return relative;
        }
        catch (Exception ex)
        {
            message = "复制" + label + "失败：" + ex.Message;
            return "";
        }
    }

    private static string ResolveInputPath(string inputPath)
    {
        var normalized = inputPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : AuraToolsConfigService.ResolveConfiguredPath(inputPath);
    }

    private static bool HasSupportedExtension(string path, string[] supportedExtensions)
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

    private static string NormalizePathInput(string value)
    {
        return (value ?? "").Trim().Trim('"');
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void OpenDirectoryOnWorker(string directory)
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
            AuraToolsLog.Warn("Failed to open directory " + directory + ": " + ex.Message);
        }
    }
}
