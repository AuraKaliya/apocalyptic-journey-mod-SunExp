using System;
using System.IO;
using AuraAudio.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Infrastructure;

public static class FileResourceUtil
{
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
        return EnsureDirectory(AuraToolsConfigService.DataRootDirectory,
            AuraSharedResourcePathPolicy.StorageResourcePath(
                AuraToolsConfigService.DataRootDirectory,
                Scope(AuraSharedSystems.Audio, "LocalAudio", "Role", roleId),
                AuraToolsIds.ModId,
                DirectoryResource("user-imports")));
    }

    public static string RoleSkillCgDirectory(string roleId)
    {
        return EnsureDirectory(AuraToolsConfigService.DataRootDirectory,
            AuraSharedResourcePathPolicy.StorageResourcePath(
                AuraToolsConfigService.DataRootDirectory,
                Scope(AuraSharedSystems.Cg, "SkillCg", "Role", roleId),
                AuraToolsIds.ModId,
                DirectoryResource("user-imports")));
    }

    public static string CommonAudioDirectory()
    {
        return EnsureDirectory(AuraToolsConfigService.DataRootDirectory,
            AuraSharedResourcePathPolicy.StorageResourcePath(
                AuraToolsConfigService.DataRootDirectory,
                Scope(AuraSharedSystems.Audio, "LocalAudio", "Global", "all"),
                AuraToolsIds.ModId,
                DirectoryResource("user-imports")));
    }

    private static AuraSharedScopeKey Scope(string module, string feature, string scopeType, string scopeId)
    {
        return new AuraSharedScopeKey
        {
            ModuleId = module,
            FeatureId = feature,
            ScopeType = scopeType,
            ScopeId = SafeFolderName(scopeId)
        };
    }

    private static AuraSharedResourceDeclarationV4 DirectoryResource(string resourceId)
    {
        return new AuraSharedResourceDeclarationV4
        {
            ResourceId = resourceId,
            Kind = AuraSharedResourceKinds.Directory
        };
    }

    public static bool RegisterManualDirectory(
        string moduleId,
        string featureId,
        string scopeType,
        string scopeId,
        string scopeOwnerModId,
        string resourceId,
        string directory,
        out string message)
    {
        var declaration = new AuraSharedResourceDeclarationV4
        {
            ModuleId = moduleId,
            FeatureId = featureId,
            ScopeType = scopeType,
            ScopeId = SafeFolderName(scopeId),
            ScopeOwnerModId = string.IsNullOrWhiteSpace(scopeOwnerModId) ? AuraToolsIds.ModId : scopeOwnerModId,
            ScopeAliases = new System.Collections.Generic.List<string> { scopeId },
            ResourceId = resourceId,
            Kind = AuraSharedResourceKinds.Directory,
            OriginKind = AuraSharedOriginKinds.UserManual,
            WriterId = "LocalUser",
            DefaultEnabled = true,
            Priority = 1000
        };
        var result = AuraSharedResourceProtocol.UpsertManualResource(AuraToolsIds.ModId, new AuraSharedManualResourceRequestV4
        {
            OwnerModId = AuraToolsIds.ModId,
            WriterId = "LocalUser",
            SourcePath = directory,
            Resource = declaration
        });
        message = result.Message;
        return result.Success;
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
        return AudioFileFormatProbe.Probe(path).Success;
    }

    public static bool IsSupportedImageFile(string path)
    {
        return AuraSharedFileResource.HasSupportedExtension(path, SupportedImageExtensions);
    }

    public static string ImportAudioPath(string inputPath, string targetDirectory, string targetBaseName, out string message)
    {
        message = "";
        var candidate = NormalizePathInput(inputPath);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            message = "音频路径为空。";
            return "";
        }

        string fullPath;
        try
        {
            fullPath = ResolveInputPath(candidate);
        }
        catch (Exception ex)
        {
            message = "音频路径无效：" + ex.Message;
            LogAudioImportFailure("invalid-path", candidate, null, message);
            return "";
        }

        var descriptor = AudioFileFormatProbe.Probe(fullPath);
        AuraSharedLog.Info("AuraTools.AudioImport", "probe: source=" + fullPath
            + ", sourceExtension=" + Display(Path.GetExtension(fullPath))
            + ", targetDirectory=" + targetDirectory
            + ", targetBaseName=" + targetBaseName
            + ", " + descriptor.Describe());
        if (!descriptor.Success)
        {
            message = "无法导入音频：" + descriptor.Message
                      + "（" + Display(descriptor.FailureCode) + "）";
            LogAudioImportFailure(descriptor.FailureCode, fullPath, descriptor, message);
            return "";
        }

        try
        {
            var relative = CopyIntoData(
                fullPath,
                targetDirectory,
                targetBaseName,
                descriptor.CanonicalExtension,
                descriptor);
            if (string.IsNullOrWhiteSpace(relative))
            {
                message = "复制音频失败。";
                LogAudioImportFailure("copy-failed", fullPath, descriptor, message);
                return "";
            }

            message = "已识别为 " + descriptor.Codec + " 并复制为规范文件。";
            return relative;
        }
        catch (Exception ex)
        {
            message = "复制音频失败：" + ex.Message;
            LogAudioImportFailure("registry-commit-failed", fullPath, descriptor, message);
            return "";
        }
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
        return CopyIntoData(sourcePath, targetDirectory, targetBaseName, null, null);
    }

    private static string CopyIntoData(
        string sourcePath,
        string targetDirectory,
        string targetBaseName,
        string? canonicalExtension,
        AudioFileFormatDescriptor? descriptor)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return "";
        }

        var extension = string.IsNullOrWhiteSpace(canonicalExtension)
            ? Path.GetExtension(sourcePath)
            : canonicalExtension;
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".dat";
        }

        extension ??= ".dat";

        var targetPath = Path.Combine(
            targetDirectory,
            SafeFolderName(targetBaseName) + extension.ToLowerInvariant());
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

        if (descriptor != null)
        {
            AuraSharedLog.Info("AuraTools.AudioImport", "committed: source=" + sourcePath
                + ", destination=" + destination
                + ", format=" + descriptor.Format
                + ", container=" + descriptor.Container
                + ", codec=" + descriptor.Codec
                + ", canonicalExtension=" + descriptor.CanonicalExtension
                + ", packageStatus=" + Display(response.Status)
                + ", changed=" + response.Changed
                + ", contentHash=" + Display(response.ContentHash));
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

    private static void LogAudioImportFailure(
        string? failureCode,
        string source,
        AudioFileFormatDescriptor? descriptor,
        string message)
    {
        AuraSharedLog.Warn("AuraTools.AudioImport", "failed: failureCode=" + Display(failureCode)
            + ", source=" + source
            + ", format=" + (descriptor?.Format.ToString() ?? "Unknown")
            + ", container=" + (descriptor?.Container ?? "Unknown")
            + ", codec=" + (descriptor?.Codec ?? "Unknown")
            + ", message=" + message);
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

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value ?? "<none>";
    }
}
