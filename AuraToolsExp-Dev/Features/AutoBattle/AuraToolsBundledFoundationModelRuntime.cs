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
using AuraDirector.Shared;
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
    Registering,
    Completed,
    Failed
}

internal sealed class BundledFoundationImportStatus
{
    public BundledFoundationImportStage Stage { get; set; }

    public string Message { get; set; } = "Model 底模尚未批量扫描";

    public int Scanned { get; set; }

    public int Installed { get; set; }

    public int Deduplicated { get; set; }

    public int Conflicts { get; set; }

    public int Failed { get; set; }

    public int OfficialTrusted { get; set; }

    public int PlayerValidated { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool Busy => Stage == BundledFoundationImportStage.Queued
                        || Stage == BundledFoundationImportStage.Scanning
                        || Stage == BundledFoundationImportStage.Registering;

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

    public string SourceDirectory { get; set; } = "";

    public string DistributionOrigin { get; set; } = "player-trained";
}

internal sealed class BundledFoundationRegistrationSummary
{
    public bool LibraryChanged { get; set; }

    public int Installed { get; set; }

    public int Deduplicated { get; set; }

    public int Conflicts { get; set; }

    public int Failed { get; set; }

    public List<string> Diagnostics { get; set; } = new();
}

internal static class AuraToolsBundledFoundationModelRuntime
{
    private const string WorkKey = "AutoBattle.BundledFoundationModels";
    private const string ModelRelativeDirectory = "ModResource/Model";
    private const string SubjectCatalogRelativePath =
        "Config/combat-simulation/witch-game-subjects-v1.catalog.json";
    private const string FoundationTrustCatalogRelativePath =
        "Config/aura-director.foundation-model-allowlist.json";
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

        lock (Gate)
        {
            if (status.Busy)
            {
                message = "Model 底模批量导入正在进行";
                return false;
            }
            status.Stage = BundledFoundationImportStage.Queued;
            status.Message = "Model 底模批量扫描已排队";
            status.UpdatedUtc = DateTime.UtcNow;
        }
        var rootSnapshot = modRoot;
        var runtimeContextSnapshot = AuraToolsAutoBattleModelRuntime
            .SnapshotModelRuntimeContext();
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<BundledFoundationImportResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = WorkKey,
                Source = "AutoBattle.BundledFoundationModels." + source,
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = cancellation => Import(
                    rootSnapshot,
                    runtimeContextSnapshot,
                    cancellation),
                ApplyOnMainThread = Apply,
                OnFailedOnMainThread = ex =>
                {
                    SetStatus(
                        BundledFoundationImportStage.Failed,
                        "Model 底模批量扫描失败：" + ex.Message);
                    AuraToolsLog.Warn(
                        "[AutoBattle][BundledModels] scan failed: " + ex);
                }
            });
        if (!queued)
        {
            message = "Model 底模批量扫描任务未能提交";
            SetStatus(BundledFoundationImportStage.Failed, message);
            return false;
        }

        message = "Model 底模批量扫描已提交";
        return true;
    }

    private static BundledFoundationScanResult Scan(
        string root,
        CancellationToken cancellation)
    {
        SetStatus(BundledFoundationImportStage.Scanning, "正在批量扫描 Model 底模");
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

        var discovery = AuraToolsBundledFoundationModelLayout.Discover(
            directory,
            cancellation);
        result.Failed += discovery.Rejected;
        result.Diagnostics.AddRange(discovery.Diagnostics);
        if (discovery.Sources.Count == 0)
        {
            return result;
        }

        var catalog = LoadSubjectCatalog(root);
        var trustCatalog = LoadFoundationTrustCatalog(
            root,
            out var trustCatalogDiagnostic);
        if (!string.IsNullOrWhiteSpace(trustCatalogDiagnostic))
        {
            result.Diagnostics.Add(trustCatalogDiagnostic);
        }
        var seenPackageHashes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        long aggregateBytes = 0;
        foreach (var source in discovery.Sources)
        {
            cancellation.ThrowIfCancellationRequested();
            result.Scanned++;
            try
            {
                var info = new FileInfo(source.ManifestPath);
                if (info.Length <= 0
                    || info.Length
                    > AuraToolsBundledFoundationModelLayout.MaximumManifestBytes)
                {
                    throw new InvalidDataException("文件大小必须在 1 字节至 64MB 之间");
                }
                if (!AuraToolsBundledFoundationModelLayout.TryReserveBytes(
                        ref aggregateBytes,
                        info.Length,
                        out var diagnostic))
                {
                    throw new InvalidDataException(diagnostic);
                }

                var bytes = File.ReadAllBytes(source.ManifestPath);
                var sourceSha256 = Hash(bytes);
                var json = new UTF8Encoding(false, true).GetString(bytes);
                var package = AuraSharedJson.Deserialize<CombatFoundationModelPackage>(json);
                if (!CombatFoundationModelPackageProtocol.TryValidate(
                        package,
                        out diagnostic))
                {
                    throw new InvalidDataException(diagnostic);
                }
                if (!AuraToolsBundledFoundationModelLayout.TryValidateIdentity(
                        source,
                        package!.RoleId,
                        package.PartnerId,
                        sourceSha256,
                        out diagnostic))
                {
                    throw new InvalidDataException(diagnostic);
                }
                if (!source.LegacyRootPackage
                    && (package.SchemaVersion
                        != CombatFoundationModelPackageProtocol.SchemaVersion
                        || package.ModelArtifact == null))
                {
                    throw new InvalidDataException(
                        "角色/使魔发布目录只接受当前 v5 FP32 双文件底模包");
                }
                if (package.ModelArtifact != null)
                {
                    if (!AuraToolsBundledFoundationModelLayout.TryResolveWeightsPath(
                            source,
                            package.ModelArtifact.WeightsFile,
                            out var weightsPath,
                            out diagnostic))
                    {
                        throw new InvalidDataException(diagnostic);
                    }
                    if (!AuraToolsBundledFoundationModelLayout.TryReserveBytes(
                            ref aggregateBytes,
                            new FileInfo(weightsPath).Length,
                            out diagnostic))
                    {
                        throw new InvalidDataException(diagnostic);
                    }
                    if (!CombatPolicyValueArtifactProtocol.TryValidatePayload(
                            source.ManifestDirectory,
                            package.ModelArtifact,
                            out diagnostic))
                    {
                        throw new InvalidDataException(diagnostic);
                    }
                }

                var modelId = package.ModelArtifact?.ModelId
                    ?? package.Model?.ModelId
                    ?? "";
                var featureSchemaVersion = package.ModelArtifact?.FeatureSchemaVersion
                    ?? package.Model?.FeatureSchemaVersion
                    ?? 0;
                var trustCandidate = new AuraDirectorFoundationCandidate
                {
                    FoundationLineage = CombatFoundationModelPackageProtocol.ResolveFoundationLineage(package),
                    ModelId = modelId,
                    ArtifactSha256 = sourceSha256,
                    WeightsSha256 = package.ModelArtifact?.WeightsSha256 ?? "",
                    FeatureSchemaVersion = featureSchemaVersion,
                    ContentSetHash = package.ContentSetHash,
                    RulesetHash = package.RulesetHash,
                    NativeProgramPackageHash = package.Compatibility?.NativeProgramPackageHash ?? "",
                    AvailableStartGateCapability = AuraDirectorFoundationTrustProtocol.ReadyToStartCapabilityV1
                };
                var exactArtifactTrustCatalog = new AuraDirectorFoundationTrustCatalog
                {
                    SchemaVersion = trustCatalog.SchemaVersion,
                    Entries = (trustCatalog.Entries
                               ?? new List<AuraDirectorFoundationTrustEntry>())
                        .Where(entry => entry != null
                                        && string.IsNullOrWhiteSpace(
                                            entry.WeightsSha256)
                                        && string.Equals(
                                            entry.ArtifactSha256,
                                            sourceSha256,
                                            StringComparison.OrdinalIgnoreCase))
                        .ToList()
                };
                var officialTrusted = AuraDirectorFoundationTrustPolicy.TryAuthorize(
                        exactArtifactTrustCatalog,
                        trustCandidate,
                        out _,
                        out _);
                var distributionOrigin = officialTrusted
                    ? "bundled"
                    : "player-trained";
                // A player-trained package is data-only and never auto-enabled.
                // The package protocol, formal acceptance evidence, current
                // compatibility tuple, payload bounds and weights hash above
                // are its local admission gate. Its computed source hash is
                // persisted into the local model library during registration.

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
                if (!seenPackageHashes.Add(sourceSha256))
                {
                    result.Deduplicated++;
                    continue;
                }
                if (officialTrusted)
                {
                    result.OfficialTrusted++;
                }
                else
                {
                    result.PlayerValidated++;
                }

                result.Candidates.Add(new BundledFoundationPackageCandidate
                {
                    Package = package,
                    SourceFileName = source.RelativeManifestPath,
                    SourceSha256 = sourceSha256,
                    ModelVersion = version,
                    SourceDirectory = source.ManifestDirectory,
                    DistributionOrigin = distributionOrigin,
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
                    source.RelativeManifestPath + "：" + ex.Message);
            }
        }

        return result;
    }

    internal static bool TryResolveSubjectReferences(
        CombatGameSubjectPreset preset)
    {
        if (preset == null)
        {
            return false;
        }
        lock (Gate)
        {
            if (subjectCatalog.Roles.Count == 0)
            {
                return false;
            }
            subjectCatalog.ResolveReferences(preset);
            return subjectCatalog.Roles.Any(item => string.Equals(
                item.Id,
                preset.RoleId,
                StringComparison.OrdinalIgnoreCase));
        }
    }

    private static BundledFoundationImportResult Import(
        string root,
        CombatModelRuntimeContext runtimeContext,
        CancellationToken cancellation)
    {
        var scan = Scan(root, cancellation);
        cancellation.ThrowIfCancellationRequested();
        SetStatus(
            BundledFoundationImportStage.Registering,
            "正在批量注册 Model 底模到共享模型库");
        var registration = AuraToolsAutoBattleModelRuntime
            .RegisterBundledFoundationPackages(
                scan.Candidates,
                runtimeContext);
        return new BundledFoundationImportResult
        {
            Scan = scan,
            Registration = registration
        };
    }

    private static void Apply(BundledFoundationImportResult result)
    {
        var scan = result.Scan;
        var registration = result.Registration;
        var failed = scan.Failed + registration.Failed;
        var deduplicated = scan.Deduplicated + registration.Deduplicated;
        var message = "Model 底模批量导入完成：扫描 "
                      + scan.Scanned
                      + "，官方 "
                      + scan.OfficialTrusted
                      + "，玩家训练 "
                      + scan.PlayerValidated
                      + "，新增 "
                      + registration.Installed
                      + "，已存在 "
                      + deduplicated
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
                Deduplicated = deduplicated,
                Conflicts = registration.Conflicts,
                Failed = failed,
                OfficialTrusted = scan.OfficialTrusted,
                PlayerValidated = scan.PlayerValidated,
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
        if (registration.LibraryChanged)
        {
            AuraToolsAutoBattleRuntime.NotifyModelLibraryChanged();
        }
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

    private static AuraDirectorFoundationTrustCatalog LoadFoundationTrustCatalog(
        string root,
        out string diagnostic)
    {
        try
        {
            var path = Path.Combine(
                root,
                FoundationTrustCatalogRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                diagnostic = "官方底模精确信任目录不存在；玩家训练底模仍按本地完整门禁导入";
                return new AuraDirectorFoundationTrustCatalog();
            }

            var json = new UTF8Encoding(false, true).GetString(
                File.ReadAllBytes(path));
            var catalog = AuraSharedJson
                .Deserialize<AuraDirectorFoundationTrustCatalog>(json);
            if (catalog == null
                || catalog.SchemaVersion
                != AuraDirectorFoundationTrustProtocol.SchemaVersion
                || catalog.Entries == null)
            {
                diagnostic = "官方底模精确信任目录为空或不兼容；玩家训练底模仍按本地完整门禁导入";
                return new AuraDirectorFoundationTrustCatalog();
            }
            diagnostic = "";
            return catalog;
        }
        catch (Exception ex)
        {
            diagnostic = "读取官方底模精确信任目录失败："
                         + ex.Message
                         + "；玩家训练底模仍按本地完整门禁导入";
            return new AuraDirectorFoundationTrustCatalog();
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

        public int Deduplicated { get; set; }

        public int OfficialTrusted { get; set; }

        public int PlayerValidated { get; set; }

        public List<BundledFoundationPackageCandidate> Candidates { get; } = new();

        public List<string> Diagnostics { get; } = new();

    }

    private sealed class BundledFoundationImportResult
    {
        public BundledFoundationScanResult Scan { get; set; } = new();

        public BundledFoundationRegistrationSummary Registration { get; set; } =
            new();
    }

}
