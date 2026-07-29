using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
    public int SchemaVersion { get; set; } = 2;

    public string BundleId { get; set; } = "";

    public string Profile { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string CardPoolScope { get; set; } = "";

    public string ModelPurpose { get; set; } = "candidate";

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

    public string Profile { get; set; } = "balanced";

    public string RoleId { get; set; } = "";

    public string CardPoolScope { get; set; } = "";

    public string ModelPurpose { get; set; } = "candidate";

    public double ProjectionNormalWinRate { get; set; }

    public double ProjectionAdvancedWinRate { get; set; }

    public string BundleFile { get; set; } = "";

    public DateTime CreatedUtc { get; set; }
}

internal sealed class AutoBattleModelLibraryDocument
{
    public int SchemaVersion { get; set; } = 2;

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
}

internal sealed class AutoBattleTrainingSnapshotManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string SnapshotId { get; set; } = "";

    public string Profile { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string CardPoolScope { get; set; } = "";

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
    private static readonly object StatusGate = new();
    private static readonly object LibraryGate = new();
    private static readonly Dictionary<string, AutoBattleTrainingStatus> StatusByProfile =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CancellationTokenSource> CancellationByProfile =
        new(StringComparer.Ordinal);
    private static readonly string[] TrainingFiles =
    {
        "auto-battle-training-v6.jsonl"
    };

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

        if (TryReadLibraryBundle(profile, selectedModelId, out var libraryBundle, out _)
            && libraryBundle.Residual != null
            && TryValidate(libraryBundle.Residual, profile, out diagnostic))
        {
            diagnostic = "已加载模型库残差=" + libraryBundle.Residual.ModelId;
            return new BoundedLinearDecisionResidualModel(libraryBundle.Residual);
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

        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            SearchModelFile(profile),
            new CombatSearchGuidanceDefinition());
        if (!snapshot.Found)
        {
            diagnostic = "当前决策风格没有已安装的搜索引导模型：" + profile;
            return NullCombatSearchGuidanceModel.Instance;
        }
        if (!TryValidateSearchGuidance(snapshot.Value, profile, out diagnostic))
        {
            return NullCombatSearchGuidanceModel.Instance;
        }
        diagnostic = "已加载搜索引导模型=" + snapshot.Value.ModelId
                     + "，revision=" + snapshot.Revision;
        return new BoundedTreeCombatSearchGuidanceModel(snapshot.Value);
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
            diagnostic = "已加载模型库策略价值=" + libraryBundle.PolicyValue.ModelId;
            return new ManagedCombatPolicyValueModel(libraryBundle.PolicyValue);
        }

        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            PolicyValueModelFile(profile),
            new CombatPolicyValueNetworkDefinition());
        if (!snapshot.Found)
        {
            diagnostic = "当前决策风格没有已安装的长期策略价值网络：" + profile;
            return NullCombatPolicyValueModel.Instance;
        }
        if (!CombatPolicyValueNetworkValidator.TryValidate(snapshot.Value, out diagnostic)
            || !string.Equals(
                NormalizeProfile(snapshot.Value.DecisionProfile),
                profile,
                StringComparison.Ordinal))
        {
            return NullCombatPolicyValueModel.Instance;
        }
        diagnostic = "已加载长期策略价值网络="
                     + snapshot.Value.ModelId
                     + "，revision="
                     + snapshot.Revision;
        return new ManagedCombatPolicyValueModel(snapshot.Value);
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
        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            SystemId,
            PolicyValueModelFile(profile),
            new CombatPolicyValueNetworkDefinition());
        return snapshot.Found
               && CombatPolicyValueNetworkValidator.TryValidate(snapshot.Value, out _)
            ? snapshot.Value
            : null;
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
        if (bundle.Residual != null)
        {
            residual = new BoundedLinearDecisionResidualModel(bundle.Residual);
        }
        if (bundle.SearchGuidance != null)
        {
            guidance = new BoundedTreeCombatSearchGuidanceModel(bundle.SearchGuidance);
        }
        if (bundle.PolicyValue != null)
        {
            policyValue = new ManagedCombatPolicyValueModel(bundle.PolicyValue);
        }
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
        if (bundle.Residual != null)
        {
            residual = new BoundedLinearDecisionResidualModel(bundle.Residual);
        }
        if (bundle.SearchGuidance != null)
        {
            guidance = new BoundedTreeCombatSearchGuidanceModel(bundle.SearchGuidance);
        }
        if (bundle.PolicyValue != null)
        {
            policyValue = new ManagedCombatPolicyValueModel(bundle.PolicyValue);
        }
        modelId = CandidateModelId(bundle);
        diagnostic = "已从模型库加载“" + LibraryDisplayName(modelId) + "”";
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
            if (!TryValidateExternalPackageCompatibility(package!, out message))
            {
                return false;
            }
            modelId = package!.Model!.ModelId;
            if (string.IsNullOrWhiteSpace(modelId)
                || string.Equals(modelId, "none", StringComparison.Ordinal))
            {
                message = "待验底模没有稳定模型 ID";
                return false;
            }
            var packageHash = HashBytes(Encoding.UTF8.GetBytes(json))
                .ToLowerInvariant();
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
                            DisplayName = string.IsNullOrWhiteSpace(
                                package.DisplayName)
                                ? "外部待验底模"
                                : package.DisplayName.Trim(),
                            Profile = NormalizeProfile(package.Profile),
                            PackageFile = packageFile,
                            PackageSha256 = packageHash,
                            SourcePath = Path.GetFullPath(path),
                            StagedUtc = DateTime.UtcNow
                        }),
                    createBackup: true);
            }
            message = "已暂存外部待验底模“"
                      + (string.IsNullOrWhiteSpace(package.DisplayName)
                          ? modelId
                          : package.DisplayName)
                      + "”；尚未加入模型库";
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
        policyValue = new ManagedCombatPolicyValueModel(package.Model!);
        modelId = package.Model!.ModelId;
        diagnostic = "外部待验底模“" + package.DisplayName + "”";
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
            || package.Validation.InvalidCampaigns != 0)
        {
            reason = "外部底模没有通过训练阶段的正式隔离验证";
            return false;
        }
        reason = "外部底模训练与隔离验证门禁已通过";
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
        bundle.ProjectionNormalWinRate =
            package.Validation.NormalWinRate;
        bundle.ProjectionAdvancedWinRate =
            package.Validation.AdvancedWinRate;
        var sourcePath =
            SnapshotExternalValidationModel()?.SourcePath ?? "";
        bundle.TrainingReportDirectory =
            string.IsNullOrWhiteSpace(sourcePath)
                ? ""
                : Path.GetDirectoryName(sourcePath) ?? "";
        bundle.PolicyValue = package.Model;
        promotedModelId = CandidateModelId(bundle);
        RegisterLibraryBundle(bundle, promotedModelId);
        message = "外部底模已加入模型库，默认保持关闭";
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
            policyValue = new ManagedCombatPolicyValueModel(
                externalPackage.Model!);
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
                               && string.Equals(
                                   item.RoleId,
                                   CurrentRoleId,
                                   StringComparison.OrdinalIgnoreCase)
                               && string.Equals(
                                   item.CardPoolScope,
                                   CurrentCardPoolScope,
                                   StringComparison.Ordinal))
                .OrderByDescending(item => item.CreatedUtc)
                .Select(CloneLibraryEntry)
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
            WriteLibrary(library);
        }
        message = "模型已改名为“" + name + "”";
        return true;
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
        if (validation == null || !validation.Passed)
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
        var policyValueOptions = ToPolicyValueTrainingOptions(
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
                            policyValueOptions,
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
            return StatusByProfile.Values.Any(item => item.Busy)
                   || AuraToolsAutoBattleFoundationRuntime.GetStatus().Busy;
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
                Path.Combine(ownerLogs, "model-library"),
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
            AuraToolsAutoBattleFoundationRuntime.ResetAfterDataClear();
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
                || bundle.SchemaVersion != 2
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
            if (failures.Count == 0)
            {
                var searchWrite = AuraSharedConfigStore.WriteOwner(
                    AuraToolsIds.ModId,
                    SystemId,
                    SearchModelFile(profile),
                    bundle.SearchGuidance ?? new CombatSearchGuidanceDefinition(),
                    schemaVersion: 1);
                if (!searchWrite.Success)
                {
                    failures.Add("搜索引导写入：" + searchWrite.Message);
                }
            }
            if (bundle.SearchGuidance != null && failures.Count == 0)
            {
                imported.Add("搜索引导");
            }
            if (failures.Count == 0)
            {
                var write = AuraSharedConfigStore.WriteOwner(
                    AuraToolsIds.ModId,
                    SystemId,
                    PolicyValueModelFile(profile),
                    bundle.PolicyValue ?? new CombatPolicyValueNetworkDefinition(),
                    schemaVersion: 1);
                if (!write.Success)
                {
                    failures.Add("长期策略价值网络写入：" + write.Message);
                }
            }
            if (bundle.PolicyValue != null && failures.Count == 0)
            {
                imported.Add("长期策略价值网络");
                weightCount += bundle.PolicyValue.HiddenDimensions;
            }
            if (failures.Count > 0 && !string.IsNullOrWhiteSpace(championBackup))
            {
                RestoreChampionBundle(championBackup, profile, out _);
            }
            var success = imported.Count > 0 && failures.Count == 0;
            if (success)
            {
                RegisterLibraryBundle(bundle, candidateModelId);
            }
            message = success
                ? "模型已导入并加入模型库，风格="
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
        CombatPolicyValueTrainingOptions policyValueOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArchiveExistingCandidate(profile);
        SetStatus(profile, AutoBattleTrainingStage.ReadingSamples, "正在读取训练样本");
        var snapshot = CaptureTrainingSnapshot(profile, cancellationToken);
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
                    if (CombatTrainingProtocol.IsCompatible(sample))
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
        SetStatus(
            profile,
            AutoBattleTrainingStage.ReadingSamples,
            "正在读取完整战斗轨迹",
            samples.Count,
            invalidLines,
            result.PreferencePairCount);
        var episodes = ReadEpisodes(
            snapshot,
            out var invalidEpisodeLines,
            cancellationToken);
        var reconstructedEpisodes = CombatLiveEpisodeAssembler.Assemble(samples);
        episodes = episodes
            .Concat(reconstructedEpisodes)
            .GroupBy(episode => episode.EpisodeId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var journeys = ReadJourneys(
            snapshot,
            out var invalidJourneyLines,
            cancellationToken);
        CombatJourneyTrainingProjection.ApplyJourneyReturns(episodes, journeys);
        invalidLines += invalidEpisodeLines;
        invalidLines += invalidJourneyLines;
        SetStatus(
            profile,
            AutoBattleTrainingStage.Training,
            "正在训练长期策略价值网络（完整战斗 "
            + episodes.Count
            + " / 完整旅程 "
            + journeys.Count
            + "）",
            samples.Count + episodes.Sum(episode => episode.Frames.Count),
            invalidLines,
            result.PreferencePairCount);
        var policyValue = CombatPolicyValueTrainer.Train(
            episodes,
            profile,
            policyValueOptions,
            cancellationToken);
        if ((!result.Success || result.Model == null)
            && (!policyValue.Success || policyValue.Model == null))
        {
            return new TrainingWorkResult(
                false,
                result.Message
                + "；"
                + policyValue.Message
                + "；读取样本="
                + samples.Count
                + "，完整战斗="
                + episodes.Count
                + "，无效行="
                + invalidLines,
                samples.Count + episodes.Sum(episode => episode.Frames.Count),
                invalidLines,
                result.PreferencePairCount);
        }

        var candidatePath = CandidateBundlePath(profile);
        CombatSearchGuidanceTrainingResult? searchGuidance = null;
        if (result.Model != null)
        {
            SetStatus(
                profile,
                AutoBattleTrainingStage.Training,
                "正在训练搜索引导模型",
                samples.Count + episodes.Sum(episode => episode.Frames.Count),
                invalidLines,
                result.PreferencePairCount);
            searchGuidance = CombatSearchGuidanceTrainer.Train(
                samples,
                profile,
                rounds: Math.Max(8, Math.Min(128, options.Epochs / 2)),
                learningRate: options.LearningRate,
                cancellationToken: cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        SetStatus(
            profile,
            AutoBattleTrainingStage.WritingCandidate,
            "正在写入候选模型",
            samples.Count,
            invalidLines,
            result.PreferencePairCount,
            (result.Model?.Weights.Count ?? 0)
            + (policyValue.Model?.HiddenDimensions ?? 0),
            candidatePath);
        var bundle = NewCandidateBundle(
            profile,
            snapshot.SnapshotId,
            snapshot.AggregateSha256);
        bundle.Residual = result.Model;
        bundle.SearchGuidance = searchGuidance?.Success == true
            ? searchGuidance.Model
            : null;
        bundle.PolicyValue = policyValue.Model;
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
            episodes.Count,
            journeys.Count,
            invalidLines,
            result.PreferencePairCount,
            result.Model?.Metrics,
            policyValue.Model?.Metrics);
        return new TrainingWorkResult(
            true,
            (result.Success ? result.Message : "人工残差未更新")
            + "；"
            + (policyValue.Success ? policyValue.Message : "长期策略价值网络未更新：" + policyValue.Message)
            + "；候选包已写入=" + candidatePath
            + "；训练快照=" + snapshot.SnapshotId
            + " sha256=" + snapshot.AggregateSha256
            + "；无效行=" + invalidLines,
            samples.Count + episodes.Sum(episode => episode.Frames.Count),
            invalidLines,
            result.PreferencePairCount,
            (result.Model?.Weights.Count ?? 0)
            + (policyValue.Model?.HiddenDimensions ?? 0),
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

    private static CombatPolicyValueTrainingOptions ToPolicyValueTrainingOptions(
        AutoBattleTrainingSettings? settings)
    {
        settings ??= AutoBattleTrainingSettings.CreateSteady();
        settings.Normalize();
        return new CombatPolicyValueTrainingOptions
        {
            Epochs = settings.Epochs,
            LearningRate = Math.Min(0.02d, settings.LearningRate),
            L2 = settings.L2,
            HiddenDimensions = settings.PolicyValueHiddenDimensions,
            MinimumEpisodes = settings.MinimumEpisodes
        }.Normalized();
    }

    private static List<CombatEpisode> ReadEpisodes(
        AutoBattleTrainingSnapshotManifest snapshot,
        out int invalidLines,
        CancellationToken cancellationToken)
    {
        var result = new List<CombatEpisode>();
        invalidLines = 0;
        foreach (var file in snapshot.Files.Where(item =>
                     string.Equals(item.Kind, "episodes", StringComparison.Ordinal)))
        {
            foreach (var line in File.ReadLines(file.SnapshotPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    var episode = AuraSharedJson.Deserialize<CombatEpisode>(line);
                    if (episode != null
                        && string.Equals(
                            episode.ModelProtocol,
                            CombatPolicyValueProtocol.EpisodeProtocol,
                            StringComparison.Ordinal)
                        && episode.FeatureSchemaVersion
                           == CombatPolicyValueProtocol.FeatureSchemaVersion
                        && !string.Equals(
                            episode.Provenance,
                            "offline-formal-evaluation",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(episode);
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
        return result
            .GroupBy(episode => episode.EpisodeId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static List<CombatJourneyTrainingEpisode> ReadJourneys(
        AutoBattleTrainingSnapshotManifest snapshot,
        out int invalidLines,
        CancellationToken cancellationToken)
    {
        var result = new List<CombatJourneyTrainingEpisode>();
        invalidLines = 0;
        foreach (var file in snapshot.Files.Where(item =>
                     string.Equals(item.Kind, "journeys", StringComparison.Ordinal)))
        {
            foreach (var line in File.ReadLines(file.SnapshotPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    var journey =
                        AuraSharedJson.Deserialize<CombatJourneyTrainingEpisode>(line);
                    if (journey != null)
                    {
                        result.Add(journey);
                    }
                }
                catch
                {
                    invalidLines++;
                }
            }
        }
        return result
            .GroupBy(journey => journey.JourneyRunId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.EndedUtc).First())
            .ToList();
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
            CapturedUtc = DateTime.UtcNow
        };
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        foreach (var fileName in TrainingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = AuraSharedLogStore.OwnerLogPath(AuraToolsIds.ModId, fileName);
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
        var episodeSources = new[]
            {
                AuraToolsAutoBattleSimulationRuntime.InputDirectory
            }
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(
                root,
                "*episodes-v1.jsonl",
                SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (Directory.Exists(AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory))
        {
            episodeSources.AddRange(
                Directory.EnumerateDirectories(
                        AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
                        "*-evolution",
                        SearchOption.TopDirectoryOnly)
                    .SelectMany(directory => Directory.EnumerateFiles(
                        directory,
                        "*episodes-v1.jsonl",
                        SearchOption.AllDirectories))
                    .Where(path => !episodeSources.Contains(
                        path,
                        StringComparer.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        }
        var liveEpisodesPath = AuraSharedLogStore.OwnerLogPath(
            AuraToolsIds.ModId,
            "live-combat-episodes-v4.jsonl");
        if (File.Exists(liveEpisodesPath)
            && !episodeSources.Contains(liveEpisodesPath, StringComparer.OrdinalIgnoreCase))
        {
            episodeSources.Add(liveEpisodesPath);
        }
        for (var index = 0; index < episodeSources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = episodeSources[index];
            var stableBytes = storage.ReadCompleteFileSnapshot(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();
            var text = new UTF8Encoding(false, true).GetString(stableBytes);
            var snapshotPath = Path.Combine(
                directory,
                "episodes-" + index.ToString("D4") + ".jsonl");
            storage.WriteTextAtomic(snapshotPath, text, createBackup: false);
            manifest.Files.Add(new AutoBattleTrainingSnapshotFile
            {
                Kind = "episodes",
                SourcePath = sourcePath,
                SnapshotPath = snapshotPath,
                StableLength = stableBytes.LongLength,
                Sha256 = HashBytes(stableBytes)
            });
        }
        var journeySources = new[]
            {
                AuraToolsAutoBattleSimulationRuntime.InputDirectory
            }
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(
                root,
                "*journey-episodes-v1.jsonl",
                SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var liveJourneysPath = AuraSharedLogStore.OwnerLogPath(
            AuraToolsIds.ModId,
            "journey-episodes-v1.jsonl");
        if (File.Exists(liveJourneysPath)
            && !journeySources.Contains(liveJourneysPath, StringComparer.OrdinalIgnoreCase))
        {
            journeySources.Add(liveJourneysPath);
        }
        for (var index = 0; index < journeySources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = journeySources[index];
            var stableBytes = storage.ReadCompleteFileSnapshot(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();
            var text = new UTF8Encoding(false, true).GetString(stableBytes);
            var snapshotPath = Path.Combine(
                directory,
                "journeys-" + index.ToString("D4") + ".jsonl");
            storage.WriteTextAtomic(snapshotPath, text, createBackup: false);
            manifest.Files.Add(new AutoBattleTrainingSnapshotFile
            {
                Kind = "journeys",
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
                    manifest.Files
                        .OrderBy(item => item.SnapshotPath, StringComparer.Ordinal)
                        .Select(item => item.Kind
                                        + ":" + item.SourcePath
                                        + ":" + Path.GetFileName(item.SnapshotPath)
                                        + ":" + item.StableLength
                                        + ":" + item.Sha256))));
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
        return new AutoBattleCandidateBundle
        {
            BundleId = NewRunId("candidate"),
            Profile = profile,
            RoleId = CurrentRoleId,
            CardPoolScope = CurrentCardPoolScope,
            GeneratedUtc = DateTime.UtcNow,
            TrainingSnapshotId = snapshotId ?? "",
            TrainingSnapshotHash = snapshotHash ?? ""
        };
    }

    private static bool MatchesCurrentRoleScope(AutoBattleCandidateBundle bundle)
    {
        return string.Equals(
                   bundle.RoleId,
                   CurrentRoleId,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   bundle.CardPoolScope,
                   CurrentCardPoolScope,
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
            if (bundle.SchemaVersion != 2
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
            : new ManagedCombatPolicyValueModel(bundle.PolicyValue);
    }

    private static void RegisterLibraryBundle(
        AutoBattleCandidateBundle bundle,
        string modelId)
    {
        lock (LibraryGate)
        {
            Directory.CreateDirectory(ModelLibraryDirectory());
            var fileName = "model-"
                           + HashBytes(Encoding.UTF8.GetBytes(modelId))
                               .Substring(0, 16)
                               .ToLowerInvariant()
                           + ".json";
            var bundlePath = Path.Combine(ModelLibraryDirectory(), fileName);
            using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
            {
                storage.WriteTextAtomic(
                    bundlePath,
                    AuraSharedJson.Serialize(bundle),
                    createBackup: true);
            }
            var library = ReadLibrary();
            var existing = library.Models.FirstOrDefault(item =>
                string.Equals(item.ModelId, modelId, StringComparison.Ordinal));
            if (existing == null)
            {
                existing = new AutoBattleModelLibraryEntry
                {
                    ModelId = modelId,
                    DisplayName = string.Equals(
                        bundle.ModelPurpose,
                        "foundation",
                        StringComparison.Ordinal)
                        ? bundle.RoleId + " 底模 "
                          + DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                        : bundle.RoleId + " 战斗模型 "
                                  + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    CreatedUtc = DateTime.UtcNow
                };
                library.Models.Add(existing);
            }
            existing.Profile = NormalizeProfile(bundle.Profile);
            existing.RoleId = bundle.RoleId;
            existing.CardPoolScope = bundle.CardPoolScope;
            existing.ModelPurpose = bundle.ModelPurpose;
            existing.ProjectionNormalWinRate = bundle.ProjectionNormalWinRate;
            existing.ProjectionAdvancedWinRate = bundle.ProjectionAdvancedWinRate;
            existing.BundleFile = fileName;
            WriteLibrary(library);
        }
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
                    && string.Equals(
                        item.RoleId,
                        CurrentRoleId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        item.CardPoolScope,
                        CurrentCardPoolScope,
                        StringComparison.Ordinal));
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
            if (!string.Equals(CandidateModelId(bundle), id, StringComparison.Ordinal)
                || !string.Equals(
                    NormalizeProfile(bundle.Profile),
                    profile,
                    StringComparison.Ordinal)
                || !MatchesCurrentRoleScope(bundle))
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
                    StringComparison.Ordinal)
                || !TryValidateExternalPackageCompatibility(
                    package,
                    out reason))
            {
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            reason = "读取外部待验底模失败：" + ex.Message;
            return false;
        }
    }

    private static bool TryValidateExternalPackageCompatibility(
        CombatFoundationModelPackage package,
        out string reason)
    {
        if (!AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
                out var campaign,
                out var ruleset,
                out reason))
        {
            return false;
        }
        if (!string.Equals(
                package.RulesetHash,
                ruleset.RulesetHash,
                StringComparison.Ordinal)
            || !string.Equals(
                package.RoleId,
                CurrentRoleId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                package.CardPoolScope,
                CurrentCardPoolScope,
                StringComparison.Ordinal)
            || !string.Equals(
                package.Compatibility.CampaignId,
                campaign.CampaignId,
                StringComparison.Ordinal)
            || !string.Equals(
                package.Compatibility.CampaignVersion,
                campaign.CampaignVersion,
                StringComparison.Ordinal))
        {
            reason = "外部底模与当前角色、使魔、卡包、卡组倾向或冻结规则不兼容";
            return false;
        }
        var workerPath = AuraToolsFoundationWorkerRuntime.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(package.WorkerSha256)
            && File.Exists(workerPath))
        {
            var currentWorkerHash = HashFile(workerPath);
            if (!string.Equals(
                    currentWorkerHash,
                    package.WorkerSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "外部底模由不同版本的训练 Worker 生成";
                return false;
            }
        }
        reason = "";
        return true;
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
        var path = ModelLibraryManifestPath();
        if (!File.Exists(path))
        {
            return new AutoBattleModelLibraryDocument();
        }
        return AuraSharedJson.Deserialize<AutoBattleModelLibraryDocument>(
                   File.ReadAllText(path))
               ?? new AutoBattleModelLibraryDocument();
    }

    private static void WriteLibrary(AutoBattleModelLibraryDocument library)
    {
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
            Profile = source.Profile,
            RoleId = source.RoleId,
            CardPoolScope = source.CardPoolScope,
            ModelPurpose = source.ModelPurpose,
            ProjectionNormalWinRate = source.ProjectionNormalWinRate,
            ProjectionAdvancedWinRate = source.ProjectionAdvancedWinRate,
            BundleFile = source.BundleFile,
            CreatedUtc = source.CreatedUtc
        };
    }

    private static string ModelLibraryDirectory()
    {
        return Path.Combine(
            AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId),
            "model-library");
    }

    private static string ModelLibraryManifestPath()
    {
        return Path.Combine(ModelLibraryDirectory(), "models.json");
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
        if (!residual.Found && !search.Found && !policyValue.Found)
        {
            return "";
        }
        var bundle = NewCandidateBundle(profile, "", "");
        bundle.BundleId = NewRunId("champion");
        bundle.Residual = residual.Found ? residual.Value : null;
        bundle.SearchGuidance = search.Found ? search.Value : null;
        bundle.PolicyValue = policyValue.Found ? policyValue.Value : null;
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
