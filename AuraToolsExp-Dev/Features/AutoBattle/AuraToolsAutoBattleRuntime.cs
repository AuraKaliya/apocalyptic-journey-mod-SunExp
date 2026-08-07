using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        AuraToolsCombatContentRuntime.Initialize();
        AuraToolsCombatKnowledgeRuntime.Initialize();
        AuraToolsBundledFoundationModelRuntime.Initialize(modConfig);
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
                FightRestarting = _ => EndBattle(),
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

    internal static void NotifyModelLibraryChanged()
    {
        controller?.NotifyModelLibraryChanged();
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
    private const float FailedActionSuppressionSeconds = 2f;
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
    private readonly Dictionary<string, float> failedActionStateKeys =
        new(StringComparer.Ordinal);
    private bool activeDecisionPending;
    private string pendingActiveDecisionKey = "";
    private long decisionWorkGeneration;
    private long continuationBattleSessionId;
    private string continuationCandidateId = "";
    private string continuationSourceId = "";
    private string modelConfigurationKey = "";
    private long modelLoadGeneration;
    private long lastResetBattleSessionId;

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
        ApplyConfiguration();
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
        InvalidateDecisionWork();
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
        var battleSessionId = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        if (battleSessionId > 0
            && battleSessionId == lastResetBattleSessionId)
        {
            return;
        }
        lastResetBattleSessionId = battleSessionId;
        decisionIndex = 0;
        pendingSampleRecorded = false;
        ClearTeacherAction();
        ClearPredictionMarkers();
        nextPredictionAt = 0f;
        transaction.Reset();
        failedActionStateKeys.Clear();
        ClearContinuationHint();
        SetActive(startActive);
        DestroyButton();
        nextUiProbeAt = 0f;
    }

    public void EndBattle()
    {
        InvalidateDecisionWork();
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
        ClearContinuationHint();
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

    internal void NotifyModelLibraryChanged()
    {
        modelConfigurationKey = "";
        AuraToolsAutoBattleModelRuntime.UnloadResidentModels();
        ReloadDecisionEngine(force: true);
    }

    internal void BeginGameValidationBattle()
    {
        ReloadDecisionEngine(force: true);
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
        ApplyContinuationHint(state);
        QueueActiveDecision(state);
    }

    private void QueueActiveDecision(CombatStateObservation state)
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var profile = BuildProfile();
        var cacheKey = DecisionCacheKey(state, profile);
        if (activeDecisionPending
            && string.Equals(
                pendingActiveDecisionKey,
                cacheKey,
                StringComparison.Ordinal))
        {
            return;
        }

        var engine = SelectExecutionEngine(
            state,
            out var learned,
            out var phase);
        var preparedState = engine.PrepareStateForIsolatedWorker(state);
        var simulationRules =
            engine.SnapshotSimulationRulesForIsolatedWorker();
        var worker = engine.CreateIsolatedWorker(simulationRules);
        var inferenceParallelism = preparedState.Actions.Count >= 4
            ? settings.InferenceParallelism
            : 1;
        var secondaryWorker = inferenceParallelism > 1
            ? engine.CreateIsolatedWorker(simulationRules)
            : null;
        var requestGeneration = decisionWorkGeneration;
        var capturedSessionId = state.BattleSessionId;
        var capturedFingerprint = state.Fingerprint;
        var capturedModelId = trainedModelId;
        var queuedTimestamp = Stopwatch.GetTimestamp();

        if (!string.Equals(decisionCacheKey, cacheKey, StringComparison.Ordinal))
        {
            ClearDecisionCache();
            decisionCacheKey = cacheKey;
        }
        activeDecisionPending = true;
        pendingActiveDecisionKey = cacheKey;
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<ActiveDecisionResult>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "AutoBattle.ActiveDecision",
                Source = "AutoBattle.ActiveDecision",
                Kind = AuraSharedBackgroundWorkKind.Cpu,
                CompletionPriority = 100,
                Work = cancellation =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    var stopwatch = Stopwatch.StartNew();
                    CombatDecision decision;
                    if (secondaryWorker != null)
                    {
                        var decisions = new CombatDecision[2];
                        Parallel.Invoke(
                            new ParallelOptions
                            {
                                CancellationToken = cancellation,
                                MaxDegreeOfParallelism = 2
                            },
                            () => decisions[0] = worker.Choose(
                                CombatPlayerObservationBoundary.Normalize(
                                    preparedState),
                                profile),
                            () => decisions[1] = secondaryWorker.Choose(
                                CombatPlayerObservationBoundary.Normalize(
                                    preparedState),
                                profile,
                                new CombatSearchExplorationOptions
                                {
                                    RootNoiseFraction = 0d,
                                    DeterminizationOffset = 104729
                                }));
                        decision = MergeParallelDecisions(
                            decisions,
                            profile);
                    }
                    else
                    {
                        decision = worker.Choose(preparedState, profile);
                        decision.InferenceWorkerCount = 1;
                        decision.InferenceAgreement = 1d;
                    }
                    stopwatch.Stop();
                    cancellation.ThrowIfCancellationRequested();
                    return new ActiveDecisionResult(
                        decision,
                        stopwatch.Elapsed.TotalMilliseconds,
                        queuedTimestamp,
                        capturedSessionId,
                        capturedFingerprint,
                        cacheKey,
                        learned,
                        phase);
                },
                IsStillCurrent = () =>
                    Active
                    && AuraToolsAutoBattleRuntime.ModuleEnabled
                    && decisionWorkGeneration == requestGeneration
                    && string.Equals(
                        trainedModelId,
                        capturedModelId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        decisionCacheKey,
                        cacheKey,
                        StringComparison.Ordinal),
                ApplyOnMainThread = result =>
                {
                    if (string.Equals(
                            pendingActiveDecisionKey,
                            result.CacheKey,
                            StringComparison.Ordinal))
                    {
                        activeDecisionPending = false;
                        pendingActiveDecisionKey = "";
                    }
                    if (result.Learned)
                    {
                        cachedLearnedDecision = result.Decision;
                    }
                    else
                    {
                        cachedBaselineDecision = result.Decision;
                    }
                    var totalMilliseconds = ElapsedMilliseconds(
                        result.QueuedTimestamp);
                    RecordDecisionTiming(
                        result.ComputeMilliseconds,
                        result.Phase + "-background");
                    AuraToolsLog.Debug(
                        "[AutoBattle][Performance] phase=" + result.Phase
                        + " queueAndComputeMs="
                        + totalMilliseconds.ToString("0.00")
                        + " computeMs="
                        + result.ComputeMilliseconds.ToString("0.00")
                        + " simulations=" + result.Decision.SearchSimulations
                        + " nodes=" + result.Decision.SearchNodes
                        + " budget=" + result.Decision.SearchBudgetTier
                        + " confidence="
                        + result.Decision.SearchConfidence.ToString("0.000")
                        + " candidates="
                        + result.Decision.SearchCandidateCount
                        + "/" + result.Decision.SearchOriginalCandidateCount
                        + " workers="
                        + result.Decision.InferenceWorkerCount
                        + " agreement="
                        + result.Decision.InferenceAgreement.ToString("0.00"));
                    TryExecuteCompletedDecision(result);
                },
                OnFailedOnMainThread = ex =>
                {
                    if (string.Equals(
                            pendingActiveDecisionKey,
                            cacheKey,
                            StringComparison.Ordinal))
                    {
                        activeDecisionPending = false;
                        pendingActiveDecisionKey = "";
                    }
                    nextDecisionAt = Time.unscaledTime + 0.1f;
                    if (!(ex is OperationCanceledException))
                    {
                        AuraToolsLog.Warn(
                            "[AutoBattle] background decision failed: "
                            + ex.Message);
                    }
                }
            });
        if (!queued)
        {
            activeDecisionPending = false;
            pendingActiveDecisionKey = "";
            nextDecisionAt = Time.unscaledTime + 0.1f;
            AuraToolsLog.Warn(
                "[AutoBattle] background decision task could not be queued");
        }
    }

    private CombatDecisionEngine SelectExecutionEngine(
        CombatStateObservation state,
        out bool learned,
        out string phase)
    {
        learned = string.Equals(
                      trainedModelMode,
                      "active",
                      StringComparison.OrdinalIgnoreCase)
                  && !string.Equals(
                      trainedModelId,
                      "none",
                      StringComparison.Ordinal);
        if (learned
            && !AuraToolsCombatKnowledgeRuntime.HasPlayerEquivalentReadiness(
                state,
                out var coverageReason))
        {
            learned = false;
            AuraToolsLog.Debug(
                "[AutoBattle][Observation] active model fell back to baseline: "
                + coverageReason);
        }
        phase = learned ? "learned-active" : "baseline-active";
        return learned ? trainedDecisionEngine : baselineDecisionEngine;
    }

    private void TryExecuteCompletedDecision(ActiveDecisionResult result)
    {
        if (!Active
            || transaction.IsActive
            || WitchCombatInteractionRuntime.HasActivePrompt
            || !TryCapturePlayerState(out var state, out _)
            || !state.IsPlayerActionWindow
            || state.UiBusy)
        {
            nextDecisionAt = Time.unscaledTime + 0.05f;
            return;
        }

        ApplyFailedActionSuppressions(state);
        if (!CombatDecisionFreshnessPolicy.TryBindCurrent(
                result.BattleSessionId,
                result.Fingerprint,
                state,
                result.Decision,
                out var decision,
                out var freshnessReason))
        {
            AuraToolsLog.Debug(
                "[AutoBattle][DecisionStale] discarded=" + freshnessReason
                + " captured=" + CompactFingerprint(result.Fingerprint)
                + " current=" + CompactFingerprint(state.Fingerprint));
            nextDecisionAt = Time.unscaledTime + 0.05f;
            return;
        }

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
        AuraToolsWitchSkillInteraction.Prepare(state, decision.Action);
        var execution = runtime.Execute(decision.Action);
        if (!execution.Accepted)
        {
            CombatInteractionBroker.ClearNextHint();
            AuraToolsAutoBattleGameValidationRuntime.RecordExecutionFailure(
                execution.Message);
            transaction.Fail(execution.Message);
            failedActionStateKeys[
                CombatActionExecutionPolicy.BuildFailureSuppressionKey(
                    state,
                    decision.Action)] = Time.unscaledTime
                                        + FailedActionSuppressionSeconds;
            AuraToolsLog.Warn(
                "[AutoBattle] execution rejected after revalidation: "
                + execution.Message);
            transaction.Reset();
            nextDecisionAt = Time.unscaledTime + 0.05f;
            return;
        }

        LogForcedEndTurnCandidateAudit(state, decision);

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
            + " budget=" + decision.SearchBudgetTier
            + " simulations=" + decision.SearchSimulations
            + " nodes=" + decision.SearchNodes
            + " transpositions=" + decision.SearchTranspositionHits
            + " stoppedByTime=" + decision.SearchStoppedByTime
            + " confidence=" + decision.SearchConfidence.ToString("0.000")
            + " valueGap=" + decision.SearchValueGap.ToString("0.000")
            + " rootVisits=" + decision.SearchBestVisits
            + "/" + decision.SearchSecondBestVisits
            + " candidates=" + decision.SearchCandidateCount
            + "/" + decision.SearchOriginalCandidateCount
            + " workers=" + decision.InferenceWorkerCount
            + " agreement=" + decision.InferenceAgreement.ToString("0.00")
            + " " + ScoreBreakdown(decision)
            + " " + decision.EndTurnTrace
            + " " + decision.PlanSummary);
    }

    private static void LogForcedEndTurnCandidateAudit(
        CombatStateObservation state,
        CombatDecision decision)
    {
        if (decision.Action?.Kind != CombatActionKind.EndTurn
            || state.CurrentPower <= 0
            || decision.EndTurnTrace.IndexOf(
                "endTurnVerdict=Forced",
                StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        var actionsBySource = state.Actions
            .Where(action => action != null
                             && action.Kind == CombatActionKind.PlayCard)
            .GroupBy(action => action.SourceId ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var rows = new List<string>();
        foreach (var cardId in state.HandCardIds ?? new List<string>())
        {
            if (!actionsBySource.TryGetValue(cardId ?? "", out var actions))
            {
                rows.Add((cardId ?? "<unknown>") + "{candidate=missing}");
                continue;
            }
            foreach (var action in actions)
            {
                rows.Add(
                    (cardId ?? "<unknown>")
                    + "{cost=" + action.Cost
                    + ",legal=" + (action.Legal ? 1 : 0)
                    + ",rejection=" + CompactAuditText(action.RejectionReason)
                    + ",energyGain="
                    + action.Semantics.EnergyGain.ToString("0.###")
                    + ",draw=" + action.Semantics.Draw.ToString("0.###")
                    + ",usable=" + AuditFeature(action, "runtimeUsable")
                    + ",unplayable=" + AuditFeature(action, "unplayable")
                    + ",tags=" + CompactAuditText(string.Join(
                        ",",
                        state.CardTagsById.TryGetValue(cardId ?? "", out var tags)
                            ? tags
                            : new List<string>()))
                    + "}");
            }
        }
        AuraToolsLog.Warn(
            "[AutoBattle][ForcedEndAudit] power=" + state.CurrentPower
            + "/" + state.MaxPower
            + " hand=" + string.Join(" | ", rows));
    }

    private static string AuditFeature(
        CombatActionObservation action,
        string key)
    {
        return action.Features.TryGetValue(key, out var value)
            ? value.ToString("0.###")
            : "n/a";
    }

    private static string CompactAuditText(string? value)
    {
        return (value ?? "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("|", "/");
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
                var elapsed = Time.unscaledTime - transaction.StartedAt;
                var grace = CombatActionExecutionPolicy.NoEffectGraceSeconds(
                    pendingDecision.Action,
                    settings.DecisionIntervalMs / 1000d);
                if (elapsed >= grace)
                {
                    SuppressNoEffectAction(
                        after,
                        "action-specific settlement grace elapsed");
                }
                return;
            }
            runtime.ConfirmSettledAction(pendingDecision.Action, after);
            transaction.Complete("action settled: " + settlementReason);
            RecordPendingTrainingSample(
                CombatActionTransactionState.Completed.ToString(),
                transaction.TerminalReason,
                after.Enemies.Count == 0 || after.Player.CurrentHp <= 0,
                after);
            RememberContinuationHint();
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
            RememberContinuationHint();
            ClearPendingAction();
            transaction.Reset();
            nextDecisionAt = Time.unscaledTime + 0.05f;
            return true;
        }

        SuppressNoEffectAction(
            after,
            "action transaction reached the no-effect timeout");
        return true;
    }

    private void SuppressNoEffectAction(
        CombatStateObservation? after,
        string diagnostic)
    {
        if (beforeAction == null || pendingDecision?.Action == null)
        {
            return;
        }
        var failedAction = pendingDecision.Action;
        var suppressionState = after ?? beforeAction;
        failedActionStateKeys[
            FailedActionStateKey(suppressionState, failedAction)] =
            Time.unscaledTime + FailedActionSuppressionSeconds;
        RecordPendingTrainingSample(
            CombatActionTransactionState.Failed.ToString(),
            "action produced no causal game-state effect and was suppressed: "
            + diagnostic,
            terminal: false,
            after);
        AuraToolsLog.Warn(
            "[AutoBattle] suppressed no-effect action source="
            + failedAction.SourceId
            + " candidate=" + failedAction.CandidateId);
        ClearPendingAction();
        transaction.Reset();
        nextDecisionAt = Time.unscaledTime + 0.05f;
    }

    private void ApplyFailedActionSuppressions(
        CombatStateObservation state)
    {
        foreach (var expired in failedActionStateKeys
                     .Where(pair => pair.Value <= Time.unscaledTime)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            failedActionStateKeys.Remove(expired);
        }
        foreach (var action in state.Actions)
        {
            if (!failedActionStateKeys.ContainsKey(
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
        return CombatActionExecutionPolicy.BuildFailureSuppressionKey(
            state,
            action);
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

    private void ReloadDecisionEngine(bool force = false)
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (AuraToolsAutoBattleGameValidationRuntime.TryGetValidationModels(
                out var validationResidual,
                out var validationGuidance,
                out var validationPolicyValue,
                out var validationModelId))
        {
            var validationKey = "validation\n" + validationModelId;
            if (!force
                && string.Equals(
                    modelConfigurationKey,
                    validationKey,
                    StringComparison.Ordinal))
            {
                return;
            }
            modelConfigurationKey = validationKey;
            modelLoadGeneration++;
            InvalidateDecisionWork();
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
        var configuredMode = AuraToolsAutoBattleRuntime.ModuleEnabled
            ? settings.TrainedModelMode
            : "off";
        var profile = settings.Profile;
        var selectedModelId = settings.SelectedModelId ?? "";
        var configurationKey = configuredMode
                               + "\n"
                               + profile
                               + "\n"
                               + selectedModelId;
        if (!force
            && string.Equals(
                modelConfigurationKey,
                configurationKey,
                StringComparison.Ordinal))
        {
            return;
        }
        modelConfigurationKey = configurationKey;
        var loadGeneration = ++modelLoadGeneration;
        InvalidateDecisionWork();
        baselineDecisionEngine = new CombatDecisionEngine();
        trainedDecisionEngine = new CombatDecisionEngine();
        trainedModelId = "none";
        lastModelComparisonFingerprint = "";
        pendingShadowFingerprint = "";
        ClearDecisionCache();
        if (string.Equals(
                configuredMode,
                "off",
                StringComparison.OrdinalIgnoreCase))
        {
            trainedModelMode = "off";
            AuraToolsAutoBattleModelRuntime.UnloadResidentModels();
            lastModelDiagnostic = "模型应用已关闭，驻留权重已卸载";
            AuraToolsLog.Info("[AutoBattle] " + lastModelDiagnostic);
            return;
        }
        trainedModelMode = "off";
        lastModelDiagnostic = "模型正在后台加载";
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<AutoBattleResidentModelSet>
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "AutoBattle.ModelResidency",
                Source = "AutoBattle.ModelResidency",
                Kind = AuraSharedBackgroundWorkKind.Io,
                CompletionPriority = 90,
                Work = cancellation =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    return AuraToolsAutoBattleModelRuntime.LoadResidentModels(
                        profile,
                        selectedModelId);
                },
                IsStillCurrent = () =>
                    modelLoadGeneration == loadGeneration
                    && string.Equals(
                        modelConfigurationKey,
                        configurationKey,
                        StringComparison.Ordinal),
                ApplyOnMainThread = loaded => ApplyResidentModels(
                    configuredMode,
                    profile,
                    configurationKey,
                    loadGeneration,
                    loaded),
                OnFailedOnMainThread = ex =>
                {
                    if (modelLoadGeneration != loadGeneration)
                    {
                        return;
                    }
                    lastModelDiagnostic = "模型后台加载失败：" + ex.Message;
                    AuraToolsLog.Warn("[AutoBattle] " + lastModelDiagnostic);
                }
            });
        if (!queued)
        {
            lastModelDiagnostic = "模型后台加载任务未能提交";
            AuraToolsLog.Warn("[AutoBattle] " + lastModelDiagnostic);
        }
    }

    private void ApplyResidentModels(
        string configuredMode,
        string profile,
        string configurationKey,
        long loadGeneration,
        AutoBattleResidentModelSet loaded)
    {
        if (modelLoadGeneration != loadGeneration
            || !string.Equals(
                modelConfigurationKey,
                configurationKey,
                StringComparison.Ordinal))
        {
            return;
        }
        InvalidateDecisionWork();
        trainedModelMode = configuredMode;
        trainedDecisionEngine = new CombatDecisionEngine(
            loaded.Residual,
            loaded.SearchGuidance,
            policyValueModel: loaded.PolicyValue);
        trainedModelId = loaded.ModelId;
        var diagnostic = loaded.Diagnostic;
        if (string.Equals(
                trainedModelMode,
                "active",
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trainedModelId, "none", StringComparison.Ordinal)
            && !AuraToolsAutoBattleSimulationRuntime.CanActivateModel(
                profile,
                trainedModelId,
                out var gateReason))
        {
            trainedModelMode = "shadow";
            diagnostic += "；主动应用门禁未通过，已降级为影子评估：" + gateReason;
        }
        lastModelComparisonFingerprint = "";
        pendingShadowFingerprint = "";
        ClearDecisionCache();
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
        var preparedState = engine.PrepareStateForIsolatedWorker(state);
        var worker = engine.CreateIsolatedWorker(
            engine.SnapshotSimulationRulesForIsolatedWorker());
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
                    var learned = worker.Choose(preparedState, profile);
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

    private void RememberContinuationHint()
    {
        if (beforeAction == null || pendingDecision?.Plan == null
            || pendingDecision.Plan.Count < 2)
        {
            ClearContinuationHint();
            return;
        }
        var next = pendingDecision.Plan[1];
        continuationBattleSessionId = beforeAction.BattleSessionId;
        continuationCandidateId = next.CandidateId ?? "";
        continuationSourceId = next.SourceId ?? "";
    }

    private void ApplyContinuationHint(CombatStateObservation state)
    {
        if (state.BattleSessionId != continuationBattleSessionId
            || string.IsNullOrWhiteSpace(continuationSourceId))
        {
            ClearContinuationHint();
            return;
        }
        var action = state.Actions.FirstOrDefault(candidate =>
                         candidate.Legal
                         && string.Equals(
                             candidate.CandidateId,
                             continuationCandidateId,
                             StringComparison.Ordinal))
                     ?? state.Actions.FirstOrDefault(candidate =>
                         candidate.Legal
                         && string.Equals(
                             candidate.SourceId,
                             continuationSourceId,
                             StringComparison.OrdinalIgnoreCase));
        if (action != null)
        {
            action.Features["continuationHint"] = 1d;
        }
        ClearContinuationHint();
    }

    private void ClearContinuationHint()
    {
        continuationBattleSessionId = 0;
        continuationCandidateId = "";
        continuationSourceId = "";
    }

    private static double ElapsedMilliseconds(long startedTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startedTimestamp)
               * 1000d
               / Stopwatch.Frequency;
    }

    private static CombatDecision MergeParallelDecisions(
        IReadOnlyList<CombatDecision> decisions,
        CombatDecisionProfile profile)
    {
        var available = decisions
            .Where(decision => decision != null)
            .ToList();
        if (available.Count == 0)
        {
            return new CombatDecision
            {
                Reason = "parallel inference produced no decision",
                InferenceWorkerCount = decisions.Count,
                InferenceAgreement = 0d
            };
        }
        var groups = available
            .GroupBy(
                decision => decision.Action?.CandidateId ?? "",
                StringComparer.Ordinal)
            .Select(group => new
            {
                Decisions = group.ToList(),
                Risk = group.Min(SelectedDecisionRisk),
                Confidence = group.Average(item => item.SearchConfidence),
                Score = group.Average(item => item.Score)
            })
            .OrderByDescending(group =>
                group.Risk <= profile.DeathRiskLimit)
            .ThenByDescending(group => group.Decisions.Count)
            .ThenByDescending(group => group.Confidence)
            .ThenBy(group => group.Risk)
            .ThenByDescending(group => group.Score)
            .ThenBy(
                group => group.Decisions[0].Action?.CandidateId ?? "",
                StringComparer.Ordinal)
            .First();
        var chosen = groups.Decisions
            .OrderByDescending(item => item.SearchConfidence)
            .ThenByDescending(item => item.SearchSimulations)
            .ThenByDescending(item => item.Score)
            .First();
        var agreement = groups.Decisions.Count / (double)available.Count;
        var independentConfidence = agreement >= 1d
            ? 1d - groups.Decisions.Aggregate(
                1d,
                (remaining, item) => remaining
                                     * (1d - Math.Max(
                                         0d,
                                         Math.Min(1d, item.SearchConfidence))))
            : groups.Confidence * agreement;
        chosen.SearchSimulations = available.Sum(item => item.SearchSimulations);
        chosen.SearchNodes = available.Sum(item => item.SearchNodes);
        chosen.SearchTranspositionHits = available.Sum(
            item => item.SearchTranspositionHits);
        chosen.SearchStoppedEarly = available.All(item => item.SearchStoppedEarly);
        chosen.SearchStoppedByTime = available.Any(item => item.SearchStoppedByTime);
        chosen.SearchConfidence = Math.Max(
            0d,
            Math.Min(1d, independentConfidence));
        chosen.SearchBestVisits = groups.Decisions.Sum(item => item.SearchBestVisits);
        chosen.SearchSecondBestVisits = groups.Decisions.Sum(
            item => item.SearchSecondBestVisits);
        chosen.SearchCandidateCount = available.Max(
            item => item.SearchCandidateCount);
        chosen.SearchOriginalCandidateCount = available.Max(
            item => item.SearchOriginalCandidateCount);
        chosen.InferenceWorkerCount = available.Count;
        chosen.InferenceAgreement = agreement;
        chosen.Reason += agreement >= 1d
            ? "; parallel consensus"
            : "; parallel safety arbitration";
        chosen.PlanSummary += "; parallel="
                              + groups.Decisions.Count
                              + "/" + available.Count
                              + "; merged-confidence="
                              + chosen.SearchConfidence.ToString("0.000");
        return chosen;
    }

    private static double SelectedDecisionRisk(CombatDecision decision)
    {
        if (decision.Action == null)
        {
            return double.MaxValue;
        }
        var selected = decision.Candidates.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Action.CandidateId,
                decision.Action.CandidateId,
                StringComparison.Ordinal));
        return selected?.SearchDeathRisk ?? double.MaxValue;
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
               + "|" + profile.SearchTimeBudgetMilliseconds
               + "|" + trainedModelMode
               + "|" + trainedModelId;
    }

    private sealed class ActiveDecisionResult
    {
        public ActiveDecisionResult(
            CombatDecision decision,
            double computeMilliseconds,
            long queuedTimestamp,
            long battleSessionId,
            string fingerprint,
            string cacheKey,
            bool learned,
            string phase)
        {
            Decision = decision;
            ComputeMilliseconds = computeMilliseconds;
            QueuedTimestamp = queuedTimestamp;
            BattleSessionId = battleSessionId;
            Fingerprint = fingerprint;
            CacheKey = cacheKey;
            Learned = learned;
            Phase = phase;
        }

        public CombatDecision Decision { get; }

        public double ComputeMilliseconds { get; }

        public long QueuedTimestamp { get; }

        public long BattleSessionId { get; }

        public string Fingerprint { get; }

        public string CacheKey { get; }

        public bool Learned { get; }

        public string Phase { get; }
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

    private void InvalidateDecisionWork()
    {
        decisionWorkGeneration++;
        activeDecisionPending = false;
        pendingActiveDecisionKey = "";
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
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var profile =
            AuraToolsAutoBattleSimulationRuntime.BuildDecisionProfile(
                settings);
        profile.SearchBudgetContext = "deployment";
        profile.SearchTimeBudgetMilliseconds =
            settings.DecisionTimeBudgetMs;
        return profile;
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

        var content = AuraToolsCombatContentRuntime.SnapshotContentSet();
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        sample.ContentSetHash = content.ContentSetHash;
        sample.OwnerModSetHash = content.OwnerModSetHash;
        sample.BaseModelId = settings.SelectedModelId ?? "";
        sample.ActiveAdapterIds = AuraToolsAutoBattleModelRuntime
            .SnapshotActiveAdapterIds(
                settings.Profile,
                sample.BaseModelId)
            .ToList();
        foreach (var candidate in sample.Candidates
                     ?? new List<CombatTrainingCandidate>())
        {
            candidate.OwnerModId =
                AuraToolsCombatContentRuntime.ResolveOwnerModId(
                    candidate.SourceId);
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
            var sessionsByContentSet = new Dictionary<
                string,
                Dictionary<long, List<CombatTrainingSample>>>(
                StringComparer.Ordinal);
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
                        sessionsByContentSet.Clear();
                        sessionGeneration = currentGeneration;
                    }
                    var currentBatch = batch
                        .Where(item => item.Generation == currentGeneration)
                        .ToList();
                    if (currentBatch.Count == 0)
                    {
                        continue;
                    }
                    foreach (var contentBatch in currentBatch.GroupBy(
                                 item => string.IsNullOrWhiteSpace(
                                     item.Sample.ContentSetHash)
                                     ? CombatContentSetProtocol.EmptyContentSetHash
                                     : item.Sample.ContentSetHash,
                                 StringComparer.Ordinal))
                    {
                        var directory = AuraToolsCombatContentRuntime
                            .LiveDatasetDirectory(contentBatch.Key);
                        var path = Path.Combine(
                            directory,
                            "auto-battle-training-v7.jsonl");
                        var episodesPath = Path.Combine(
                            directory,
                            "live-combat-episodes-v5.jsonl");
                        if (!sessionsByContentSet.TryGetValue(
                                contentBatch.Key,
                                out var sessions))
                        {
                            sessions = new Dictionary<long, List<CombatTrainingSample>>();
                            sessionsByContentSet[contentBatch.Key] = sessions;
                        }
                        using var writer = new StreamWriter(path, append: true);
                        using var episodeWriter = new StreamWriter(
                            episodesPath,
                            append: true);
                        foreach (var item in contentBatch)
                        {
                            writer.WriteLine(
                                AuraSharedJson.SerializeCompact(item.Sample));
                            RecordLiveEpisode(
                                item.Sample,
                                sessions,
                                episodeWriter);
                        }
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
                         "auto-battle-training-v7.jsonl",
                         "live-combat-episodes-v5.jsonl"
                     })
            {
                var root = Path.Combine(
                    AuraSharedPaths.OwnerSystemDataDirectory(
                        AuraToolsIds.ModId,
                        "AuraCombatAI"),
                    "Datasets",
                    "Live");
                if (!Directory.Exists(root))
                {
                    continue;
                }
                foreach (var path in Directory.EnumerateFiles(
                             root,
                             fileName,
                             SearchOption.AllDirectories))
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
                   "auto-battle-training-v7.jsonl",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   fileName,
                   "live-combat-episodes-v5.jsonl",
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
