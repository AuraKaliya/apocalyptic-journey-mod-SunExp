using System;
using System.Collections.Generic;
using System.IO;
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

    public static bool QueueGenerateCandidate(
        string decisionProfile,
        Action<string>? completed = null)
    {
        var profile = NormalizeProfile(decisionProfile);
        lock (StatusGate)
        {
            if (StatusByProfile.TryGetValue(profile, out var current) && current.Busy)
            {
                return false;
            }
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
                Work = _ => GenerateCandidate(profile, options),
                ApplyOnMainThread = result =>
                {
                    if (result.Success)
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

    public static bool TryImportCandidate(
        string decisionProfile,
        out string message)
    {
        var profile = NormalizeProfile(decisionProfile);
        var path = CandidatePath(profile);
        SetStatus(profile, AutoBattleTrainingStage.Importing, "正在校验并导入候选模型");
        if (!File.Exists(path))
        {
            message = "未找到训练候选模型：" + path;
            SetStatus(profile, AutoBattleTrainingStage.Failed, message);
            return false;
        }

        try
        {
            var model = AuraSharedJson.Deserialize<DecisionResidualModelDefinition>(
                File.ReadAllText(path));
            if (!TryValidate(model, profile, out message))
            {
                SetStatus(profile, AutoBattleTrainingStage.Failed, message);
                return false;
            }
            model ??= new DecisionResidualModelDefinition();

            var result = AuraSharedConfigStore.WriteOwner(
                AuraToolsIds.ModId,
                SystemId,
                ModelFile(profile),
                model,
                schemaVersion: 2);
            message = result.Success
                ? "模型已导入，风格=" + profile
                  + "，含 " + model.Weights.Count
                  + " 个上下文权重，revision=" + result.Revision
                : "模型导入失败：" + result.Message;
            SetStatus(
                profile,
                result.Success ? AutoBattleTrainingStage.Imported : AutoBattleTrainingStage.Failed,
                message,
                preferencePairCount: MetricCount(model.Metrics, "pairCount"),
                weightCount: model.Weights.Count,
                candidatePath: path);
            return result.Success;
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

        var path = CandidatePath(profile);
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

    private static TrainingWorkResult GenerateCandidate(
        string profile,
        CombatResidualTrainingOptions options)
    {
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
            "正在训练本地候选模型",
            samples.Count,
            invalidLines);
        var result = CombatResidualTrainer.Train(samples, profile, options);
        if (!result.Success || result.Model == null)
        {
            return new TrainingWorkResult(
                false,
                result.Message + "；读取样本=" + samples.Count + "，无效行=" + invalidLines,
                samples.Count,
                invalidLines,
                result.PreferencePairCount);
        }

        var candidatePath = CandidatePath(profile);
        SetStatus(
            profile,
            AutoBattleTrainingStage.WritingCandidate,
            "正在写入候选模型",
            samples.Count,
            invalidLines,
            result.PreferencePairCount,
            result.Model.Weights.Count,
            candidatePath);
        using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
        {
            storage.WriteTextAtomic(
                candidatePath,
                AuraSharedJson.Serialize(result.Model),
                createBackup: true);
        }
        return new TrainingWorkResult(
            true,
            result.Message
            + "；候选已写入=" + candidatePath
            + "；无效行=" + invalidLines,
            samples.Count,
            invalidLines,
            result.PreferencePairCount,
            result.Model.Weights.Count,
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

    private static string CandidatePath(string profile)
    {
        return AuraSharedLogStore.OwnerLogPath(
            AuraToolsIds.ModId,
            "auto-battle-model-candidate-" + profile + ".json");
    }

    private static string ModelFile(string profile)
    {
        return "residual-model-" + profile + ".json";
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

        public bool Success { get; }

        public string Message { get; }

        public int SampleCount { get; }

        public int InvalidLineCount { get; }

        public int PreferencePairCount { get; }

        public int WeightCount { get; }

        public string CandidatePath { get; }
    }
}
