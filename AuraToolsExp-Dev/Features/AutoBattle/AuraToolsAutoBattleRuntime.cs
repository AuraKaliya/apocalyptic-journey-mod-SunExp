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

    public bool Active { get; private set; }

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
            runtime.TryCapture(out after, out _);
            transaction.Complete("battle ended after action");
            RecordPendingTrainingSample(
                CombatActionTransactionState.Completed.ToString(),
                transaction.TerminalReason,
                terminal: true,
                after);
        }
        if (teacherBeforeAction != null && teacherDecision?.Action != null)
        {
            runtime.TryCapture(out var teacherAfter, out _);
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

    private void Update()
    {
        if (Time.unscaledTime >= nextUiProbeAt)
        {
            nextUiProbeAt = Time.unscaledTime + 0.5f;
            RefreshButton();
        }

        ObserveTeacherSettlement();
        UpdateShadowPrediction();

        if (transaction.CheckDeadline(Time.unscaledTime))
        {
            WitchCombatInteractionRuntime.TryResolve(false);
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
        if (!runtime.TryCapture(out var state, out _)
            || !state.IsPlayerActionWindow
            || state.UiBusy)
        {
            nextDecisionAt = Time.unscaledTime + 0.2f;
            return;
        }

        var decision = ChooseDecision(state, "execute");
        if (!decision.HasAction || decision.Action == null)
        {
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
            if (fightUi != null)
            {
                predictionPresenter?.Show(
                    fightUi,
                    state.Fingerprint,
                    decision.Action,
                    actionHoldSeconds: 0.45f);
            }
        }
        var execution = runtime.Execute(decision.Action);
        if (!execution.Accepted)
        {
            transaction.Fail(execution.Message);
            StopWithReason(execution.Message);
            return;
        }

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
            || !runtime.TryCapture(out var state, out _)
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
            || !runtime.TryCapture(out var state, out _)
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
            || !runtime.TryCapture(out var after, out _)
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

        if (!runtime.TryCapture(out var after, out _)
            || after.UiBusy
            || !after.IsPlayerActionWindow)
        {
            return;
        }

        if (beforeAction != null && pendingDecision?.Action != null)
        {
            if (string.Equals(
                    after.Fingerprint,
                    beforeAction.Fingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }
            transaction.Complete("action settled");
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
        if (!runtime.TryCapture(out var state, out _)
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
            || predictionPresenter?.Show(
                fightUi,
                state.Fingerprint,
                decision.Action) != true)
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

    private void ReloadDecisionEngine()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        trainedModelMode = settings.TrainedModelMode;
        var model = AuraToolsAutoBattleModelRuntime.Load(
            settings.Profile,
            !string.Equals(trainedModelMode, "off", StringComparison.OrdinalIgnoreCase),
            out var diagnostic);
        var searchGuidance = AuraToolsAutoBattleModelRuntime.LoadSearchGuidance(
            settings.Profile,
            !string.Equals(trainedModelMode, "off", StringComparison.OrdinalIgnoreCase),
            out var guidanceDiagnostic);
        baselineDecisionEngine = new CombatDecisionEngine();
        trainedDecisionEngine = new CombatDecisionEngine(model, searchGuidance);
        trainedModelId = !string.Equals(searchGuidance.ModelId, "none", StringComparison.Ordinal)
            ? model.ModelId + "+" + searchGuidance.ModelId
            : model.ModelId;
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
        diagnostic += "；" + guidanceDiagnostic;
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
            if (string.Equals(decisionCacheKey, cacheKey, StringComparison.Ordinal)
                && cachedLearnedDecision != null)
            {
                return cachedLearnedDecision;
            }
            ClearDecisionCache();
            decisionCacheKey = cacheKey;
            cachedLearnedDecision = RunDecisionEngine(
                trainedDecisionEngine,
                state,
                profile,
                "learned-active");
            return cachedLearnedDecision;
        }

        if (!string.Equals(decisionCacheKey, cacheKey, StringComparison.Ordinal))
        {
            ClearDecisionCache();
            decisionCacheKey = cacheKey;
        }
        var baseline = cachedBaselineDecision
                       ??= RunDecisionEngine(
                           baselineDecisionEngine,
                           state,
                           profile,
                           "baseline");
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
               + "|" + profile.SearchSimulationBudget
               + "|" + profile.SearchNodeBudget
               + "|" + profile.SearchMaxPly
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
              + " final=" + selected.RuleScore.ToString("0.00");
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
        AuraToolsLog.Warn(
            "[AutoBattle] stopped: " + reason
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
    private readonly BlockingCollection<CombatTrainingSample> queue = new(2048);
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

        if (!queue.TryAdd(sample))
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
                "auto-battle-training-v4.jsonl");
            using var writer = new StreamWriter(path, append: true);
            var pending = 0;
            foreach (var sample in queue.GetConsumingEnumerable())
            {
                writer.WriteLine(AuraSharedJson.SerializeCompact(sample));
                pending++;
                if (pending < 16 && queue.Count > 0)
                {
                    continue;
                }
                writer.Flush();
                pending = 0;
            }
            writer.Flush();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[AutoBattle] training sample writer stopped: " + ex.Message);
        }
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
}
