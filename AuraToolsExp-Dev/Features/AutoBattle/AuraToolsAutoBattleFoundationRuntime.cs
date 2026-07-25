using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal enum AutoBattleFoundationStage
{
    Idle,
    Queued,
    Training,
    Writing,
    Completed,
    Cancelling,
    Cancelled,
    Failed
}

internal sealed class AutoBattleFoundationStatus
{
    public AutoBattleFoundationStage Stage { get; set; }

    public string Message { get; set; } = "尚未训练底模";

    public int CompletedCampaigns { get; set; }

    public int RequestedCampaigns { get; set; }

    public double NormalWinRate { get; set; }

    public double AdvancedWinRate { get; set; }

    public bool AcceptancePassed { get; set; }

    public string ModelId { get; set; } = "";

    public string ResultDirectory { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool Busy => Stage is AutoBattleFoundationStage.Queued
        or AutoBattleFoundationStage.Training
        or AutoBattleFoundationStage.Writing
        or AutoBattleFoundationStage.Cancelling;

    public AutoBattleFoundationStatus Clone()
    {
        return (AutoBattleFoundationStatus)MemberwiseClone();
    }
}

internal static class AuraToolsAutoBattleFoundationRuntime
{
    private const string WorkKey = "AutoBattle.FoundationTraining";
    private static readonly object Gate = new();
    private static AutoBattleFoundationStatus status = new();
    private static CancellationTokenSource? cancellation;

    public static bool Queue(
        AutoBattleSettings settings,
        out string message)
    {
        if (settings == null)
        {
            message = "自动战斗设置为空";
            return false;
        }
        settings.Normalize();
        if (AuraToolsAutoBattleModelRuntime.AnyTrainingBusy()
            || AuraToolsAutoBattleSimulationRuntime.GetStatus().Busy)
        {
            message = "候选训练、模拟评估或导入任务仍在运行";
            return false;
        }
        if (!AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
                out _,
                out _,
                out var readinessMessage))
        {
            SetStatus(AutoBattleFoundationStage.Failed, readinessMessage);
            message = readinessMessage;
            return false;
        }
        var snapshot = AuraSharedJson.Deserialize<AutoBattleSettings>(
                           AuraSharedJson.Serialize(settings))
                       ?? new AutoBattleSettings();
        snapshot.Normalize();
        var requested = TotalCampaigns(snapshot.FoundationTraining);
        lock (Gate)
        {
            if (status.Busy)
            {
                message = "底模训练已经在运行";
                return false;
            }
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            status = new AutoBattleFoundationStatus
            {
                Stage = AutoBattleFoundationStage.Queued,
                Message = "底模训练已排队",
                RequestedCampaigns = requested
            };
        }

        var ownedCancellation = cancellation;
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<FoundationWorkResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = WorkKey,
                Source = "AutoBattle.FoundationWorldSimulationTraining",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = schedulerToken =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        schedulerToken,
                        ownedCancellation.Token);
                    return Run(snapshot, linked.Token);
                },
                ApplyOnMainThread = result =>
                {
                    if (result.AcceptancePassed
                        && !string.IsNullOrWhiteSpace(result.ModelId))
                    {
                        var current = AuraToolsConfigService.MatchExperience.AutoBattle;
                        current.SelectedModelId = result.ModelId;
                        current.TrainedModelMode = "off";
                        current.UseTrainedModel = false;
                        current.CaptureTrainingSamples = false;
                        current.Normalize();
                        AuraToolsConfigService.SaveMatchExperience();
                        AuraToolsAutoBattleRuntime.ReloadModels();
                    }
                    SetStatus(
                        result.Cancelled
                            ? AutoBattleFoundationStage.Cancelled
                            : result.Success
                                ? AutoBattleFoundationStage.Completed
                                : AutoBattleFoundationStage.Failed,
                        result.Message,
                        result.CompletedCampaigns,
                        result.RequestedCampaigns,
                        result.NormalWinRate,
                        result.AdvancedWinRate,
                        result.AcceptancePassed,
                        result.ModelId,
                        result.ResultDirectory);
                    (result.Success ? (Action<string>)AuraToolsLog.Info : AuraToolsLog.Warn)(
                        "[AutoBattle][Foundation] " + result.Message);
                },
                OnFailedOnMainThread = ex =>
                {
                    SetStatus(
                        AutoBattleFoundationStage.Failed,
                        "底模训练失败：" + ex.Message);
                    AuraToolsLog.Warn("[AutoBattle][Foundation] " + ex);
                }
            });
        if (!queued)
        {
            SetStatus(AutoBattleFoundationStage.Failed, "底模训练任务未能提交");
            message = "底模训练任务未能提交";
            return false;
        }
        message = "底模训练已提交";
        return true;
    }

    public static void Cancel()
    {
        lock (Gate)
        {
            cancellation?.Cancel();
            if (status.Busy)
            {
                status.Stage = AutoBattleFoundationStage.Cancelling;
                status.Message = "正在取消底模训练";
                status.UpdatedUtc = DateTime.UtcNow;
            }
        }
    }

    public static AutoBattleFoundationStatus GetStatus()
    {
        lock (Gate)
        {
            return status.Clone();
        }
    }

    public static bool CheckReadiness(out string message)
    {
        var ready = AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
            out _,
            out _,
            out message);
        if (ready)
        {
            message = "知识包已达到正式底模训练要求";
        }
        return ready;
    }

    public static void ResetAfterDataClear()
    {
        lock (Gate)
        {
            cancellation?.Dispose();
            cancellation = null;
            status = new AutoBattleFoundationStatus();
        }
    }

    public static void OpenResultDirectory()
    {
        var current = GetStatus();
        var path = Directory.Exists(current.ResultDirectory)
            ? current.ResultDirectory
            : AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory;
        Directory.CreateDirectory(path);
        FileResourceUtil.OpenDirectory(path);
    }

    private static FoundationWorkResult Run(
        AutoBattleSettings settings,
        CancellationToken cancellationToken)
    {
        if (!AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
                out var sourceCampaign,
                out var ruleset,
                out var resolveMessage))
        {
            return FoundationWorkResult.Failed(
                resolveMessage,
                TotalCampaigns(settings.FoundationTraining));
        }
        var trainingCampaign = CloneCampaign(sourceCampaign);
        trainingCampaign.TraceLevel = CombatSimulationTraceLevel.Summary;
        trainingCampaign.RequireAuthoritativeRules = true;
        trainingCampaign.RetainBlockBetweenTurns = true;
        var validationCampaign = CloneCampaign(sourceCampaign);
        validationCampaign.TraceLevel = CombatSimulationTraceLevel.Full;
        validationCampaign.RequireAuthoritativeRules = true;
        validationCampaign.RetainBlockBetweenTurns = true;
        var foundation = settings.FoundationTraining;
        foundation.Normalize();
        var decisionProfile =
            AuraToolsAutoBattleSimulationRuntime.BuildDecisionProfile(settings);
        decisionProfile.SearchSimulationBudget = Math.Min(
            512,
            decisionProfile.SearchSimulationBudget);
        decisionProfile.SearchNodeBudget = Math.Min(
            4096,
            decisionProfile.SearchNodeBudget);
        decisionProfile.SearchMaxPly = Math.Min(12, decisionProfile.SearchMaxPly);
        var request = new CombatCampaignFoundationTrainingRequest
        {
            DecisionProfile = decisionProfile.Id,
            Iterations = foundation.Iterations,
            TrainingCampaignsPerIteration = foundation.TrainingCampaignsPerIteration,
            ArenaCampaignsPerDifficulty = foundation.ArenaCampaignsPerDifficulty,
            NormalValidationCampaigns = foundation.NormalValidationCampaigns,
            AdvancedValidationCampaigns = foundation.AdvancedValidationCampaigns,
            TrainingSeedStart = foundation.TrainingSeedStart,
            ArenaSeedStart = foundation.ArenaSeedStart,
            ValidationSeedStart = foundation.ValidationSeedStart,
            Profile = decisionProfile,
            TrainingCampaign = trainingCampaign,
            ValidationCampaign = validationCampaign,
            Training = new CombatPolicyValueTrainingOptions
            {
                Epochs = settings.Training.Epochs,
                LearningRate = Math.Min(0.02d, settings.Training.LearningRate),
                L2 = settings.Training.L2,
                HiddenDimensions = settings.Training.PolicyValueHiddenDimensions,
                MinimumEpisodes = Math.Min(
                    settings.Training.MinimumEpisodes,
                    foundation.TrainingCampaignsPerIteration)
            }.Normalized()
        };
        var requested = TotalCampaigns(foundation);
        var completed = 0;
        request.Progress = (current, total, progressMessage) =>
        {
            completed = current;
            SetStatus(
                AutoBattleFoundationStage.Training,
                progressMessage,
                current,
                total);
        };
        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                    + "-foundation";
        var resultDirectory = Path.Combine(
            AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
            runId);
        Directory.CreateDirectory(resultDirectory);

        CombatCampaignFoundationTrainingResult trained;
        try
        {
            trained = new CombatCampaignFoundationTrainer().Run(
                request,
                ruleset,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new FoundationWorkResult
            {
                Cancelled = true,
                Message = "底模训练已取消",
                CompletedCampaigns = completed,
                RequestedCampaigns = requested,
                ResultDirectory = resultDirectory
            };
        }

        SetStatus(
            AutoBattleFoundationStage.Writing,
            "正在写入底模训练报告",
            completed,
            requested);
        foreach (var validationRun in trained.ValidationRuns)
        {
            AuraToolsAutoBattleSimulationRuntime.PruneCampaignTrace(validationRun);
        }
        WriteReports(
            resultDirectory,
            sourceCampaign,
            ruleset,
            trained,
            foundation,
            decisionProfile);
        var modelId = "";
        if (trained.Success
            && trained.AcceptancePassed
            && trained.Champion != null)
        {
            modelId = AuraToolsAutoBattleModelRuntime.SaveFoundationModel(
                decisionProfile.Id,
                trained.Champion,
                trained.Validation,
                resultDirectory);
        }
        return new FoundationWorkResult
        {
            Success = trained.Success,
            Message = trained.Message
                      + (trained.AcceptancePassed
                          ? "；已保存为 career_1 底模，默认保持关闭"
                          : "；未保存为底模，可打开报告检查失败层级与终局流程"),
            CompletedCampaigns = completed,
            RequestedCampaigns = requested,
            NormalWinRate = trained.Validation.NormalWinRate,
            AdvancedWinRate = trained.Validation.AdvancedWinRate,
            AcceptancePassed = trained.AcceptancePassed,
            ModelId = modelId,
            ResultDirectory = resultDirectory
        };
    }

    private static void WriteReports(
        string resultDirectory,
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatCampaignFoundationTrainingResult result,
        AutoBattleFoundationTrainingSettings settings,
        CombatDecisionProfile profile)
    {
        WriteText(
            Path.Combine(resultDirectory, "foundation-training-report.json"),
            AuraSharedJson.Serialize(new
            {
                schemaVersion = 1,
                reportKind = "career_1-world-simulation-foundation-training",
                createdUtc = DateTime.UtcNow,
                roleId = campaign.Player.RoleId,
                cardPoolScope = AuraToolsAutoBattleModelRuntime.CurrentCardPoolScope,
                decisionProfile = profile.Id,
                trainingSeeds = new
                {
                    settings.TrainingSeedStart,
                    settings.ArenaSeedStart,
                    settings.ValidationSeedStart
                },
                settings.Iterations,
                settings.TrainingCampaignsPerIteration,
                settings.ArenaCampaignsPerDifficulty,
                settings.NormalValidationCampaigns,
                settings.AdvancedValidationCampaigns,
                result.Success,
                result.AcceptancePassed,
                result.Message,
                result.Validation,
                trainingIterations = result.Iterations,
                validationRuns = result.ValidationRuns
            }));
        using (var writer = new StreamWriter(
                   Path.Combine(resultDirectory, "foundation-training-episodes-v1.jsonl"),
                   append: false,
                   Encoding.UTF8))
        {
            foreach (var episode in result.Replay)
            {
                writer.WriteLine(AuraSharedJson.SerializeCompact(episode));
            }
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("# career_1 世界推演底模训练报告");
        markdown.AppendLine();
        markdown.AppendLine("- 卡池：本体 Normal 全离线卡包，排除联机包、诅咒奖励、衍生牌和 MOD 牌");
        markdown.AppendLine("- 训练 / 竞技场 / 最终验证种子：完全隔离");
        markdown.AppendLine("- 验收线：普通 100%，高级至少 80%");
        markdown.AppendLine("- 普通结果："
                            + result.Validation.NormalVictories
                            + "/"
                            + result.Validation.NormalCampaigns
                            + "（"
                            + result.Validation.NormalWinRate.ToString("P1")
                            + "）");
        markdown.AppendLine("- 高级结果："
                            + result.Validation.AdvancedVictories
                            + "/"
                            + result.Validation.AdvancedCampaigns
                            + "（"
                            + result.Validation.AdvancedWinRate.ToString("P1")
                            + "）");
        markdown.AppendLine("- 正式隔离验收：" + (result.AcceptancePassed ? "通过" : "未通过"));
        markdown.AppendLine();
        markdown.AppendLine("## 训练迭代");
        markdown.AppendLine();
        foreach (var iteration in result.Iterations)
        {
            markdown.AppendLine("- 第 "
                                + iteration.Iteration
                                + " 轮：轨迹 "
                                + iteration.ReplayEpisodes
                                + "；普通 "
                                + iteration.CandidateNormalWinRate.ToString("P1")
                                + "；高级 "
                                + iteration.CandidateAdvancedWinRate.ToString("P1")
                                + "；"
                                + iteration.PromotionKind);
        }
        markdown.AppendLine();
        markdown.AppendLine("## 最终隔离验证详情");
        markdown.AppendLine();
        foreach (var run in result.ValidationRuns
                     .OrderBy(item => item.DifficultyId, StringComparer.Ordinal)
                     .ThenBy(item => item.WorldSeed))
        {
            markdown.AppendLine("## "
                                + run.DifficultyId
                                + " · 种子 "
                                + run.WorldSeed);
            markdown.AppendLine();
            AuraToolsAutoBattleSimulationRuntime.AppendCampaignSide(
                markdown,
                "底模",
                run,
                ruleset);
        }
        WriteText(
            Path.Combine(resultDirectory, "foundation-training-report.md"),
            markdown.ToString());
    }

    private static CombatCampaignDefinition CloneCampaign(
        CombatCampaignDefinition source)
    {
        return AuraSharedJson.Deserialize<CombatCampaignDefinition>(
                   AuraSharedJson.Serialize(source))
               ?? throw new InvalidOperationException("无法克隆底模训练推演包");
    }

    private static int TotalCampaigns(AutoBattleFoundationTrainingSettings settings)
    {
        settings ??= new AutoBattleFoundationTrainingSettings();
        settings.Normalize();
        return settings.Iterations
               * (settings.TrainingCampaignsPerIteration
                  + settings.ArenaCampaignsPerDifficulty * 4)
               + settings.NormalValidationCampaigns
               + settings.AdvancedValidationCampaigns;
    }

    private static void SetStatus(
        AutoBattleFoundationStage stage,
        string message,
        int completed = 0,
        int requested = 0,
        double normalWinRate = 0d,
        double advancedWinRate = 0d,
        bool acceptancePassed = false,
        string modelId = "",
        string resultDirectory = "")
    {
        lock (Gate)
        {
            status = new AutoBattleFoundationStatus
            {
                Stage = stage,
                Message = message ?? "",
                CompletedCampaigns = completed,
                RequestedCampaigns = requested,
                NormalWinRate = normalWinRate,
                AdvancedWinRate = advancedWinRate,
                AcceptancePassed = acceptancePassed,
                ModelId = modelId ?? "",
                ResultDirectory = resultDirectory ?? "",
                UpdatedUtc = DateTime.UtcNow
            };
        }
    }

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(path, text, createBackup: false);
    }

    private sealed class FoundationWorkResult
    {
        public bool Success { get; set; }

        public bool Cancelled { get; set; }

        public string Message { get; set; } = "";

        public int CompletedCampaigns { get; set; }

        public int RequestedCampaigns { get; set; }

        public double NormalWinRate { get; set; }

        public double AdvancedWinRate { get; set; }

        public bool AcceptancePassed { get; set; }

        public string ModelId { get; set; } = "";

        public string ResultDirectory { get; set; } = "";

        public static FoundationWorkResult Failed(string message, int requested)
        {
            return new FoundationWorkResult
            {
                Message = message,
                RequestedCampaigns = requested
            };
        }
    }
}
