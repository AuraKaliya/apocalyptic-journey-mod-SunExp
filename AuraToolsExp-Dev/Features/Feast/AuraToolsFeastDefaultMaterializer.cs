using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraCg.Shared;
using AuraRole.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Feast;

public static class AuraToolsFeastDefaultMaterializer
{
    public const string ContributionId = "generated-feast-defaults";
    private const string LegacyPrefix = "CG/AuraToolsExp/Templates/Feast/Roles/";
    private const string TemplateRolePrefix = "CG/Role/";
    private const string DefaultTemplateResource = "CG/Global/all/Feast/AuraToolsExp/default-template/content.png";
    private static readonly HashSet<string> DiagnosticKeys = new(StringComparer.OrdinalIgnoreCase);
    private static bool initialized;
    private static bool reconciling;
    private static float nextRefreshRealtime;
    private static long observedRoleRevision = -1;

    public static void Initialize()
    {
        if (!initialized)
        {
            AuraRoleRegistryRuntime.Changed += OnRoleRegistryChanged;
            initialized = true;
        }

        EnsureCurrent(true);
    }

    public static void EnsureCurrent(bool force = false)
    {
        if (reconciling)
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        if (!force && now < nextRefreshRealtime)
        {
            return;
        }

        reconciling = true;
        try
        {
            RoleCatalog.GetRoles(force);
            var snapshot = AuraRoleRegistryRuntime.GetSnapshot();
            if (!force && snapshot.Revision == observedRoleRevision && now < nextRefreshRealtime)
            {
                return;
            }

            Reconcile(snapshot);
            observedRoleRevision = snapshot.Revision;
            nextRefreshRealtime = now + 2f;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[FeastDefaults] reconciliation failed: " + ex.Message);
            nextRefreshRealtime = now + 5f;
        }
        finally
        {
            reconciling = false;
        }
    }

    public static string RoleDirectory(string roleId)
    {
        return FileResourceUtil.EnsureDirectory(
            AuraToolsConfigService.DataRootDirectory,
            "CG",
            "Role",
            RoleCatalog.NormalizeRoleId(roleId),
            "Feast",
            AuraToolsIds.ModId,
            "local-user");
    }

    public static string DescribeRoleResource(string roleId)
    {
        var normalized = RoleCatalog.NormalizeRoleId(roleId);
        var feast = AuraToolsConfigService.MatchExperience.Feast;
        feast.Normalize();
        if (!feast.Roles.TryGetValue(normalized, out var settings))
        {
            return "尚未物化";
        }

        if (!settings.Active)
        {
            return "角色当前未启用";
        }

        if (string.IsNullOrWhiteSpace(settings.LocalResource))
        {
            return "默认文件缺失";
        }

        var path = AuraToolsConfigService.ResolveConfiguredPath(settings.LocalResource);
        if (!File.Exists(path))
        {
            return "本地文件缺失";
        }

        return settings.LocalCustomized ? "本地自定义" : "默认兜底";
    }

    public static bool ImportRoleImage(string roleId, string sourcePath, out string message)
    {
        EnsureCurrent(true);
        var normalized = RoleCatalog.NormalizeRoleId(roleId);
        var source = ResolveInputPath(sourcePath);
        if (!TryPrepareCanonicalPng(source, out var canonicalSource, out var temporarySource, out message))
        {
            return false;
        }

        var role = AuraToolsFeastRuntime.EnsureRoleSettings(normalized, RoleCatalog.GetDisplayName(normalized));
        var destination = RoleResource(normalized);
        AuraSharedEditableResourceResult result;
        try
        {
            result = AuraSharedEditableResource.Seed(AuraToolsIds.ModId, new AuraSharedEditableResourceRequest
            {
                OwnerModId = AuraToolsIds.ModId,
                System = AuraSharedSystems.Cg,
                LogicalId = "feast.override." + normalized,
                SourcePath = canonicalSource,
                DestinationRelativePath = destination,
                PreviousSeedHash = role.LocalSeedHash,
                ForceReset = true
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

        role.LocalResource = destination;
        role.LocalContentHash = result.ContentHash;
        role.LocalCustomized = true;
        role.Enabled = true;
        AuraToolsConfigService.SaveMatchExperience();
        EnsureCurrent(true);
        message = "已导入角色美餐CG；旧文件已进入 AuraShared/Backups/Editable。";
        return true;
    }

    public static bool ResetRoleImage(string roleId, out string message)
    {
        EnsureCurrent(true);
        var normalized = RoleCatalog.NormalizeRoleId(roleId);
        var role = AuraToolsFeastRuntime.EnsureRoleSettings(normalized, RoleCatalog.GetDisplayName(normalized));
        var template = ResolveTemplate(normalized);
        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
        {
            message = "默认美餐CG模板缺失，无法重置。";
            return false;
        }

        var destination = RoleResource(normalized);
        var result = AuraSharedEditableResource.Seed(AuraToolsIds.ModId, new AuraSharedEditableResourceRequest
        {
            OwnerModId = AuraToolsIds.ModId,
            System = AuraSharedSystems.Cg,
            LogicalId = "feast.override." + normalized,
            SourcePath = template,
            DestinationRelativePath = destination,
            PreviousSeedHash = role.LocalSeedHash,
            ForceReset = true
        });
        if (!result.Success)
        {
            message = "重置失败：" + result.Message;
            return false;
        }

        ApplySeedState(role, normalized, destination, result, AuraRoleRegistryRuntime.GetSnapshot().Revision);
        AuraToolsConfigService.SaveMatchExperience();
        EnsureCurrent(true);
        message = "已重置为默认美餐CG；此前文件已备份。";
        return true;
    }

    private static void OnRoleRegistryChanged(long revision)
    {
        if (revision != observedRoleRevision)
        {
            EnsureCurrent(true);
        }
    }

    private static void Reconcile(AuraRoleRegistrySnapshot snapshot)
    {
        if (snapshot.Entries.Count == 0)
        {
            LogOnce("empty-role-snapshot", "[FeastDefaults] role snapshot is empty; existing defaults were preserved.");
            return;
        }

        var feast = AuraToolsConfigService.MatchExperience.Feast;
        feast.Normalize();
        var activeIds = new HashSet<string>(snapshot.Entries.Select(entry => RoleCatalog.NormalizeRoleId(entry.RoleId)), StringComparer.OrdinalIgnoreCase);
        var generated = new List<AuraCgRegistryEntry>();
        var changed = false;
        foreach (var entry in snapshot.Entries)
        {
            var roleId = RoleCatalog.NormalizeRoleId(entry.RoleId);
            if (string.IsNullOrWhiteSpace(roleId))
            {
                continue;
            }

            var existed = feast.Roles.TryGetValue(roleId, out var before) && before != null;
            var role = AuraToolsFeastRuntime.EnsureRoleSettings(roleId, entry.DisplayName);
            changed |= !existed;
            changed |= Set(role.Active, true, value => role.Active = value);
            changed |= Set(role.LastSeenRoleRevision, snapshot.Revision, value => role.LastSeenRoleRevision = value);
            if (string.IsNullOrWhiteSpace(role.DisplayName) && !string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                role.DisplayName = entry.DisplayName;
                changed = true;
            }

            var template = ResolveTemplate(roleId);
            if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
            {
                LogOnce("template-missing:" + roleId, "[FeastDefaults] template missing for role=" + roleId + ".");
                continue;
            }

            var destination = RoleResource(roleId);
            var destinationPath = AuraToolsConfigService.ResolveConfiguredPath(destination);
            AuraSharedEditableResourceResult result;
            if (role.LocalCustomized && File.Exists(destinationPath))
            {
                result = new AuraSharedEditableResourceResult
                {
                    Success = true,
                    Status = AuraSharedEditableResourceStatuses.PreservedCustomized,
                    SeedHash = role.LocalSeedHash,
                    ContentHash = HashFile(destinationPath),
                    Customized = true,
                    InstalledPath = destinationPath
                };
            }
            else
            {
                var templateResource = AuraToolsConfigService.ToDataRelativePath(template);
                var templateHash = HashFile(template);
                destination = templateResource;
                result = new AuraSharedEditableResourceResult
                {
                    Success = true,
                    Status = AuraSharedEditableResourceStatuses.ExistingDefault,
                    SeedHash = templateHash,
                    ContentHash = templateHash,
                    Customized = false,
                    InstalledPath = template
                };
            }

            changed |= ApplySeedState(role, roleId, destination, result, snapshot.Revision);
            generated.Add(CreateGeneratedEntry(role));
            AuraSharedLog.Info("AuraTools.FeastDefaults", "role=" + roleId
                + ", status=" + result.Status
                + ", customized=" + result.Customized
                + ", resource=" + destination
                + ", template=" + AuraToolsConfigService.ToDataRelativePath(template));
        }

        foreach (var role in feast.Roles.Values.Where(role => role != null && !activeIds.Contains(RoleCatalog.NormalizeRoleId(role.RoleId))))
        {
            changed |= Set(role.Active, false, value => role.Active = value);
        }

        if (!AuraCgRegistryRuntime.RegisterContribution(AuraToolsIds.ModId, ContributionId, generated))
        {
            AuraToolsLog.Warn("[FeastDefaults] generated CG contribution registration failed.");
        }

        if (changed)
        {
            AuraToolsConfigService.SaveMatchExperience();
        }

        AuraSharedLog.Info("AuraTools.FeastDefaults", "reconciled: roleRevision=" + snapshot.Revision
            + ", activeRoles=" + activeIds.Count
            + ", generatedEntries=" + generated.Count
            + ", configChanged=" + changed);
    }

    private static bool ApplySeedState(
        FeastRoleSettings role,
        string roleId,
        string destination,
        AuraSharedEditableResourceResult result,
        long roleRevision)
    {
        var changed = false;
        changed |= Set(role.LocalCgId, FeastRoleResourceIdentity.CgId(roleId), value => role.LocalCgId = value);
        changed |= Set(role.LocalResource, destination, value => role.LocalResource = value);
        changed |= Set(role.LocalSeedHash, result.SeedHash, value => role.LocalSeedHash = value);
        changed |= Set(role.LocalContentHash, result.ContentHash, value => role.LocalContentHash = value);
        changed |= Set(role.LocalCustomized, result.Customized, value => role.LocalCustomized = value);
        changed |= Set(role.LastSeenRoleRevision, roleRevision, value => role.LastSeenRoleRevision = value);
        changed |= Set(role.Active, true, value => role.Active = value);
        return changed;
    }

    private static AuraCgRegistryEntry CreateGeneratedEntry(FeastRoleSettings role)
    {
        var presentation = role.EffectivePresentation ?? FeastSettings.CreateDefaultPresentation();
        return new AuraCgRegistryEntry
        {
            CgId = role.LocalCgId,
            DisplayName = (string.IsNullOrWhiteSpace(role.DisplayName) ? role.RoleId : role.DisplayName)
                          + (role.LocalCustomized ? " - 本地美餐CG" : " - 默认美餐CG"),
            Kind = AuraToolsFeastRuntime.FeastKind,
            TargetRoleIds = new List<string> { role.RoleId },
            CardIds = new List<string> { "*" },
            Media = new AuraCgMediaSpec
            {
                Type = SkillCgMediaTypes.Image,
                Resource = role.LocalResource,
                FallbackImage = DefaultTemplateResource,
                Hash = role.LocalContentHash
            },
            DefaultPresentation = new AuraCgPresentationSpec
            {
                Mode = presentation.Mode,
                Fit = presentation.Fit,
                FadeIn = presentation.FadeIn,
                Hold = presentation.Hold,
                FadeOut = presentation.FadeOut,
                FocusX = presentation.FocusX,
                FocusY = presentation.FocusY,
                SafeScale = presentation.SafeScale
            },
            DefaultActivation = new AuraCgDefaultActivationSpec
            {
                Enabled = true,
                ConsumerMode = AuraCgConsumerModes.ToolManaged,
                ConsumerModId = AuraToolsIds.ModId
            },
            Priority = role.LocalCustomized ? 1000 : -1000,
            Tags = new List<string>
            {
                "feast-cg",
                "generated-default",
                role.LocalCustomized ? "user-customized" : "fallback"
            },
            Enabled = role.Active
        };
    }

    private static string ResolveTemplate(string roleId)
    {
        var staticEntry = AuraCgRegistryRuntime.GetRegisteredEntries(AuraToolsIds.ModId)
            .Where(entry => string.Equals(entry.Kind, AuraToolsFeastRuntime.FeastKind, StringComparison.OrdinalIgnoreCase))
            .Where(entry => !string.Equals(entry.RegistrationSourceId, ContributionId, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(entry => TargetsRole(entry, roleId)
                                     && (entry.Media.Resource.StartsWith(TemplateRolePrefix, StringComparison.OrdinalIgnoreCase)
                                         || entry.Media.Resource.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase)));
        if (staticEntry != null)
        {
            var specificResource = staticEntry.Media.Resource;
            var specificPath = AuraToolsConfigService.ResolveConfiguredPath(specificResource);
            if (File.Exists(specificPath))
            {
                return specificPath;
            }
        }

        return AuraToolsConfigService.ResolveConfiguredPath(DefaultTemplateResource);
    }

    private static string RoleResource(string roleId)
    {
        return "CG/Role/" + RoleCatalog.NormalizeRoleId(roleId)
               + "/Feast/" + AuraToolsIds.ModId + "/local-user/content.png";
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
                "feast-import",
                "png",
                bytes);
            temporarySource = true;
            message = "";
            AuraSharedLog.Info("AuraTools.FeastDefaults", "normalized JPG/JPEG import to PNG: source=" + source);
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

    private static string HashFile(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
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

    private static bool TargetsRole(AuraCgRegistryEntry entry, string roleId)
    {
        var targets = (entry.TargetRoleIds ?? new List<string>())
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => target.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targets.Any(target => string.Equals(target, "*", StringComparison.Ordinal)))
        {
            return true;
        }

        return targets.Count(target => AuraSharedContentId.Resolve(
            target,
            new[] { roleId },
            entry.OwnerModId,
            AuraSharedIdentity.OfficialCareerPrefix).Success) == 1;
    }

    private static bool Set<T>(T current, T value, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        assign(value);
        return true;
    }

    private static void LogOnce(string key, string message)
    {
        if (DiagnosticKeys.Add(key))
        {
            AuraToolsLog.Warn(message);
        }
    }
}
