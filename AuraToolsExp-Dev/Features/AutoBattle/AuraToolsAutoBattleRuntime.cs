using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using AuraCombatAi.Shared;
using AuraCombatAi.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Michsky.MUIP;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.AutoBattle;

public static class AuraToolsAutoBattleRuntime
{
    private const string HandlerId = "AutoBattle";
    private static bool initialized;
    private static AuraToolsAutoBattleController? controller;
    private static IDisposable? lifecycleSubscription;
    private static IDisposable? trainingSinkRegistration;

    internal static bool ModuleEnabled =>
        AuraToolsConfigService.Root.MatchExperience.Enabled
        && AuraToolsConfigService.MatchExperience.AutoBattle.Enabled;

    public static bool Active => controller != null && controller.Active;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraToolsCombatKnowledgeRuntime.Initialize();
        AuraToolsAutoBattleJourneyRuntime.Initialize(modConfig);
        EnsureController();
        AuraToolsHookRegistry.After(
            modConfig,
            "DeckUI.CreateDeckMenuForSelect",
            WitchCombatInteractionRuntime.ObserveDeckPrompt,
            HandlerId);
        AuraToolsHookRegistry.Before(
            modConfig,
            "FightUI.SelectCardToAction",
            WitchCombatInteractionRuntime.ObserveHandPrompt,
            HandlerId);
        AuraToolsHookRegistry.Before(
            modConfig,
            "FightUI.ThrowCardScript",
            WitchCombatInteractionRuntime.ObserveDiscardPrompt,
            HandlerId);
        AuraToolsHookRegistry.Before(
            modConfig,
            "FightUI.Burning",
            WitchCombatInteractionRuntime.ObserveBurnPrompt,
            HandlerId);
        AuraToolsHookRegistry.Before(
            modConfig,
            "CommonCardItem.TrueUse",
            ObserveHumanAction,
            HandlerId + ".Teacher");
        AuraToolsHookRegistry.Before(
            modConfig,
            "AttackCardItem.TrueUse",
            ObserveHumanAction,
            HandlerId + ".Teacher");
        AuraToolsHookRegistry.Before(
            modConfig,
            "SkillItem.TrueUse",
            ObserveHumanAction,
            HandlerId + ".Teacher");
        AuraToolsHookRegistry.Before(
            modConfig,
            "FightUI.onChangeTurnBtn",
            ObserveHumanEndTurn,
            HandlerId + ".Teacher");
        lifecycleSubscription = AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            HandlerId,
            new AuraBattleLifecycleSubscription
            {
                FightStarting = _ => ResetForBattle(),
                FightStarted = _ => ResetForBattle(),
                FightEnding = _ => EndBattle(),
                FightEnded = _ => EndBattle()
            },
            AuraToolsLog.Info,
            AuraToolsLog.Warn);
        AuraToolsAutoBattleGameValidationRuntime.Initialize(modConfig);
        trainingSinkRegistration = CombatAiRegistry.RegisterTrainingSink(
            AuraToolsIds.ModId,
            "JsonLinesV4",
            new AuraToolsAutoBattleTrainingSink());
        AuraToolsConfigService.Changed += OnConfigurationChanged;
    }

    public static void SetActive(bool active)
    {
        EnsureController().SetActive(active);
    }

    private static void OnConfigurationChanged()
    {
        EnsureController().ApplyConfiguration();
    }

    public static void ReloadModels()
    {
        controller?.ApplyConfiguration();
    }

    public static bool TrySetModelApplicationMode(
        string requestedMode,
        out AutoBattleModelApplicationStatus status,
        out string message)
    {
        var mode = NormalizeModelApplicationMode(requestedMode);
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var selectedModelId = (settings.SelectedModelId ?? "").Trim();
        if (!string.Equals(mode, "off", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(selectedModelId))
        {
            status = SnapshotModelApplicationStatus();
            message = "请先从模型库选择一个模型";
            return false;
        }
        if (string.Equals(mode, "active", StringComparison.Ordinal)
            && !AuraToolsAutoBattleSimulationRuntime.CanActivateModel(
                settings.Profile,
                selectedModelId,
                out var gateReason))
        {
            status = SnapshotModelApplicationStatus();
            message = "所选模型尚不能受限应用：" + gateReason;
            return false;
        }

        settings.TrainedModelMode = mode;
        settings.Normalize();
        AuraToolsConfigService.SaveMatchExperience();
        EnsureController().ApplyConfiguration();
        status = SnapshotModelApplicationStatus();
        var applied = string.Equals(
            status.EffectiveMode,
            mode,
            StringComparison.Ordinal);
        message = applied
            ? "模型应用状态已切换为" + ModelApplicationModeLabel(mode)
            : "配置已保存，但运行时实际状态为"
              + ModelApplicationModeLabel(status.EffectiveMode)
              + "：" + status.Diagnostic;
        AuraToolsLog.Info(
            "[AutoBattle][ModelActivation] configured="
            + status.ConfiguredMode
            + " effective="
            + status.EffectiveMode
            + " selected="
            + status.SelectedModelId
            + " loaded="
            + status.LoadedModelId);
        return applied;
    }

    public static AutoBattleModelApplicationStatus
        SnapshotModelApplicationStatus()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        return controller?.SnapshotModelApplicationStatus(
                   settings.TrainedModelMode,
                   settings.SelectedModelId)
               ?? new AutoBattleModelApplicationStatus
               {
                   ConfiguredMode = NormalizeModelApplicationMode(
                       settings.TrainedModelMode),
                   EffectiveMode = "off",
                   SelectedModelId = settings.SelectedModelId ?? "",
                   LoadedModelId = "none",
                   Diagnostic = "自动战斗运行时尚未初始化"
               };
    }

    private static string NormalizeModelApplicationMode(string value)
    {
        return value switch
        {
            "shadow" => "shadow",
            "active" => "active",
            _ => "off"
        };
    }

    private static string ModelApplicationModeLabel(string value)
    {
        return value switch
        {
            "shadow" => "影子评估",
            "active" => "受限应用",
            _ => "关闭"
        };
    }

    internal static void BeginGameValidationBattle()
    {
        EnsureController().BeginGameValidationBattle();
    }

    internal static void EndGameValidationBattle()
    {
        controller?.EndGameValidationBattle();
    }

    private static void ResetForBattle()
    {
        WitchCombatInteractionRuntime.Reset();
        EnsureController().ResetForBattle(
            ModuleEnabled && AuraToolsConfigService.MatchExperience.AutoBattle.StartActive);
    }

    private static void EndBattle()
    {
        controller?.EndBattle();
        WitchCombatInteractionRuntime.Reset();
    }

    private static void ObserveHumanAction(ModHookContext context)
    {
        controller?.CaptureTeacherAction(context.Target);
    }

    private static void ObserveHumanEndTurn(ModHookContext context)
    {
        controller?.CaptureTeacherEndTurn();
    }

    private static AuraToolsAutoBattleController EnsureController()
    {
        if (controller != null)
        {
            return controller;
        }

        var host = new GameObject("AuraToolsAutoBattleRuntime");
        Object.DontDestroyOnLoad(host);
        controller = host.AddComponent<AuraToolsAutoBattleController>();
        return controller;
    }
}

public sealed class AutoBattleModelApplicationStatus
{
    public string ConfiguredMode { get; set; } = "off";

    public string EffectiveMode { get; set; } = "off";

    public string SelectedModelId { get; set; } = "";

    public string LoadedModelId { get; set; } = "none";

    public string Diagnostic { get; set; } = "";
}

internal sealed class AuraToolsAutoBattleController : MonoBehaviour
{
    private const string ButtonName = "AuraToolsAutoBattleButton";
    private readonly WitchCombatRuntime runtime = new();
    private CombatDecisionEngine baselineDecisionEngine = new();
    private CombatDecisionEngine trainedDecisionEngine = new();
    private readonly CombatActionTransaction transaction = new();
    private AuraToolsAutoBattlePredictionPresenter? predictionPresenter;
    private GameObject? buttonRoot;
    private ButtonManager? buttonManager;
    private FightUI? buttonOwner;
    private float nextDecisionAt;
    private float nextUiProbeAt;
    private long decisionIndex;
    private bool pendingSampleRecorded;
    private string lastInteractionDiagnostic = "";
    private CombatStateObservation? beforeAction;
    private CombatDecision? pendingDecision;
    private CombatStateObservation? teacherBeforeAction;
    private CombatDecision? teacherDecision;
    private string teacherRecommendedCandidateId = "";
    private float teacherStartedAt;
    private long teacherTransactionId;
    private string lastModelDiagnostic = "";
    private string trainedModelMode = "off";
    private string trainedModelId = "none";
    private string lastModelComparisonFingerprint = "";
    private float nextPredictionAt;
    private string shadowPredictionFingerprint = "";
    private string shadowPredictionCandidateId = "";
    private bool teacherPolicyVisibleToHuman;
    private string decisionCacheKey = "";
    private CombatDecision? cachedBaselineDecision;
    private CombatDecision? cachedLearnedDecision;
    private readonly List<double> decisionTimingsMs = new();
    private string pendingShadowFingerprint = "";
    private readonly HashSet<string> failedActionStateKeys =
        new(StringComparer.Ordinal);

    public bool Active { get; private set; }

    internal AutoBattleModelApplicationStatus SnapshotModelApplicationStatus(
        string configuredMode,
        string selectedModelId)
    {
        return new AutoBattleModelApplicationStatus
        {
            ConfiguredMode = configuredMode ?? "off",
            EffectiveMode = trainedModelMode,
            SelectedModelId = selectedModelId ?? "",
            LoadedModelId = trainedModelId,
            Diagnostic = lastModelDiagnostic
        };
    }

    private void Awake()
    {
        predictionPresenter = gameObject.GetComponent<AuraToolsAutoBattlePredictionPresenter>()
                              ?? gameObject.AddComponent<AuraToolsAutoBattlePredictionPresenter>();
        ReloadDecisionEngine();
    }

    public void SetActive(bool active)
    {
        var nextActive = active && AuraToolsAutoBattleRuntime.ModuleEnabled;
        if (Active && !nextActive && transaction.IsActive)
        {
            WitchCombatInteractionRuntime.TryResolve(false);
            transaction.HandOff("automation disabled by player or configuration");
            RecordPendingTrainingSample(
                CombatActionTransactionState.HandedOff.ToString(),
                transaction.TerminalReason,
                terminal: false);
        }

        Active = nextActive;
        if (Active)
        {
            ClearTeacherAction();
        }
        ClearPendingAction();
        ClearPredictionMarkers();
        transaction.Reset();
        nextDecisionAt = Time.unscaledTime + 0.15f;
        nextPredictionAt = 0f;
        UpdateButtonLabel();
    }

    public void ResetForBattle(bool startActive)
    {
        decisionIndex = 0;
        pendingSampleRecorded = false;
        ClearTeacherAction();
        ClearPredictionMarkers();
        nextPredictionAt = 0f;
        transaction.Reset();
        failedActionStateKeys.Clear();
        ReloadDecisionEngine();
        SetActive(startActive);
        DestroyButton();
        nextUiProbeAt = 0f;
    }

    public void EndBattle()
    {
        CombatStateObservation? after = null;
        if (beforeAction != null && pendingDecision?.Action != null)
        {
            TryCapturePlayerState(out after, out _);
            transaction.Complete("battle ended after action");
            RecordPendingTrainingSample(
                CombatActionTransactionState.Completed.ToString(),
                transaction.TerminalReason,
                terminal: true,
                after);
        }
        if (teacherBeforeAction != null && teacherDecision?.Action != null)
        {
            TryCapturePlayerState(out var teacherAfter, out _);
            RecordTeacherTrainingSample(
                CombatActionTransactionState.Completed.ToString(),
                "battle ended after teacher action",
                terminal: true,
                teacherAfter);
        }

        Active = false;
        ClearTeacherAction();
        ClearPendingAction();
        ClearPredictionMarkers();
        transaction.Reset();
        DestroyButton();
    }

    public void ApplyConfiguration()
    {
        ReloadDecisionEngine();
        ClearPredictionMarkers();
        nextPredictionAt = 0f;
        if (!AuraToolsAutoBattleRuntime.ModuleEnabled)
        {
            SetActive(false);
            DestroyButton();
        }

        UpdateButtonLabel();
    }

    internal void BeginGameValidationBattle()
    {
        ReloadDecisionEngine();
        SetActive(true);
        DestroyButton();
    }

    internal void EndGameValidationBattle()
    {
        SetActive(false);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextUiProbeAt)
        {
            nextUiProbeAt = Time.unscaledTime + 0.5f;
            RefreshButton();
        }

        ObserveTeacherSettlement();
        UpdateShadowPrediction();

        if (transaction.IsActive
            && Time.unscaledTime > transaction.Deadline)
        {
            WitchCombatInteractionRuntime.TryResolve(false);
            if (HandleNoEffectTimeout())
            {
                return;
            }
            transaction.CheckDeadline(Time.unscaledTime);
            RecordPendingTrainingSample(
                CombatActionTransactionState.TimedOut.ToString(),
                transaction.TerminalReason,
                terminal: false);
            DeactivateWithReason("action transaction timed out");
            return;
        }

        var interaction = WitchCombatInteractionRuntime.TryResolve(Active);
        if (interaction == WitchInteractionResolveResult.Pending)
        {
            ClearPredictionMarkers();
            EnsurePromptTransaction();
            var snapshot = CombatInteractionBroker.Snapshot();
            LogInteractionProgress(snapshot);
            if (snapshot?.State == CombatInteractionState.Resolving)
            {
                transaction.Selecting();
            }
            else
            {
                transaction.AwaitPrompt();
            }
            return;
        }
        if (interaction == WitchInteractionResolveResult.Failed)
        {
            var message = CombatInteractionBroker.Snapshot()?.Message;
            transaction.Fail(string.IsNullOrWhiteSpace(message) ? "interaction failed" : message!);
            RecordPendingTrainingSample(
                CombatActionTransactionState.Failed.ToString(),
                transaction.TerminalReason,
                terminal: false);
            DeactivateWithReason(transaction.TerminalReason);
            return;
        }
        if (interaction == WitchInteractionResolveResult.HandedToPlayer)
        {
            transaction.HandOff("interaction handed to player");
            RecordPendingTrainingSample(
                CombatActionTransactionState.HandedOff.ToString(),
                transaction.TerminalReason,
                terminal: false);
            DeactivateWithReason(transaction.TerminalReason);
            return;
        }
        if (interaction == WitchInteractionResolveResult.Completed)
        {
            if (pendingDecision?.Action != null)
            {
                transaction.AwaitSettlement();
            }
            else
            {
                transaction.Complete("standalone prompt completed");
                transaction.Reset();
            }
        }

        if (!Active || !AuraToolsAutoBattleRuntime.ModuleEnabled)
        {
            return;
        }

        if (transaction.State == CombatActionTransactionState.AwaitingSettlement)
        {
            ObserveSettlement();
            return;
        }

        if (Time.unscaledTime < nextDecisionAt)
        {
            return;
        }

        DecideAndExecute();
    }

    private void DecideAndExecute()
    {
        if (!TryCapturePlayerState(out var state, out _)
            || !state.IsPlayerActionWindow
            || state.UiBusy)
        {
            nextDecisionAt = Time.unscaledTime + 0.2f;
            return;
        }

        ApplyFailedActionSuppressions(state);
        var decision = ChooseDecision(state, "execute");
        if (!decision.HasAction || decision.Action == null)
        {
            AuraToolsAutoBattleGameValidationRuntime.RecordExecutionFailure(
                "模型没有返回可执行动作");
            StopWithReason("没有可执行的合法动作");
            return;
        }

        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (string.Equals(settings.UnknownActionPolicy, "handoff", StringComparison.OrdinalIgnoreCase)
            && decision.Action.Semantics.Uncertainty >= 1.5d)
        {
            StopWithReason("遇到未识别动作，已交还玩家");
            return;
        }

        if (!transaction.TryBegin(
                state.BattleSessionId,
                decision.Action.CandidateId,
                Time.unscaledTime,
                settings.ActionTimeoutSeconds))
        {
            return;
        }

        if (settings.ShowPredictionMarkers)
        {
            var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
            if (fightUi != null
                && runtime.TryResolvePresentation(
                    decision.Action,
                    out var actionComponent,
                    out var target)
                && actionComponent != null)
            {
                predictionPresenter?.Show(
                    fightUi,
                    state.Fingerprint,
                    decision.Action,
                    actionComponent,
                    target,
                    actionHoldSeconds: 0.45f);
            }
        }
        var execution = runtime.Execute(decision.Action);
        if (!execution.Accepted)
        {
            AuraToolsAutoBattleGameValidationRuntime.RecordExecutionFailure(
                execution.Message);
            transaction.Fail(execution.Message);
            StopWithReason(execution.Message);
            return;
        }

        AuraToolsAutoBattleGameValidationRuntime.RecordDecision(state, decision);
        beforeAction = state;
        pendingDecision = decision;
        pendingSampleRecorded = false;
        decisionIndex++;
        transaction.AwaitSettlement();
        nextDecisionAt = Time.unscaledTime
                         + AuraToolsConfigService.MatchExperience.AutoBattle.DecisionIntervalMs / 1000f;
        AuraToolsLog.Debug(
            "[AutoBattle] tx=" + transaction.TransactionId
            + " source=" + decision.Action.SourceId
            + " name=" + decision.Action.DisplayName
            + " candidate=" + decision.Action.CandidateId
            + " score=" + decision.Score.ToString("0.00")
            + " reason=" + decision.Reason
            + " search=" + decision.SearchAlgorithm
            + " simulations=" + decision.SearchSimulations
            + " nodes=" + decision.SearchNodes
            + " transpositions=" + decision.SearchTranspositionHits
            + " " + ScoreBreakdown(decision)
            + " " + decision.PlanSummary);
    }

    internal void CaptureTeacherAction(object? runtimeHandle)
    {
        if (!CanCaptureTeacher()
            || runtimeHandle == null
            || !TryCapturePlayerState(out var state, out _)
            || !state.IsPlayerActionWindow)
        {
            return;
        }

        var actualAction = runtime.FindActionForRuntimeHandle(state, runtimeHandle);
        if (actualAction == null || !actualAction.Legal)
        {
            return;
        }
        CaptureTeacherDecision(state, actualAction);
    }

    internal void CaptureTeacherEndTurn()
    {
        if (!CanCaptureTeacher()
            || !TryCapturePlayerState(out var state, out _)
            || !state.IsPlayerActionWindow)
        {
            return;
        }

        var actualAction = state.Actions.FirstOrDefault(action =>
            action.Kind == CombatActionKind.EndTurn);
        if (actualAction != null)
        {
            CaptureTeacherDecision(state, actualAction);
        }
    }

    private void CaptureTeacherDecision(
        CombatStateObservation state,
        CombatActionObservation actualAction)
    {
        if (!CanCaptureTeacher())
        {
            return;
        }
        var recommendation = ChooseDecision(state, "teacher");
        var actualEvaluation = recommendation.Candidates.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Action.CandidateId,
                actualAction.CandidateId,
                StringComparison.Ordinal));
        if (actualEvaluation == null)
        {
            return;
        }

        decisionIndex++;
        teacherTransactionId++;
        teacherStartedAt = Time.unscaledTime;
        teacherBeforeAction = state;
        teacherRecommendedCandidateId = recommendation.Action?.CandidateId ?? "";
        teacherPolicyVisibleToHuman = recommendation.Action != null
                                      && predictionPresenter?.IsShowing(
                                          state.Fingerprint,
                                          recommendation.Action.CandidateId) == true;
        ClearPredictionMarkers();
        teacherDecision = new CombatDecision
        {
            HasAction = true,
            Action = actualAction,
            Score = actualEvaluation.PlanScore != 0d
                ? actualEvaluation.PlanScore
                : actualEvaluation.RuleScore,
            Reason = "human teacher action",
            ProfileId = recommendation.ProfileId,
            Candidates = recommendation.Candidates,
            Plan = recommendation.Plan,
            PlanSummary = "teacher="
                          + actualAction.DisplayName
                          + "; recommended="
                          + (recommendation.Action?.DisplayName ?? "end-turn")
                          + "; "
                          + recommendation.PlanSummary
        };
        AuraToolsLog.Debug(
            "[AutoBattle][Teacher] chosen="
            + actualAction.DisplayName
            + " recommended="
            + (recommendation.Action?.DisplayName ?? "end-turn")
            + " "
                          + recommendation.PlanSummary);
    }

    private bool CanCaptureTeacher()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        return !Active
               && AuraToolsAutoBattleRuntime.ModuleEnabled
               && settings.CaptureTrainingSamples
               && TrainingModeAllows("shadow")
               && teacherBeforeAction == null;
    }

    private void ObserveTeacherSettlement()
    {
        if (teacherBeforeAction == null || teacherDecision?.Action == null)
        {
            return;
        }

        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (Time.unscaledTime - teacherStartedAt > settings.ActionTimeoutSeconds)
        {
            RecordTeacherTrainingSample(
                CombatActionTransactionState.TimedOut.ToString(),
                "teacher action settlement timed out",
                terminal: false);
            ClearTeacherAction();
            return;
        }
        if (Time.unscaledTime - teacherStartedAt < settings.DecisionIntervalMs / 1000f
            || WitchCombatInteractionRuntime.HasActivePrompt
            || !TryCapturePlayerState(out var after, out _)
            || after.UiBusy
            || !after.IsPlayerActionWindow
            || string.Equals(
                after.Fingerprint,
                teacherBeforeAction.Fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        RecordTeacherTrainingSample(
            CombatActionTransactionState.Completed.ToString(),
            "human teacher action settled",
            after.Enemies.Count == 0 || after.Player.CurrentHp <= 0,
            after);
        ClearTeacherAction();
    }

    private void RecordTeacherTrainingSample(
        string completionState,
        string terminalReason,
        bool terminal,
        CombatStateObservation? after = null)
    {
        if (teacherBeforeAction == null || teacherDecision?.Action == null)
        {
            return;
        }

        CombatAiRegistry.RecordTrainingSample(CombatTrainingSampleBuilder.Create(
            teacherBeforeAction,
            after,
            teacherDecision,
            decisionIndex,
            1000000000L + teacherTransactionId,
            completionState,
            terminalReason,
            terminal,
            typeof(FightUI).Assembly.GetName().Version?.ToString() ?? "",
            typeof(CombatDecisionEngine).Assembly.GetName().Version?.ToString() ?? "",
            demonstrator: "human",
            recommendedCandidateId: teacherRecommendedCandidateId,
            policyVisibleToHuman: teacherPolicyVisibleToHuman));
    }

    private void ObserveSettlement()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (Time.unscaledTime - (float)transaction.StartedAt < settings.DecisionIntervalMs / 1000f
            || WitchCombatInteractionRuntime.HasActivePrompt)
        {
            return;
        }

        if (!TryCapturePlayerState(out var after, out _)
            || after.UiBusy
            || !after.IsPlayerActionWindow)
        {
            return;
        }

        if (beforeAction != null && pendingDecision?.Action != null)
        {
            if (!CombatActionSettlementPolicy.HasMeaningfulProgress(
                    beforeAction,
                    after,
                    pendingDecision.Action,
                    out var settlementReason))
            {
                return;
            }
            runtime.ConfirmSettledAction(pendingDecision.Action, after);
            transaction.Complete("action settled: " + settlementReason);
            RecordPendingTrainingSample(
                CombatActionTransactionState.Completed.ToString(),
                transaction.TerminalReason,
                after.Enemies.Count == 0 || after.Player.CurrentHp <= 0,
                after);
        }

        ClearPendingAction();
        transaction.Reset();
        nextDecisionAt = Time.unscaledTime + settings.DecisionIntervalMs / 1000f;
    }

    private bool HandleNoEffectTimeout()
    {
        if (beforeAction == null || pendingDecision?.Action == null)
        {
            return false;
        }
        if (TryCapturePlayerState(out var after, out _)
            && CombatActionSettlementPolicy.HasMeaningfulProgress(
                beforeAction,
                after,
                pendingDecision.Action,
                out var progressReason))
        {
            runtime.ConfirmSettledAction(pendingDecision.Action, after);
            transaction.Complete(
                "action settled at timeout boundary: " + progressReason);
            RecordPendingTrainingSample(
                CombatActionTransactionState.Completed.ToString(),
                transaction.TerminalReason,
                after.Enemies.Count == 0 || after.Player.CurrentHp <= 0,
                after);
            ClearPendingAction();
            transaction.Reset();
            nextDecisionAt = Time.unscaledTime + 0.05f;
            return true;
        }

        var failedAction = pendingDecision.Action;
        failedActionStateKeys.Add(
            FailedActionStateKey(beforeAction, failedAction));
        RecordPendingTrainingSample(
            CombatActionTransactionState.Failed.ToString(),
            "action produced no semantic game-state effect and was suppressed",
            terminal: false,
            after);
        AuraToolsLog.Warn(
            "[AutoBattle] suppressed no-effect action source="
            + failedAction.SourceId
            + " candidate=" + failedAction.CandidateId);
        ClearPendingAction();
        transaction.Reset();
        nextDecisionAt = Time.unscaledTime + 0.05f;
        return true;
    }

    private void ApplyFailedActionSuppressions(
        CombatStateObservation state)
    {
        foreach (var action in state.Actions)
        {
            if (!failedActionStateKeys.Contains(
                    FailedActionStateKey(state, action)))
            {
                continue;
            }
            action.Legal = false;
            action.RejectionReason =
                "suppressed after producing no semantic game-state effect";
            action.Features["semanticUnavailable"] = 1d;
        }
    }

    private static string FailedActionStateKey(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        return state.BattleSessionId
               + "|" + state.Fingerprint
               + "|" + action.CandidateId;
    }

    private void EnsurePromptTransaction()
    {
        if (!Active || transaction.IsActive)
        {
            return;
        }

        var requestId = CombatInteractionBroker.Snapshot()?.RequestId ?? 0;
        transaction.TryBegin(
            AuraBattleLifecycleRouter.CurrentBattleSessionId,
            "prompt:" + requestId,
            Time.unscaledTime,
            AuraToolsConfigService.MatchExperience.AutoBattle.ActionTimeoutSeconds);
        transaction.AwaitPrompt();
    }

    private void RecordPendingTrainingSample(
        string completionState,
        string terminalReason,
        bool terminal,
        CombatStateObservation? after = null)
    {
        if (pendingSampleRecorded
            || !AuraToolsConfigService.MatchExperience.AutoBattle.CaptureTrainingSamples
            || !TrainingModeAllows("auto")
            || beforeAction == null
            || pendingDecision?.Action == null)
        {
            return;
        }

        pendingSampleRecorded = true;
        CombatAiRegistry.RecordTrainingSample(CombatTrainingSampleBuilder.Create(
            beforeAction,
            after,
            pendingDecision,
            decisionIndex,
            transaction.TransactionId,
            completionState,
            terminalReason,
            terminal,
            typeof(FightUI).Assembly.GetName().Version?.ToString() ?? "",
            typeof(CombatDecisionEngine).Assembly.GetName().Version?.ToString() ?? "",
            demonstrator: "policy",
            recommendedCandidateId: pendingDecision.Action.CandidateId));
    }

    private void ClearPendingAction()
    {
        beforeAction = null;
        pendingDecision = null;
        pendingSampleRecorded = false;
        lastInteractionDiagnostic = "";
        ClearPredictionMarkers();
    }

    private void ClearTeacherAction()
    {
        teacherBeforeAction = null;
        teacherDecision = null;
        teacherRecommendedCandidateId = "";
        teacherPolicyVisibleToHuman = false;
        teacherStartedAt = 0f;
    }

    private void UpdateShadowPrediction()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (Active
            || !AuraToolsAutoBattleRuntime.ModuleEnabled
            || !settings.ShowPredictionMarkers
            || teacherBeforeAction != null
            || WitchCombatInteractionRuntime.HasActivePrompt)
        {
            if (!Active)
            {
                ClearPredictionMarkers();
            }
            return;
        }
        if (Time.unscaledTime < nextPredictionAt)
        {
            return;
        }

        nextPredictionAt = Time.unscaledTime + settings.DecisionIntervalMs / 1000f;
        if (!TryCapturePlayerState(out var state, out _)
            || !state.IsPlayerActionWindow
            || state.UiBusy)
        {
            ClearPredictionMarkers();
            return;
        }

        if (string.Equals(
                shadowPredictionFingerprint,
                state.Fingerprint,
                StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(shadowPredictionCandidateId)
            && predictionPresenter?.IsShowing(
                state.Fingerprint,
                shadowPredictionCandidateId) == true)
        {
            return;
        }

        var decision = ChooseDecision(state, "prediction");
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (!decision.HasAction
            || decision.Action == null
            || fightUi == null
            || !runtime.TryResolvePresentation(
                decision.Action,
                out var actionComponent,
                out var target)
            || actionComponent == null
            || predictionPresenter?.Show(
                fightUi,
                state.Fingerprint,
                decision.Action,
                actionComponent,
                target) != true)
        {
            ClearPredictionMarkers();
            return;
        }

        shadowPredictionFingerprint = state.Fingerprint;
        shadowPredictionCandidateId = decision.Action.CandidateId;
    }

    private void ClearPredictionMarkers()
    {
        shadowPredictionFingerprint = "";
        shadowPredictionCandidateId = "";
        predictionPresenter?.Clear();
    }

    private static bool TrainingModeAllows(string mode)
    {
        var configured = AuraToolsConfigService.MatchExperience.AutoBattle.TrainingMode;
        return string.Equals(configured, "hybrid", StringComparison.OrdinalIgnoreCase)
               || string.Equals(configured, mode, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryCapturePlayerState(
        out CombatStateObservation state,
        out string reason)
    {
        if (runtime.TryCapturePlayerObservation(out var observation, out reason))
        {
            state = observation.State;
            return true;
        }
        state = new CombatStateObservation();
        return false;
    }

    private void ReloadDecisionEngine()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (AuraToolsAutoBattleGameValidationRuntime.TryGetValidationModels(
                out var validationResidual,
                out var validationGuidance,
                out var validationPolicyValue,
                out var validationModelId))
        {
            trainedModelMode = "active";
            baselineDecisionEngine = new CombatDecisionEngine();
            trainedDecisionEngine = new CombatDecisionEngine(
                validationResidual,
                validationGuidance,
                policyValueModel: validationPolicyValue);
            trainedModelId = validationModelId;
            lastModelComparisonFingerprint = "";
            pendingShadowFingerprint = "";
            ClearDecisionCache();
            lastModelDiagnostic = "游戏主体验证专用加载=" + validationModelId;
            AuraToolsLog.Info("[AutoBattle] " + lastModelDiagnostic);
            return;
        }
        trainedModelMode = settings.TrainedModelMode;
        var model = AuraToolsAutoBattleModelRuntime.Load(
            settings.Profile,
            !string.Equals(trainedModelMode, "off", StringComparison.OrdinalIgnoreCase),
            out var diagnostic,
            settings.SelectedModelId);
        var searchGuidance = AuraToolsAutoBattleModelRuntime.LoadSearchGuidance(
            settings.Profile,
            !string.Equals(trainedModelMode, "off", StringComparison.OrdinalIgnoreCase),
            out var guidanceDiagnostic,
            settings.SelectedModelId);
        var policyValue = AuraToolsAutoBattleModelRuntime.LoadPolicyValue(
            settings.Profile,
            !string.Equals(trainedModelMode, "off", StringComparison.OrdinalIgnoreCase),
            out var policyValueDiagnostic,
            settings.SelectedModelId);
        baselineDecisionEngine = new CombatDecisionEngine();
        trainedDecisionEngine = new CombatDecisionEngine(
            model,
            searchGuidance,
            policyValueModel: policyValue);
        trainedModelId = string.Join(
            "+",
            new[] { model.ModelId, searchGuidance.ModelId, policyValue.ModelId }
                .Where(id => !string.Equals(id, "none", StringComparison.Ordinal)));
        if (string.IsNullOrWhiteSpace(trainedModelId))
        {
            trainedModelId = "none";
        }
        if (string.Equals(trainedModelMode, "active", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trainedModelId, "none", StringComparison.Ordinal)
            && !AuraToolsAutoBattleSimulationRuntime.CanActivateModel(
                settings.Profile,
                trainedModelId,
                out var gateReason))
        {
            trainedModelMode = "shadow";
            diagnostic += "；主动应用门禁未通过，已降级为影子评估：" + gateReason;
        }
        lastModelComparisonFingerprint = "";
        pendingShadowFingerprint = "";
        ClearDecisionCache();
        diagnostic += "；" + guidanceDiagnostic + "；" + policyValueDiagnostic;
        if (!string.Equals(lastModelDiagnostic, diagnostic, StringComparison.Ordinal))
        {
            lastModelDiagnostic = diagnostic;
            AuraToolsLog.Info("[AutoBattle] " + diagnostic);
        }
    }

    private CombatDecision ChooseDecision(
        CombatStateObservation state,
        string source)
    {
        var profile = BuildProfile();
        var cacheKey = DecisionCacheKey(state, profile);
        if (string.Equals(trainedModelMode, "active", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trainedModelId, "none", StringComparison.Ordinal))
        {
            if (!AuraToolsCombatKnowledgeRuntime.HasPlayerEquivalentReadiness(
                    state,
                    out var coverageReason))
            {
                AuraToolsLog.Debug(
                    "[AutoBattle][Observation] 主动模型已对当前状态降级为底模：" + coverageReason);
                return RunDecisionEngine(
                    baselineDecisionEngine,
                    state,
                    profile,
                    "baseline-knowledge-fallback");
            }
            if (string.Equals(decisionCacheKey, cacheKey, StringComparison.Ordinal)
                && cachedLearnedDecision != null)
            {
                return BindDecisionToCurrentObservation(
                    cachedLearnedDecision,
                    state,
                    trainedDecisionEngine,
                    profile,
                    learned: true,
                    "learned-active-cache");
            }
            ClearDecisionCache();
            decisionCacheKey = cacheKey;
            cachedLearnedDecision = RunDecisionEngine(
                trainedDecisionEngine,
                state,
                profile,
                "learned-active");
            return BindDecisionToCurrentObservation(
                cachedLearnedDecision,
                state,
                trainedDecisionEngine,
                profile,
                learned: true,
                "learned-active");
        }

        if (!string.Equals(decisionCacheKey, cacheKey, StringComparison.Ordinal))
        {
            ClearDecisionCache();
            decisionCacheKey = cacheKey;
        }
        var baselineTemplate = cachedBaselineDecision
                               ??= RunDecisionEngine(
                                   baselineDecisionEngine,
                                   state,
                                   profile,
                                   "baseline");
        var baseline = BindDecisionToCurrentObservation(
            baselineTemplate,
            state,
            baselineDecisionEngine,
            profile,
            learned: false,
            "baseline-cache");
        if (!string.Equals(trainedModelMode, "shadow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trainedModelId, "none", StringComparison.Ordinal)
            || string.Equals(
                lastModelComparisonFingerprint,
                state.Fingerprint,
                StringComparison.Ordinal))
        {
            return baseline;
        }

        QueueShadowComparison(state, profile, baseline, cacheKey, source);
        return baseline;
    }

    private CombatDecision BindDecisionToCurrentObservation(
        CombatDecision template,
        CombatStateObservation state,
        CombatDecisionEngine engine,
        CombatDecisionProfile profile,
        bool learned,
        string source)
    {
        if (CombatDecisionExecutionBindingProtocol.TryBindToObservation(
                template,
                state,
                out var bound,
                out _))
        {
            return bound;
        }

        var refreshed = RunDecisionEngine(
            engine,
            state,
            profile,
            source + "-refresh");
        if (learned)
        {
            cachedLearnedDecision = refreshed;
        }
        else
        {
            cachedBaselineDecision = refreshed;
        }
        if (CombatDecisionExecutionBindingProtocol.TryBindToObservation(
                refreshed,
                state,
                out bound,
                out var reason))
        {
            AuraToolsLog.Debug(
                "[AutoBattle][ActionRebind] cached decision refreshed for "
                + state.ObservationId);
            return bound;
        }

        AuraToolsLog.Warn(
            "[AutoBattle][ActionRebind] current action binding failed: "
            + reason);
        return new CombatDecision
        {
            Reason = "current action binding failed: " + reason,
            ProfileId = profile.Id
        };
    }

    private void QueueShadowComparison(
        CombatStateObservation state,
        CombatDecisionProfile profile,
        CombatDecision baseline,
        string cacheKey,
        string source)
    {
        if (string.Equals(pendingShadowFingerprint, state.Fingerprint, StringComparison.Ordinal))
        {
            return;
        }
        pendingShadowFingerprint = state.Fingerprint;
        var fingerprint = state.Fingerprint;
        var modelId = trainedModelId;
        var engine = trainedDecisionEngine;
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<ShadowDecisionResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "AutoBattle.ShadowDecision",
                Source = "AutoBattle.LearnedShadow",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                Work = cancellation =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    var stopwatch = Stopwatch.StartNew();
                    var learned = engine.Choose(state, profile);
                    stopwatch.Stop();
                    return new ShadowDecisionResult(learned, stopwatch.Elapsed.TotalMilliseconds);
                },
                IsStillCurrent = () =>
                    string.Equals(trainedModelMode, "shadow", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(trainedModelId, modelId, StringComparison.Ordinal)
                    && string.Equals(decisionCacheKey, cacheKey, StringComparison.Ordinal),
                ApplyOnMainThread = result =>
                {
                    cachedLearnedDecision = result.Decision;
                    lastModelComparisonFingerprint = fingerprint;
                    RecordDecisionTiming(result.ElapsedMilliseconds, "learned-shadow-background");
                    AuraToolsLog.Info(
                        "[AutoBattle][ModelShadow] source=" + source
                        + " stateId=" + CompactFingerprint(fingerprint)
                        + " baseline=" + DecisionLabel(baseline)
                        + " learned=" + DecisionLabel(result.Decision)
                        + " changed=" + !string.Equals(
                            baseline.Action?.CandidateId,
                            result.Decision.Action?.CandidateId,
                            StringComparison.Ordinal)
                        + " learnedScore=" + ScoreBreakdown(result.Decision));
                },
                OnFailedOnMainThread = ex =>
                {
                    if (!(ex is OperationCanceledException))
                    {
                        AuraToolsLog.Warn("[AutoBattle][ModelShadow] 后台评估失败：" + ex.Message);
                    }
                }
            });
        if (!queued)
        {
            pendingShadowFingerprint = "";
            AuraToolsLog.Warn("[AutoBattle][ModelShadow] 后台评估任务未能提交");
        }
    }

    private static string CompactFingerprint(string value)
    {
        unchecked
        {
            var hash = 1469598103934665603UL;
            foreach (var character in value ?? "")
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }

    private CombatDecision RunDecisionEngine(
        CombatDecisionEngine engine,
        CombatStateObservation state,
        CombatDecisionProfile profile,
        string phase)
    {
        var stopwatch = Stopwatch.StartNew();
        var decision = engine.Choose(state, profile);
        stopwatch.Stop();
        RecordDecisionTiming(stopwatch.Elapsed.TotalMilliseconds, phase);
        return decision;
    }

    private void RecordDecisionTiming(double elapsedMilliseconds, string phase)
    {
        decisionTimingsMs.Add(elapsedMilliseconds);
        if (decisionTimingsMs.Count >= 32)
        {
            var sorted = decisionTimingsMs.OrderBy(value => value).ToArray();
            AuraToolsLog.Info(
                "[AutoBattle][Performance] samples=" + sorted.Length
                + " p50Ms=" + Percentile(sorted, 0.5d).ToString("0.00")
                + " p95Ms=" + Percentile(sorted, 0.95d).ToString("0.00")
                + " p99Ms=" + Percentile(sorted, 0.99d).ToString("0.00")
                + " lastPhase=" + phase
                + " lastMs=" + elapsedMilliseconds.ToString("0.00"));
            decisionTimingsMs.Clear();
        }
    }

    private static double Percentile(IReadOnlyList<double> sorted, double probability)
    {
        if (sorted.Count == 0)
        {
            return 0d;
        }
        var index = Math.Max(
            0,
            Math.Min(sorted.Count - 1, (int)Math.Ceiling(probability * sorted.Count) - 1));
        return sorted[index];
    }

    private string DecisionCacheKey(
        CombatStateObservation state,
        CombatDecisionProfile profile)
    {
        return state.Fingerprint
               + "|" + profile.Id
               + "|" + profile.SearchBudgetMode
               + "|" + profile.SearchQuality
               + "|" + trainedModelMode
               + "|" + trainedModelId;
    }

    private sealed class ShadowDecisionResult
    {
        public ShadowDecisionResult(CombatDecision decision, double elapsedMilliseconds)
        {
            Decision = decision;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public CombatDecision Decision { get; }

        public double ElapsedMilliseconds { get; }
    }

    private void ClearDecisionCache()
    {
        decisionCacheKey = "";
        cachedBaselineDecision = null;
        cachedLearnedDecision = null;
    }

    private static string DecisionLabel(CombatDecision decision)
    {
        return decision.Action == null
            ? "none"
            : decision.Action.DisplayName + "(" + decision.Action.CandidateId + ")";
    }

    private static string ScoreBreakdown(CombatDecision decision)
    {
        if (decision.Action == null)
        {
            return "scoreBreakdown=none";
        }

        var selected = decision.Candidates.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Action.CandidateId,
                decision.Action.CandidateId,
                StringComparison.Ordinal));
        return selected == null
            ? "scoreBreakdown=missing"
            : "base=" + selected.BaseRuleScore.ToString("0.00")
              + " residualRaw=" + selected.RawResidualScore.ToString("0.00")
              + " residualSupport=" + selected.ResidualApplicability.ToString("0.00")
              + " residualApplied=" + selected.AppliedResidualScore.ToString("0.00")
              + " policyPrior=" + selected.SearchPrior.ToString("0.000")
              + " visits=" + selected.SearchVisits
              + " plan=" + selected.PlanScore.ToString("0.00")
              + " ruleFinal=" + selected.RuleScore.ToString("0.00");
    }

    private void LogInteractionProgress(CombatInteractionRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return;
        }

        var diagnostic = request.RequestId + "|" + request.State + "|" + request.Message;
        if (string.Equals(lastInteractionDiagnostic, diagnostic, StringComparison.Ordinal))
        {
            return;
        }

        lastInteractionDiagnostic = diagnostic;
        AuraToolsLog.Debug(
            "[AutoBattle] tx=" + transaction.TransactionId
            + " request=" + request.RequestId
            + " state=" + request.State
            + " " + request.Message
            + " deadlineRemaining="
            + Math.Max(0d, transaction.Deadline - Time.unscaledTime).ToString("0.00"));
    }

    private CombatDecisionProfile BuildProfile()
    {
        return AuraToolsAutoBattleSimulationRuntime.BuildDecisionProfile(
            AuraToolsConfigService.MatchExperience.AutoBattle);
    }

    private void RefreshButton()
    {
        if (!AuraToolsAutoBattleRuntime.ModuleEnabled)
        {
            DestroyButton();
            return;
        }

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null || !fightUi.gameObject.activeInHierarchy || fightUi.turnButton == null)
        {
            DestroyButton();
            return;
        }

        if (buttonRoot != null && buttonOwner == fightUi)
        {
            buttonRoot.SetActive(true);
            return;
        }

        DestroyButton();
        var native = fightUi.turnButton;
        var result = AuraUiNativeButtonCloneAdapter.TryClone(new AuraUiNativeButtonCloneRequest
        {
            Template = native,
            Parent = native.transform.parent,
            CloneName = ButtonName,
            Label = ButtonLabel(),
            TextSizeOverride = 18f,
            MinimumTextSizeOverride = 12f,
            OnClick = ToggleActive
        });
        if (!result.Success || result.Root == null)
        {
            AuraToolsLog.Warn("[AutoBattle] failed to create battle button: " + result.FailureReason);
            return;
        }

        buttonRoot = result.Root;
        buttonManager = result.Manager as ButtonManager;
        buttonOwner = fightUi;
        PositionButton(native.transform, buttonRoot.transform);
        buttonRoot.SetActive(true);
    }

    private void ToggleActive()
    {
        SetActive(!Active);
    }

    private void UpdateButtonLabel()
    {
        if (buttonManager == null)
        {
            return;
        }

        buttonManager.SetText(ButtonLabel());
        buttonManager.UpdateUI();
        buttonRoot?.GetComponent<AuraUiNativeButtonLabelOwner>()?.Configure(
            buttonManager,
            ButtonLabel(),
            18f,
            12f);
    }

    private string ButtonLabel()
    {
        return Active ? "自动战斗：开" : "自动战斗：关";
    }

    private static void PositionButton(Transform native, Transform clone)
    {
        clone.SetSiblingIndex(native.GetSiblingIndex() + 1);
        if (native is not RectTransform nativeRect || clone is not RectTransform cloneRect)
        {
            return;
        }

        var width = Mathf.Max(120f, Mathf.Abs(nativeRect.rect.width), Mathf.Abs(nativeRect.sizeDelta.x));
        cloneRect.anchorMin = nativeRect.anchorMin;
        cloneRect.anchorMax = nativeRect.anchorMax;
        cloneRect.pivot = nativeRect.pivot;
        cloneRect.sizeDelta = nativeRect.sizeDelta;
        cloneRect.anchoredPosition = nativeRect.anchoredPosition + Vector2.left * (width + 12f);
    }

    private void StopWithReason(string reason)
    {
        if (transaction.IsActive)
        {
            transaction.Fail(reason);
            RecordPendingTrainingSample(
                CombatActionTransactionState.Failed.ToString(),
                reason,
                terminal: false);
            WitchCombatInteractionRuntime.TryResolve(false);
        }

        DeactivateWithReason(reason);
    }

    private void DeactivateWithReason(string reason)
    {
        var resolvedReason = string.IsNullOrWhiteSpace(reason)
            ? "自动战斗已停止（未提供原因）"
            : reason.Trim();
        AuraToolsLog.Warn(
            "[AutoBattle] stopped: " + resolvedReason
            + ", tx=" + transaction.TransactionId
            + ", state=" + transaction.State);
        Active = false;
        ClearPendingAction();
        transaction.Reset();
        nextDecisionAt = Time.unscaledTime + 0.15f;
        UpdateButtonLabel();
    }

    private void DestroyButton()
    {
        if (buttonRoot != null)
        {
            buttonRoot.SetActive(false);
            Object.Destroy(buttonRoot);
        }

        buttonRoot = null;
        buttonManager = null;
        buttonOwner = null;
    }
}

internal sealed class AuraToolsAutoBattleTrainingSink : ICombatTrainingSampleSink
{
    private static readonly object StorageGate = new();
    private static int storageGeneration;
    private readonly BlockingCollection<QueuedTrainingSample> queue = new(2048);
    private readonly Thread writerThread;

    public AuraToolsAutoBattleTrainingSink()
    {
        writerThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "AuraTools.AutoBattleTrainingWriter"
        };
        writerThread.Start();
        Application.quitting += Shutdown;
    }

    public void Record(CombatTrainingSample sample)
    {
        if (!AuraToolsConfigService.MatchExperience.AutoBattle.CaptureTrainingSamples)
        {
            return;
        }

        if (!queue.TryAdd(new QueuedTrainingSample
            {
                Generation = Volatile.Read(ref storageGeneration),
                Sample = sample
            }))
        {
            AuraToolsLog.Warn("[AutoBattle] training sample queue is full; sample dropped");
        }
        var selection = sample.Selection ?? new CombatTrainingSelectionTrace();
        AuraToolsLog.Debug(
            "[AutoBattle][Training] actor="
            + selection.ExecutedBy
            + " executed="
            + TrainingActionLabel(
                selection.ExecutedDisplayName,
                selection.ExecutedCandidateId)
            + " policyPreselected="
            + TrainingActionLabel(
                selection.PolicyPreselectedDisplayName,
                selection.PolicyPreselectedCandidateId)
            + " agreement="
            + selection.HumanPolicyAgreement
            + " visibleToHuman="
            + selection.PolicyVisibleToHuman
            + " label="
            + selection.LabelKind);
    }

    private void WriteLoop()
    {
        try
        {
            var path = AuraSharedLogStore.OwnerLogPath(
                AuraToolsIds.ModId,
                "auto-battle-training-v6.jsonl");
            var episodesPath = AuraSharedLogStore.OwnerLogPath(
                AuraToolsIds.ModId,
                "live-combat-episodes-v4.jsonl");
            var sessions = new Dictionary<long, List<CombatTrainingSample>>();
            var sessionGeneration = Volatile.Read(ref storageGeneration);
            foreach (var queued in queue.GetConsumingEnumerable())
            {
                var batch = new List<QueuedTrainingSample>(16) { queued };
                while (batch.Count < 16 && queue.TryTake(out var pending))
                {
                    batch.Add(pending);
                }
                lock (StorageGate)
                {
                    var currentGeneration = Volatile.Read(ref storageGeneration);
                    if (sessionGeneration != currentGeneration)
                    {
                        sessions.Clear();
                        sessionGeneration = currentGeneration;
                    }
                    var currentBatch = batch
                        .Where(item => item.Generation == currentGeneration)
                        .ToList();
                    if (currentBatch.Count == 0)
                    {
                        continue;
                    }
                    using var writer = new StreamWriter(path, append: true);
                    using var episodeWriter = new StreamWriter(episodesPath, append: true);
                    foreach (var item in currentBatch)
                    {
                        writer.WriteLine(AuraSharedJson.SerializeCompact(item.Sample));
                        RecordLiveEpisode(item.Sample, sessions, episodeWriter);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[AutoBattle] training sample writer stopped: " + ex.Message);
        }
    }

    public static void ClearPersistedData()
    {
        lock (StorageGate)
        {
            Interlocked.Increment(ref storageGeneration);
            foreach (var fileName in new[]
                     {
                         "auto-battle-training-v6.jsonl",
                         "live-combat-episodes-v4.jsonl"
                     })
            {
                var path = AuraSharedLogStore.OwnerLogPath(
                    AuraToolsIds.ModId,
                    fileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    public static bool OwnsPersistedFile(string fileName)
    {
        return string.Equals(
                   fileName,
                   "auto-battle-training-v6.jsonl",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   fileName,
                   "live-combat-episodes-v4.jsonl",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void RecordLiveEpisode(
        CombatTrainingSample sample,
        IDictionary<long, List<CombatTrainingSample>> sessions,
        TextWriter episodeWriter)
    {
        if (sample == null || sample.BattleSessionId <= 0)
        {
            return;
        }
        if (!sessions.TryGetValue(sample.BattleSessionId, out var samples))
        {
            samples = new List<CombatTrainingSample>();
            sessions[sample.BattleSessionId] = samples;
        }
        samples.Add(sample);
        if (!sample.Terminal
            || (!string.Equals(sample.BattleOutcome, "victory", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sample.BattleOutcome, "defeat", StringComparison.OrdinalIgnoreCase))
            || !CombatLiveEpisodeAssembler.TryAssemble(
                sample.BattleSessionId,
                samples,
                out var episode))
        {
            PruneSessions(sessions);
            return;
        }

        episodeWriter.WriteLine(AuraSharedJson.SerializeCompact(episode));
        sessions.Remove(sample.BattleSessionId);
        AuraToolsLog.Info(
            "[AutoBattle][Training] 已聚合完整实战轨迹：battleSession="
            + sample.BattleSessionId
            + "，outcome="
            + episode.Outcome
            + "，frames="
            + episode.Frames.Count);
    }

    private static void PruneSessions(
        IDictionary<long, List<CombatTrainingSample>> sessions)
    {
        const int maximumBufferedSessions = 64;
        if (sessions.Count <= maximumBufferedSessions)
        {
            return;
        }
        var oldest = sessions
            .OrderBy(pair => pair.Value.Count == 0
                ? DateTime.MinValue
                : pair.Value.Min(sample => sample.CreatedUtc))
            .First();
        sessions.Remove(oldest.Key);
    }

    private void Shutdown()
    {
        Application.quitting -= Shutdown;
        queue.CompleteAdding();
        writerThread.Join(1500);
    }

    private static string TrainingActionLabel(string displayName, string candidateId)
    {
        if (string.IsNullOrWhiteSpace(displayName)
            || string.Equals(displayName, candidateId, StringComparison.Ordinal))
        {
            return candidateId ?? "";
        }

        return displayName + "(" + candidateId + ")";
    }

    private sealed class QueuedTrainingSample
    {
        public int Generation { get; set; }

        public CombatTrainingSample Sample { get; set; } = new();
    }
}
