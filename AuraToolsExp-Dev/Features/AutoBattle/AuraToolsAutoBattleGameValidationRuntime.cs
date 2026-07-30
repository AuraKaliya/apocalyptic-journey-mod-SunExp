using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal enum AutoBattleGameValidationStage
{
    Idle,
    Queued,
    Running,
    Restoring,
    Cancelling,
    Cancelled,
    Passed,
    Failed
}

internal sealed class AutoBattleGameValidationStatus
{
    public AutoBattleGameValidationStage Stage { get; set; }

    public string Message { get; set; } = "尚未进行游戏主体验证";

    public string ModelId { get; set; } = "";

    public int CompletedBattles { get; set; }

    public int RequestedBattles { get; set; }

    public string ResultDirectory { get; set; } = "";

    public bool Busy => Stage == AutoBattleGameValidationStage.Queued
                        || Stage == AutoBattleGameValidationStage.Running
                        || Stage == AutoBattleGameValidationStage.Restoring
                        || Stage == AutoBattleGameValidationStage.Cancelling;

    public AutoBattleGameValidationStatus Clone()
    {
        return (AutoBattleGameValidationStatus)MemberwiseClone();
    }
}

internal static class AuraToolsAutoBattleGameValidationRuntime
{
    private const string HandlerId = "AutoBattle.GameValidation";
    private static readonly object Gate = new();
    private static readonly CombatGameValidationCase[] FinalBossCases =
    {
        new()
        {
            CaseId = "final-boss.evernight",
            LevelId = "level_0",
            EncounterId = "enemy_10027"
        },
        new()
        {
            CaseId = "final-boss.demon-king",
            LevelId = "level_10046",
            EncounterId = "enemy_10048"
        },
        new()
        {
            CaseId = "final-boss.hje",
            LevelId = "level_10048",
            EncounterId = "enemy_10055"
        },
        new()
        {
            CaseId = "final-boss.caroline",
            LevelId = "level_10051",
            EncounterId = "enemy_10058"
        }
    };

    private static bool initialized;
    private static IDisposable? lifecycleSubscription;
    private static AuraToolsAutoBattleGameValidationDriver? driver;
    private static AutoBattleGameValidationStatus status = new();
    private static CombatGameValidationRequest? request;
    private static CombatGameValidationReport? report;
    private static List<ValidationRun> runs = new();
    private static int runIndex;
    private static int currentDecisions;
    private static float currentStartedAt;
    private static bool battleStarted;
    private static bool cleanupPending;
    private static bool cancelRequested;
    private static string roleSnapshot = "";
    private static IDecisionResidualModel validationResidual =
        NullDecisionResidualModel.Instance;
    private static ICombatSearchGuidanceModel validationGuidance =
        NullCombatSearchGuidanceModel.Instance;
    private static ICombatPolicyValueModel validationPolicyValue =
        NullCombatPolicyValueModel.Instance;

    public static bool Active
    {
        get
        {
            lock (Gate)
            {
                return request != null && status.Busy;
            }
        }
    }

    public static string ResultsRootDirectory => Path.Combine(
        AuraSharedLogStore.OwnerDirectory(AuraToolsIds.ModId),
        "game-validation");

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        var host = new GameObject("AuraToolsAutoBattleGameValidation");
        Object.DontDestroyOnLoad(host);
        driver = host.AddComponent<AuraToolsAutoBattleGameValidationDriver>();
        lifecycleSubscription = AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            HandlerId,
            new AuraBattleLifecycleSubscription
            {
                FightStarted = OnFightStarted,
                FightEnding = OnFightEnding,
                FightEnded = OnFightEnded
            },
            AuraToolsLog.Info,
            AuraToolsLog.Warn);
    }

    public static AutoBattleGameValidationStatus GetStatus()
    {
        lock (Gate)
        {
            return status.Clone();
        }
    }

    public static bool IsStartEnvironmentReady(out string message)
    {
        if (!AuraToolsAutoBattleRuntime.ModuleEnabled)
        {
            message = "请先启用战斗策略实验室";
            return false;
        }
        if (FightManager.Instance == null
            || FightManager.Instance.fightType != FightType.None
            || RoleTable.Instance == null)
        {
            message = "仅可在非战斗状态下启动游戏主体验证";
            return false;
        }
        message = "";
        return true;
    }

    public static bool Queue(AutoBattleSettings settings, out string message)
    {
        settings ??= new AutoBattleSettings();
        settings.Normalize();
        lock (Gate)
        {
            if (status.Busy)
            {
                message = "游戏主体验证已经在运行";
                return false;
            }
        }
        if (!IsStartEnvironmentReady(out message))
        {
            return false;
        }
        var evaluationModelId = string.IsNullOrWhiteSpace(
            settings.EvaluationModelId)
            ? settings.SelectedModelId
            : settings.EvaluationModelId;
        if (!AuraToolsAutoBattleModelRuntime.TryResolveGameValidationArtifact(
                settings.Profile,
                evaluationModelId,
                preferCandidate: string.IsNullOrWhiteSpace(
                    evaluationModelId),
                out validationResidual,
                out validationGuidance,
                out validationPolicyValue,
                out var modelId,
                out var artifactHash,
                out _,
                out var modelDiagnostic))
        {
            message = "没有可验证的候选或模型库模型：" + modelDiagnostic;
            return false;
        }
        if (!AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
                out var campaign,
                out var ruleset,
                out message))
        {
            return false;
        }

        var validation = settings.GameValidation;
        var created = DateTime.UtcNow;
        var nextRequest = new CombatGameValidationRequest
        {
            RequestId = "game-validation-" + created.ToString("yyyyMMdd-HHmmss-fff"),
            Profile = settings.Profile,
            ModelId = modelId,
            ModelArtifactHash = artifactHash,
            GameBuild = typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown",
            CampaignId = campaign.CampaignId,
            CampaignVersion = campaign.CampaignVersion,
            RulesetHash = ruleset.RulesetHash,
            NativePackageHash = CurrentRuntimePackageHash(),
            CreatedUtc = created.ToString("O"),
            HidePresentation = validation.HidePresentation,
            MaximumActionsPerBattle = validation.MaximumActionsPerBattle,
            MinimumDecisionsPerBattle = validation.MinimumDecisionsPerBattle,
            BattleTimeoutSeconds = validation.BattleTimeoutSeconds,
            MaximumInvalidRuns = validation.MaximumInvalidRuns,
            Cases = FinalBossCases.Select(item => new CombatGameValidationCase
            {
                CaseId = item.CaseId,
                LevelId = item.LevelId,
                EncounterId = item.EncounterId,
                Repetitions = validation.RepetitionsPerFinalBoss,
                MinimumWins = validation.MinimumWinsPerFinalBoss,
                Required = true
            }).ToList()
        };
        if (!CombatGameValidationProtocol.ValidateRequest(nextRequest, out message))
        {
            return false;
        }

        try
        {
            roleSnapshot = AuraSharedJson.Serialize(RoleTable.Instance);
            request = nextRequest;
            report = NewReport(nextRequest);
            runs = BuildRuns(nextRequest);
            runIndex = 0;
            currentDecisions = 0;
            battleStarted = false;
            cleanupPending = false;
            cancelRequested = false;
            Directory.CreateDirectory(ResultDirectory(nextRequest.ModelId));
            WriteJson(RequestPath(nextRequest.ModelId), nextRequest);
            SetStatus(
                AutoBattleGameValidationStage.Queued,
                "已排队，准备在隐藏战斗界面中验证四名最终首领",
                modelId,
                0,
                runs.Count);
            message = "游戏主体验证已排队：" + modelId;
            return true;
        }
        catch (Exception ex)
        {
            ResetSession();
            message = "创建游戏主体验证失败：" + ex.Message;
            return false;
        }
    }

    public static void Cancel()
    {
        lock (Gate)
        {
            if (!status.Busy)
            {
                return;
            }
            cancelRequested = true;
            status.Stage = AutoBattleGameValidationStage.Cancelling;
            status.Message = "正在取消并还原游戏状态";
        }
    }

    public static void OpenResultDirectory()
    {
        var current = GetStatus();
        var path = Directory.Exists(current.ResultDirectory)
            ? current.ResultDirectory
            : ResultsRootDirectory;
        Directory.CreateDirectory(path);
        FileResourceUtil.OpenDirectory(path);
    }

    public static bool TryGetValidationModels(
        out IDecisionResidualModel residual,
        out ICombatSearchGuidanceModel guidance,
        out ICombatPolicyValueModel policyValue,
        out string modelId)
    {
        lock (Gate)
        {
            residual = validationResidual;
            guidance = validationGuidance;
            policyValue = validationPolicyValue;
            modelId = request?.ModelId ?? "none";
            return request != null && status.Busy;
        }
    }

    public static void RecordDecision(
        CombatStateObservation state,
        CombatDecision decision)
    {
        lock (Gate)
        {
            if (request == null || !status.Busy || !battleStarted)
            {
                return;
            }
            currentDecisions++;
            report!.TotalDecisions++;
            if (currentDecisions > request.MaximumActionsPerBattle)
            {
                MarkCurrentInvalid("超过单场最大动作数");
            }
        }
    }

    public static void RecordExecutionFailure(string reason)
    {
        lock (Gate)
        {
            if (request != null && status.Busy)
            {
                MarkCurrentInvalid("动作执行失败：" + reason);
            }
        }
    }

    public static bool CanPromote(string profile, string modelId, out string reason)
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (!settings.GameValidation.RequiredForPromotion)
        {
            reason = "配置允许跳过游戏主体验证";
            return true;
        }
        if (!AuraToolsAutoBattleModelRuntime.TryResolveGameValidationArtifact(
                profile,
                modelId,
                preferCandidate: string.Equals(
                    AuraToolsAutoBattleModelRuntime.CandidateModelId(profile),
                    modelId,
                    StringComparison.Ordinal),
                out _,
                out _,
                out _,
                out var resolvedModelId,
                out var currentArtifactHash,
                out _,
                out reason)
            || !string.Equals(resolvedModelId, modelId, StringComparison.Ordinal))
        {
            reason = "无法解析当前模型工件：" + reason;
            return false;
        }
        var requestPath = RequestPath(modelId);
        var reportPath = ReportPath(modelId);
        if (!File.Exists(requestPath) || !File.Exists(reportPath))
        {
            reason = "尚无当前模型的游戏主体验证回执";
            return false;
        }
        try
        {
            var savedRequest = JsonConvert.DeserializeObject<CombatGameValidationRequest>(
                File.ReadAllText(requestPath));
            var savedReport = JsonConvert.DeserializeObject<CombatGameValidationReport>(
                File.ReadAllText(reportPath));
            if (savedRequest == null)
            {
                reason = "游戏主体验证请求损坏";
                return false;
            }
            if (!AuraToolsAutoBattleSimulationRuntime.TryResolveFoundationPackage(
                    out var campaign,
                    out var ruleset,
                    out reason))
            {
                return false;
            }
            var currentCompatibilityKey =
                CombatGameValidationProtocol.BuildCompatibilityKey(
                    profile,
                    modelId,
                    currentArtifactHash,
                    typeof(FightManager).Assembly.GetName().Version?.ToString()
                    ?? "unknown",
                    campaign.CampaignId,
                    campaign.CampaignVersion,
                    ruleset.RulesetHash,
                    CurrentRuntimePackageHash());
            var savedCompatibilityKey =
                CombatGameValidationProtocol.BuildCompatibilityKey(
                    savedRequest.Profile,
                    savedRequest.ModelId,
                    savedRequest.ModelArtifactHash,
                    savedRequest.GameBuild,
                    savedRequest.CampaignId,
                    savedRequest.CampaignVersion,
                    savedRequest.RulesetHash,
                    savedRequest.NativePackageHash);
            if (!string.Equals(
                    currentCompatibilityKey,
                    savedCompatibilityKey,
                    StringComparison.Ordinal))
            {
                reason = "模型、游戏版本或权威语义包已变化，必须重新进行游戏主体验证";
                return false;
            }
            return CombatGameValidationProtocol.ValidateReport(
                savedRequest,
                savedReport,
                out reason);
        }
        catch (Exception ex)
        {
            reason = "读取游戏主体验证回执失败：" + ex.Message;
            return false;
        }
    }

    internal static void Tick()
    {
        lock (Gate)
        {
            if (request == null || report == null || !status.Busy)
            {
                return;
            }
            if (cancelRequested)
            {
                AbortCurrentBattle();
                RestoreRole();
                report.Completed = true;
                report.Passed = false;
                report.FailureReason = "用户取消";
                report.CompletedUtc = DateTime.UtcNow.ToString("O");
                report.ReceiptHash = CombatGameValidationProtocol.BuildReceiptHash(report);
                WriteJson(ReportPath(request.ModelId), report);
                SetStatus(
                    AutoBattleGameValidationStage.Cancelled,
                    "验证已取消，角色状态已还原",
                    request.ModelId,
                    runIndex,
                    runs.Count);
                ResetSession(keepStatus: true);
                return;
            }
            if (cleanupPending)
            {
                CleanupAfterBattle();
                cleanupPending = false;
                battleStarted = false;
                runIndex++;
                status.CompletedBattles = runIndex;
                if (runIndex >= runs.Count)
                {
                    CompleteSession();
                }
                return;
            }
            if (runIndex >= runs.Count)
            {
                CompleteSession();
                return;
            }
            if (battleStarted)
            {
                if (Time.unscaledTime - currentStartedAt > request.BattleTimeoutSeconds)
                {
                    MarkCurrentInvalid("单场战斗超时");
                    AbortCurrentBattle();
                    cleanupPending = true;
                }
                return;
            }
            if (FightManager.Instance == null
                || FightManager.Instance.fightType != FightType.None)
            {
                return;
            }
            StartCurrentBattle();
        }
    }

    private static void StartCurrentBattle()
    {
        RestoreRole();
        var run = runs[runIndex];
        currentDecisions = 0;
        currentStartedAt = Time.unscaledTime;
        battleStarted = true;
        SetStatus(
            AutoBattleGameValidationStage.Running,
            "正在验证 " + run.Case.CaseId + "（"
            + (run.Repetition + 1) + "/" + run.Case.Repetitions + "）",
            request!.ModelId,
            runIndex,
            runs.Count);
        FightManager.Instance.ReadyToInit(run.Case.LevelId);
        FightManager.Instance.IsFake = true;
    }

    private static void OnFightStarted(ModHookContext context)
    {
        lock (Gate)
        {
            if (request == null || !status.Busy || !battleStarted)
            {
                return;
            }
            AuraToolsAutoBattleRuntime.BeginGameValidationBattle();
            if (request.HidePresentation)
            {
                HideFightPresentation();
            }
        }
    }

    private static void OnFightEnding(ModHookContext context)
    {
        lock (Gate)
        {
            if (request == null || report == null || !status.Busy || !battleStarted)
            {
                return;
            }
            var name = context.Target?.GetType().Name ?? "";
            var outcome = name.IndexOf("Win", StringComparison.OrdinalIgnoreCase) >= 0
                ? "win"
                : name.IndexOf("Loss", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "loss"
                    : name.IndexOf("Escape", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "escape"
                        : "invalid";
            CompleteCurrentRun(outcome, name);
            AuraToolsAutoBattleRuntime.EndGameValidationBattle();
        }
    }

    private static void OnFightEnded(ModHookContext context)
    {
        lock (Gate)
        {
            if (request != null && status.Busy && battleStarted)
            {
                cleanupPending = true;
            }
        }
    }

    private static void CompleteCurrentRun(string outcome, string diagnostic)
    {
        var run = runs[runIndex];
        var result = report!.Cases.First(item =>
            string.Equals(item.CaseId, run.Case.CaseId, StringComparison.Ordinal));
        result.Attempts++;
        result.Decisions += currentDecisions;
        result.LastDiagnostic = diagnostic;
        switch (outcome)
        {
            case "win":
                result.Wins++;
                break;
            case "loss":
                result.Losses++;
                break;
            case "escape":
                result.Escapes++;
                break;
            default:
                result.InvalidRuns++;
                break;
        }
    }

    private static void MarkCurrentInvalid(string diagnostic)
    {
        if (request == null || report == null || runIndex >= runs.Count)
        {
            return;
        }
        var run = runs[runIndex];
        var result = report.Cases.First(item =>
            string.Equals(item.CaseId, run.Case.CaseId, StringComparison.Ordinal));
        if (result.Attempts <= run.Repetition)
        {
            result.Attempts++;
            result.InvalidRuns++;
        }
        result.Decisions += currentDecisions;
        result.LastDiagnostic = diagnostic;
        cleanupPending = true;
    }

    private static void CompleteSession()
    {
        RestoreRole();
        var currentRequest = request!;
        var currentReport = report!;
        currentReport.Completed = true;
        currentReport.CompletedUtc = DateTime.UtcNow.ToString("O");
        currentReport.Passed = currentReport.Cases.All(item =>
        {
            var expected = currentRequest.Cases.First(value =>
                string.Equals(value.CaseId, item.CaseId, StringComparison.Ordinal));
            return item.Attempts >= expected.Repetitions
                   && item.InvalidRuns <= currentRequest.MaximumInvalidRuns
                   && item.Decisions
                   >= currentRequest.MinimumDecisionsPerBattle
                   * expected.Repetitions
                   && (!expected.Required || item.Wins >= expected.MinimumWins);
        });
        currentReport.FailureReason = currentReport.Passed
            ? ""
            : "至少一个最终首领用例未达到动作覆盖、可选胜场或无效运行门槛";
        currentReport.ReceiptHash =
            CombatGameValidationProtocol.BuildReceiptHash(currentReport);
        WriteJson(ReportPath(currentRequest.ModelId), currentReport);
        SetStatus(
            currentReport.Passed
                ? AutoBattleGameValidationStage.Passed
                : AutoBattleGameValidationStage.Failed,
            currentReport.Passed
                ? "游戏主体验证通过，可进入晋升流程"
                : "游戏主体验证未通过，请查看回执",
            currentRequest.ModelId,
            runs.Count,
            runs.Count);
        ResetSession(keepStatus: true);
    }

    private static CombatGameValidationReport NewReport(
        CombatGameValidationRequest validationRequest)
    {
        return new CombatGameValidationReport
        {
            RequestId = validationRequest.RequestId,
            ModelId = validationRequest.ModelId,
            CompatibilityKey = CombatGameValidationProtocol.BuildCompatibilityKey(
                validationRequest.Profile,
                validationRequest.ModelId,
                validationRequest.ModelArtifactHash,
                validationRequest.GameBuild,
                validationRequest.CampaignId,
                validationRequest.CampaignVersion,
                validationRequest.RulesetHash,
                validationRequest.NativePackageHash),
            PresentationVisible = !validationRequest.HidePresentation,
            StartedUtc = DateTime.UtcNow.ToString("O"),
            Cases = validationRequest.Cases.Select(item =>
                new CombatGameValidationCaseResult
                {
                    CaseId = item.CaseId,
                    LevelId = item.LevelId
                }).ToList()
        };
    }

    private static List<ValidationRun> BuildRuns(CombatGameValidationRequest value)
    {
        var result = new List<ValidationRun>();
        foreach (var item in value.Cases)
        {
            for (var repetition = 0; repetition < item.Repetitions; repetition++)
            {
                result.Add(new ValidationRun(item, repetition));
            }
        }
        return result;
    }

    private static void HideFightPresentation()
    {
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null)
        {
            return;
        }
        var group = fightUi.gameObject.GetComponent<CanvasGroup>()
                    ?? fightUi.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
    }

    private static void CleanupAfterBattle()
    {
        AuraToolsAutoBattleRuntime.EndGameValidationBattle();
        WitchUiManager.Instance?.CloseUI("FightUI");
        WitchUiManager.Instance?.CloseUI("BattleRewardsUI");
        RestoreRole();
        if (FightManager.Instance != null)
        {
            FightManager.Instance.fightType = FightType.None;
        }
    }

    private static void AbortCurrentBattle()
    {
        AuraToolsAutoBattleRuntime.EndGameValidationBattle();
        WitchUiManager.Instance?.CloseUI("FightUI");
        WitchUiManager.Instance?.CloseUI("BattleRewardsUI");
        if (FightManager.Instance != null)
        {
            FightManager.Instance.fightType = FightType.None;
        }
    }

    private static void RestoreRole()
    {
        if (RoleTable.Instance == null || string.IsNullOrWhiteSpace(roleSnapshot))
        {
            return;
        }
        var restored = JsonConvert.DeserializeObject<RoleTable>(roleSnapshot);
        if (restored != null)
        {
            RoleTable.Instance.ResetFight(restored);
        }
    }

    private static void SetStatus(
        AutoBattleGameValidationStage stage,
        string message,
        string modelId,
        int completed,
        int requested)
    {
        status = new AutoBattleGameValidationStatus
        {
            Stage = stage,
            Message = message,
            ModelId = modelId,
            CompletedBattles = completed,
            RequestedBattles = requested,
            ResultDirectory = string.IsNullOrWhiteSpace(modelId)
                ? ResultsRootDirectory
                : ResultDirectory(modelId)
        };
    }

    private static void ResetSession(bool keepStatus = false)
    {
        request = null;
        report = null;
        runs = new List<ValidationRun>();
        runIndex = 0;
        currentDecisions = 0;
        battleStarted = false;
        cleanupPending = false;
        cancelRequested = false;
        roleSnapshot = "";
        validationResidual = NullDecisionResidualModel.Instance;
        validationGuidance = NullCombatSearchGuidanceModel.Instance;
        validationPolicyValue = NullCombatPolicyValueModel.Instance;
        if (!keepStatus)
        {
            status = new AutoBattleGameValidationStatus();
        }
    }

    private static string ResultDirectory(string modelId)
    {
        return Path.Combine(ResultsRootDirectory, SafeModelKey(modelId));
    }

    private static string RequestPath(string modelId)
    {
        return Path.Combine(ResultDirectory(modelId), "request.json");
    }

    private static string ReportPath(string modelId)
    {
        return Path.Combine(ResultDirectory(modelId), "report.json");
    }

    private static string SafeModelKey(string modelId)
    {
        using var sha = SHA256.Create();
        return string.Concat(
            sha.ComputeHash(Encoding.UTF8.GetBytes(modelId ?? ""))
                .Take(12)
                .Select(value => value.ToString("x2")));
    }

    private static string CurrentRuntimePackageHash()
    {
        try
        {
            var path = typeof(AuraToolsAutoBattleGameValidationRuntime).Assembly.Location;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                return BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", "")
                    .ToLowerInvariant();
            }
        }
        catch
        {
        }
        return NativeRewardScriptGlobals.PrecompiledProgramProtocol
               + ":"
               + NativeRewardScriptGlobals.PrecompiledProgramCount;
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ResultsRootDirectory);
        using var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory);
        storage.WriteRawJsonAtomic(path, value!, createBackup: true);
    }

    private sealed class ValidationRun
    {
        public ValidationRun(CombatGameValidationCase @case, int repetition)
        {
            Case = @case;
            Repetition = repetition;
        }

        public CombatGameValidationCase Case { get; }

        public int Repetition { get; }
    }
}

internal sealed class AuraToolsAutoBattleGameValidationDriver : MonoBehaviour
{
    private void Update()
    {
        AuraToolsAutoBattleGameValidationRuntime.Tick();
    }
}
