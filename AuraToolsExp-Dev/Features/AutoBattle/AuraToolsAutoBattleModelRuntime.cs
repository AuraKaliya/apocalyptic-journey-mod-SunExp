using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AuraCombatAi.Shared;
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

internal static class AuraToolsAutoBattleModelRuntime
{
    private const string SystemId = "AuraCombatAI";
    private static readonly object StatusGate = new();
    private static readonly Dictionary<string, AutoBattleTrainingStatus> StatusByProfile =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CancellationTokenSource> CancellationByProfile =
        new(StringComparer.Ordinal);
    private static readonly string[] TrainingFiles =
    {
        "auto-battle-training-v4.jsonl",
        "auto-battle-training-v3.jsonl"
    };

    public static IDecisionResidualModel Load(
        string decisionProfile,
        bool enabled,
        out string diagnostic)
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

        diagnostic = "已加载本地模型=" + snapshot.Value.ModelId
                     + "，风格=" + profile
                     + "，revision=" + snapshot.Revision;
        return new BoundedLinearDecisionResidualModel(snapshot.Value);
    }

    public static ICombatSearchGuidanceModel LoadSearchGuidance(
        string decisionProfile,
        bool enabled,
        out string diagnostic)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (!enabled)
        {
            diagnostic = "搜索引导模型已关闭";
            return NullCombatSearchGuidanceModel.Instance;
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
        out string diagnostic)
    {
        var profile = NormalizeProfile(decisionProfile);
        if (!enabled)
        {
            diagnostic = "长期策略价值网络已关闭";
            return NullCombatPolicyValueModel.Instance;
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
        string decisionProfile)
    {
        var profile = NormalizeProfile(decisionProfile);
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
        var path = PolicyValueCandidatePath(profile);
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(path, AuraSharedJson.Serialize(model), createBackup: true);
        return path;
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

    public static bool TryImportCandidate(
        string decisionProfile,
        out string message)
    {
        var profile = NormalizeProfile(decisionProfile);
        var residualPath = CandidatePath(profile);
        var searchPath = SearchCandidatePath(profile);
        var policyValuePath = PolicyValueCandidatePath(profile);
        SetStatus(profile, AutoBattleTrainingStage.Importing, "正在校验并导入候选模型");
        if (!File.Exists(residualPath)
            && !File.Exists(searchPath)
            && !File.Exists(policyValuePath))
        {
            message = "未找到任何训练候选模型";
            SetStatus(profile, AutoBattleTrainingStage.Failed, message);
            return false;
        }

        try
        {
            var imported = new List<string>();
            var failures = new List<string>();
            var weightCount = 0;
            var preferencePairs = 0;
            if (File.Exists(residualPath))
            {
                var model = AuraSharedJson.Deserialize<DecisionResidualModelDefinition>(
                    File.ReadAllText(residualPath));
                if (!TryValidate(model, profile, out var reason))
                {
                    failures.Add("残差模型：" + reason);
                }
                else
                {
                    model ??= new DecisionResidualModelDefinition();
                    var write = AuraSharedConfigStore.WriteOwner(
                        AuraToolsIds.ModId,
                        SystemId,
                        ModelFile(profile),
                        model,
                        schemaVersion: 2);
                    if (write.Success)
                    {
                        imported.Add("人工残差");
                        weightCount += model.Weights.Count;
                        preferencePairs = MetricCount(model.Metrics, "pairCount");
                    }
                    else
                    {
                        failures.Add("残差模型写入：" + write.Message);
                    }
                }
            }
            if (File.Exists(searchPath))
            {
                var searchModel = AuraSharedJson.Deserialize<CombatSearchGuidanceDefinition>(
                    File.ReadAllText(searchPath));
                if (!TryValidateSearchGuidance(searchModel, profile, out var searchReason))
                {
                    failures.Add("搜索引导：" + searchReason);
                }
                else
                {
                    var searchWrite = AuraSharedConfigStore.WriteOwner(
                        AuraToolsIds.ModId,
                        SystemId,
                        SearchModelFile(profile),
                        searchModel ?? new CombatSearchGuidanceDefinition(),
                        schemaVersion: 1);
                    if (searchWrite.Success)
                    {
                        imported.Add("搜索引导");
                    }
                    else
                    {
                        failures.Add("搜索引导写入：" + searchWrite.Message);
                    }
                }
            }
            if (File.Exists(policyValuePath))
            {
                var policyValue =
                    AuraSharedJson.Deserialize<CombatPolicyValueNetworkDefinition>(
                        File.ReadAllText(policyValuePath));
                if (!CombatPolicyValueNetworkValidator.TryValidate(policyValue, out var reason)
                    || !string.Equals(
                        NormalizeProfile(policyValue?.DecisionProfile ?? ""),
                        profile,
                        StringComparison.Ordinal))
                {
                    failures.Add("长期策略价值网络：" + reason);
                }
                else
                {
                    var write = AuraSharedConfigStore.WriteOwner(
                        AuraToolsIds.ModId,
                        SystemId,
                        PolicyValueModelFile(profile),
                        policyValue ?? new CombatPolicyValueNetworkDefinition(),
                        schemaVersion: 1);
                    if (write.Success)
                    {
                        imported.Add("长期策略价值网络");
                        weightCount += policyValue?.HiddenDimensions ?? 0;
                    }
                    else
                    {
                        failures.Add("长期策略价值网络写入：" + write.Message);
                    }
                }
            }
            var success = imported.Count > 0 && failures.Count == 0;
            message = success
                ? "模型已导入，风格=" + profile + "，组件=" + string.Join("、", imported)
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
                candidatePath: File.Exists(policyValuePath) ? policyValuePath : residualPath);
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

        var path = File.Exists(PolicyValueCandidatePath(profile))
            ? PolicyValueCandidatePath(profile)
            : CandidatePath(profile);
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
        return File.Exists(CandidatePath(NormalizeProfile(decisionProfile)));
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
            || model.FeatureSchemaVersion != 4
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
            || model.FeatureSchemaVersion != 4
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
        SetStatus(profile, AutoBattleTrainingStage.ReadingSamples, "正在读取训练样本");
        var samples = new List<CombatTrainingSample>();
        var invalidLines = 0;
        foreach (var file in TrainingFiles)
        {
            var path = AuraSharedLogStore.OwnerLogPath(AuraToolsIds.ModId, file);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var line in File.ReadLines(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    var sample = AuraSharedJson.Deserialize<CombatTrainingSample>(line);
                    if (sample != null)
                    {
                        samples.Add(sample);
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
        var episodes = ReadEpisodes(out var invalidEpisodeLines, cancellationToken);
        invalidLines += invalidEpisodeLines;
        SetStatus(
            profile,
            AutoBattleTrainingStage.Training,
            "正在训练长期策略价值网络",
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

        var candidatePath = result.Model != null
            ? CandidatePath(profile)
            : PolicyValueCandidatePath(profile);
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
        using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
        {
            if (result.Model != null)
            {
                storage.WriteTextAtomic(
                    CandidatePath(profile),
                    AuraSharedJson.Serialize(result.Model),
                    createBackup: true);
                if (searchGuidance?.Success == true && searchGuidance.Model != null)
                {
                    storage.WriteTextAtomic(
                        SearchCandidatePath(profile),
                        AuraSharedJson.Serialize(searchGuidance.Model),
                        createBackup: true);
                }
            }
            if (policyValue.Model != null)
            {
                storage.WriteTextAtomic(
                    PolicyValueCandidatePath(profile),
                    AuraSharedJson.Serialize(policyValue.Model),
                    createBackup: true);
            }
        }
        return new TrainingWorkResult(
            true,
            (result.Success ? result.Message : "人工残差未更新")
            + "；"
            + (policyValue.Success ? policyValue.Message : "长期策略价值网络未更新：" + policyValue.Message)
            + "；候选已写入=" + candidatePath
            + "；无效行=" + invalidLines,
            samples.Count + episodes.Sum(episode => episode.Frames.Count),
            invalidLines,
            result.PreferencePairCount,
            (result.Model?.Weights.Count ?? 0)
            + (policyValue.Model?.HiddenDimensions ?? 0),
            candidatePath);
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
        out int invalidLines,
        CancellationToken cancellationToken)
    {
        var result = new List<CombatEpisode>();
        invalidLines = 0;
        var roots = new[]
        {
            AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
            AuraToolsAutoBattleSimulationRuntime.InputDirectory
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(
                         root,
                         "*episodes-v1.jsonl",
                         SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadLines(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    try
                    {
                        var episode = AuraSharedJson.Deserialize<CombatEpisode>(line);
                        if (episode != null)
                        {
                            result.Add(episode);
                        }
                    }
                    catch
                    {
                        invalidLines++;
                    }
                }
            }
        }
        return result
            .GroupBy(episode => episode.EpisodeId, StringComparer.Ordinal)
            .Select(group => group.First())
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
