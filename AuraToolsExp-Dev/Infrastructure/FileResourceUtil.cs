using System;
using System.IO;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Infrastructure;

public static class FileResourceUtil
{
    private static readonly string[] SupportedAudioExtensions = { ".mp3", ".wav", ".ogg" };
    private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg" };

    public static string EnsureDirectory(params string[] segments)
    {
        return AuraSharedFileResource.EnsureDirectory(segments);
    }

    public static string SafeFolderName(string id)
    {
        return AuraSharedFileResource.SafeFolderName(id);
    }

    public static string RoleAudioDirectory(string roleId)
    {
        return EnsureDirectory(
            AuraToolsConfigService.AudioDirectory,
            "Roles",
            SafeFolderName(roleId));
    }

    public static string RoleSkillCgDirectory(string roleId)
    {
        return EnsureDirectory(
            AuraToolsConfigService.CgDirectory,
            "Roles",
            SafeFolderName(roleId));
    }

    public static string CommonAudioDirectory()
    {
        return EnsureDirectory(
            AuraToolsConfigService.AudioDirectory,
            "Common");
    }

    public static void OpenDirectory(string directory)
    {
        AuraSharedFileResource.OpenDirectory(
            directory,
            message => AuraToolsLog.Info("[FileResource] " + message),
            AuraToolsLog.Warn,
            "AuraTools.OpenDirectory");
    }

    public static bool IsSupportedAudioFile(string path)
    {
        return AuraSharedFileResource.HasSupportedExtension(path, SupportedAudioExtensions);
    }

    public static bool IsSupportedImageFile(string path)
    {
        return AuraSharedFileResource.HasSupportedExtension(path, SupportedImageExtensions);
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

        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".dat";
        }

        var targetPath = Path.Combine(
            targetDirectory,
            SafeFolderName(targetBaseName) + extension);
        var destination = AuraToolsConfigService.ToDataRelativePath(targetPath);
        var system = AuraToolsPaths.IsInsideDirectory(targetPath, AuraToolsConfigService.AudioDirectory)
            ? AuraSharedSystems.Audio
            : AuraToolsPaths.IsInsideDirectory(targetPath, AuraToolsConfigService.CgDirectory)
                ? AuraSharedSystems.Cg
                : "Files";
        var response = AuraSharedPackageEngine.Install(AuraToolsIds.ModId, new AuraSharedInstallRequest
        {
            OwnerModId = AuraToolsIds.ModId,
            System = system,
            LogicalId = destination,
            PackageId = "AuraTools.UserImports",
            PackageVersion = DateTime.UtcNow.Ticks,
            Kind = AuraSharedResourceKinds.File,
            SourcePath = sourcePath,
            DestinationRelativePath = destination
        });
        if (!response.Success)
        {
            throw new IOException("Shared resource import failed: " + response.Message);
        }

        return destination;
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
            try
            {
                var registered = CopyIntoData(
                    fullPath,
                    Path.GetDirectoryName(fullPath) ?? targetDirectory,
                    Path.GetFileNameWithoutExtension(fullPath));
                message = "已使用 ModsData 内" + label + "。";
                return registered;
            }
            catch (Exception ex)
            {
                message = label + "注册失败：" + ex.Message;
                return "";
            }
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

    private static string NormalizePathInput(string value)
    {
        return (value ?? "").Trim().Trim('"');
    }
}
