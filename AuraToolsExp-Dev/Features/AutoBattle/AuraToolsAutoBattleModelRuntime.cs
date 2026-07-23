using System;
using System.Collections.Generic;
using System.IO;
using AuraCombatAi.Shared;
using AuraDecision.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal static class AuraToolsAutoBattleModelRuntime
{
    private const string SystemId = "AuraCombatAI";
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
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<TrainingWorkResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "AutoBattle.Train:" + profile,
                Source = "AutoBattle.LocalResidualTraining",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = _ => GenerateCandidate(profile),
                ApplyOnMainThread = result =>
                {
                    if (result.Success)
                    {
                        AuraToolsLog.Info("[AutoBattle][Training] " + result.Message);
                    }
                    else
                    {
                        AuraToolsLog.Warn("[AutoBattle][Training] " + result.Message);
                    }
                    completed?.Invoke(result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    var message = "本地训练任务失败：" + ex.Message;
                    AuraToolsLog.Warn("[AutoBattle][Training] " + message);
                    completed?.Invoke(message);
                }
            });
        if (queued)
        {
            AuraToolsLog.Info("[AutoBattle][Training] 已提交本地训练任务，决策风格=" + profile);
        }
        return queued;
    }

    public static bool TryImportCandidate(
        string decisionProfile,
        out string message)
    {
        var profile = NormalizeProfile(decisionProfile);
        var path = CandidatePath(profile);
        if (!File.Exists(path))
        {
            message = "未找到训练候选模型：" + path;
            return false;
        }

        try
        {
            var model = AuraSharedJson.Deserialize<DecisionResidualModelDefinition>(
                File.ReadAllText(path));
            if (!TryValidate(model, profile, out message))
            {
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
            return result.Success;
        }
        catch (Exception ex)
        {
            message = "模型导入失败：" + ex.Message;
            return false;
        }
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

    private static TrainingWorkResult GenerateCandidate(string profile)
    {
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

        var result = CombatResidualTrainer.Train(samples, profile);
        if (!result.Success || result.Model == null)
        {
            return new TrainingWorkResult(
                false,
                result.Message + "；读取样本=" + samples.Count + "，无效行=" + invalidLines);
        }

        var candidatePath = CandidatePath(profile);
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
            + "；无效行=" + invalidLines);
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
        public TrainingWorkResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public bool Success { get; }

        public string Message { get; }
    }
}
