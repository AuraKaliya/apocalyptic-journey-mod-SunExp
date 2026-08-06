using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal enum BundledFoundationImportStage
{
    Idle,
    Queued,
    Scanning,
    Completed,
    Failed
}

internal sealed class BundledFoundationImportStatus
{
    public BundledFoundationImportStage Stage { get; set; }

    public string Message { get; set; } = "内置底模尚未扫描";

    public int Scanned { get; set; }

    public int Installed { get; set; }

    public int Deduplicated { get; set; }

    public int Conflicts { get; set; }

    public int Failed { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool Busy => Stage == BundledFoundationImportStage.Queued
                        || Stage == BundledFoundationImportStage.Scanning;

    public BundledFoundationImportStatus Clone()
    {
        return (BundledFoundationImportStatus)MemberwiseClone();
    }
}

internal sealed class BundledFoundationPackageCandidate
{
    public CombatFoundationModelPackage Package { get; set; } = new();

    public string SourceFileName { get; set; } = "";

    public string SourceSha256 { get; set; } = "";

    public string ModelVersion { get; set; } = "";

    public string DisplayName { get; set; } = "";
}

internal sealed class BundledFoundationRegistrationSummary
{
    public int Installed { get; set; }

    public int Deduplicated { get; set; }

    public int Conflicts { get; set; }

    public int Failed { get; set; }

    public List<string> Diagnostics { get; set; } = new();
}

internal static class AuraToolsBundledFoundationModelRuntime
{
    private const string WorkKey = "AutoBattle.BundledFoundationModels";
    private const long MaximumPackageBytes = 64L * 1024L * 1024L;
    private const string ModelRelativeDirectory = "ModResource/Model";
    private const string SubjectCatalogRelativePath =
        "Config/combat-simulation/witch-game-subjects-v1.catalog.json";
    private static readonly Regex ModelVersionPattern = new(
        @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant);
    private static readonly object Gate = new();
    private static string modRoot = "";
    private static CombatGameSubjectCatalog subjectCatalog = new();
    private static BundledFoundationImportStatus status = new();

    public static void Initialize(ModConfig modConfig)
    {
        modRoot = Path.GetFullPath(modConfig?.DirectoryName ?? "");
        subjectCatalog = LoadSubjectCatalog(modRoot);
        QueueScan("startup", out _);
    }

    internal static string BuildCanonicalDisplayName(
        string roleId,
        string partnerId,
        IEnumerable<string>? enabledRewardCardPackIds,
        string modelVersion)
    {
        CombatGameSubjectCatalog catalog;
        lock (Gate)
        {
            catalog = subjectCatalog;
        }

        return BuildCanonicalDisplayName(
            catalog,
            roleId,
            partnerId,
            enabledRewardCardPackIds,
            modelVersion);
    }

    public static bool TryQueueRescan(out string message)
    {
        return QueueScan("manual", out message);
    }

    public static BundledFoundationImportStatus SnapshotStatus()
    {
        lock (Gate)
        {
            return status.Clone();
        }
    }

    private static bool QueueScan(string source, out string message)
    {
        if (string.IsNullOrWhiteSpace(modRoot) || !Directory.Exists(modRoot))
        {
            message = "AuraToolsExp 模组目录不可用";
            SetStatus(BundledFoundationImportStage.Failed, message);
            return false;
        }

        SetStatus(BundledFoundationImportStage.Queued, "内置底模扫描已排队");
        var rootSnapshot = modRoot;
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<BundledFoundationScanResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = WorkKey,
                Source = "AutoBattle.BundledFoundationModels." + source,
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = cancellation => Scan(rootSnapshot, cancellation),
                ApplyOnMainThread = Apply,
                OnFailedOnMainThread = ex =>
                {
                    SetStatus(
                        BundledFoundationImportStage.Failed,
                        "内置底模扫描失败：" + ex.Message);
                    AuraToolsLog.Warn(
                        "[AutoBattle][BundledModels] scan failed: " + ex);
                }
            });
        if (!queued)
        {
            message = "内置底模扫描任务未能提交";
            SetStatus(BundledFoundationImportStage.Failed, message);
            return false;
        }

        message = "内置底模扫描已提交";
        return true;
    }

    private static BundledFoundationScanResult Scan(
        string root,
        CancellationToken cancellation)
    {
        SetStatus(BundledFoundationImportStage.Scanning, "正在扫描内置底模");
        var result = new BundledFoundationScanResult();
        var directory = Path.Combine(
            root,
            ModelRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            result.Failed++;
            result.Diagnostics.Add("内置底模目录不存在：" + directory);
            return result;
        }

        var catalog = LoadSubjectCatalog(root);
        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     "*.json",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            result.Scanned++;
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaximumPackageBytes)
                {
                    throw new InvalidDataException("文件大小必须在 1 字节至 64MB 之间");
                }

                var bytes = File.ReadAllBytes(path);
                var json = new UTF8Encoding(false, true).GetString(bytes);
                var package = AuraSharedJson.Deserialize<CombatFoundationModelPackage>(json);
                if (!CombatFoundationModelPackageProtocol.TryValidate(
                        package,
                        out var diagnostic))
                {
                    throw new InvalidDataException(diagnostic);
                }

                var version = NormalizeVersion(package!.ModelVersion);
                if (string.IsNullOrWhiteSpace(version))
                {
                    throw new InvalidDataException(
                        "内置底模必须声明当前语义版本 ModelVersion");
                }

                if (!ModelVersionPattern.IsMatch(version))
                {
                    throw new InvalidDataException(
                        "内置底模必须声明语义版本 ModelVersion，例如 1.0.0");
                }

                result.Candidates.Add(new BundledFoundationPackageCandidate
                {
                    Package = package,
                    SourceFileName = Path.GetFileName(path),
                    SourceSha256 = Hash(bytes),
                    ModelVersion = version,
                    DisplayName = BuildCanonicalDisplayName(
                        catalog,
                        package.RoleId,
                        package.PartnerId,
                        package.EnabledRewardCardPackIds,
                        version)
                });
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Diagnostics.Add(
                    Path.GetFileName(path) + "：" + ex.Message);
            }
        }

        return result;
    }

    private static void Apply(BundledFoundationScanResult scan)
    {
        var registration = AuraToolsAutoBattleModelRuntime
            .RegisterBundledFoundationPackages(scan.Candidates);
        var failed = scan.Failed + registration.Failed;
        var message = "内置底模扫描完成：扫描 "
                      + scan.Scanned
                      + "，新增 "
                      + registration.Installed
                      + "，已存在 "
                      + registration.Deduplicated
                      + "，冲突 "
                      + registration.Conflicts
                      + "，失败 "
                      + failed;
        lock (Gate)
        {
            status = new BundledFoundationImportStatus
            {
                Stage = failed > 0 || registration.Conflicts > 0
                    ? BundledFoundationImportStage.Failed
                    : BundledFoundationImportStage.Completed,
                Message = message,
                Scanned = scan.Scanned,
                Installed = registration.Installed,
                Deduplicated = registration.Deduplicated,
                Conflicts = registration.Conflicts,
                Failed = failed,
                UpdatedUtc = DateTime.UtcNow
            };
        }

        foreach (var diagnostic in scan.Diagnostics.Concat(registration.Diagnostics))
        {
            AuraToolsLog.Warn("[AutoBattle][BundledModels] " + diagnostic);
        }
        (failed == 0 && registration.Conflicts == 0
            ? (Action<string>)AuraToolsLog.Info
            : AuraToolsLog.Warn)("[AutoBattle][BundledModels] " + message);
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        AuraToolsAutoBattleUiSnapshotRuntime.RequestRefresh(
            settings.Profile,
            settings.SelectedModelId,
            force: true);
    }

    private static CombatGameSubjectCatalog LoadSubjectCatalog(string root)
    {
        try
        {
            var path = Path.Combine(
                root,
                SubjectCatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path)
                ? (AuraSharedJson.Deserialize<CombatGameSubjectCatalog>(
                       new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)))
                   ?? new CombatGameSubjectCatalog()).Normalize()
                : new CombatGameSubjectCatalog();
        }
        catch
        {
            return new CombatGameSubjectCatalog();
        }
    }

    private static string ResolveRoleName(
        CombatGameSubjectCatalog catalog,
        string roleId)
    {
        return catalog.Roles.FirstOrDefault(item => string.Equals(
                   item.Id,
                   roleId,
                   StringComparison.OrdinalIgnoreCase))?.DisplayName
               ?? roleId;
    }

    private static string ResolveFamiliarName(
        CombatGameSubjectCatalog catalog,
        string partnerId)
    {
        return catalog.Familiars.FirstOrDefault(item => string.Equals(
                   item.Id,
                   partnerId,
                   StringComparison.OrdinalIgnoreCase))?.DisplayName
               ?? partnerId;
    }

    private static string BuildCanonicalDisplayName(
        CombatGameSubjectCatalog catalog,
        string roleId,
        string partnerId,
        IEnumerable<string>? enabledRewardCardPackIds,
        string modelVersion)
    {
        var roleName = ResolveRoleName(catalog, roleId);
        var familiarName = ResolveFamiliarName(catalog, partnerId);
        var cardPackCount = (enabledRewardCardPackIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var version = NormalizeVersion(modelVersion);
        return roleName
               + "-"
               + familiarName
               + "-"
               + cardPackCount
               + "卡包-"
               + (string.IsNullOrWhiteSpace(version) ? "底模" : "v" + version);
    }

    private static string NormalizeVersion(string value)
    {
        return (value ?? "").Trim().TrimStart('v', 'V');
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes))
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private static void SetStatus(
        BundledFoundationImportStage stage,
        string message)
    {
        lock (Gate)
        {
            status.Stage = stage;
            status.Message = message;
            status.UpdatedUtc = DateTime.UtcNow;
        }
    }

    private sealed class BundledFoundationScanResult
    {
        public int Scanned { get; set; }

        public int Failed { get; set; }

        public List<BundledFoundationPackageCandidate> Candidates { get; } = new();

        public List<string> Diagnostics { get; } = new();

    }

}
