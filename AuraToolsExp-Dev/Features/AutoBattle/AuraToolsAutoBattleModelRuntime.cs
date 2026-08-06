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
using AuraDecision.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal enum AutoBattleTrainingStage
{
    Idle,
    Queued,
    ReadingSamples,
    Training,
    WritingCandidate,
    Cancelling,
    Cancelled,
    CandidateReady,
    Importing,
    Imported,
    Failed
}

internal sealed class AutoBattleTrainingStatus
{
    public string Profile { get; set; } = "balanced";

    public AutoBattleTrainingStage Stage { get; set; }

    public string Message { get; set; } = "尚未开始训练";

    public int SampleCount { get; set; }

    public int InvalidLineCount { get; set; }

    public int PreferencePairCount { get; set; }

    public int WeightCount { get; set; }

    public string CandidatePath { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool Busy => Stage == AutoBattleTrainingStage.Queued
                        || Stage == AutoBattleTrainingStage.ReadingSamples
                        || Stage == AutoBattleTrainingStage.Training
                        || Stage == AutoBattleTrainingStage.WritingCandidate
                        || Stage == AutoBattleTrainingStage.Cancelling
                        || Stage == AutoBattleTrainingStage.Importing;

    public AutoBattleTrainingStatus Clone()
    {
        return (AutoBattleTrainingStatus)MemberwiseClone();
    }
}

internal sealed class AutoBattleCandidateBundle
{
    public int SchemaVersion { get; set; } = 4;

    public string BundleId { get; set; } = "";

    public string Profile { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string CardPoolScope { get; set; } = "";

    public string PartnerId { get; set; } = "";

    public List<string> EnabledRewardCardPackIds { get; set; } = new();

    public int PreferredDeckSizeMinimum { get; set; }

    public int PreferredDeckSizeMaximum { get; set; }

    public CombatFoundationTrainingSubject? TrainingSubject { get; set; }

    public CombatFoundationDeclaredCoverage? DeclaredCoverage { get; set; }

    public bool FoundationArtifactValidated { get; set; }

    public string FoundationPackageId { get; set; } = "";

    public string FoundationWorkerSha256 { get; set; } = "";

    public string FoundationRulesetHash { get; set; } = "";

    public string FoundationModelVersion { get; set; } = "";

    public string FoundationAcceptanceKind { get; set; } = "";

    public string FoundationPromotionProtocolVersion { get; set; } = "";

    public double FoundationPairedRegressionUpperBound { get; set; }

    public CombatFoundationModelAcceptance? FoundationAcceptance { get; set; }

    public string FoundationDistributionOrigin { get; set; } = "";

    public string FoundationSourcePackageSha256 { get; set; } = "";

    public string FoundationSourcePackageFile { get; set; } = "";

    public string ModelPurpose { get; set; } = "candidate";

    public string BaseModelId { get; set; } = "";

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public CombatDecisionAdapterManifest? AdapterBinding { get; set; }

    public double ProjectionNormalWinRate { get; set; }

    public double ProjectionAdvancedWinRate { get; set; }

    public string TrainingReportDirectory { get; set; } = "";

    public DateTime GeneratedUtc { get; set; }

    public string TrainingSnapshotId { get; set; } = "";

    public string TrainingSnapshotHash { get; set; } = "";

    public DecisionResidualModelDefinition? Residual { get; set; }

    public CombatSearchGuidanceDefinition? SearchGuidance { get; set; }

    public CombatPolicyValueNetworkDefinition? PolicyValue { get; set; }
}

internal sealed class AutoBattleModelLibraryEntry
{
    public string ModelId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string DisplayNameMode { get; set; } = "";

    public string GeneratedDisplayName { get; set; } = "";

    public string Profile { get; set; } = "balanced";

    public string RoleId { get; set; } = "";

    public string CardPoolScope { get; set; } = "";

    public string PartnerId { get; set; } = "";

    public List<string> EnabledRewardCardPackIds { get; set; } = new();

    public int PreferredDeckSizeMinimum { get; set; }

    public int PreferredDeckSizeMaximum { get; set; }

    public string CoverageLevel { get; set; } = "partial";

    public string CoverageSummary { get; set; } = "";

    public string ModelPurpose { get; set; } = "candidate";

    public double ProjectionNormalWinRate { get; set; }

    public double ProjectionAdvancedWinRate { get; set; }

    public string BundleFile { get; set; } = "";

    public string ModelVersion { get; set; } = "";

    public string AcceptanceKind { get; set; } = "";

    public string DistributionOrigin { get; set; } = "";

    public string SourcePackageSha256 { get; set; } = "";

    public string SourcePackageFile { get; set; } = "";

    public DateTime CreatedUtc { get; set; }
}

internal sealed class AutoBattleModelLibraryDocument
{
    public int SchemaVersion { get; set; } = 5;

    public List<AutoBattleModelLibraryEntry> Models { get; set; } = new();
}

internal sealed class AutoBattleExternalValidationEntry
{
    public int SchemaVersion { get; set; } = 1;

    public string ModelId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Profile { get; set; } = "balanced";

    public string PackageFile { get; set; } = "";

    public string PackageSha256 { get; set; } = "";

    public string SourcePath { get; set; } = "";

    public DateTime StagedUtc { get; set; }

    public CombatFoundationTrainingSubject? TrainingSubject { get; set; }

    public CombatFoundationDeclaredCoverage? DeclaredCoverage { get; set; }

    public CombatModelCoverageAssessment? CoverageAssessment { get; set; }

    public string WorkerProvenance { get; set; } = "";

    public string AcceptanceKind { get; set; } = "";

    public string PromotionProtocolVersion { get; set; } = "";
}

internal sealed class AutoBattleTrainingSnapshotManifest
{
    public int SchemaVersion { get; set; } = 2;

    public string SnapshotId { get; set; } = "";

    public string Profile { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string CardPoolScope { get; set; } = "";

    public string BaseModelId { get; set; } = "";

    public string ContentSetHash { get; set; } =
        CombatContentSetProtocol.EmptyContentSetHash;

    public string OwnerModSetHash { get; set; } =
        CombatContentSetProtocol.EmptyOwnerModSetHash;

    public DateTime CapturedUtc { get; set; }

    public string AggregateSha256 { get; set; } = "";

    public List<AutoBattleTrainingSnapshotFile> Files { get; set; } = new();
}

internal sealed class AutoBattleTrainingSnapshotFile
{
    public string Kind { get; set; } = "samples";

    public string SourcePath { get; set; } = "";

    public string SnapshotPath { get; set; } = "";

    public long StableLength { get; set; }

    public string Sha256 { get; set; } = "";
}

internal static class AuraToolsAutoBattleModelRuntime
{
    private const string SystemId = "AuraCombatAI";
    public static string CurrentRoleId
    {
        get
        {
            var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
            settings.Normalize();
            return settings.GameParameters.ActivePreset.RoleId;
        }
    }

    public static string CurrentCardPoolScope
    {
        get
        {
            var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
            settings.Normalize();
            var preset = settings.GameParameters.ActivePreset;
            return CombatFoundationModelPackageProtocol.BuildCardPoolScope(
                preset.PartnerId,
                preset.EnabledRewardCardPackIds,
                preset.PreferredDeckSizeMinimum,
                preset.PreferredDeckSizeMaximum);
        }
    }

    private static CombatModelRuntimeContext CurrentRuntimeContext()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        settings.Normalize();
        var preset = settings.GameParameters.ActivePreset;
        return new CombatModelRuntimeContext
        {
            RoleId = preset.RoleId,
            PartnerId = preset.PartnerId,
            EnabledRewardCardPackIds =
                preset.EnabledRewardCardPackIds.ToList(),
            RoleSkillCardIds = preset.ResolvedRoleSkillIds.ToList(),
            FamiliarBlessingIds =
                preset.ResolvedFamiliarBlessingIds.ToList(),
            PreferredDeckSizeMinimum = preset.PreferredDeckSizeMinimum,
            PreferredDeckSizeMaximum = preset.PreferredDeckSizeMaximum
        };
    }
    private static readonly object StatusGate = new();
    private static readonly object LibraryGate = new();
    private static readonly Dictionary<string, AutoBattleTrainingStatus> StatusByProfile =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CancellationTokenSource> CancellationByProfile =
        new(StringComparer.Ordinal);
    private static readonly string[] TrainingFiles =
    {
        "auto-battle-training-v7.jsonl"
    };

    public static IReadOnlyList<string> SnapshotActiveAdapterIds(
        string decisionProfile,
        string selectedBaseModelId)
    {
        var result = AuraToolsCombatContentRuntime
            .SnapshotPolicyAdapters((selectedBaseModelId ?? "").Trim())
            .Select(item => item.Manifest.AdapterId)
            .ToList();
        if (TryValidateInstalledAdapterBinding(
                NormalizeProfile(decisionProfile),
                selectedBaseModelId ?? "",
                out _))
        {
            var snapshot = AuraSharedConfigStore.ReadOwner(
                AuraToolsIds.ModId,
                SystemId,
                AdapterBindingFile(NormalizeProfile(decisionProfile)),
                new CombatDecisionAdapterManifest());
            if (snapshot.Found
                && !string.IsNullOrWhiteSpace(snapshot.Value.AdapterId))
            {
                result.Add(snapshot.Value.AdapterId);
            }
        }
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IDecisionResidualModel Load(
        string decisionProfile,
        bool enabled,
        out string diagnostic,
        string selectedModelId = "")
    {
        var profile = NormalizeProfile(decisionProfile);
        if (!enabled)
        {
            diagnostic = "学习型评分修正已关闭";
            return NullDecisionResidualModel.Instance;
        }

        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            ModelFile(profile),
            new DecisionResidualModelDefinition());
        if (!snapshot.Found)
        {
            diagnostic = "当前决策风格没有已安装的本地模型：" + profile;
            return NullDecisionResidualModel.Instance;
        }
        if (!TryValidate(snapshot.Value, profile, out diagnostic))
        {
            return NullDecisionResidualModel.Instance;
        }
        if (!TryValidateInstalledAdapterBinding(
                profile,
                selectedModelId,
                out diagnostic))
        {
            return NullDecisionResidualModel.Instance;
        }

        diagnostic = "已加载本地模型=" + snapshot.Value.ModelId
                     + "，风格=" + profile
                     + "，revision=" + snapshot.Revision;
        return new BoundedLinearDecisionResidualModel(snapshot.Value);
    }

    public static ICombatSearchGuidanceModel LoadSearchGuidance(
        string decisionProfile,
        bool enabled,
        out string diagnostic,
        string selectedModelId = "")
    {
        var profile = NormalizeProfile(decisionProfile);
        if (!enabled)
        {
            diagnostic = "搜索引导模型已关闭";
            return NullCombatSearchGuidanceModel.Instance;
        }

        if (TryReadLibraryBundle(profile, selectedModelId, out var libraryBundle, out _)
            && libraryBundle.SearchGuidance != null
            && TryValidateSearchGuidance(
                libraryBundle.SearchGuidance,
                profile,
                out diagnostic))
        {
            diagnostic = "已加载模型库搜索引导=" + libraryBundle.SearchGuidance.ModelId;
            return new BoundedTreeCombatSearchGuidanceModel(libraryBundle.SearchGuidance);
        }

        diagnostic = "所选底模未携带搜索引导；旧本地搜索模型不会跨底模复用";
        return NullCombatSearchGuidanceModel.Instance;
    }

    public static ICombatPolicyValueModel LoadPolicyValue(
        string decisionProfile,
        bool enabled,
        out string diagnostic,
        string selectedModelId = "")
    {
        var profile = NormalizeProfile(decisionProfile);
        if (!enabled)
        {
            diagnostic = "长期策略价值网络已关闭";
            return NullCombatPolicyValueModel.Instance;
        }
        if (TryReadLibraryBundle(profile, selectedModelId, out var libraryBundle, out _)
            && libraryBundle.PolicyValue != null
            && CombatPolicyValueNetworkValidator.TryValidate(
                libraryBundle.PolicyValue,
                out diagnostic))
        {
            var assessment = AssessBundleCoverage(libraryBundle);
            diagnostic = "已加载模型库策略价值="
                         + libraryBundle.PolicyValue.ModelId
                         + (assessment == null
                             ? ""
                             : "；" + assessment.Summary);
            return CreateCoverageAwarePolicyValue(
                libraryBundle.PolicyValue,
                libraryBundle);
        }

        diagnostic = "所选模型库底模没有当前 v2 策略价值网络";
        return NullCombatPolicyValueModel.Instance;
    }

    public static CombatPolicyValueNetworkDefinition? LoadPolicyValueDefinition(
        string decisionProfile,
        string selectedModelId = "")
    {
        var profile = NormalizeProfile(decisionProfile);
        if (TryReadLibraryBundle(profile, selectedModelId, out var libraryBundle, out _)
            && libraryBundle.PolicyValue != null
            && CombatPolicyValueNetworkValidator.TryValidate(
                libraryBundle.PolicyValue,
                out _))
        {
            return libraryBundle.PolicyValue;
        }
        return null;
    }

    public static bool TryLoadCandidate(
        string decisionProfile,
        out IDecisionResidualModel residual,
        out ICombatSearchGuidanceModel guidance,
        out ICombatPolicyValueModel policyValue,
        out string modelId,
        out string diagnostic)
    {
        var profile = NormalizeProfile(decisionProfile);
        residual = NullDecisionResidualModel.Instance;
        guidance = NullCombatSearchGuidanceModel.Instance;
        policyValue = NullCombatPolicyValueModel.Instance;
        modelId = "none";
        if (!TryReadValidatedCandidateBundle(profile, out var bundle, out diagnostic))
        {
            return false;
        }
        PopulateModels(
            bundle,
            out residual,
            out guidance,
            out policyValue);
        modelId = CandidateModelId(bundle);
        diagnostic = "已加载候选原子包 " + bundle.BundleId
                     + "，训练快照=" + bundle.TrainingSnapshotId;
        return true;
    }

    public static bool TryLoadLibraryModel(
        string decisionProfile,
        string selectedModelId,
        out IDecisionResidualModel residual,
        out ICombatSearchGuidanceModel guidance,
        out ICombatPolicyValueModel policyValue,
        out string modelId,
        out string diagnostic)
    {
        var profile = NormalizeProfile(decisionProfile);
        residual = NullDecisionResidualModel.Instance;
        guidance = NullCombatSearchGuidanceModel.Instance;
        policyValue = NullCombatPolicyValueModel.Instance;
        modelId = "none";
        if (!TryReadLibraryBundle(profile, selectedModelId, out var bundle, out diagnostic))
        {
            return false;
        }
        PopulateModels(
            bundle,
            out residual,
            out guidance,
            out policyValue);
        modelId = CandidateModelId(bundle);
        var assessment = AssessBundleCoverage(bundle);
        diagnostic = "已从模型库加载“"
                     + LibraryDisplayName(modelId)
                     + "”"
                     + (assessment == null
                         ? ""
                         : "；" + assessment.Summary);
        return true;
    }

    public static bool TryStageExternalFoundationPackage(
        string sourcePath,
        out string modelId,
        out string message)
    {
        modelId = "";
        var path = (sourcePath ?? "").Trim().Trim('"');
        try
        {
            if (!File.Exists(path))
            {
                message = "待验底模包不存在";
                return false;
            }
            var file = new FileInfo(path);
            if (file.Length <= 0L || file.Length > 64L * 1024L * 1024L)
            {
                message = "待验底模包大小必须在 1 字节到 64MB 之间";
                return false;
            }
            var json = File.ReadAllText(path);
            var package = AuraSharedJson.Deserialize<CombatFoundationModelPackage>(
                json);
            if (!CombatFoundationModelPackageProtocol.TryValidate(
                    package,
                    out message))
            {
                return false;
            }
            ResolvePackageCoverage(
                package!,
                out var trainingSubject,
                out var declaredCoverage);
            var coverageAssessment =
                CombatFoundationModelCoverageProtocol.Assess(
                    trainingSubject,
                    declaredCoverage,
                    CurrentRuntimeContext());
            var acceptance = CombatFoundationModelPackageProtocol
                .NormalizeAcceptance(package!);
            modelId = package!.Model!.ModelId;
            if (string.IsNullOrWhiteSpace(modelId)
                || string.Equals(modelId, "none", StringComparison.Ordinal))
            {
                message = "待验底模没有稳定模型 ID";
                return false;
            }
            var packageHash = HashBytes(Encoding.UTF8.GetBytes(json))
                .ToLowerInvariant();
            var stagedDisplayName = AuraToolsBundledFoundationModelRuntime
                .BuildCanonicalDisplayName(
                    package.RoleId,
                    package.PartnerId,
                    package.EnabledRewardCardPackIds,
                    package.ModelVersion);
            Directory.CreateDirectory(ExternalValidationDirectory());
            var packageFile = "foundation-"
                              + packageHash.Substring(0, 20)
                              + ".json";
            var destination = Path.Combine(
                ExternalValidationDirectory(),
                packageFile);
            using (var storage = new AuraSharedStorageCoordinator(
                       AuraSharedPaths.RootDirectory))
            {
                storage.WriteTextAtomic(
                    destination,
                    json,
                    createBackup: false);
                storage.WriteTextAtomic(
                    ExternalValidationManifestPath(),
                    AuraSharedJson.Serialize(
                        new AutoBattleExternalValidationEntry
                        {
                            ModelId = modelId,
                            DisplayName = stagedDisplayName,
                            Profile = NormalizeProfile(package.Profile),
                            PackageFile = packageFile,
                            PackageSha256 = packageHash,
                            SourcePath = Path.GetFullPath(path),
                            StagedUtc = DateTime.UtcNow,
                            TrainingSubject = trainingSubject,
                            DeclaredCoverage = declaredCoverage,
                            CoverageAssessment = coverageAssessment,
                            WorkerProvenance =
                                DescribeWorkerProvenance(package),
                            AcceptanceKind = acceptance.Classification,
                            PromotionProtocolVersion =
                                acceptance.PromotionProtocolVersion
                        }),
                    createBackup: true);
            }
            message = "底模正确性验证通过，已解析并暂存“"
                      + stagedDisplayName
                      + "”；"
                      + coverageAssessment.Summary
                      + "；尚未加入模型库";
            return true;
        }
        catch (Exception ex)
        {
            message = "导入待验底模失败：" + ex.Message;
            return false;
        }
    }

    public static AutoBattleExternalValidationEntry?
        SnapshotExternalValidationModel()
    {
        try
        {
            var path = ExternalValidationManifestPath();
            if (!File.Exists(path))
            {
                return null;
            }
            var entry = AuraSharedJson.Deserialize<AutoBattleExternalValidationEntry>(
                File.ReadAllText(path));
            return entry?.SchemaVersion == 1 ? entry : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryLoadExternalValidationModel(
        string decisionProfile,
        string selectedModelId,
        out IDecisionResidualModel residual,
        out ICombatSearchGuidanceModel guidance,
        out ICombatPolicyValueModel policyValue,
        out string modelId,
        out string diagnostic)
    {
        residual = NullDecisionResidualModel.Instance;
        guidance = NullCombatSearchGuidanceModel.Instance;
        policyValue = NullCombatPolicyValueModel.Instance;
        modelId = "none";
        if (!TryReadExternalValidationPackage(
                decisionProfile,
                selectedModelId,
                out var package,
                out diagnostic))
        {
            return false;
        }
        policyValue = CreateCoverageAwarePolicyValue(
            package.Model!,
            package);
        modelId = package.Model!.ModelId;
        ResolvePackageCoverage(
            package,
            out var subject,
            out var coverage);
        diagnostic = "外部待验底模“"
                     + package.DisplayName
                     + "”；"
                     + CombatFoundationModelCoverageProtocol.Assess(
                         subject,
                         coverage,
                         CurrentRuntimeContext()).Summary;
        return true;
    }

    public static bool ExternalValidationMeetsGate(
        string decisionProfile,
        string modelId,
        out string reason)
    {
        if (!TryReadExternalValidationPackage(
                decisionProfile,
                modelId,
                out var package,
                out reason))
        {
            return false;
        }
        if (!package.Validation.Passed
            || !package.Validation.BehaviorPassed
            || package.Validation.SevereEndTurnMistakes != 0
            || package.Validation.AvoidableEndTurnsWithUnusedEnergy != 0
            || package.Validation.NoEffectActionAttempts != 0
            || package.Validation.RepeatedNoEffectActionAttempts != 0
            || package.Validation.GuaranteedNoEffectActionAttempts != 0
            || package.Validation.InteractiveActionContractFailures != 0
            || package.Validation.InvalidCampaigns != 0)
        {
            reason = "外部底模没有通过训练阶段的正式隔离验证";
            return false;
        }
        reason = "外部底模训练与隔离验证门禁已通过";
        return true;
    }

    public static bool PortableFoundationMeetsActivationGate(
        string decisionProfile,
        string modelId,
        out string reason)
    {
        if (!TryReadLibraryBundle(
                NormalizeProfile(decisionProfile),
                modelId,
                out var bundle,
                out reason))
        {
            return false;
        }
        if (!string.Equals(
                bundle.ModelPurpose,
                "foundation",
                StringComparison.Ordinal)
            || !bundle.FoundationArtifactValidated
            || bundle.PolicyValue == null
            || !ValidFoundationAcceptance(bundle))
        {
            reason = "所选模型不是已通过正确性验证的可移植底模";
            return false;
        }
        var contentSet = AuraToolsCombatContentRuntime.SnapshotContentSet();
        var universal = string.Equals(
            bundle.ContentSetHash,
            CombatContentSetProtocol.EmptyContentSetHash,
            StringComparison.Ordinal);
        if (!universal
            && (!string.Equals(
                    bundle.ContentSetHash,
                    contentSet.ContentSetHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    bundle.OwnerModSetHash,
                    contentSet.OwnerModSetHash,
                    StringComparison.Ordinal)))
        {
            reason = "底模绑定的内容集合与当前 AuraShared 内容目录不一致";
            return false;
        }
        var assessment = AssessBundleCoverage(bundle);
        reason = "底模工件正确性验证已通过"
                 + (assessment == null
                     ? ""
                     : "；" + assessment.Summary);
        return true;
    }

    public static bool TryPromoteExternalValidationModel(
        string decisionProfile,
        string modelId,
        out string promotedModelId,
        out string message)
    {
        promotedModelId = "";
        if (!TryReadExternalValidationPackage(
                decisionProfile,
                modelId,
                out var package,
                out message))
        {
            return false;
        }
        var bundle = NewCandidateBundle(
            NormalizeProfile(package.Profile),
            "external-foundation:" + package.JobId,
            package.PackageId);
        bundle.BundleId = package.PackageId;
        bundle.ModelPurpose = "foundation";
        bundle.BaseModelId = "";
        bundle.AdapterBinding = null;
        ResolvePackageCoverage(
            package,
            out var trainingSubject,
            out var declaredCoverage);
        bundle.RoleId = package.RoleId;
        bundle.PartnerId = package.PartnerId;
        bundle.EnabledRewardCardPackIds =
            package.EnabledRewardCardPackIds.ToList();
        bundle.PreferredDeckSizeMinimum =
            package.PreferredDeckSizeMinimum;
        bundle.PreferredDeckSizeMaximum =
            package.PreferredDeckSizeMaximum;
        bundle.CardPoolScope = package.CardPoolScope;
        bundle.TrainingSubject = trainingSubject;
        bundle.DeclaredCoverage = declaredCoverage;
        bundle.FoundationArtifactValidated = true;
        bundle.FoundationPackageId = package.PackageId;
        bundle.FoundationWorkerSha256 = package.WorkerSha256;
        bundle.FoundationRulesetHash = package.RulesetHash;
        bundle.ContentSetHash = package.ContentSetHash;
        bundle.OwnerModSetHash = package.OwnerModSetHash;
        bundle.FoundationModelVersion = (package.ModelVersion ?? "").Trim().TrimStart('v', 'V');
        var acceptance = CombatFoundationModelPackageProtocol
            .NormalizeAcceptance(package);
        bundle.FoundationAcceptanceKind = acceptance.Classification;
        bundle.FoundationPromotionProtocolVersion =
            acceptance.PromotionProtocolVersion;
        bundle.FoundationPairedRegressionUpperBound =
            acceptance.PairedRegressionWilsonUpperBound;
        bundle.FoundationAcceptance = acceptance;
        bundle.FoundationDistributionOrigin = "external";
        bundle.ProjectionNormalWinRate =
            package.Validation.NormalWinRate;
        bundle.ProjectionAdvancedWinRate =
            package.Validation.AdvancedWinRate;
        var sourcePath =
            SnapshotExternalValidationModel()?.SourcePath ?? "";
        var stagedEntry = SnapshotExternalValidationModel();
        bundle.FoundationSourcePackageSha256 = stagedEntry?.PackageSha256 ?? "";
        bundle.FoundationSourcePackageFile = string.IsNullOrWhiteSpace(sourcePath)
            ? ""
            : Path.GetFileName(sourcePath);
        bundle.TrainingReportDirectory =
            string.IsNullOrWhiteSpace(sourcePath)
                ? ""
                : Path.GetDirectoryName(sourcePath) ?? "";
        bundle.PolicyValue = package.Model;
        promotedModelId = CandidateModelId(bundle);
        RegisterLibraryBundle(bundle, promotedModelId);
        message = "外部底模已加入模型库，默认保持关闭；"
                  + CombatFoundationModelCoverageProtocol.Assess(
                      trainingSubject,
                      declaredCoverage,
                      CurrentRuntimeContext()).Summary;
        return true;
    }

    public static void ClearExternalValidationModel()
    {
        try
        {
            var entry = SnapshotExternalValidationModel();
            if (entry != null && !string.IsNullOrWhiteSpace(entry.PackageFile))
            {
                var packagePath = Path.Combine(
                    ExternalValidationDirectory(),
                    Path.GetFileName(entry.PackageFile));
                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                }
            }
            if (File.Exists(ExternalValidationManifestPath()))
            {
                File.Delete(ExternalValidationManifestPath());
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn(
                "[AutoBattle][ExternalValidation] 清理待验底模失败：" + ex.Message);
        }
    }

    public static bool TryResolveGameValidationArtifact(
        string decisionProfile,
        string selectedModelId,
        bool preferCandidate,
        out IDecisionResidualModel residual,
        out ICombatSearchGuidanceModel guidance,
        out ICombatPolicyValueModel policyValue,
        out string modelId,
        out string artifactHash,
        out bool candidate,
        out string diagnostic)
    {
        var profile = NormalizeProfile(decisionProfile);
        residual = NullDecisionResidualModel.Instance;
        guidance = NullCombatSearchGuidanceModel.Instance;
        policyValue = NullCombatPolicyValueModel.Instance;
        modelId = "none";
        artifactHash = "";
        candidate = false;

        if (TryReadExternalValidationPackage(
                profile,
                selectedModelId,
                out var externalPackage,
                out diagnostic))
        {
            policyValue = CreateCoverageAwarePolicyValue(
                externalPackage.Model!,
                externalPackage);
            modelId = externalPackage.Model!.ModelId;
            artifactHash = HashBytes(
                    Encoding.UTF8.GetBytes(
                        AuraSharedJson.Serialize(externalPackage)))
                .ToLowerInvariant();
            diagnostic = "外部待验底模";
            return true;
        }

        if (preferCandidate
            && TryReadValidatedCandidateBundle(profile, out var candidateBundle, out diagnostic))
        {
            candidate = true;
            PopulateModels(
                candidateBundle,
                out residual,
                out guidance,
                out policyValue);
            modelId = CandidateModelId(candidateBundle);
            artifactHash = HashBytes(
                Encoding.UTF8.GetBytes(AuraSharedJson.Serialize(candidateBundle)))
                .ToLowerInvariant();
            diagnostic = "待导入候选模型";
            return true;
        }

        if (!TryReadLibraryBundle(profile, selectedModelId, out var libraryBundle, out diagnostic))
        {
            return false;
        }
        PopulateModels(libraryBundle, out residual, out guidance, out policyValue);
        modelId = CandidateModelId(libraryBundle);
        artifactHash = HashBytes(
            Encoding.UTF8.GetBytes(AuraSharedJson.Serialize(libraryBundle)))
            .ToLowerInvariant();
        diagnostic = "模型库模型";
        return true;
    }

    public static IReadOnlyList<AutoBattleModelLibraryEntry> SnapshotModelLibrary(
        string decisionProfile)
    {
        var profile = NormalizeProfile(decisionProfile);
        lock (LibraryGate)
        {
            return ReadLibrary().Models
                .Where(item => string.Equals(
                    NormalizeProfile(item.Profile),
                    profile,
                    StringComparison.Ordinal)
                               && (string.Equals(
                                       item.ModelPurpose,
                                       "foundation",
                                       StringComparison.Ordinal)
                                   || string.Equals(
                                       item.RoleId,
                                       CurrentRoleId,
                                       StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(
                                       item.CardPoolScope,
                                       CurrentCardPoolScope,
                                       StringComparison.Ordinal)))
                .OrderByDescending(item => item.CreatedUtc)
                .Select(CloneLibraryEntryForCurrentContext)
                .ToArray();
        }
    }

    public static bool TryRenameLibraryModel(
        string modelId,
        string displayName,
        out string message)
    {
        var id = (modelId ?? "").Trim();
        var name = (displayName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            message = "请先选择模型并填写新名称";
            return false;
        }
        if (name.Length > 40)
        {
            message = "模型名称不能超过 40 个字符";
            return false;
        }
        lock (LibraryGate)
        {
            var library = ReadLibrary();
            var entry = library.Models.FirstOrDefault(item =>
                string.Equals(item.ModelId, id, StringComparison.Ordinal));
            if (entry == null)
            {
                message = "模型库中不存在该模型";
                return false;
            }
            entry.DisplayName = name;
            entry.DisplayNameMode = "user";
            WriteLibrary(library);
        }
        message = "模型已改名为“" + name + "”";
        return true;
    }

    public static bool TryRestoreGeneratedLibraryModelName(
        string modelId,
        out string message)
    {
        var id = (modelId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            message = "请先选择模型";
            return false;
        }

        lock (LibraryGate)
        {
            var library = ReadLibrary();
            var entry = library.Models.FirstOrDefault(item =>
                string.Equals(item.ModelId, id, StringComparison.Ordinal));
            if (entry == null)
            {
                message = "模型库中不存在该模型";
                return false;
            }
            if (!string.Equals(
                    entry.ModelPurpose,
                    "foundation",
                    StringComparison.Ordinal))
            {
                message = "只有底模支持恢复规范自动命名";
                return false;
            }

            var generatedName = string.IsNullOrWhiteSpace(entry.GeneratedDisplayName)
                ? AuraToolsBundledFoundationModelRuntime.BuildCanonicalDisplayName(
                    entry.RoleId,
                    entry.PartnerId,
                    entry.EnabledRewardCardPackIds,
                    entry.ModelVersion)
                : entry.GeneratedDisplayName.Trim();
            if (string.IsNullOrWhiteSpace(generatedName))
            {
                message = "当前模型缺少自动命名所需的主体或版本信息";
                return false;
            }

            entry.GeneratedDisplayName = generatedName;
            entry.DisplayName = generatedName;
            entry.DisplayNameMode = "generated";
            WriteLibrary(library);
            message = "模型已恢复自动命名：“" + generatedName + "”";
            return true;
        }
    }

    public static bool CandidateMeetsValidationGate(
        string decisionProfile,
        out string reason)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (!TryReadValidatedCandidateBundle(profile, out var bundle, out reason))
        {
            return false;
        }
        if (bundle.Residual != null)
        {
            var battleSessions = MetricCount(
                bundle.Residual.Metrics,
                "battleSessionCount");
            var groupedAccuracy = Metric(
                bundle.Residual.Metrics,
                "groupedValidationAccuracy");
            if (battleSessions < 2)
            {
                reason = "候选至少需要覆盖 2 场独立战斗";
                return false;
            }
            if (groupedAccuracy < 0.55d)
            {
                reason = "候选按战斗分组验证准确率低于 55%（当前 "
                         + groupedAccuracy.ToString("P1")
                         + "）";
                return false;
            }
            reason = "候选人工残差分组验证通过";
            return true;
        }
        if (bundle.PolicyValue == null)
        {
            reason = "候选包没有可验证的残差或策略价值组件";
            return false;
        }
        var episodeCount = MetricCount(bundle.PolicyValue.Metrics, "episodeCount");
        var validationEpisodes = MetricCount(
            bundle.PolicyValue.Metrics,
            "validationEpisodeCount");
        var valueMae = Metric(bundle.PolicyValue.Metrics, "validationValueMae");
        if (episodeCount < 8 || validationEpisodes < 2)
        {
            reason = "候选策略价值网络至少需要 8 条轨迹和 2 条验证轨迹";
            return false;
        }
        if (valueMae > 1.25d)
        {
            reason = "候选策略价值验证 MAE 超过 1.25（当前 "
                     + valueMae.ToString("0.000")
                     + "）";
            return false;
        }
        reason = "候选长期策略价值验证通过";
        return true;
    }

    public static string CandidateModelId(string decisionProfile)
    {
        return TryReadValidatedCandidateBundle(
            NormalizeProfile(decisionProfile),
            out var bundle,
            out _)
            ? CandidateModelId(bundle)
            : "none";
    }

    public static string WritePolicyValueCandidate(
        string decisionProfile,
        CombatPolicyValueNetworkDefinition model)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (!CombatPolicyValueNetworkValidator.TryValidate(model, out var reason)
            || !string.Equals(
                NormalizeProfile(model.DecisionProfile),
                profile,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("策略价值候选无效：" + reason);
        }
        var path = CandidateBundlePath(profile);
        var bundle = ReadCandidateBundle(profile) ?? NewCandidateBundle(profile, "", "");
        bundle.BundleId = NewRunId("evolution");
        bundle.GeneratedUtc = DateTime.UtcNow;
        bundle.PolicyValue = model;
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(path, AuraSharedJson.Serialize(bundle), createBackup: true);
        return path;
    }

    public static string SaveFoundationModel(
        string decisionProfile,
        CombatPolicyValueNetworkDefinition model,
        CombatCampaignFoundationValidation validation,
        string reportDirectory)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (!CombatPolicyValueNetworkValidator.TryValidate(model, out var reason)
            || !string.Equals(
                NormalizeProfile(model.DecisionProfile),
                profile,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("底模策略价值网络无效：" + reason);
        }
        if (validation == null
            || !validation.Passed
            || !validation.BehaviorPassed
            || validation.SevereEndTurnMistakes != 0
            || validation.AvoidableEndTurnsWithUnusedEnergy != 0
            || validation.NoEffectActionAttempts != 0
            || validation.RepeatedNoEffectActionAttempts != 0
            || validation.GuaranteedNoEffectActionAttempts != 0
            || validation.InteractiveActionContractFailures != 0)
        {
            throw new InvalidOperationException(
                "底模尚未通过正式隔离验收线：普通难度须 200/200，"
                + "高级难度须至少 400/500，且不得出现无效冒险");
        }
        var bundle = NewCandidateBundle(profile, "foundation-self-play", "");
        bundle.BundleId = NewRunId("foundation");
        bundle.ModelPurpose = "foundation";
        bundle.ProjectionNormalWinRate = validation.NormalWinRate;
        bundle.ProjectionAdvancedWinRate = validation.AdvancedWinRate;
        bundle.TrainingReportDirectory = reportDirectory ?? "";
        bundle.PolicyValue = model;
        var modelId = CandidateModelId(bundle);
        var path = AuraSharedLogStore.OwnerLogPath(
            AuraToolsIds.ModId,
            "foundation-model-bundle-" + profile + ".json");
        using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
        {
            storage.WriteTextAtomic(
                path,
                AuraSharedJson.Serialize(bundle),
                createBackup: false);
        }
        RegisterLibraryBundle(bundle, modelId);
        return modelId;
    }

    public static bool TryGetInstalledModelInfo(
        string decisionProfile,
        out string modelId,
        out double groupedValidationAccuracy,
        out int battleSessionCount,
        out string reason)
    {
        var profile = NormalizeProfile(decisionProfile);
        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            ModelFile(profile),
            new DecisionResidualModelDefinition());
        if (!snapshot.Found)
        {
            modelId = "";
            groupedValidationAccuracy = 0d;
            battleSessionCount = 0;
            reason = "当前决策风格没有已安装的本地模型：" + profile;
            return false;
        }
        if (!TryValidate(snapshot.Value, profile, out reason))
        {
            modelId = "";
            groupedValidationAccuracy = 0d;
            battleSessionCount = 0;
            return false;
        }

        modelId = snapshot.Value.ModelId ?? "";
        groupedValidationAccuracy = Metric(
            snapshot.Value.Metrics,
            "groupedValidationAccuracy");
        battleSessionCount = MetricCount(snapshot.Value.Metrics, "battleSessionCount");
        reason = "";
        return true;
    }

    public static bool MeetsValidationGate(string decisionProfile, out string reason)
    {
        if (TryGetInstalledModelInfo(
                decisionProfile,
                out _,
                out var groupedAccuracy,
                out var battleSessions,
                out reason))
        {
            if (battleSessions < 2)
            {
                reason = "至少需要覆盖 2 场独立战斗才能进行分组验证";
                return false;
            }
            if (groupedAccuracy < 0.55d)
            {
                reason = "按战斗分组验证准确率低于 55%（当前 "
                         + groupedAccuracy.ToString("P1")
                         + "）";
                return false;
            }
            reason = "人工残差分组验证通过";
            return true;
        }

        var profile = NormalizeProfile(decisionProfile);
        var policyValue = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            PolicyValueModelFile(profile),
            new CombatPolicyValueNetworkDefinition());
        if (!policyValue.Found
            || !CombatPolicyValueNetworkValidator.TryValidate(policyValue.Value, out reason))
        {
            return false;
        }
        var episodeCount = MetricCount(policyValue.Value.Metrics, "episodeCount");
        var validationFrames = MetricCount(policyValue.Value.Metrics, "validationEpisodeCount");
        var valueMae = Metric(policyValue.Value.Metrics, "validationValueMae");
        if (episodeCount < 8 || validationFrames < 2)
        {
            reason = "长期策略价值网络至少需要 8 场训练战斗和 2 场独立验证战斗";
            return false;
        }
        if (valueMae > 0.75d)
        {
            reason = "长期价值验证误差过高（当前 " + valueMae.ToString("0.000") + "）";
            return false;
        }
        reason = "长期策略价值网络分组验证通过";
        return true;
    }

    public static bool QueueGenerateCandidate(
        string decisionProfile,
        Action<string>? completed = null)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy)
        {
            return false;
        }
        CancellationTokenSource ownedCancellation;
        lock (StatusGate)
        {
            if (StatusByProfile.TryGetValue(profile, out var current) && current.Busy)
            {
                return false;
            }
            if (StatusByProfile.Values.Any(item => item.Busy))
            {
                return false;
            }
            if (CancellationByProfile.TryGetValue(profile, out var previous))
            {
                previous.Dispose();
            }
            ownedCancellation = new CancellationTokenSource();
            CancellationByProfile[profile] = ownedCancellation;
        }

        var options = ToTrainingOptions(
            AuraToolsConfigService.MatchExperience.AutoBattle.Training);
        SetStatus(profile, AutoBattleTrainingStage.Queued, "训练任务已排队");
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<TrainingWorkResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "AutoBattle.Train:" + profile,
                Source = "AutoBattle.LocalResidualTraining",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = schedulerToken =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        schedulerToken,
                        ownedCancellation.Token);
                    try
                    {
                        return GenerateCandidate(
                            profile,
                            options,
                            linked.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return TrainingWorkResult.CancelledResult();
                    }
                },
                ApplyOnMainThread = result =>
                {
                    if (result.Cancelled)
                    {
                        var cancelledStatus = GetTrainingStatus(profile);
                        SetStatus(
                            profile,
                            AutoBattleTrainingStage.Cancelled,
                            "候选训练已取消",
                            cancelledStatus.SampleCount,
                            cancelledStatus.InvalidLineCount,
                            cancelledStatus.PreferencePairCount);
                        completed?.Invoke("候选训练已取消");
                    }
                    else if (result.Success)
                    {
                        SetStatus(
                            profile,
                            AutoBattleTrainingStage.CandidateReady,
                            result.Message,
                            result.SampleCount,
                            result.InvalidLineCount,
                            result.PreferencePairCount,
                            result.WeightCount,
                            result.CandidatePath);
                        AuraToolsLog.Info("[AutoBattle][Training] " + result.Message);
                    }
                    else
                    {
                        SetStatus(
                            profile,
                            AutoBattleTrainingStage.Failed,
                            result.Message,
                            result.SampleCount,
                            result.InvalidLineCount,
                            result.PreferencePairCount);
                        AuraToolsLog.Warn("[AutoBattle][Training] " + result.Message);
                    }
                    completed?.Invoke(result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    var message = "本地训练任务失败：" + ex.Message;
                    SetStatus(profile, AutoBattleTrainingStage.Failed, message);
                    AuraToolsLog.Warn("[AutoBattle][Training] " + message);
                    completed?.Invoke(message);
                }
            });
        if (queued)
        {
            AuraToolsLog.Info("[AutoBattle][Training] 已提交本地训练任务，决策风格=" + profile);
        }
        else
        {
            SetStatus(profile, AutoBattleTrainingStage.Failed, "训练任务未能提交，请稍后重试");
        }
        return queued;
    }

    public static bool QueueImportCandidate(
        string decisionProfile,
        Action<string>? completed = null)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (AnyTrainingBusy()
            || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy)
        {
            return false;
        }
        SetStatus(profile, AutoBattleTrainingStage.Importing, "正在校验并导入候选模型");
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<ImportWorkResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "AutoBattle.Import:" + profile,
                Source = "AutoBattle.CandidateImport",
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ =>
                {
                    var success = TryImportCandidate(profile, out var message);
                    return new ImportWorkResult(success, message);
                },
                ApplyOnMainThread = result =>
                {
                    (result.Success ? (Action<string>)AuraToolsLog.Info : AuraToolsLog.Warn)(
                        "[AutoBattle][Import] " + result.Message);
                    completed?.Invoke(result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    var message = "候选模型导入失败：" + ex.Message;
                    SetStatus(profile, AutoBattleTrainingStage.Failed, message);
                    AuraToolsLog.Warn("[AutoBattle][Import] " + message);
                    completed?.Invoke(message);
                }
            });
        if (!queued)
        {
            SetStatus(profile, AutoBattleTrainingStage.Failed, "候选导入任务未能提交");
        }
        return queued;
    }

    public static bool QueueRollbackChampion(
        string decisionProfile,
        Action<string>? completed = null)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (AnyTrainingBusy()
            || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy)
        {
            return false;
        }
        SetStatus(profile, AutoBattleTrainingStage.Importing, "正在回退到上一个冠军模型");
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<ImportWorkResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "AutoBattle.Rollback:" + profile,
                Source = "AutoBattle.ChampionRollback",
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ =>
                {
                    var success = TryRollbackChampion(profile, out var message);
                    return new ImportWorkResult(success, message);
                },
                ApplyOnMainThread = result =>
                {
                    SetStatus(
                        profile,
                        result.Success
                            ? AutoBattleTrainingStage.Imported
                            : AutoBattleTrainingStage.Failed,
                        result.Message);
                    (result.Success ? (Action<string>)AuraToolsLog.Info : AuraToolsLog.Warn)(
                        "[AutoBattle][Rollback] " + result.Message);
                    completed?.Invoke(result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    var message = "冠军模型回退失败：" + ex.Message;
                    SetStatus(profile, AutoBattleTrainingStage.Failed, message);
                    completed?.Invoke(message);
                }
            });
        if (!queued)
        {
            SetStatus(profile, AutoBattleTrainingStage.Failed, "冠军模型回退任务未能提交");
        }
        return queued;
    }

    public static void CancelTraining(string decisionProfile)
    {
        var profile = NormalizeProfile(decisionProfile);
        lock (StatusGate)
        {
            if (!StatusByProfile.TryGetValue(profile, out var current)
                || !current.Busy
                || current.Stage == AutoBattleTrainingStage.Importing)
            {
                return;
            }
            if (CancellationByProfile.TryGetValue(profile, out var source))
            {
                source.Cancel();
            }
            current.Stage = AutoBattleTrainingStage.Cancelling;
            current.Message = "正在取消候选训练";
            current.UpdatedUtc = DateTime.UtcNow;
        }
    }

    public static bool AnyTrainingBusy()
    {
        lock (StatusGate)
        {
            return StatusByProfile.Values.Any(item => item.Busy);
        }
    }

    public static bool TryClearAllCombatLearningData(out string message)
    {
        if (AnyTrainingBusy()
            || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy)
        {
            message = "训练、模拟或导入任务仍在运行，不能清理";
            return false;
        }
        try
        {
            var ownerLogs = Path.GetFullPath(
                AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId));
            var ownerConfig = Path.GetFullPath(
                AuraSharedPaths.OwnerSystemConfigDirectory(
                    AuraToolsIds.ModId,
                    SystemId));
            var ownerData = Path.GetFullPath(
                AuraSharedPaths.OwnerDataDirectory(AuraToolsIds.ModId));
            var foundationTrainerConfig = Path.GetFullPath(
                AuraSharedPaths.OwnerSystemConfigDirectory(
                    AuraToolsIds.ModId,
                    "FoundationTrainer"));
            var resultRoot = Path.GetFullPath(
                AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory);
            AuraToolsAutoBattleTrainingSink.ClearPersistedData();
            var directories = new[]
            {
                Path.Combine(ownerLogs, "training-snapshots"),
                Path.Combine(ownerLogs, "candidate-archive"),
                Path.Combine(ownerLogs, "champion-history"),
                ModelLibraryDirectory(),
                Path.Combine(ownerLogs, "training-batches"),
                resultRoot,
                ownerConfig,
                foundationTrainerConfig
            };
            foreach (var directory in directories
                         .Select(Path.GetFullPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsInside(directory, ownerLogs)
                    && !IsInside(directory, ownerData)
                    && !IsInside(
                        directory,
                        AuraSharedPaths.OwnerConfigDirectory(AuraToolsIds.ModId)))
                {
                    throw new InvalidOperationException("拒绝清理范围外目录：" + directory);
                }
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            if (Directory.Exists(ownerLogs))
            {
                var filePrefixes = new[]
                {
                    "auto-battle-training-",
                    "auto-battle-candidate-",
                    "auto-battle-model-candidate-",
                    "auto-battle-search-model-candidate-",
                    "auto-battle-policy-value-candidate-",
                    "foundation-model-bundle-",
                    "foundation-cleanup-manifest-",
                    "live-combat-episodes-",
                    "journey-episodes-"
                };
                foreach (var path in Directory.EnumerateFiles(
                             ownerLogs,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(path);
                    if (AuraToolsAutoBattleTrainingSink.OwnsPersistedFile(name))
                    {
                        continue;
                    }
                    if (filePrefixes.Any(prefix =>
                            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        File.Delete(path);
                    }
                }
                var controllerSessionPath = Path.Combine(
                    ownerLogs,
                    "foundation-controller-session.json");
                if (File.Exists(controllerSessionPath))
                {
                    File.Delete(controllerSessionPath);
                }
            }
            lock (StatusGate)
            {
                StatusByProfile.Clear();
            }
            AuraToolsAutoBattleSimulationRuntime.ResetAfterDataClear();
            var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
            settings.SelectedModelId = "";
            settings.TrainedModelMode = "off";
            settings.CaptureTrainingSamples = false;
            settings.Normalize();
            AuraToolsConfigService.SaveMatchExperience();
            AuraToolsAutoBattleRuntime.ReloadModels();
            message = "旧战斗样本、训练快照、候选/冠军/模型库和模拟结果已直接删除；知识包与规则配置已保留";
            return true;
        }
        catch (Exception ex)
        {
            message = "清理失败：" + ex.Message;
            return false;
        }
    }

    private static bool IsInside(string candidate, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(
            normalizedRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryImportCandidate(
        string decisionProfile,
        out string message)
    {
        var profile = NormalizeProfile(decisionProfile);
        var bundlePath = CandidateBundlePath(profile);
        SetStatus(profile, AutoBattleTrainingStage.Importing, "正在校验并导入候选模型");
        if (!File.Exists(bundlePath))
        {
            message = "未找到原子候选包；旧版零散候选不会自动导入，请重新训练";
            SetStatus(profile, AutoBattleTrainingStage.Failed, message);
            return false;
        }

        try
        {
            var bundle = ReadCandidateBundle(profile);
            if (bundle == null
                || bundle.SchemaVersion < 3
                || bundle.SchemaVersion > 4
                || !string.Equals(
                    NormalizeProfile(bundle.Profile),
                    profile,
                    StringComparison.Ordinal)
                || !MatchesCurrentRoleScope(bundle)
                || (bundle.Residual == null
                    && bundle.SearchGuidance == null
                    && bundle.PolicyValue == null))
            {
                message = "候选包协议、风格或组件无效";
                SetStatus(profile, AutoBattleTrainingStage.Failed, message);
                return false;
            }
            var contentSet = AuraToolsCombatContentRuntime.SnapshotContentSet();
            var bindingValid = CombatModelAdapterValidator.TryValidate(
                bundle.AdapterBinding,
                (AuraToolsConfigService.MatchExperience.AutoBattle
                    .SelectedModelId ?? "").Trim(),
                contentSet.ContentSetHash,
                out var bindingReason);
            if (!string.Equals(
                    bundle.ModelPurpose,
                    "player-adapter",
                    StringComparison.Ordinal)
                || bundle.Residual == null
                || bundle.SearchGuidance != null
                || bundle.PolicyValue != null
                || !string.Equals(
                    bundle.AdapterBinding?.AdapterKind,
                    CombatModelAdapterProtocol.PersonalKind,
                    StringComparison.Ordinal)
                || !string.Equals(
                    bundle.OwnerModSetHash,
                    contentSet.OwnerModSetHash,
                    StringComparison.Ordinal)
                || !string.Equals(
                    bundle.AdapterBinding?.OwnerModSetHash,
                    contentSet.OwnerModSetHash,
                    StringComparison.Ordinal)
                || !bindingValid)
            {
                message = "玩家适配器绑定无效：" + bindingReason;
                SetStatus(profile, AutoBattleTrainingStage.Failed, message);
                return false;
            }

            var candidateModelId = CandidateModelId(bundle);
            if (!AuraToolsAutoBattleSimulationRuntime.CanActivateModel(
                    profile,
                    candidateModelId,
                    out var promotionReason))
            {
                message = "候选尚不能提升：" + promotionReason;
                SetStatus(profile, AutoBattleTrainingStage.Failed, message);
                return false;
            }

            // Validate every component before mutating installed model state. This prevents
            // an invalid sibling component from producing a partially accepted candidate.
            var validationFailures = new List<string>();
            if (bundle.Residual != null
                && !TryValidate(bundle.Residual, profile, out var residualReason))
            {
                validationFailures.Add("人工残差：" + residualReason);
            }
            if (bundle.SearchGuidance != null
                && !TryValidateSearchGuidance(
                    bundle.SearchGuidance,
                    profile,
                    out var searchReason))
            {
                validationFailures.Add("搜索引导：" + searchReason);
            }
            if (bundle.PolicyValue != null
                && (!CombatPolicyValueNetworkValidator.TryValidate(
                        bundle.PolicyValue,
                        out var policyReason)
                    || !string.Equals(
                        NormalizeProfile(bundle.PolicyValue.DecisionProfile),
                        profile,
                        StringComparison.Ordinal)))
            {
                validationFailures.Add("长期策略价值网络：" + policyReason);
            }
            if (validationFailures.Count > 0)
            {
                message = "候选包校验失败：" + string.Join("；", validationFailures);
                SetStatus(profile, AutoBattleTrainingStage.Failed, message);
                return false;
            }

            var imported = new List<string>();
            var failures = new List<string>();
            var weightCount = 0;
            var preferencePairs = 0;
            var championBackup = ArchiveInstalledChampion(profile);
            if (bundle.AdapterBinding != null)
            {
                var bindingWrite = AuraSharedConfigStore.WriteOwner(
                    AuraToolsIds.ModId,
                    SystemId,
                    AdapterBindingFile(profile),
                    bundle.AdapterBinding,
                    schemaVersion: 1);
                if (!bindingWrite.Success)
                {
                    failures.Add("适配器绑定写入：" + bindingWrite.Message);
                }
            }
            if (failures.Count == 0)
            {
                var write = AuraSharedConfigStore.WriteOwner(
                    AuraToolsIds.ModId,
                    SystemId,
                    ModelFile(profile),
                    bundle.Residual ?? new DecisionResidualModelDefinition(),
                    schemaVersion: 2);
                if (!write.Success)
                {
                    failures.Add("残差模型写入：" + write.Message);
                }
            }
            if (bundle.Residual != null && failures.Count == 0)
            {
                imported.Add("人工残差");
                weightCount += bundle.Residual.Weights.Count;
                preferencePairs = MetricCount(bundle.Residual.Metrics, "pairCount");
            }
            if (failures.Count > 0 && !string.IsNullOrWhiteSpace(championBackup))
            {
                RestoreChampionBundle(championBackup, profile, out _);
            }
            var success = imported.Count > 0 && failures.Count == 0;
            if (success)
            {
                imported.Add("底模绑定");
            }
            message = success
                ? "玩家残差适配器已导入；底模未修改，风格="
                  + profile
                  + "，组件="
                  + string.Join("、", imported)
                : "模型导入未完整完成：已导入="
                  + string.Join("、", imported)
                  + "；失败="
                  + string.Join("；", failures);
            SetStatus(
                profile,
                success ? AutoBattleTrainingStage.Imported : AutoBattleTrainingStage.Failed,
                message,
                preferencePairCount: preferencePairs,
                weightCount: weightCount,
                candidatePath: bundlePath);
            return success;
        }
        catch (Exception ex)
        {
            message = "模型导入失败：" + ex.Message;
            SetStatus(profile, AutoBattleTrainingStage.Failed, message);
            return false;
        }
    }

    public static AutoBattleTrainingStatus GetTrainingStatus(string decisionProfile)
    {
        var profile = NormalizeProfile(decisionProfile);
        lock (StatusGate)
        {
            if (StatusByProfile.TryGetValue(profile, out var current))
            {
                return current.Clone();
            }
        }

        var path = CandidateBundlePath(profile);
        AutoBattleTrainingStatus initial;
        if (File.Exists(path))
        {
            initial = new AutoBattleTrainingStatus
            {
                Profile = profile,
                Stage = AutoBattleTrainingStage.CandidateReady,
                Message = "检测到可导入的候选模型",
                CandidatePath = path
            };
        }
        else
        {
            var installed = AuraSharedConfigStore.ReadOwner(
                AuraToolsIds.ModId,
                SystemId,
                ModelFile(profile),
                new DecisionResidualModelDefinition());
            initial = installed.Found && TryValidate(installed.Value, profile, out _)
                ? new AutoBattleTrainingStatus
                {
                    Profile = profile,
                    Stage = AutoBattleTrainingStage.Imported,
                    Message = "检测到已安装的本地模型",
                    PreferencePairCount = MetricCount(installed.Value.Metrics, "pairCount"),
                    WeightCount = installed.Value.Weights.Count
                }
                : new AutoBattleTrainingStatus
                {
                    Profile = profile
                };
        }

        lock (StatusGate)
        {
            if (!StatusByProfile.ContainsKey(profile))
            {
                StatusByProfile[profile] = initial;
            }
            return StatusByProfile[profile].Clone();
        }
    }

    public static bool CandidateExists(string decisionProfile)
    {
        return File.Exists(CandidateBundlePath(NormalizeProfile(decisionProfile)));
    }

    internal static bool TryValidate(
        DecisionResidualModelDefinition? model,
        string decisionProfile,
        out string reason)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (model == null
            || !string.Equals(
                model.ModelProtocol,
                "aura.decision-residual.linear.v1",
                StringComparison.Ordinal)
            || model.ProtocolVersion != 1
            || model.FeatureSchemaVersion
               != CombatTrainingProtocol.FeatureSchemaVersion
            || model.ApplicabilityProtocolVersion != 1)
        {
            reason = "模型协议、特征版本或适用性协议不兼容";
            return false;
        }
        if (!string.Equals(
                NormalizeProfile(model.DecisionProfile),
                profile,
                StringComparison.Ordinal))
        {
            reason = "模型决策风格不匹配：模型="
                     + NormalizeProfile(model.DecisionProfile)
                     + "，当前=" + profile;
            return false;
        }
        if (model.Weights == null || model.Weights.Count == 0 || model.Weights.Count > 512)
        {
            reason = "模型权重数量无效";
            return false;
        }
        if (model.FeatureMinimums == null
            || model.FeatureMaximums == null
            || model.CategoryObservationCounts == null
            || model.FeatureMinimums.Count == 0
            || model.FeatureMaximums.Count == 0
            || model.CategoryObservationCounts.Count == 0)
        {
            reason = "模型缺少适用范围或动作类别支持数据";
            return false;
        }
        if (!Finite(model.Bias)
            || !Finite(model.MaximumCorrection)
            || model.MaximumCorrection <= 0d
            || model.MaximumCorrection > 2d)
        {
            reason = "模型偏置或修正边界无效";
            return false;
        }
        foreach (var pair in model.Weights)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !Finite(pair.Value))
            {
                reason = "模型包含无效权重";
                return false;
            }
        }

        reason = "";
        return true;
    }

    internal static bool TryValidateSearchGuidance(
        CombatSearchGuidanceDefinition? model,
        string decisionProfile,
        out string reason)
    {
        if (model == null
            || !string.Equals(model.ModelProtocol, "aura.combat-search.gbdt.v1", StringComparison.Ordinal)
            || model.ProtocolVersion != 1
            || model.FeatureSchemaVersion
               != CombatTrainingProtocol.FeatureSchemaVersion
            || !string.Equals(
                NormalizeProfile(model.DecisionProfile),
                NormalizeProfile(decisionProfile),
                StringComparison.Ordinal)
            || model.Policy == null
            || model.Value == null
            || model.Risk == null
            || !Finite(model.Policy.Bias)
            || !Finite(model.Value.Bias)
            || !Finite(model.Risk.Bias)
            || !Finite(model.Policy.MaximumMagnitude)
            || !Finite(model.Value.MaximumMagnitude)
            || !Finite(model.Risk.MaximumMagnitude)
            || model.Policy.MaximumMagnitude < 0d
            || model.Value.MaximumMagnitude < 0d
            || model.Risk.MaximumMagnitude < 0d
            || model.Policy.Trees.Count > 256
            || model.Value.Trees.Count > 256
            || model.Risk.Trees.Count > 256)
        {
            reason = "搜索引导模型协议、风格或树数量无效";
            return false;
        }
        foreach (var tree in model.Policy.Trees
                     .Concat(model.Value.Trees)
                     .Concat(model.Risk.Trees))
        {
            if (string.IsNullOrWhiteSpace(tree.Feature)
                || !Finite(tree.Threshold)
                || !Finite(tree.LeftValue)
                || !Finite(tree.RightValue))
            {
                reason = "搜索引导模型包含无效树节点";
                return false;
            }
        }
        reason = "";
        return true;
    }

    private static TrainingWorkResult GenerateCandidate(
        string profile,
        CombatResidualTrainingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArchiveExistingCandidate(profile);
        SetStatus(profile, AutoBattleTrainingStage.ReadingSamples, "正在读取训练样本");
        var snapshot = CaptureTrainingSnapshot(profile, cancellationToken);
        if (string.IsNullOrWhiteSpace(snapshot.BaseModelId))
        {
            return new TrainingWorkResult(
                false,
                "玩家适配器训练前必须先选择可直接使用的底模",
                0,
                0,
                0);
        }
        var samples = new List<CombatTrainingSample>();
        var invalidLines = 0;
        foreach (var file in snapshot.Files.Where(item =>
                     string.Equals(item.Kind, "samples", StringComparison.Ordinal)))
        {
            if (!File.Exists(file.SnapshotPath))
            {
                continue;
            }

            foreach (var line in File.ReadLines(file.SnapshotPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    var sample = AuraSharedJson.Deserialize<CombatTrainingSample>(line);
                    if (CombatTrainingProtocol.IsCompatible(sample)
                        && string.Equals(
                            sample!.ContentSetHash,
                            snapshot.ContentSetHash,
                            StringComparison.Ordinal))
                    {
                        samples.Add(sample!);
                    }
                    else
                    {
                        invalidLines++;
                    }
                }
                catch
                {
                    invalidLines++;
                }
            }
        }

        SetStatus(
            profile,
            AutoBattleTrainingStage.Training,
            "正在训练人工残差模型",
            samples.Count,
            invalidLines);
        var result = CombatResidualTrainer.Train(
            samples,
            profile,
            options,
            cancellationToken);
        if (!result.Success || result.Model == null)
        {
            return new TrainingWorkResult(
                false,
                result.Message
                + "；读取样本="
                + samples.Count
                + "，无效行="
                + invalidLines,
                samples.Count,
                invalidLines,
                result.PreferencePairCount);
        }

        var candidatePath = CandidateBundlePath(profile);
        cancellationToken.ThrowIfCancellationRequested();
        SetStatus(
            profile,
            AutoBattleTrainingStage.WritingCandidate,
            "正在写入候选模型",
            samples.Count,
            invalidLines,
            result.PreferencePairCount,
            result.Model!.Weights.Count,
            candidatePath);
        var bundle = NewCandidateBundle(
            profile,
            snapshot.SnapshotId,
            snapshot.AggregateSha256);
        bundle.Residual = result.Model;
        using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
        {
            storage.WriteTextAtomic(
                candidatePath,
                AuraSharedJson.Serialize(bundle),
                createBackup: true);
        }
        WriteTrainingBatchManifest(
            bundle,
            samples.Count,
            0,
            0,
            invalidLines,
            result.PreferencePairCount,
            result.Model?.Metrics,
            null);
        return new TrainingWorkResult(
            true,
            result.Message
            + "；底模保持冻结，仅生成玩家偏好残差适配器"
            + "；候选包已写入=" + candidatePath
            + "；训练快照=" + snapshot.SnapshotId
            + " sha256=" + snapshot.AggregateSha256
            + "；无效行=" + invalidLines,
            samples.Count,
            invalidLines,
            result.PreferencePairCount,
            result.Model!.Weights.Count,
            candidatePath);
    }

    private static void WriteTrainingBatchManifest(
        AutoBattleCandidateBundle bundle,
        int sampleCount,
        int episodeCount,
        int journeyCount,
        int invalidLineCount,
        int preferencePairCount,
        IReadOnlyDictionary<string, double>? residualMetrics,
        IReadOnlyDictionary<string, double>? policyValueMetrics)
    {
        var directory = Path.Combine(
            AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId),
            "training-batches",
            bundle.TrainingSnapshotId);
        Directory.CreateDirectory(directory);
        var report = new
        {
            schemaVersion = 1,
            reportKind = "role-specific-combat-model-training",
            bundle.BundleId,
            bundle.GeneratedUtc,
            bundle.Profile,
            bundle.RoleId,
            bundle.CardPoolScope,
            bundle.TrainingSnapshotId,
            bundle.TrainingSnapshotHash,
            sampleCount,
            episodeCount,
            journeyCount,
            invalidLineCount,
            preferencePairCount,
            residualMetrics,
            policyValueMetrics,
            campaignEvaluationStatus = "pending",
            nextStep =
                "Run normal and advanced fixed seven-layer world simulation; each evaluation writes training-report.json and training-report.md."
        };
        var markdown = new StringBuilder()
            .AppendLine("# 角色战斗 AI 训练批次")
            .AppendLine()
            .AppendLine("- 角色：`" + bundle.RoleId + "`")
            .AppendLine("- 卡池范围：`" + bundle.CardPoolScope + "`")
            .AppendLine("- 决策风格：`" + bundle.Profile + "`")
            .AppendLine("- 候选包：`" + bundle.BundleId + "`")
            .AppendLine("- 训练快照：`" + bundle.TrainingSnapshotId + "`")
            .AppendLine("- 决策样本：" + sampleCount)
            .AppendLine("- 完整战斗：" + episodeCount)
            .AppendLine("- 完整旅程：" + journeyCount)
            .AppendLine("- 偏好对：" + preferencePairCount)
            .AppendLine("- 无效行：" + invalidLineCount)
            .AppendLine()
            .AppendLine("当前状态：候选训练完成，世界推演评估待运行。普通与高级难度将分别生成构筑、最终牌组、遗物、祝福及最终首领/失败战斗报告。")
            .ToString();
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(
            Path.Combine(directory, "training-batch.json"),
            AuraSharedJson.Serialize(report),
            createBackup: false);
        storage.WriteTextAtomic(
            Path.Combine(directory, "training-batch.md"),
            markdown,
            createBackup: false);
    }

    private static CombatResidualTrainingOptions ToTrainingOptions(
        AutoBattleTrainingSettings? settings)
    {
        settings ??= AutoBattleTrainingSettings.CreateSteady();
        settings.Normalize();
        return new CombatResidualTrainingOptions
        {
            PresetId = settings.Preset,
            Epochs = settings.Epochs,
            LearningRate = settings.LearningRate,
            L2 = settings.L2,
            MaximumCorrection = settings.MaximumCorrection,
            MinimumPreferencePairs = settings.MinimumPreferencePairs,
            MinimumCategoryObservations = settings.MinimumCategoryObservations
        }.Normalized();
    }

    private static void SetStatus(
        string profile,
        AutoBattleTrainingStage stage,
        string message,
        int sampleCount = 0,
        int invalidLineCount = 0,
        int preferencePairCount = 0,
        int weightCount = 0,
        string candidatePath = "")
    {
        var normalized = NormalizeProfile(profile);
        lock (StatusGate)
        {
            StatusByProfile[normalized] = new AutoBattleTrainingStatus
            {
                Profile = normalized,
                Stage = stage,
                Message = message ?? "",
                SampleCount = sampleCount,
                InvalidLineCount = invalidLineCount,
                PreferencePairCount = preferencePairCount,
                WeightCount = weightCount,
                CandidatePath = candidatePath ?? "",
                UpdatedUtc = DateTime.UtcNow
            };
        }
    }

    private static int MetricCount(
        IReadOnlyDictionary<string, double>? metrics,
        string key)
    {
        if (metrics == null
            || !metrics.TryGetValue(key, out var value)
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            return 0;
        }
        return Math.Max(0, (int)Math.Round(value));
    }

    private static double Metric(
        IReadOnlyDictionary<string, double>? metrics,
        string key)
    {
        if (metrics == null
            || !metrics.TryGetValue(key, out var value)
            || !Finite(value))
        {
            return 0d;
        }
        return value;
    }

    private static AutoBattleTrainingSnapshotManifest CaptureTrainingSnapshot(
        string profile,
        CancellationToken cancellationToken)
    {
        var contentSet = AuraToolsCombatContentRuntime.SnapshotContentSet();
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var snapshotId = NewRunId("training");
        var directory = Path.Combine(
            AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId),
            "training-snapshots",
            snapshotId);
        Directory.CreateDirectory(directory);
        var manifest = new AutoBattleTrainingSnapshotManifest
        {
            SnapshotId = snapshotId,
            Profile = profile,
            RoleId = CurrentRoleId,
            CardPoolScope = CurrentCardPoolScope,
            BaseModelId = (settings.SelectedModelId ?? "").Trim(),
            ContentSetHash = contentSet.ContentSetHash,
            OwnerModSetHash = contentSet.OwnerModSetHash,
            CapturedUtc = DateTime.UtcNow
        };
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        foreach (var fileName in TrainingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(
                AuraToolsCombatContentRuntime.LiveDatasetDirectory(
                    contentSet.ContentSetHash),
                fileName);
            if (!File.Exists(sourcePath))
            {
                continue;
            }
            var stableBytes = storage.ReadCompleteFileSnapshot(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();
            var text = new UTF8Encoding(false, true).GetString(stableBytes);
            var snapshotPath = Path.Combine(directory, fileName);
            storage.WriteTextAtomic(snapshotPath, text, createBackup: false);
            manifest.Files.Add(new AutoBattleTrainingSnapshotFile
            {
                Kind = "samples",
                SourcePath = sourcePath,
                SnapshotPath = snapshotPath,
                StableLength = stableBytes.LongLength,
                Sha256 = HashBytes(stableBytes)
            });
        }
        manifest.AggregateSha256 = HashBytes(
            Encoding.UTF8.GetBytes(
                string.Join(
                    "|",
                    new[]
                    {
                        "schema=" + manifest.SchemaVersion,
                        "profile=" + manifest.Profile,
                        "role=" + manifest.RoleId,
                        "scope=" + manifest.CardPoolScope,
                        "base=" + manifest.BaseModelId,
                        "content=" + manifest.ContentSetHash,
                        "owners=" + manifest.OwnerModSetHash
                    }.Concat(manifest.Files
                        .OrderBy(item => item.SnapshotPath, StringComparer.Ordinal)
                        .Select(item => item.Kind
                                        + ":" + item.SourcePath
                                        + ":" + Path.GetFileName(item.SnapshotPath)
                                        + ":" + item.StableLength
                                        + ":" + item.Sha256)))));
        storage.WriteTextAtomic(
            Path.Combine(directory, "manifest.json"),
            AuraSharedJson.Serialize(manifest),
            createBackup: false);
        return manifest;
    }

    private static AutoBattleCandidateBundle NewCandidateBundle(
        string profile,
        string snapshotId,
        string snapshotHash)
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        settings.Normalize();
        var preset = settings.GameParameters.ActivePreset;
        var contentSet = AuraToolsCombatContentRuntime.SnapshotContentSet();
        var baseModelId = (settings.SelectedModelId ?? "").Trim();
        var adapterId = "player-adapter-"
                        + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        return new AutoBattleCandidateBundle
        {
            BundleId = NewRunId("candidate"),
            Profile = profile,
            RoleId = CurrentRoleId,
            CardPoolScope = CurrentCardPoolScope,
            PartnerId = preset.PartnerId,
            EnabledRewardCardPackIds =
                preset.EnabledRewardCardPackIds.ToList(),
            PreferredDeckSizeMinimum = preset.PreferredDeckSizeMinimum,
            PreferredDeckSizeMaximum = preset.PreferredDeckSizeMaximum,
            GeneratedUtc = DateTime.UtcNow,
            TrainingSnapshotId = snapshotId ?? "",
            TrainingSnapshotHash = snapshotHash ?? "",
            ModelPurpose = "player-adapter",
            BaseModelId = baseModelId,
            ContentSetHash = contentSet.ContentSetHash,
            OwnerModSetHash = contentSet.OwnerModSetHash,
            AdapterBinding = new CombatDecisionAdapterManifest
            {
                AdapterId = adapterId,
                AdapterKind = CombatModelAdapterProtocol.PersonalKind,
                OwnerModId = AuraToolsIds.ModId,
                BaseModelId = baseModelId,
                ContentSetHash = contentSet.ContentSetHash,
                OwnerModSetHash = contentSet.OwnerModSetHash,
                AdjustsPolicy = true,
                AdjustsActionValue = false,
                MaximumPolicyDelta = settings.Training.MaximumCorrection,
                MaximumActionValueDelta = 0d
            }
        };
    }

    private static bool MatchesCurrentRoleScope(AutoBattleCandidateBundle bundle)
    {
        return string.Equals(
                   bundle.ModelPurpose,
                   "foundation",
                   StringComparison.Ordinal)
               || string.Equals(
                   bundle.ModelPurpose,
                   "player-adapter",
                   StringComparison.Ordinal)
                  && string.Equals(
                      bundle.BaseModelId,
                      (AuraToolsConfigService.MatchExperience.AutoBattle
                          .SelectedModelId ?? "").Trim(),
                      StringComparison.Ordinal)
                  && string.Equals(
                      bundle.ContentSetHash,
                      AuraToolsCombatContentRuntime.SnapshotContentSet()
                          .ContentSetHash,
                      StringComparison.Ordinal)
               || string.Equals(
                   bundle.RoleId,
                   CurrentRoleId,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   bundle.CardPoolScope,
                   CurrentCardPoolScope,
                   StringComparison.Ordinal);
    }

    private static bool MatchesContentBinding(AutoBattleCandidateBundle bundle)
    {
        if (!string.Equals(
                bundle.ModelPurpose,
                "foundation",
                StringComparison.Ordinal))
        {
            return string.Equals(
                bundle.ContentSetHash,
                AuraToolsCombatContentRuntime.SnapshotContentSet()
                    .ContentSetHash,
                StringComparison.Ordinal);
        }
        if (string.Equals(
                bundle.ContentSetHash,
                CombatContentSetProtocol.EmptyContentSetHash,
                StringComparison.Ordinal))
        {
            return true;
        }
        var current = AuraToolsCombatContentRuntime.SnapshotContentSet();
        return string.Equals(
                   bundle.ContentSetHash,
                   current.ContentSetHash,
                   StringComparison.Ordinal)
               && string.Equals(
                   bundle.OwnerModSetHash,
                   current.OwnerModSetHash,
                   StringComparison.Ordinal);
    }

    private static AutoBattleCandidateBundle? ReadCandidateBundle(string profile)
    {
        var path = CandidateBundlePath(profile);
        if (!File.Exists(path))
        {
            return null;
        }
        return AuraSharedJson.Deserialize<AutoBattleCandidateBundle>(File.ReadAllText(path));
    }

    private static bool TryReadValidatedCandidateBundle(
        string profile,
        out AutoBattleCandidateBundle bundle,
        out string reason)
    {
        try
        {
            bundle = ReadCandidateBundle(profile) ?? new AutoBattleCandidateBundle();
            if ((bundle.SchemaVersion < 3 || bundle.SchemaVersion > 4)
                || string.IsNullOrWhiteSpace(bundle.BundleId)
                || !string.Equals(
                    NormalizeProfile(bundle.Profile),
                    profile,
                    StringComparison.Ordinal)
                || !MatchesCurrentRoleScope(bundle)
                || (bundle.Residual == null
                    && bundle.SearchGuidance == null
                    && bundle.PolicyValue == null))
            {
                reason = "候选包协议、标识、风格或组件无效";
                return false;
            }
            if (string.Equals(
                    bundle.ModelPurpose,
                    "player-adapter",
                    StringComparison.Ordinal)
                && !CombatModelAdapterValidator.TryValidate(
                    bundle.AdapterBinding,
                    (AuraToolsConfigService.MatchExperience.AutoBattle
                        .SelectedModelId ?? "").Trim(),
                    AuraToolsCombatContentRuntime.SnapshotContentSet()
                        .ContentSetHash,
                    out reason))
            {
                return false;
            }
            if (bundle.Residual != null
                && !TryValidate(bundle.Residual, profile, out reason))
            {
                return false;
            }
            if (bundle.SearchGuidance != null
                && !TryValidateSearchGuidance(bundle.SearchGuidance, profile, out reason))
            {
                return false;
            }
            if (bundle.PolicyValue != null
                && (!CombatPolicyValueNetworkValidator.TryValidate(
                        bundle.PolicyValue,
                        out reason)
                    || !string.Equals(
                        NormalizeProfile(bundle.PolicyValue.DecisionProfile),
                        profile,
                        StringComparison.Ordinal)))
            {
                return false;
            }
            if (string.Equals(
                    bundle.ModelPurpose,
                    "foundation",
                    StringComparison.Ordinal)
                && !ValidFoundationAcceptance(bundle))
            {
                reason = "底模验收证明与模型库协议不兼容";
                return false;
            }
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            bundle = new AutoBattleCandidateBundle();
            reason = "读取候选原子包失败：" + ex.Message;
            return false;
        }
    }

    private static string CandidateModelId(AutoBattleCandidateBundle bundle)
    {
        var ids = new[]
            {
                bundle.Residual?.ModelId,
                bundle.SearchGuidance?.ModelId,
                bundle.PolicyValue?.ModelId
            }
            .Where(id => !string.IsNullOrWhiteSpace(id)
                         && !string.Equals(id, "none", StringComparison.Ordinal))
            .ToArray();
        return ids.Length == 0 ? "none" : string.Join("+", ids);
    }

    private static void PopulateModels(
        AutoBattleCandidateBundle bundle,
        out IDecisionResidualModel residual,
        out ICombatSearchGuidanceModel guidance,
        out ICombatPolicyValueModel policyValue)
    {
        residual = bundle.Residual == null
            ? NullDecisionResidualModel.Instance
            : new BoundedLinearDecisionResidualModel(bundle.Residual);
        guidance = bundle.SearchGuidance == null
            ? NullCombatSearchGuidanceModel.Instance
            : new BoundedTreeCombatSearchGuidanceModel(bundle.SearchGuidance);
        policyValue = bundle.PolicyValue == null
            ? NullCombatPolicyValueModel.Instance
            : CreateCoverageAwarePolicyValue(bundle.PolicyValue, bundle);
    }

    private static ICombatPolicyValueModel CreateCoverageAwarePolicyValue(
        CombatPolicyValueNetworkDefinition definition,
        CombatFoundationModelPackage package)
    {
        ResolvePackageCoverage(
            package,
            out var subject,
            out var coverage);
        return ApplyContentPolicyAdapters(new CoverageAwareCombatPolicyValueModel(
            new ManagedCombatPolicyValueModel(definition),
            subject,
            coverage,
            CurrentRuntimeContext()));
    }

    private static ICombatPolicyValueModel CreateCoverageAwarePolicyValue(
        CombatPolicyValueNetworkDefinition definition,
        AutoBattleCandidateBundle bundle)
    {
        if (!string.Equals(
                bundle.ModelPurpose,
                "foundation",
                StringComparison.Ordinal)
            || bundle.TrainingSubject == null)
        {
            return ApplyContentPolicyAdapters(
                new ManagedCombatPolicyValueModel(definition));
        }
        return ApplyContentPolicyAdapters(new CoverageAwareCombatPolicyValueModel(
            new ManagedCombatPolicyValueModel(definition),
            bundle.TrainingSubject,
            bundle.DeclaredCoverage
            ?? CombatFoundationModelCoverageProtocol.LegacyUnknownCoverage(
                bundle.TrainingSubject),
            CurrentRuntimeContext()));
    }

    private static ICombatPolicyValueModel ApplyContentPolicyAdapters(
        ICombatPolicyValueModel basis)
    {
        var adapters = AuraToolsCombatContentRuntime.SnapshotPolicyAdapters(
            basis.ModelId);
        return adapters.Count == 0
            ? basis
            : new AdaptedCombatPolicyValueModel(basis, adapters);
    }

    private static void RegisterLibraryBundle(
        AutoBattleCandidateBundle bundle,
        string modelId)
    {
        lock (LibraryGate)
        {
            Directory.CreateDirectory(ModelLibraryDirectory());
            var fileName = ModelBundleFileName(modelId);
            var bundlePath = Path.Combine(ModelLibraryDirectory(), fileName);
            var library = ReadLibrary();
            var existing = library.Models.FirstOrDefault(item =>
                string.Equals(item.ModelId, modelId, StringComparison.Ordinal));
            var generatedName = string.Equals(
                bundle.ModelPurpose,
                "foundation",
                StringComparison.Ordinal)
                ? FoundationDisplayName(bundle)
                : "";
            if (existing != null
                && ShouldPreserveBundledRegistration(existing, bundle))
            {
                ApplyFoundationDisplayName(existing, generatedName);
                WriteLibrary(library);
                return;
            }

            using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
            {
                storage.WriteTextAtomic(
                    bundlePath,
                    AuraSharedJson.Serialize(bundle),
                    createBackup: true);
            }
            if (existing == null)
            {
                existing = new AutoBattleModelLibraryEntry
                {
                    ModelId = modelId,
                    DisplayName = string.Equals(
                        bundle.ModelPurpose,
                        "foundation",
                        StringComparison.Ordinal)
                        ? generatedName
                        : bundle.RoleId + " 战斗模型 "
                                  + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    DisplayNameMode = string.Equals(
                        bundle.ModelPurpose,
                        "foundation",
                        StringComparison.Ordinal)
                        ? "generated"
                        : "user",
                    GeneratedDisplayName = generatedName,
                    CreatedUtc = DateTime.UtcNow
                };
                library.Models.Add(existing);
            }
            existing.Profile = NormalizeProfile(bundle.Profile);
            existing.RoleId = bundle.RoleId;
            existing.CardPoolScope = bundle.CardPoolScope;
            existing.PartnerId = bundle.PartnerId;
            existing.EnabledRewardCardPackIds =
                bundle.EnabledRewardCardPackIds.ToList();
            existing.PreferredDeckSizeMinimum =
                bundle.PreferredDeckSizeMinimum;
            existing.PreferredDeckSizeMaximum =
                bundle.PreferredDeckSizeMaximum;
            var coverage = AssessBundleCoverage(bundle);
            existing.CoverageLevel = coverage?.Level ?? "exact-local";
            existing.CoverageSummary = coverage?.Summary
                                       ?? "当前角色与卡池专属模型";
            existing.ModelPurpose = bundle.ModelPurpose;
            existing.ProjectionNormalWinRate = bundle.ProjectionNormalWinRate;
            existing.ProjectionAdvancedWinRate = bundle.ProjectionAdvancedWinRate;
            existing.BundleFile = fileName;
            existing.ModelVersion = bundle.FoundationModelVersion;
            existing.AcceptanceKind = bundle.FoundationAcceptanceKind;
            existing.DistributionOrigin = bundle.FoundationDistributionOrigin;
            existing.SourcePackageSha256 = bundle.FoundationSourcePackageSha256;
            existing.SourcePackageFile = bundle.FoundationSourcePackageFile;
            if (string.Equals(
                    bundle.ModelPurpose,
                    "foundation",
                    StringComparison.Ordinal))
            {
                ApplyFoundationDisplayName(existing, generatedName);
            }
            WriteLibrary(library);
        }
    }

    private static string FoundationDisplayName(AutoBattleCandidateBundle bundle)
    {
        return AuraToolsBundledFoundationModelRuntime.BuildCanonicalDisplayName(
            bundle.RoleId,
            bundle.PartnerId,
            bundle.EnabledRewardCardPackIds,
            bundle.FoundationModelVersion);
    }

    private static bool ShouldPreserveBundledRegistration(
        AutoBattleModelLibraryEntry existing,
        AutoBattleCandidateBundle incoming)
    {
        if (!string.Equals(
                existing.DistributionOrigin,
                "bundled",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                incoming.FoundationDistributionOrigin,
                "external",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(existing.SourcePackageSha256)
            && !string.IsNullOrWhiteSpace(incoming.FoundationSourcePackageSha256))
        {
            return string.Equals(
                existing.SourcePackageSha256,
                incoming.FoundationSourcePackageSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        var existingBundle = ReadLibraryBundleFile(existing.BundleFile);
        return existingBundle != null
               && !string.IsNullOrWhiteSpace(existingBundle.FoundationPackageId)
               && string.Equals(
                   existingBundle.FoundationPackageId,
                   incoming.FoundationPackageId,
                   StringComparison.Ordinal);
    }

    private static void ApplyFoundationDisplayName(
        AutoBattleModelLibraryEntry entry,
        string generatedName,
        string packageDisplayName = "")
    {
        var normalizedGenerated = (generatedName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedGenerated))
        {
            return;
        }

        entry.GeneratedDisplayName = normalizedGenerated;
        if (string.Equals(
                entry.DisplayNameMode,
                "user",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(
                entry.DisplayNameMode,
                "generated",
                StringComparison.OrdinalIgnoreCase)
            || IsLegacyGeneratedFoundationName(
                entry.DisplayName,
                normalizedGenerated,
                packageDisplayName))
        {
            entry.DisplayName = normalizedGenerated;
            entry.DisplayNameMode = "generated";
            return;
        }

        entry.DisplayNameMode = "user";
    }

    private static bool IsLegacyGeneratedFoundationName(
        string displayName,
        string generatedName,
        string packageDisplayName)
    {
        var name = (displayName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)
            || string.Equals(name, generatedName, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(packageDisplayName)
               && string.Equals(
                   name,
                   packageDisplayName.Trim(),
                   StringComparison.Ordinal))
        {
            return true;
        }

        return Regex.IsMatch(
                   name,
                   @"^.+\s底模\s\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}$",
                   RegexOptions.CultureInvariant)
               || name.EndsWith("-全卡包底模", StringComparison.Ordinal)
               || name.EndsWith("-底模", StringComparison.Ordinal);
    }

    internal static BundledFoundationRegistrationSummary
        RegisterBundledFoundationPackages(
            IReadOnlyList<BundledFoundationPackageCandidate> candidates)
    {
        var summary = new BundledFoundationRegistrationSummary();
        lock (LibraryGate)
        {
            Directory.CreateDirectory(ModelLibraryDirectory());
            var library = ReadLibrary();
            var libraryTouched = false;
            using var storage = new AuraSharedStorageCoordinator(
                AuraSharedPaths.RootDirectory);
            foreach (var candidate in candidates
                         ?? Array.Empty<BundledFoundationPackageCandidate>())
            {
                try
                {
                    var package = candidate.Package;
                    if (!CombatFoundationModelPackageProtocol.TryValidate(
                            package,
                            out var validationDiagnostic)
                        || package.Model == null)
                    {
                        summary.Failed++;
                        summary.Diagnostics.Add(
                            candidate.SourceFileName
                            + "：入库前复验失败："
                            + validationDiagnostic);
                        continue;
                    }

                    var modelId = package.Model.ModelId;
                    var existing = library.Models.FirstOrDefault(item =>
                        string.Equals(
                            item.ModelId,
                            modelId,
                            StringComparison.Ordinal));
                    var existingBundle = existing == null
                        ? null
                        : ReadLibraryBundleFile(existing.BundleFile);
                    var existingHash = !string.IsNullOrWhiteSpace(
                        existing?.SourcePackageSha256)
                        ? existing!.SourcePackageSha256
                        : existingBundle?.FoundationSourcePackageSha256 ?? "";
                    if (existing != null
                        && existingBundle == null
                        && string.IsNullOrWhiteSpace(existingHash))
                    {
                        summary.Conflicts++;
                        summary.Diagnostics.Add(
                            candidate.SourceFileName
                            + "：同模型 ID 的既有模型包缺失，无法验证来源哈希，已拒绝自动覆盖："
                            + modelId);
                        continue;
                    }
                    if (existing != null
                        && ((!string.IsNullOrWhiteSpace(existingHash)
                             && !string.Equals(
                                 existingHash,
                                 candidate.SourceSha256,
                                 StringComparison.OrdinalIgnoreCase))
                            || (string.IsNullOrWhiteSpace(existingHash)
                                && existingBundle != null
                                && !string.Equals(
                                    existingBundle.FoundationPackageId,
                                    package.PackageId,
                                    StringComparison.Ordinal))))
                    {
                        summary.Conflicts++;
                        summary.Diagnostics.Add(
                            candidate.SourceFileName
                            + "：模型 ID 已存在但来源哈希不同，已拒绝覆盖："
                            + modelId);
                        continue;
                    }

                    var semanticConflict = library.Models.FirstOrDefault(item =>
                        !string.Equals(
                            item.ModelId,
                            modelId,
                            StringComparison.Ordinal)
                        && SameFoundationRelease(
                            item,
                            package,
                            candidate.ModelVersion));
                    if (semanticConflict != null)
                    {
                        summary.Conflicts++;
                        summary.Diagnostics.Add(
                            candidate.SourceFileName
                            + "：相同角色、使魔、卡池与版本已由另一模型 ID 占用；请提升 ModelVersion："
                            + semanticConflict.ModelId);
                        continue;
                    }

                    var bundle = CreateBundledFoundationBundle(candidate);
                    var fileName = ModelBundleFileName(modelId);
                    var bundlePath = Path.Combine(
                        ModelLibraryDirectory(),
                        fileName);
                    var provenanceMissing = existing != null
                                            && string.IsNullOrWhiteSpace(
                                                existingHash);
                    var bundledMetadataStale = existing != null
                                               && (!string.Equals(
                                                       existing.DistributionOrigin,
                                                       "bundled",
                                                       StringComparison.OrdinalIgnoreCase)
                                                   || existingBundle == null
                                                   || !string.Equals(
                                                       existingBundle.FoundationDistributionOrigin,
                                                       "bundled",
                                                       StringComparison.OrdinalIgnoreCase)
                                                   || !string.Equals(
                                                       existing.SourcePackageFile,
                                                       candidate.SourceFileName,
                                                       StringComparison.OrdinalIgnoreCase));
                    if (existing == null
                        || !File.Exists(bundlePath)
                        || provenanceMissing
                        || bundledMetadataStale)
                    {
                        storage.WriteTextAtomic(
                            bundlePath,
                            AuraSharedJson.Serialize(bundle),
                            createBackup: existing != null);
                    }

                    if (existing == null)
                    {
                        existing = new AutoBattleModelLibraryEntry
                        {
                            ModelId = modelId,
                            DisplayName = candidate.DisplayName,
                            DisplayNameMode = "generated",
                            GeneratedDisplayName = candidate.DisplayName,
                            CreatedUtc = package.CreatedUtc == default
                                ? DateTime.UtcNow
                                : package.CreatedUtc
                        };
                        library.Models.Add(existing);
                        summary.Installed++;
                    }
                    else
                    {
                        summary.Deduplicated++;
                    }

                    UpdateLibraryEntry(existing, bundle, fileName);
                    ApplyFoundationDisplayName(
                        existing,
                        candidate.DisplayName,
                        package.DisplayName);
                    libraryTouched = true;
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    summary.Diagnostics.Add(
                        candidate.SourceFileName + "：入库失败：" + ex.Message);
                }
            }

            if (libraryTouched)
            {
                WriteLibrary(library);
            }
        }
        return summary;
    }

    private static AutoBattleCandidateBundle CreateBundledFoundationBundle(
        BundledFoundationPackageCandidate candidate)
    {
        var package = candidate.Package;
        var bundle = NewCandidateBundle(
            NormalizeProfile(package.Profile),
            "bundled-foundation:" + package.JobId,
            package.PackageId);
        bundle.BundleId = package.PackageId;
        bundle.ModelPurpose = "foundation";
        bundle.BaseModelId = "";
        bundle.AdapterBinding = null;
        ResolvePackageCoverage(
            package,
            out var trainingSubject,
            out var declaredCoverage);
        bundle.RoleId = package.RoleId;
        bundle.PartnerId = package.PartnerId;
        bundle.EnabledRewardCardPackIds = package.EnabledRewardCardPackIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        bundle.PreferredDeckSizeMinimum = package.PreferredDeckSizeMinimum;
        bundle.PreferredDeckSizeMaximum = package.PreferredDeckSizeMaximum;
        bundle.CardPoolScope = package.CardPoolScope;
        bundle.TrainingSubject = trainingSubject;
        bundle.DeclaredCoverage = declaredCoverage;
        bundle.FoundationArtifactValidated = true;
        bundle.FoundationPackageId = package.PackageId;
        bundle.FoundationWorkerSha256 = package.WorkerSha256;
        bundle.FoundationRulesetHash = package.RulesetHash;
        bundle.ContentSetHash = package.ContentSetHash;
        bundle.OwnerModSetHash = package.OwnerModSetHash;
        bundle.FoundationModelVersion = candidate.ModelVersion;
        var acceptance = CombatFoundationModelPackageProtocol
            .NormalizeAcceptance(package);
        bundle.FoundationAcceptanceKind = acceptance.Classification;
        bundle.FoundationPromotionProtocolVersion =
            acceptance.PromotionProtocolVersion;
        bundle.FoundationPairedRegressionUpperBound =
            acceptance.PairedRegressionWilsonUpperBound;
        bundle.FoundationAcceptance = acceptance;
        bundle.FoundationDistributionOrigin = "bundled";
        bundle.FoundationSourcePackageSha256 = candidate.SourceSha256;
        bundle.FoundationSourcePackageFile = candidate.SourceFileName;
        bundle.ProjectionNormalWinRate = package.Validation.NormalWinRate;
        bundle.ProjectionAdvancedWinRate = package.Validation.AdvancedWinRate;
        bundle.GeneratedUtc = package.CreatedUtc;
        bundle.PolicyValue = package.Model;
        return bundle;
    }

    private static bool SameFoundationRelease(
        AutoBattleModelLibraryEntry entry,
        CombatFoundationModelPackage package,
        string modelVersion)
    {
        return !string.IsNullOrWhiteSpace(entry.ModelVersion)
               && string.Equals(
                   entry.ModelVersion.Trim().TrimStart('v', 'V'),
                   modelVersion,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   entry.RoleId,
                   package.RoleId,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   entry.PartnerId,
                   package.PartnerId,
                   StringComparison.OrdinalIgnoreCase)
               && SameIds(
                   entry.EnabledRewardCardPackIds,
                   package.EnabledRewardCardPackIds);
    }

    private static bool SameIds(
        IEnumerable<string>? left,
        IEnumerable<string>? right)
    {
        var leftIds = (left ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
        var rightIds = (right ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
        return leftIds.SequenceEqual(rightIds, StringComparer.OrdinalIgnoreCase);
    }

    private static AutoBattleCandidateBundle? ReadLibraryBundleFile(
        string bundleFile)
    {
        try
        {
            var safeFile = Path.GetFileName(bundleFile ?? "");
            if (string.IsNullOrWhiteSpace(safeFile))
            {
                return null;
            }
            var path = Path.Combine(ModelLibraryDirectory(), safeFile);
            return File.Exists(path)
                ? AuraSharedJson.Deserialize<AutoBattleCandidateBundle>(
                    File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ModelBundleFileName(string modelId)
    {
        return "model-"
               + HashBytes(Encoding.UTF8.GetBytes(modelId))
                   .Substring(0, 16)
                   .ToLowerInvariant()
               + ".json";
    }

    private static void UpdateLibraryEntry(
        AutoBattleModelLibraryEntry entry,
        AutoBattleCandidateBundle bundle,
        string fileName)
    {
        entry.Profile = NormalizeProfile(bundle.Profile);
        entry.RoleId = bundle.RoleId;
        entry.CardPoolScope = bundle.CardPoolScope;
        entry.PartnerId = bundle.PartnerId;
        entry.EnabledRewardCardPackIds = bundle.EnabledRewardCardPackIds.ToList();
        entry.PreferredDeckSizeMinimum = bundle.PreferredDeckSizeMinimum;
        entry.PreferredDeckSizeMaximum = bundle.PreferredDeckSizeMaximum;
        var coverage = AssessBundleCoverage(bundle);
        entry.CoverageLevel = coverage?.Level ?? "exact-local";
        entry.CoverageSummary = coverage?.Summary ?? "当前角色与卡池专属模型";
        entry.ModelPurpose = bundle.ModelPurpose;
        entry.ProjectionNormalWinRate = bundle.ProjectionNormalWinRate;
        entry.ProjectionAdvancedWinRate = bundle.ProjectionAdvancedWinRate;
        entry.BundleFile = fileName;
        entry.ModelVersion = bundle.FoundationModelVersion;
        entry.AcceptanceKind = bundle.FoundationAcceptanceKind;
        entry.DistributionOrigin = bundle.FoundationDistributionOrigin;
        entry.SourcePackageSha256 = bundle.FoundationSourcePackageSha256;
        entry.SourcePackageFile = bundle.FoundationSourcePackageFile;
    }

    private static bool TryReadLibraryBundle(
        string profile,
        string selectedModelId,
        out AutoBattleCandidateBundle bundle,
        out string reason)
    {
        bundle = new AutoBattleCandidateBundle();
        var id = (selectedModelId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            reason = "未选择模型库模型";
            return false;
        }
        try
        {
            AutoBattleModelLibraryEntry? entry;
            lock (LibraryGate)
            {
                entry = ReadLibrary().Models.FirstOrDefault(item =>
                    string.Equals(item.ModelId, id, StringComparison.Ordinal)
                    && string.Equals(
                        NormalizeProfile(item.Profile),
                        profile,
                        StringComparison.Ordinal)
                    && (string.Equals(
                            item.ModelPurpose,
                            "foundation",
                            StringComparison.Ordinal)
                        || string.Equals(
                            item.RoleId,
                            CurrentRoleId,
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            item.CardPoolScope,
                            CurrentCardPoolScope,
                            StringComparison.Ordinal)));
            }
            if (entry == null)
            {
                reason = "所选模型不属于当前决策风格";
                return false;
            }
            var path = Path.Combine(ModelLibraryDirectory(), entry.BundleFile);
            bundle = AuraSharedJson.Deserialize<AutoBattleCandidateBundle>(
                         File.ReadAllText(path))
                     ?? new AutoBattleCandidateBundle();
            if ((bundle.SchemaVersion < 3 || bundle.SchemaVersion > 4)
                || !string.Equals(CandidateModelId(bundle), id, StringComparison.Ordinal)
                || !string.Equals(
                    NormalizeProfile(bundle.Profile),
                    profile,
                    StringComparison.Ordinal)
                || !MatchesCurrentRoleScope(bundle)
                || !MatchesContentBinding(bundle))
            {
                reason = "模型库索引与模型包不一致";
                return false;
            }
            if (bundle.Residual != null
                && !TryValidate(bundle.Residual, profile, out reason))
            {
                return false;
            }
            if (bundle.SearchGuidance != null
                && !TryValidateSearchGuidance(bundle.SearchGuidance, profile, out reason))
            {
                return false;
            }
            if (bundle.PolicyValue != null
                && (!CombatPolicyValueNetworkValidator.TryValidate(
                        bundle.PolicyValue,
                        out reason)
                    || !string.Equals(
                        NormalizeProfile(bundle.PolicyValue.DecisionProfile),
                        profile,
                        StringComparison.Ordinal)))
            {
                return false;
            }
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = "读取模型库失败：" + ex.Message;
            return false;
        }
    }

    private static bool TryReadExternalValidationPackage(
        string decisionProfile,
        string selectedModelId,
        out CombatFoundationModelPackage package,
        out string reason)
    {
        package = new CombatFoundationModelPackage();
        var profile = NormalizeProfile(decisionProfile);
        var modelId = (selectedModelId ?? "").Trim();
        var entry = SnapshotExternalValidationModel();
        if (entry == null
            || string.IsNullOrWhiteSpace(modelId)
            || !string.Equals(entry.ModelId, modelId, StringComparison.Ordinal)
            || !string.Equals(
                NormalizeProfile(entry.Profile),
                profile,
                StringComparison.Ordinal))
        {
            reason = "未选择匹配的外部待验底模";
            return false;
        }
        try
        {
            var path = Path.Combine(
                ExternalValidationDirectory(),
                Path.GetFileName(entry.PackageFile));
            if (!File.Exists(path))
            {
                reason = "外部待验底模文件已丢失";
                return false;
            }
            var json = File.ReadAllText(path);
            var hash = HashBytes(Encoding.UTF8.GetBytes(json))
                .ToLowerInvariant();
            if (!string.Equals(
                    hash,
                    entry.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "外部待验底模暂存哈希不匹配";
                return false;
            }
            package = AuraSharedJson.Deserialize<CombatFoundationModelPackage>(
                          json)
                      ?? new CombatFoundationModelPackage();
            if (!CombatFoundationModelPackageProtocol.TryValidate(
                    package,
                    out reason)
                || !string.Equals(
                    package.Model!.ModelId,
                    entry.ModelId,
                    StringComparison.Ordinal))
            {
                return false;
            }
            ResolvePackageCoverage(package, out _, out _);
            return true;
        }
        catch (Exception ex)
        {
            reason = "读取外部待验底模失败：" + ex.Message;
            return false;
        }
    }

    private static void ResolvePackageCoverage(
        CombatFoundationModelPackage package,
        out CombatFoundationTrainingSubject subject,
        out CombatFoundationDeclaredCoverage coverage)
    {
        subject = package.TrainingSubject
                  ?? CombatFoundationModelCoverageProtocol.FromLegacyPackage(
                      package);
        subject = CombatFoundationModelCoverageProtocol.Normalize(subject);
        if (package.DeclaredCoverage != null)
        {
            coverage = package.DeclaredCoverage;
            return;
        }
        if (AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
                out var campaign,
                out var ruleset,
                out _))
        {
            coverage =
                CombatFoundationModelCoverageProtocol.CreateDeclaredCoverage(
                    campaign,
                    ruleset,
                    subject);
            return;
        }
        coverage =
            CombatFoundationModelCoverageProtocol.LegacyUnknownCoverage(
                subject);
    }

    private static bool ValidFoundationAcceptance(
        AutoBattleCandidateBundle bundle)
    {
        if (bundle.SchemaVersion < 4)
        {
            return true;
        }
        var acceptance = bundle.FoundationAcceptance;
        if (acceptance == null
            || !string.Equals(
                bundle.FoundationAcceptanceKind,
                acceptance.Classification,
                StringComparison.Ordinal))
        {
            return false;
        }
        if (string.Equals(
                acceptance.Classification,
                "legacy-formal-acceptance",
                StringComparison.Ordinal))
        {
            return acceptance.FormalIsolationPassed;
        }
        return CombatFoundationModelPackageProtocol.IsValidAcceptance(
            acceptance);
    }

    private static CombatModelCoverageAssessment? AssessBundleCoverage(
        AutoBattleCandidateBundle bundle)
    {
        if (!string.Equals(
                bundle.ModelPurpose,
                "foundation",
                StringComparison.Ordinal)
            || bundle.TrainingSubject == null)
        {
            return null;
        }
        return CombatFoundationModelCoverageProtocol.Assess(
            bundle.TrainingSubject,
            bundle.DeclaredCoverage
            ?? CombatFoundationModelCoverageProtocol.LegacyUnknownCoverage(
                bundle.TrainingSubject),
            CurrentRuntimeContext());
    }

    private static string DescribeWorkerProvenance(
        CombatFoundationModelPackage package)
    {
        var workerPath = Path.Combine(
            AuraToolsConfigService.ModDirectory,
            "TrainingWorker",
            "AuraFoundationTrainer.Worker.exe");
        if (string.IsNullOrWhiteSpace(package.WorkerSha256))
        {
            return "训练 Worker 未记录；模型语义协议已验证";
        }
        if (!File.Exists(workerPath))
        {
            return "训练 Worker SHA256=" + package.WorkerSha256;
        }
        return string.Equals(
            HashFile(workerPath),
            package.WorkerSha256,
            StringComparison.OrdinalIgnoreCase)
            ? "训练 Worker 与本机一致"
            : "训练 Worker 与本机不同；仅作为来源提示，不影响导入";
    }

    private static string LibraryDisplayName(string modelId)
    {
        lock (LibraryGate)
        {
            return ReadLibrary().Models.FirstOrDefault(item =>
                       string.Equals(item.ModelId, modelId, StringComparison.Ordinal))
                       ?.DisplayName
                   ?? modelId;
        }
    }

    private static AutoBattleModelLibraryDocument ReadLibrary()
    {
        EnsureModelLibraryMigrated();
        var path = ModelLibraryManifestPath();
        if (!File.Exists(path))
        {
            return new AutoBattleModelLibraryDocument();
        }
        var library = AuraSharedJson.Deserialize<AutoBattleModelLibraryDocument>(
                          File.ReadAllText(path))
                      ?? new AutoBattleModelLibraryDocument();
        library.SchemaVersion = Math.Max(5, library.SchemaVersion);
        library.Models ??= new List<AutoBattleModelLibraryEntry>();
        return library;
    }

    private static void WriteLibrary(AutoBattleModelLibraryDocument library)
    {
        EnsureModelLibraryMigrated();
        library.SchemaVersion = Math.Max(5, library.SchemaVersion);
        Directory.CreateDirectory(ModelLibraryDirectory());
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(
            ModelLibraryManifestPath(),
            AuraSharedJson.Serialize(library),
            createBackup: true);
    }

    private static AutoBattleModelLibraryEntry CloneLibraryEntry(
        AutoBattleModelLibraryEntry source)
    {
        return new AutoBattleModelLibraryEntry
        {
            ModelId = source.ModelId,
            DisplayName = source.DisplayName,
            DisplayNameMode = source.DisplayNameMode,
            GeneratedDisplayName = source.GeneratedDisplayName,
            Profile = source.Profile,
            RoleId = source.RoleId,
            CardPoolScope = source.CardPoolScope,
            PartnerId = source.PartnerId,
            EnabledRewardCardPackIds =
                source.EnabledRewardCardPackIds.ToList(),
            PreferredDeckSizeMinimum = source.PreferredDeckSizeMinimum,
            PreferredDeckSizeMaximum = source.PreferredDeckSizeMaximum,
            CoverageLevel = source.CoverageLevel,
            CoverageSummary = source.CoverageSummary,
            ModelPurpose = source.ModelPurpose,
            ProjectionNormalWinRate = source.ProjectionNormalWinRate,
            ProjectionAdvancedWinRate = source.ProjectionAdvancedWinRate,
            BundleFile = source.BundleFile,
            ModelVersion = source.ModelVersion,
            AcceptanceKind = source.AcceptanceKind,
            DistributionOrigin = source.DistributionOrigin,
            SourcePackageSha256 = source.SourcePackageSha256,
            SourcePackageFile = source.SourcePackageFile,
            CreatedUtc = source.CreatedUtc
        };
    }

    private static AutoBattleModelLibraryEntry
        CloneLibraryEntryForCurrentContext(
            AutoBattleModelLibraryEntry source)
    {
        var clone = CloneLibraryEntry(source);
        if (!string.Equals(
                clone.ModelPurpose,
                "foundation",
                StringComparison.Ordinal))
        {
            return clone;
        }
        var subject = new CombatFoundationTrainingSubject
        {
            RoleId = clone.RoleId,
            PartnerId = clone.PartnerId,
            EnabledRewardCardPackIds =
                clone.EnabledRewardCardPackIds.ToList(),
            PreferredDeckSizeMinimum = Math.Max(
                1,
                clone.PreferredDeckSizeMinimum),
            PreferredDeckSizeMaximum = Math.Max(
                Math.Max(1, clone.PreferredDeckSizeMinimum),
                clone.PreferredDeckSizeMaximum)
        };
        var assessment = CombatFoundationModelCoverageProtocol.Assess(
            subject,
            new CombatFoundationDeclaredCoverage
            {
                EntityCoverageKnown = !string.Equals(
                    clone.CoverageLevel,
                    "legacy",
                    StringComparison.OrdinalIgnoreCase)
            },
            CurrentRuntimeContext());
        clone.CoverageLevel = assessment.Level;
        clone.CoverageSummary = assessment.Summary;
        return clone;
    }

    private static string ModelLibraryDirectory()
    {
        return AuraSharedPaths.OwnerSystemDataDirectory(
            AuraToolsIds.ModId,
            "FoundationModels");
    }

    private static string ModelLibraryManifestPath()
    {
        return Path.Combine(ModelLibraryDirectory(), "models.json");
    }

    private static void EnsureModelLibraryMigrated()
    {
        var destination = ModelLibraryDirectory();
        var destinationManifest = Path.Combine(destination, "models.json");
        if (File.Exists(destinationManifest))
        {
            return;
        }

        var legacy = Path.Combine(
            AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId),
            "model-library");
        var legacyManifest = Path.Combine(legacy, "models.json");
        if (!File.Exists(legacyManifest))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(destination);
            var files = Directory.EnumerateFiles(legacy, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => string.Equals(
                    Path.GetFileName(path),
                    "models.json",
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0)
                .ToArray();
            using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
            foreach (var source in files)
            {
                var target = Path.Combine(destination, Path.GetFileName(source));
                if (File.Exists(target))
                {
                    continue;
                }

                storage.WriteTextAtomic(
                    target,
                    File.ReadAllText(source),
                    createBackup: false);
            }
            AuraToolsLog.Info("[AutoBattle][Models] copied legacy model library into AuraShared owner data; source="
                              + legacy
                              + "; destination="
                              + destination
                              + ".");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[AutoBattle][Models] legacy model library migration failed; continuing without deletion: "
                              + ex.Message);
        }
    }

    private static string ExternalValidationDirectory()
    {
        return Path.Combine(
            AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId),
            "external-foundation-validation");
    }

    private static string ExternalValidationManifestPath()
    {
        return Path.Combine(
            ExternalValidationDirectory(),
            "selected-foundation.json");
    }

    private static string NewRunId(string prefix)
    {
        return prefix
               + "-"
               + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")
               + "-"
               + Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    private static void ArchiveExistingCandidate(string profile)
    {
        var path = CandidateBundlePath(profile);
        if (!File.Exists(path))
        {
            return;
        }
        var directory = Path.Combine(
            AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId),
            "candidate-archive");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(path)
            + "-"
            + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")
            + ".json");
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.MoveFileInsideRoot(path, destination);
    }

    private static string ArchiveInstalledChampion(string profile)
    {
        var residual = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            ModelFile(profile),
            new DecisionResidualModelDefinition());
        var search = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            SearchModelFile(profile),
            new CombatSearchGuidanceDefinition());
        var policyValue = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            PolicyValueModelFile(profile),
            new CombatPolicyValueNetworkDefinition());
        var adapterBinding = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            AdapterBindingFile(profile),
            new CombatDecisionAdapterManifest());
        if (!residual.Found
            && !search.Found
            && !policyValue.Found
            && !adapterBinding.Found)
        {
            return "";
        }
        var bundle = NewCandidateBundle(profile, "", "");
        bundle.BundleId = NewRunId("champion");
        bundle.Residual = residual.Found ? residual.Value : null;
        bundle.SearchGuidance = search.Found ? search.Value : null;
        bundle.PolicyValue = policyValue.Found ? policyValue.Value : null;
        bundle.AdapterBinding = adapterBinding.Found ? adapterBinding.Value : null;
        var directory = ChampionHistoryDirectory(profile);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, bundle.BundleId + ".json");
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(path, AuraSharedJson.Serialize(bundle), createBackup: false);
        return path;
    }

    private static bool TryRollbackChampion(string profile, out string message)
    {
        var directory = ChampionHistoryDirectory(profile);
        if (!Directory.Exists(directory))
        {
            message = "没有可回退的冠军模型";
            return false;
        }
        var path = Directory.EnumerateFiles(directory, "champion-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "没有可回退的冠军模型";
            return false;
        }
        if (!RestoreChampionBundle(path, profile, out message))
        {
            return false;
        }
        var restoredPath = path + ".restored";
        if (File.Exists(restoredPath))
        {
            restoredPath += "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        }
        using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
        {
            storage.MoveFileInsideRoot(path, restoredPath);
        }
        message = "已回退到上一个冠军模型；当前 active 开关未改变";
        return true;
    }

    private static bool RestoreChampionBundle(
        string path,
        string profile,
        out string message)
    {
        try
        {
            var bundle = AuraSharedJson.Deserialize<AutoBattleCandidateBundle>(
                File.ReadAllText(path));
            if (bundle == null
                || !string.Equals(
                    NormalizeProfile(bundle.Profile),
                    profile,
                    StringComparison.Ordinal))
            {
                message = "冠军历史包无效";
                return false;
            }
            var writes = new[]
            {
                AuraSharedConfigStore.WriteOwner(
                    AuraToolsIds.ModId,
                    SystemId,
                    ModelFile(profile),
                    bundle.Residual ?? new DecisionResidualModelDefinition(),
                    schemaVersion: 2),
                AuraSharedConfigStore.WriteOwner(
                    AuraToolsIds.ModId,
                    SystemId,
                    SearchModelFile(profile),
                    bundle.SearchGuidance ?? new CombatSearchGuidanceDefinition(),
                    schemaVersion: 1),
                AuraSharedConfigStore.WriteOwner(
                    AuraToolsIds.ModId,
                    SystemId,
                    PolicyValueModelFile(profile),
                    bundle.PolicyValue ?? new CombatPolicyValueNetworkDefinition(),
                    schemaVersion: 1),
                AuraSharedConfigStore.WriteOwner(
                    AuraToolsIds.ModId,
                    SystemId,
                    AdapterBindingFile(profile),
                    bundle.AdapterBinding ?? new CombatDecisionAdapterManifest(),
                    schemaVersion: 1)
            };
            var failed = writes.FirstOrDefault(write => !write.Success);
            if (failed != null)
            {
                message = "冠军历史恢复写入失败：" + failed.Message;
                return false;
            }
            message = "冠军历史恢复成功";
            return true;
        }
        catch (Exception ex)
        {
            message = "冠军历史恢复失败：" + ex.Message;
            return false;
        }
    }

    private static string ChampionHistoryDirectory(string profile)
    {
        return Path.Combine(
            AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId),
            "champion-history",
            profile);
    }

    private static string HashBytes(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes ?? Array.Empty<byte>()))
            .Replace("-", "");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream))
            .Replace("-", "");
    }

    private static string CandidateBundlePath(string profile)
    {
        return AuraSharedLogStore.OwnerLogPath(
            AuraToolsIds.ModId,
            "auto-battle-candidate-bundle-" + profile + ".json");
    }

    private static string CandidatePath(string profile)
    {
        return AuraSharedLogStore.OwnerLogPath(
            AuraToolsIds.ModId,
            "auto-battle-model-candidate-" + profile + ".json");
    }

    private static string SearchCandidatePath(string profile)
    {
        return AuraSharedLogStore.OwnerLogPath(
            AuraToolsIds.ModId,
            "auto-battle-search-model-candidate-" + profile + ".json");
    }

    private static string PolicyValueCandidatePath(string profile)
    {
        return AuraSharedLogStore.OwnerLogPath(
            AuraToolsIds.ModId,
            "auto-battle-policy-value-candidate-" + profile + ".json");
    }

    private static string ModelFile(string profile)
    {
        return "residual-model-" + profile + ".json";
    }

    private static string AdapterBindingFile(string profile)
    {
        return "player-adapter-binding-" + profile + ".json";
    }

    private static bool TryValidateInstalledAdapterBinding(
        string profile,
        string baseModelId,
        out string diagnostic)
    {
        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            AdapterBindingFile(profile),
            new CombatDecisionAdapterManifest());
        if (!snapshot.Found)
        {
            diagnostic = "已安装残差缺少底模/内容集合绑定，请重新训练玩家适配器";
            return false;
        }
        return CombatModelAdapterValidator.TryValidate(
            snapshot.Value,
            (baseModelId ?? "").Trim(),
            AuraToolsCombatContentRuntime.SnapshotContentSet().ContentSetHash,
            out diagnostic);
    }

    private static string SearchModelFile(string profile)
    {
        return "search-guidance-model-" + profile + ".json";
    }

    private static string PolicyValueModelFile(string profile)
    {
        return "policy-value-model-" + profile + ".json";
    }

    private static string NormalizeProfile(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "aggressive" => "aggressive",
            "defensive" => "defensive",
            _ => "balanced"
        };
    }

    private static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed class TrainingWorkResult
    {
        public TrainingWorkResult(
            bool success,
            string message,
            int sampleCount,
            int invalidLineCount,
            int preferencePairCount,
            int weightCount = 0,
            string candidatePath = "")
        {
            Success = success;
            Message = message;
            SampleCount = sampleCount;
            InvalidLineCount = invalidLineCount;
            PreferencePairCount = preferencePairCount;
            WeightCount = weightCount;
            CandidatePath = candidatePath;
        }

        public bool Cancelled { get; private set; }

        public bool Success { get; }

        public string Message { get; }

        public int SampleCount { get; }

        public int InvalidLineCount { get; }

        public int PreferencePairCount { get; }

        public int WeightCount { get; }

        public string CandidatePath { get; }

        public static TrainingWorkResult CancelledResult()
        {
            return new TrainingWorkResult(false, "候选训练已取消", 0, 0, 0)
            {
                Cancelled = true
            };
        }
    }

    private sealed class ImportWorkResult
    {
        public ImportWorkResult(bool success, string message)
        {
            Success = success;
            Message = message ?? "";
        }

        public bool Success { get; }

        public string Message { get; }
    }
}
