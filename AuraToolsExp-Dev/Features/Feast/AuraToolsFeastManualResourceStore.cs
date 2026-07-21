using System;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Feast;

public static class AuraToolsFeastManualResourceStore
{
    private const string LocalManualId = "local";

    public static string RoleDirectory(string roleId)
    {
        var normalized = RoleCatalog.NormalizeRoleId(roleId);
        var resourcePath = AuraSharedResourcePathPolicy.StorageResourcePath(
            AuraToolsConfigService.DataRootDirectory,
            ManualScope(normalized),
            AuraToolsIds.ModId,
            ManualDeclaration(normalized, LocalManualId));
        var absolute = AuraToolsConfigService.ResolveConfiguredPath(resourcePath);
        return FileResourceUtil.EnsureDirectory(Path.GetDirectoryName(absolute) ?? absolute);
    }

    public static bool ImportRoleImage(string roleId, string sourcePath, out string message)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var source = ResolveInputPath(sourcePath);
        if (string.IsNullOrWhiteSpace(normalizedRole))
        {
            message = "角色标识无效。";
            return false;
        }

        if (!TryPrepareCanonicalPng(source, out var canonicalSource, out var temporarySource, out message))
        {
            return false;
        }

        var role = AuraToolsFeastRuntime.EnsureRoleSettings(
            normalizedRole,
            RoleCatalog.GetDisplayName(normalizedRole));
        var existing = role.ManualResources.FirstOrDefault(manual =>
            string.Equals(manual.ManualId, LocalManualId, StringComparison.OrdinalIgnoreCase));
        var destination = ManualResourcePath(normalizedRole, LocalManualId);
        AuraSharedRegistrationItemResultV4 result;
        try
        {
            result = AuraSharedResourceProtocol.UpsertManualResource(AuraToolsIds.ModId, new AuraSharedManualResourceRequestV4
            {
                OwnerModId = AuraToolsIds.ModId,
                WriterId = "LocalUser",
                SourcePath = canonicalSource,
                Resource = ManualDeclaration(normalizedRole, LocalManualId)
            });
        }
        finally
        {
            if (temporarySource)
            {
                AuraSharedEditableResource.ReleaseTemporary(AuraToolsIds.ModId, canonicalSource);
            }
        }

        if (!result.Success)
        {
            message = "导入失败：" + result.Message;
            return false;
        }

        if (existing == null)
        {
            existing = new FeastManualResourceSettings { ManualId = LocalManualId };
            role.ManualResources.Add(existing);
        }

        existing.DisplayName = (string.IsNullOrWhiteSpace(role.DisplayName) ? role.RoleId : role.DisplayName)
                               + " - 人工配置";
        existing.Resource = destination;
        existing.SeedHash = "";
        existing.ContentHash = "";
        existing.Priority = 1000;
        role.ResourceOverrides[FeastRoleResourceIdentity.ManualId(normalizedRole, LocalManualId)] = true;
        AuraToolsFeastRuntime.SaveRoleSettings(role);
        message = "已按共享资源协议 v4 注册人工美餐 CG。";
        return true;
    }

    public static bool RemoveRoleImage(string roleId, out string message)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var role = AuraToolsFeastRuntime.EnsureRoleSettings(
            normalizedRole,
            RoleCatalog.GetDisplayName(normalizedRole));
        var removed = role.ManualResources.RemoveAll(manual =>
            string.Equals(manual.ManualId, LocalManualId, StringComparison.OrdinalIgnoreCase));
        var archived = AuraSharedResourceProtocol.UpsertManualResource(AuraToolsIds.ModId, new AuraSharedManualResourceRequestV4
        {
            OwnerModId = AuraToolsIds.ModId,
            WriterId = "LocalUser",
            Archive = true,
            Resource = ManualDeclaration(normalizedRole, LocalManualId)
        });
        role.ResourceOverrides.Remove(FeastRoleResourceIdentity.ManualId(normalizedRole, LocalManualId));
        AuraToolsFeastRuntime.SaveRoleSettings(role);
        message = removed > 0 && archived.Success
            ? "已停用人工配置；资源已移入历史资源视图。"
            : "当前角色没有可移除的人工配置。";
        return removed > 0 && archived.Success;
    }

    private static string ManualResourcePath(string roleId, string manualId)
    {
        return AuraSharedResourcePathPolicy.StorageResourcePath(
            AuraToolsConfigService.DataRootDirectory,
            ManualScope(roleId),
            AuraToolsIds.ModId,
            ManualDeclaration(roleId, manualId));
    }

    private static AuraSharedScopeKey ManualScope(string roleId)
    {
        return new AuraSharedScopeKey
        {
            ModuleId = AuraSharedSystems.Cg,
            FeatureId = "Feast",
            ScopeType = "Role",
            ScopeId = RoleCatalog.NormalizeRoleId(roleId)
        };
    }

    private static AuraSharedResourceDeclarationV4 ManualDeclaration(string roleId, string manualId)
    {
        var normalized = RoleCatalog.NormalizeRoleId(roleId);
        var role = RoleCatalog.GetRoles().FirstOrDefault(item => RoleCatalog.MatchesRole(normalized, item.Id));
        return new AuraSharedResourceDeclarationV4
        {
            ModuleId = AuraSharedSystems.Cg,
            FeatureId = "Feast",
            ScopeType = "Role",
            ScopeId = normalized,
            ScopeOwnerModId = role?.OwnerModId ?? AuraToolsIds.ModId,
            ScopeAliases = role?.Aliases?.ToList() ?? new System.Collections.Generic.List<string> { normalized },
            ResourceId = "manual." + AuraSharedPaths.SafeSegment(manualId, LocalManualId),
            Kind = AuraSharedResourceKinds.File,
            FileName = "content.png",
            OriginKind = AuraSharedOriginKinds.UserManual,
            WriterId = "LocalUser",
            DefaultEnabled = true,
            Priority = 1000,
            EffectMode = AuraSharedEffectModes.Additive,
            MissingPolicy = AuraSharedMissingPolicies.Skip,
            Metadata = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["displayName"] = RoleCatalog.GetDisplayName(normalized) + " - 人工配置",
                ["mediaType"] = "image"
            }
        };
    }

    private static string ResolveInputPath(string value)
    {
        var candidate = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "";
        }

        return Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : AuraToolsConfigService.ResolveConfiguredPath(candidate);
    }

    private static bool IsPng(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var signature = new byte[8];
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return stream.Read(signature, 0, signature.Length) == signature.Length
                   && signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        }
        catch
        {
            return false;
        }
    }

    private static bool TryPrepareCanonicalPng(
        string source,
        out string canonicalSource,
        out bool temporarySource,
        out string message)
    {
        canonicalSource = "";
        temporarySource = false;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            message = "所选图片文件不存在。";
            return false;
        }

        if (IsPng(source))
        {
            canonicalSource = source;
            message = "";
            return true;
        }

        var extension = Path.GetExtension(source);
        if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            message = "仅支持 PNG、JPG 或 JPEG 图片。";
            return false;
        }

        Texture2D? texture = null;
        try
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadImageIntoTexture(texture, File.ReadAllBytes(source)))
            {
                message = "所选 JPG/JPEG 无法解析为有效图片。";
                return false;
            }

            var bytes = ImageConversion.EncodeToPNG(texture);
            if (bytes == null || bytes.Length == 0)
            {
                message = "图片转换为 PNG 失败。";
                return false;
            }

            canonicalSource = AuraSharedEditableResource.StageTemporary(
                AuraToolsIds.ModId,
                "feast-manual-import",
                "png",
                bytes);
            temporarySource = true;
            message = "";
            AuraSharedLog.Info("AuraTools.FeastManual", "normalized JPG/JPEG import to PNG: source=" + source);
            return true;
        }
        catch (Exception ex)
        {
            message = "图片解析失败：" + ex.Message;
            return false;
        }
        finally
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }

    private static bool LoadImageIntoTexture(Texture2D texture, byte[] bytes)
    {
        try
        {
            var imageConversion = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEngine.ImageConversion"))
                .FirstOrDefault(type => type != null);
            var method = imageConversion?.GetMethod(
                "LoadImage",
                new[] { typeof(Texture2D), typeof(byte[]) });
            return method?.Invoke(null, new object[] { texture, bytes }) is true;
        }
        catch
        {
            return false;
        }
    }
}
