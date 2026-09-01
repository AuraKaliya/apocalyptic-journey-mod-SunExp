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
using AuraCombatSimulation.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
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
    private static ModConfig? currentConfig;
    private static AuraToolsLiveCombatSession? controller;
    private static IDisposable? lifecycleSubscription;
    private static IDisposable? trainingSinkRegistration;
    private static IDisposable? automationCapabilityRegistration;
    private static IDisposable? cardActionRegistration;
    private static IDisposable? skillActionRegistration;
    private static AuraToolsAutoBattleTrainingSink? trainingSink;

    internal static bool ModuleEnabled =>
        AuraToolsConfigService.MatchExperience.AutoBattle.Enabled;

    public static bool Active => controller != null && controller.Active;

    internal static string TrainingRecorderDiagnostic =>
        trainingSink?.Diagnostic ?? "";

    internal static bool RecordJourney(
        CombatJourneyTrainingEpisode episode)
    {
        return trainingSink?.RecordJourney(episode) == true;
    }

    internal static bool FinalizeTrainingBattle(
        long battleSessionId,
        string outcome,
        string reason)
    {
        return trainingSink?.FinalizeBattle(
            battleSessionId,
            outcome,
            reason) == true;
    }

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        currentConfig = modConfig;
        AuraToolsCombatContentRuntime.Initialize();
        AuraToolsCombatKnowledgeRuntime.Initialize();
        AuraToolsBundledFoundationModelRuntime.Initialize(modConfig);
        AuraToolsAutoBattleJourneyRuntime.Initialize(modConfig);
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
            "FightUI.onChangeTurnBtn",
            ObserveHumanEndTurn,
            HandlerId + ".Teacher");
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.AutoBattle,
            OnConfigurationChanged);
        ApplyModuleActivation(ModuleEnabled);
    }

    internal static void ApplyModuleActivation(bool enabled)
    {
        AuraToolsAutoBattleJourneyRuntime.ApplyModuleActivation(enabled);
        if (!initialized || currentConfig == null) return;
        if (!enabled)
        {
            AuraSharedBackgroundWorkScheduler.CancelOwner(
                AuraToolsIds.ModId + ".AutoBattle");
            controller?.AbortBattle("module-disabled");
            WitchCombatInteractionRuntime.Reset();
            if (controller != null)
            {
                Object.Destroy(controller.gameObject);
                controller = null;
            }
            lifecycleSubscription?.Dispose();
            lifecycleSubscription = null;
            automationCapabilityRegistration?.Dispose();
            automationCapabilityRegistration = null;
            cardActionRegistration?.Dispose();
            cardActionRegistration = null;
            skillActionRegistration?.Dispose();
            skillActionRegistration = null;
            trainingSinkRegistration?.Dispose();
            trainingSinkRegistration = null;
            trainingSink?.Dispose();
            trainingSink = null;
            AuraToolsAutoBattleGameValidationRuntime.ApplyModuleActivation(
                false);
            return;
        }

        EnsureController();
        automationCapabilityRegistration ??=
            CombatActionAutomationRegistry.Register(
                AuraToolsIds.ModId,
                "player-ui-runtime",
                new AuraToolsPlayerActionAutomationProvider(),
                priority: 10);
        cardActionRegistration ??= AuraCardActionTransactionRouter.Register(
            currentConfig,
            AuraToolsIds.ModId,
            HandlerId + ".NativeCardSettlement",
            new AuraCardActionSubscription
            {
                Phases = AuraCardActionPhase.Attempting
                         | AuraCardActionPhase.Committed
                         | AuraCardActionPhase.Completed
                         | AuraCardActionPhase.Aborted,
                Priority = 100,
                Handler = context =>
                {
                    if (context.Phase == AuraCardActionPhase.Attempting)
                    {
                        controller?.CaptureTeacherAction(context.Card);
                    }
                    else
                    {
                        if (context.Phase == AuraCardActionPhase.Committed)
                        {
                            controller?.ObserveHumanActionCommitted();
                        }
                        controller?.NotifyNativeActionProgress();
                    }
                }
            },
            AuraToolsLog.Info,
            AuraToolsLog.Warn);
        skillActionRegistration ??= AuraSkillActionTransactionRouter.Register(
            currentConfig,
            AuraToolsIds.ModId,
            HandlerId + ".NativeSkillSettlement",
            new AuraSkillActionSubscription
            {
                Phases = AuraSkillActionPhase.Attempting
                         | AuraSkillActionPhase.Committed
                         | AuraSkillActionPhase.Completed
                         | AuraSkillActionPhase.Aborted,
                Priority = 100,
                Handler = context =>
                {
                    if (context.Phase == AuraSkillActionPhase.Attempting)
                    {
                        controller?.CaptureTeacherAction(context.Skill);
                    }
                    else
                    {
                        if (context.Phase == AuraSkillActionPhase.Committed)
                        {
                            controller?.ObserveHumanActionCommitted();
                        }
                        controller?.NotifyNativeActionProgress();
                    }
                }
            },
            AuraToolsLog.Info,
            AuraToolsLog.Warn);
        lifecycleSubscription ??= AuraBattleLifecycleRouter.Register(
            currentConfig,
            AuraToolsIds.ModId,
            HandlerId,
            new AuraBattleLifecycleSubscription
            {
                BattleInitializing = _ => ResetForBattle(),
                BattleRestarting = _ => AbortBattle("battle-restarting"),
                OutcomeEntering = outcome =>
                    controller?.BeginBattleClosing(outcome.Outcome),
                BattleSettling = outcome =>
                    controller?.PrepareBattleSettlement(outcome.Outcome),
                BattleEnded = outcome =>
                    controller?.ReleaseBattlePresentation(outcome.Outcome),
                BattleFinalized = outcome =>
                    controller?.FinalizeBattle(outcome.Outcome)
            },
            AuraToolsLog.Info,
            AuraToolsLog.Warn);
        trainingSink ??= new AuraToolsAutoBattleTrainingSink();
        trainingSinkRegistration ??= CombatAiRegistry.RegisterTrainingSink(
            AuraToolsIds.ModId,
            "JsonLinesV4",
            trainingSink);
        AuraToolsAutoBattleGameValidationRuntime.Initialize(currentConfig);
        AuraToolsAutoBattleGameValidationRuntime.ApplyModuleActivation(true);
    }

    public static void SetActive(bool active)
    {
        EnsureController().SetActive(active);
    }

    private static void OnConfigurationChanged()
    {
        if (!ModuleEnabled)
        {
            controller?.ApplyConfiguration();
            return;
        }
        EnsureController().ApplyConfiguration();
    }

    public static void ReloadModels()
    {
        controller?.ApplyConfiguration();
    }

    internal static void NotifyModelLibraryChanged()
    {
        AuraToolsAutoBattleUiSnapshotRuntime.Invalidate();
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
        if (!string.Equals(mode, "off", StringComparison.Ordinal)
            && !AuraToolsAutoBattleModelRuntime
                .PortableFoundationMeetsActivationGate(
                settings.Profile,
                selectedModelId,
                out var gateReason))
        {
            status = SnapshotModelApplicationStatus();
            message = "所选模型尚不能应用：" + gateReason;
            return false;
        }
        if ((string.Equals(mode, "trial", StringComparison.Ordinal)
             || string.Equals(mode, "full", StringComparison.Ordinal))
            && !AuraToolsAutoBattleModelRuntime
                .FoundationActiveUseRiskAcknowledged(
                    settings.Profile,
                    selectedModelId,
                    out var riskReason))
        {
            status = SnapshotModelApplicationStatus();
            message = "所选模型尚不能主动接管：" + riskReason;
            return false;
        }

        settings.TrainedModelMode = mode;
        settings.Normalize();
        AuraToolsConfigService.SaveAutoBattle();
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
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "shadow" => "shadow",
            "trial" => "trial",
            "full" => "full",
            "active" => "trial",
            _ => "off"
        };
    }

    internal static bool IsModelApplicationMode(string value)
    {
        var mode = NormalizeModelApplicationMode(value);
        return string.Equals(mode, "trial", StringComparison.Ordinal)
               || string.Equals(mode, "full", StringComparison.Ordinal);
    }

    private static string ModelApplicationModeLabel(string value)
    {
        return value switch
        {
            "shadow" => "观察模式",
            "trial" => "试用",
            "full" => "正式接管",
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
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        var startActive = ModuleEnabled
                          && string.Equals(
                              NormalizeModelApplicationMode(
                                  autoBattle.TrainedModelMode),
                              "full",
                              StringComparison.Ordinal)
                          && autoBattle.StartActive;
        EnsureController().ResetForBattle(
            startActive);
    }

    private static void AbortBattle(string reason)
    {
        controller?.AbortBattle(reason);
        WitchCombatInteractionRuntime.Reset();
    }

    private static void ObserveHumanEndTurn(ModHookContext context)
    {
        controller?.CaptureTeacherEndTurn();
    }

    private static AuraToolsLiveCombatSession EnsureController()
    {
        if (controller != null)
        {
            return controller;
        }

        var host = new GameObject("AuraToolsAutoBattleRuntime");
        Object.DontDestroyOnLoad(host);
        controller = host.AddComponent<AuraToolsLiveCombatSession>();
        return controller;
    }
}

internal sealed class AuraToolsPlayerActionAutomationProvider :
    ICombatActionAutomationProvider
{
    public bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionAutomationDescriptor descriptor)
    {
        descriptor = new CombatActionAutomationDescriptor();
        if (action == null
            || action.Kind is not (CombatActionKind.PlayCard
                or CombatActionKind.UseSkill
                or CombatActionKind.EndTurn
                or CombatActionKind.ResolvePrompt))
        {
            return false;
        }

        descriptor = new CombatActionAutomationDescriptor
        {
            HeadlessSupported = false,
            FailureScope = CombatAgentFailureScope.Candidate,
            Reason = "AuraTools player automation requires the visible player UI"
        };
        return true;
    }
}

public sealed class AutoBattleModelApplicationStatus
{
    public string ConfiguredMode { get; set; } = "off";

    public string EffectiveMode { get; set; } = "off";

    public string SelectedModelId { get; set; } = "";

    public string LoadedModelId { get; set; } = "none";

    public string Diagnostic { get; set; } = "";

    public string DecisionOwner { get; set; } = "baseline";

    public bool ModelLoaded { get; set; }

    public bool ModelLoading { get; set; }

    public bool ModelIsolatedForBattle { get; set; }

    public int EmergencyFallbackCount { get; set; }

    public string LastFallbackReason { get; set; } = "";
}

internal sealed class AuraToolsLiveCombatSession : MonoBehaviour
{
    private const string ButtonName = "AuraToolsAutoBattleButton";
    private const float FailedActionSuppressionSeconds = 2f;
    private readonly WitchCombatRuntime runtime = new();
    private CombatDecisionEngine baselineDecisionEngine = new();
    private CombatDecisionEngine trainedDecisionEngine = new();
    private CombatDecisionEngineWorker baselineDecisionWorker = null!;
    private CombatDecisionEngineWorker trainedDecisionWorker = null!;
    private CombatLiveDecisionLane decisionLane = null!;
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
    private long pendingDecisionReceiptId;
    private string pendingDecisionAuthority = "rule-baseline";
    private string pendingDecisionModelId = "none";
    private CombatLiveDecisionTiming? pendingDecisionTiming;
    private CombatStateObservation? teacherBeforeAction;
    private CombatDecision? teacherDecision;
    private CombatActionObservation? teacherActualAction;
    private long teacherDecisionReceiptId;
    private string teacherDecisionAuthority = "rule-baseline";
    private string teacherDecisionModelId = "none";
    private CombatLiveDecisionTiming? teacherDecisionTiming;
    private string teacherRecommendedCandidateId = "";
    private float teacherStartedAt;
    private long teacherTransactionId;
    private string lastModelDiagnostic = "";
    private string trainedModelMode = "off";
    private string trainedModelId = "none";
    private float nextPredictionAt;
    private string shadowPredictionFingerprint = "";
    private string shadowPredictionCandidateId = "";
    private bool teacherPolicyVisibleToHuman;
    private string decisionCacheKey = "";
    private readonly List<double> decisionTimingsMs = new();
    private readonly Dictionary<string, float> failedActionStateKeys =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> persistentNoEffectActionKeys =
        new(StringComparer.Ordinal);
    private bool activeDecisionPending;
    private long pendingActiveDecisionRequestId;
    private string pendingActiveDecisionKey = "";
    private bool predictionDecisionPending;
    private long pendingPredictionDecisionRequestId;
    private long pendingTeacherDecisionRequestId;
    private string pendingPredictionFingerprint = "";
    private long decisionWorkGeneration;
    private long continuationBattleSessionId;
    private string continuationCandidateId = "";
    private string continuationSourceId = "";
    private string modelConfigurationKey = "";
    private long modelLoadGeneration;
    private long lastResetBattleSessionId;
    private string noActionFingerprint = "";
    private float noActionSince = -1f;
    private readonly AutoBattleTechnicalFallbackState technicalFallback = new();
    private bool modelAvailable;
    private bool modelLoadPending;
    private string modelLoadFailureReason = "";
    private string currentDecisionOwner = "baseline";
    private string pendingDecisionOwner = "none";
    private bool closing;
    private CombatStateObservation? finalizationAfterState;
    private CombatStateObservation? teacherFinalizationAfterState;
    private float nextSettlementProbeAt;
    private int settlementCaptureCount;
    private float nextTeacherSettlementProbeAt;
    private int teacherSettlementCaptureCount;

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
            Diagnostic = lastModelDiagnostic,
            DecisionOwner = currentDecisionOwner,
            ModelLoaded = modelAvailable,
            ModelLoading = modelLoadPending,
            ModelIsolatedForBattle = technicalFallback.IsolatedForBattle,
            EmergencyFallbackCount = technicalFallback.FallbackDecisionCount,
            LastFallbackReason = technicalFallback.LastReason
        };
    }

    private void Awake()
    {
        decisionLane = new CombatLiveDecisionLane(
            "AuraTools.AutoBattle.LiveDecision");
        predictionPresenter = gameObject.GetComponent<AuraToolsAutoBattlePredictionPresenter>()
                              ?? gameObject.AddComponent<AuraToolsAutoBattlePredictionPresenter>();
        ApplyConfiguration();
    }

    public void SetActive(bool active)
    {
        var nextActive = active
                         && AuraToolsAutoBattleRuntime.ModuleEnabled
                         && !closing;
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
        ResetNoActionWatchdog();
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
        closing = false;
        finalizationAfterState = null;
        teacherFinalizationAfterState = null;
        decisionIndex = 0;
        pendingSampleRecorded = false;
        ClearTeacherAction();
        ClearPredictionMarkers();
        nextPredictionAt = 0f;
        transaction.Reset();
        failedActionStateKeys.Clear();
        persistentNoEffectActionKeys.Clear();
        ResetNoActionWatchdog();
        ClearContinuationHint();
        technicalFallback.ResetBattle(modelAvailable, modelLoadFailureReason);
        currentDecisionOwner = AuraToolsAutoBattleRuntime
            .IsModelApplicationMode(trainedModelMode)
            ? modelAvailable ? "model" : "emergency-baseline"
            : "baseline";
        SetActive(startActive);
        DestroyButton();
        nextUiProbeAt = 0f;
    }

    public void BeginBattleClosing(AuraBattleOutcome outcome)
    {
        if (closing) return;
        closing = true;
        Active = false;
        InvalidateDecisionWork();
        WitchCombatInteractionRuntime.TryResolve(false);
        ClearPredictionMarkers();
        UpdateButtonLabel();
    }

    public void PrepareBattleSettlement(AuraBattleOutcome outcome)
    {
        BeginBattleClosing(outcome);
        if (beforeAction != null && pendingDecision?.Action != null)
        {
            TryCapturePlayerState(out finalizationAfterState, out _);
            transaction.Complete("battle ended after action");
        }
        if (teacherBeforeAction != null && teacherDecision?.Action != null)
        {
            TryCapturePlayerState(
                out teacherFinalizationAfterState,
                out _);
        }
    }

    public void ReleaseBattlePresentation(AuraBattleOutcome outcome)
    {
        BeginBattleClosing(outcome);
        ClearPredictionMarkers();
        DestroyButton();
    }

    public void FinalizeBattle(AuraBattleOutcome outcome)
    {
        BeginBattleClosing(outcome);
        var outcomeLabel = OutcomeLabel(outcome);
        if (beforeAction != null && pendingDecision?.Action != null)
        {
            RecordPendingTrainingSample(
                CombatActionTransactionState.Completed.ToString(),
                transaction.TerminalReason.Length > 0
                    ? transaction.TerminalReason
                    : "battle finalized after action",
                terminal: true,
                finalizationAfterState,
                outcomeLabel);
        }
        if (teacherBeforeAction != null && teacherDecision?.Action != null)
        {
            RecordTeacherTrainingSample(
                CombatActionTransactionState.Completed.ToString(),
                "battle finalized after teacher action",
                terminal: true,
                teacherFinalizationAfterState,
                outcomeLabel);
        }
        AuraToolsAutoBattleRuntime.FinalizeTrainingBattle(
            ResolveTrainingBattleSessionId(),
            outcomeLabel,
            "battle-finalized");
        CompleteBattleCleanup();
    }

    public void AbortBattle(string reason)
    {
        InvalidateDecisionWork();
        if (transaction.IsActive)
        {
            transaction.HandOff(reason);
            RecordPendingTrainingSample(
                CombatActionTransactionState.HandedOff.ToString(),
                transaction.TerminalReason,
                terminal: false);
        }
        AuraToolsAutoBattleRuntime.FinalizeTrainingBattle(
            ResolveTrainingBattleSessionId(),
            "abandoned",
            reason);
        CompleteBattleCleanup();
    }

    private long ResolveTrainingBattleSessionId()
    {
        return beforeAction?.BattleSessionId
               ?? teacherBeforeAction?.BattleSessionId
               ?? (lastResetBattleSessionId > 0
                   ? lastResetBattleSessionId
                   : AuraBattleLifecycleRouter.CurrentBattleSessionId);
    }

    private void CompleteBattleCleanup()
    {
        Active = false;
        failedActionStateKeys.Clear();
        persistentNoEffectActionKeys.Clear();
        ResetNoActionWatchdog();
        ClearContinuationHint();
        currentDecisionOwner = "baseline";
        pendingDecisionOwner = "none";
        ClearTeacherAction();
        ClearPendingAction();
        ClearPredictionMarkers();
        transaction.Reset();
        DestroyButton();
        finalizationAfterState = null;
        teacherFinalizationAfterState = null;
        closing = true;
        try
        {
            baselineDecisionWorker?.ReleaseRetainedMemory();
            trainedDecisionWorker?.ReleaseRetainedMemory();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn(
                "[AutoBattle][LiveDecision] retained memory release failed: "
                + ex.Message);
        }
    }

    private static string OutcomeLabel(AuraBattleOutcome outcome)
    {
        return outcome switch
        {
            AuraBattleOutcome.Win => "victory",
            AuraBattleOutcome.Loss => "defeat",
            _ => "abandoned"
        };
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

    internal void NotifyNativeActionProgress()
    {
        if (closing) return;
        if (transaction.IsActive)
        {
            nextSettlementProbeAt = 0f;
        }
        if (teacherBeforeAction != null)
        {
            nextTeacherSettlementProbeAt = 0f;
        }
    }

    internal void ObserveHumanActionCommitted()
    {
        if (!Active && !closing)
        {
            AuraToolsAutoBattleJourneyRuntime.ObserveExecutedAction(
                automated: false);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextUiProbeAt)
        {
            nextUiProbeAt = Time.unscaledTime + 0.5f;
            RefreshButton();
        }

        DrainDecisionReceipts();
        if (closing)
        {
            return;
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

    private void DrainDecisionReceipts()
    {
        if (decisionLane == null) return;
        while (decisionLane.TryTakeReceipt(out var receipt))
        {
            switch (receipt.Purpose)
            {
                case CombatLiveDecisionPurpose.Execution:
                case CombatLiveDecisionPurpose.GameValidation:
                    ApplyActiveDecisionReceipt(receipt);
                    break;
                case CombatLiveDecisionPurpose.Prediction:
                case CombatLiveDecisionPurpose.Shadow:
                    ApplyPredictionDecisionReceipt(receipt);
                    break;
                case CombatLiveDecisionPurpose.Teacher:
                    ApplyTeacherDecisionReceipt(receipt);
                    break;
            }
        }
    }

    private void ApplyActiveDecisionReceipt(
        CombatLiveDecisionReceipt receipt)
    {
        if (receipt.RequestId != pendingActiveDecisionRequestId)
        {
            return;
        }
        activeDecisionPending = false;
        pendingActiveDecisionKey = "";
        pendingActiveDecisionRequestId = 0L;
        if (receipt.Generation != decisionWorkGeneration
            || !Active
            || !AuraToolsAutoBattleRuntime.ModuleEnabled
            || receipt.BattleSessionId
               != AuraBattleLifecycleRouter.CurrentBattleSessionId)
        {
            return;
        }

        if (receipt.Status != CombatLiveDecisionReceiptStatus.Completed)
        {
            nextDecisionAt = Time.unscaledTime + 0.05f;
            if (receipt.Status is CombatLiveDecisionReceiptStatus.Cancelled
                or CombatLiveDecisionReceiptStatus.Superseded)
            {
                return;
            }
            AuraToolsLog.Warn(
                "[AutoBattle][LiveDecision] status=" + receipt.Status
                + " authority=" + receipt.Authority
                + " reason=" + receipt.Reason);
            if (receipt.Authority == CombatLiveDecisionAuthority.Model)
            {
                ReportTechnicalModelFailure(
                    receipt.Status == CombatLiveDecisionReceiptStatus
                        .DeadlineExceeded
                        ? "inference-timeout"
                        : "inference-exception",
                    receipt.Reason);
            }
            return;
        }

        currentDecisionOwner = receipt.Authority switch
        {
            CombatLiveDecisionAuthority.Model => "model",
            CombatLiveDecisionAuthority.EmergencyBaseline =>
                "emergency-baseline",
            _ => "baseline"
        };
        UpdateButtonLabel();
        var phase = receipt.Authority switch
        {
            CombatLiveDecisionAuthority.Model => "learned-active",
            CombatLiveDecisionAuthority.EmergencyBaseline =>
                "emergency-baseline",
            _ => "baseline-active"
        };
        RecordDecisionTiming(
            receipt.Timing.ComputeMilliseconds,
            phase + "-live-lane");
        AuraToolsLog.Debug(
            "[AutoBattle][Performance] phase=" + phase
            + " queueAndComputeMs="
            + receipt.Timing.TotalMilliseconds.ToString("0.00")
            + " queueMs="
            + receipt.Timing.QueueMilliseconds.ToString("0.00")
            + " computeMs="
            + receipt.Timing.ComputeMilliseconds.ToString("0.00")
            + " simulations=" + receipt.Decision.SearchSimulations
            + " nodes=" + receipt.Decision.SearchNodes
            + " budget=" + receipt.Decision.SearchBudgetTier
            + " confidence="
            + receipt.Decision.SearchConfidence.ToString("0.000")
            + " model=" + receipt.ModelId
            + " path=" + receipt.Decision.DecisionPath
            + " proposed="
            + receipt.Decision.SearchProposedCandidateId
            + " executed="
            + (receipt.Decision.Action?.CandidateId ?? "none")
            + " governance="
            + receipt.Decision.GovernanceDecision
            + " stop=" + receipt.Decision.SearchStopReason);
        TryExecuteCompletedDecision(receipt);
    }

    private void DecideAndExecute()
    {
        if (modelLoadPending
            && AuraToolsAutoBattleRuntime
                .IsModelApplicationMode(trainedModelMode))
        {
            nextDecisionAt = Time.unscaledTime + 0.10f;
            return;
        }
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
        var engine = SelectExecutionEngine(
            state,
            out var learned,
            out var phase);
        var profile = BuildProfile();
        if (learned)
        {
            ConfigureModelAuthority(profile);
        }
        var cacheKey = DecisionCacheKey(state, profile)
                       + "|" + phase
                       + "|" + technicalFallback.FallbackDecisionCount;
        if (activeDecisionPending
            && string.Equals(
                pendingActiveDecisionKey,
                cacheKey,
                StringComparison.Ordinal))
        {
            return;
        }

        var capturedFingerprint = state.Fingerprint;
        var capturedSequence = state.Sequence;
        var capturedSessionId = state.BattleSessionId;
        var preparedState = engine
            .PrepareNormalizedOwnedStateForIsolatedWorker(state);
        var worker = learned ? trainedDecisionWorker : baselineDecisionWorker;
        var requestGeneration = decisionWorkGeneration;

        if (!string.Equals(decisionCacheKey, cacheKey, StringComparison.Ordinal))
        {
            ClearDecisionCache();
            decisionCacheKey = cacheKey;
        }
        activeDecisionPending = true;
        pendingActiveDecisionKey = cacheKey;
        try
        {
            pendingActiveDecisionRequestId = decisionLane.Submit(
                new CombatLiveDecisionRequest
                {
                    BattleSessionId = capturedSessionId,
                    Generation = requestGeneration,
                    ObservationRevision = capturedSequence,
                    StateFingerprint = capturedFingerprint,
                    Purpose = CombatLiveDecisionPurpose.Execution,
                    Authority = learned
                        ? CombatLiveDecisionAuthority.Model
                        : phase.StartsWith("emergency-", StringComparison.Ordinal)
                            ? CombatLiveDecisionAuthority.EmergencyBaseline
                            : CombatLiveDecisionAuthority.RuleBaseline,
                    Priority = CombatLiveDecisionPriority.Execution,
                    ModelId = learned ? trainedModelId : "baseline",
                    State = preparedState,
                    Profile = profile,
                    Worker = worker,
                    HardDeadlineMilliseconds = Math.Max(
                        2000,
                        Math.Min(10000, settings.DecisionTimeBudgetMs * 8))
                });
        }
        catch (Exception ex)
        {
            activeDecisionPending = false;
            pendingActiveDecisionKey = "";
            pendingActiveDecisionRequestId = 0L;
            nextDecisionAt = Time.unscaledTime + 0.1f;
            AuraToolsLog.Warn(
                "[AutoBattle] live decision task could not be submitted: "
                + ex.Message);
            lastModelDiagnostic =
                "实时决策通道提交失败（未计入模型健康）：" + ex.Message;
        }
    }

    private CombatDecisionEngine SelectExecutionEngine(
        CombatStateObservation state,
        out bool learned,
        out string phase)
    {
        var application = AuraToolsAutoBattleRuntime
            .IsModelApplicationMode(trainedModelMode);
        if (application && technicalFallback.TryConsumeEmergencyFallback())
        {
            learned = false;
            phase = "emergency-baseline";
            currentDecisionOwner = "emergency-baseline";
            return baselineDecisionEngine;
        }
        learned = application
                  && modelAvailable
                  && !modelLoadPending
                  && !technicalFallback.IsolatedForBattle
                  && !string.Equals(
                      trainedModelId,
                      "none",
                      StringComparison.Ordinal);
        phase = learned ? "learned-active" : "baseline-active";
        if (application && !learned)
        {
            phase = "emergency-baseline";
            currentDecisionOwner = "emergency-baseline";
        }
        return learned ? trainedDecisionEngine : baselineDecisionEngine;
    }

    private void TryExecuteCompletedDecision(CombatLiveDecisionReceipt result)
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
                result.StateFingerprint,
                state,
                result.Decision,
                out var decision,
                out var freshnessReason))
        {
            AuraToolsLog.Debug(
                "[AutoBattle][DecisionStale] discarded=" + freshnessReason
                + " captured=" + CompactFingerprint(result.StateFingerprint)
                + " current=" + CompactFingerprint(state.Fingerprint));
            nextDecisionAt = Time.unscaledTime + 0.05f;
            return;
        }

        if (!decision.HasAction || decision.Action == null)
        {
            if (RetryTransientNoAction(state, decision))
            {
                return;
            }
            AuraToolsAutoBattleGameValidationRuntime.RecordExecutionFailure(
                "模型在事务看门狗期限内始终没有返回可执行动作");
            if (result.Authority == CombatLiveDecisionAuthority.Model)
            {
                ReportTechnicalModelFailure(
                    "model-no-action",
                    "模型在动作看门狗期限内没有返回合法动作");
                ResetNoActionWatchdog();
                nextDecisionAt = Time.unscaledTime + 0.05f;
                return;
            }
            StopWithReason("紧急策略也没有获得可执行动作");
            return;
        }

        ResetNoActionWatchdog();

        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (result.Authority != CombatLiveDecisionAuthority.Model
            && string.Equals(settings.UnknownActionPolicy, "handoff", StringComparison.OrdinalIgnoreCase)
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
                    result.Authority,
                    shadow: false,
                    actionHoldSeconds: 0.45f);
            }
        }
        AuraToolsWitchSkillInteraction.Prepare(state, decision.Action);
        var execution = runtime.ExecutePrepared(decision.Action);
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

        AuraToolsAutoBattleJourneyRuntime.ObserveExecutedAction(
            automated: true);
        AuraToolsAutoBattleGameValidationRuntime.RecordDecision(state, decision);
        beforeAction = state;
        pendingDecision = decision;
        pendingDecisionReceiptId = result.RequestId;
        pendingDecisionAuthority = AuthorityLabel(result.Authority);
        pendingDecisionModelId = result.ModelId;
        pendingDecisionTiming = result.Timing;
        pendingDecisionOwner = result.Authority == CombatLiveDecisionAuthority.Model
            ? "model"
            : result.Authority == CombatLiveDecisionAuthority.EmergencyBaseline
                ? "emergency-baseline"
                : "baseline";
        pendingSampleRecorded = false;
        decisionIndex++;
        transaction.AwaitSettlement();
        settlementCaptureCount = 0;
        nextSettlementProbeAt = Time.unscaledTime
                                + Math.Max(
                                    0.05f,
                                    settings.DecisionIntervalMs / 1000f);
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
            + " evidence=" + decision.SearchEvidence.ToString("0.000")
            + " policyAmbiguity="
            + decision.PolicyAmbiguity.ToString("0.000")
            + " semanticRisk="
            + decision.SemanticCoverageRisk.ToString("0.000")
            + " outcomeUncertainty="
            + decision.OutcomeUncertainty.ToString("0.000")
            + " valueGap=" + decision.SearchValueGap.ToString("0.000")
            + " rootVisits=" + decision.SearchBestVisits
            + "/" + decision.SearchSecondBestVisits
            + " candidates=" + decision.SearchCandidateCount
            + "/" + decision.SearchOriginalCandidateCount
            + " workers=" + decision.InferenceWorkerCount
            + " agreement=" + decision.InferenceAgreement.ToString("0.00")
            + " path=" + decision.DecisionPath
            + " proposed=" + decision.SearchProposedCandidateId
            + " governance=" + decision.GovernanceDecision
            + " governanceReason=" + decision.GovernanceReason
            + " stop=" + decision.SearchStopReason
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
        ObserveHumanActionCommitted();
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
        decisionIndex++;
        teacherTransactionId++;
        teacherStartedAt = Time.unscaledTime;
        teacherSettlementCaptureCount = 0;
        nextTeacherSettlementProbeAt = teacherStartedAt
                                       + Math.Max(
                                           0.05f,
                                           AuraToolsConfigService
                                               .MatchExperience.AutoBattle
                                               .DecisionIntervalMs / 1000f);
        teacherBeforeAction = state;
        teacherDecision = null;
        teacherActualAction = actualAction;
        teacherRecommendedCandidateId = "";
        teacherPolicyVisibleToHuman = predictionPresenter?.IsShowing(
                                          state.Fingerprint,
                                          shadowPredictionCandidateId) == true;
        ClearPredictionMarkers();
        var profile = BuildProfile();
        var engine = SelectPredictionEngine(out var learned);
        if (learned)
        {
            ConfigureModelAuthority(profile);
        }
        var preparedState = engine.PrepareStateForIsolatedWorker(state);
        var worker = learned ? trainedDecisionWorker : baselineDecisionWorker;
        var generation = decisionWorkGeneration;
        var fingerprint = state.Fingerprint;
        try
        {
            pendingTeacherDecisionRequestId = decisionLane.Submit(
                new CombatLiveDecisionRequest
                {
                    BattleSessionId = state.BattleSessionId,
                    Generation = generation,
                    ObservationRevision = state.Sequence,
                    StateFingerprint = fingerprint,
                    Purpose = CombatLiveDecisionPurpose.Teacher,
                    Authority = learned
                        ? CombatLiveDecisionAuthority.Model
                        : CombatLiveDecisionAuthority.RuleBaseline,
                    Priority = CombatLiveDecisionPriority.Opportunistic,
                    ModelId = learned ? trainedModelId : "baseline",
                    State = preparedState,
                    Profile = profile,
                    Worker = worker,
                    HardDeadlineMilliseconds = Math.Max(
                        2000,
                        AuraToolsConfigService.MatchExperience.AutoBattle
                            .DecisionTimeBudgetMs * 8)
                });
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn(
                "[AutoBattle][Teacher] live recommendation submission failed: "
                + ex.Message);
            ClearTeacherAction();
        }
    }

    private void ApplyTeacherDecisionReceipt(
        CombatLiveDecisionReceipt result)
    {
        if (result.RequestId != pendingTeacherDecisionRequestId)
        {
            return;
        }
        pendingTeacherDecisionRequestId = 0L;
        if (result.Status != CombatLiveDecisionReceiptStatus.Completed
            || result.Generation != decisionWorkGeneration
            || teacherBeforeAction == null
            || teacherActualAction == null
            || !string.Equals(
                teacherBeforeAction.Fingerprint,
                result.StateFingerprint,
                StringComparison.Ordinal))
        {
            if (result.Status is not (
                    CombatLiveDecisionReceiptStatus.Cancelled
                    or CombatLiveDecisionReceiptStatus.Superseded))
            {
                AuraToolsLog.Warn(
                    "[AutoBattle][Teacher] recommendation ended: "
                    + result.Status + " " + result.Reason);
            }
            ClearTeacherAction();
            return;
        }
        ApplyTeacherDecision(
            teacherBeforeAction,
            teacherActualAction,
            result);
    }

    private void ApplyTeacherDecision(
        CombatStateObservation state,
        CombatActionObservation actualAction,
        CombatLiveDecisionReceipt result)
    {
        var recommendation = result.Decision;
        var actualEvaluation = recommendation.Candidates.FirstOrDefault(
            candidate => string.Equals(
                candidate.Action.CandidateId,
                actualAction.CandidateId,
                StringComparison.Ordinal));
        if (actualEvaluation == null
            || teacherBeforeAction == null
            || !string.Equals(
                teacherBeforeAction.Fingerprint,
                state.Fingerprint,
                StringComparison.Ordinal))
        {
            ClearTeacherAction();
            return;
        }

        RecordDecisionTiming(
            result.Timing.ComputeMilliseconds,
            result.Authority == CombatLiveDecisionAuthority.Model
                ? "teacher-learned-live-lane"
                : "teacher-baseline-live-lane");
        teacherRecommendedCandidateId =
            recommendation.Action?.CandidateId ?? "";
        teacherDecisionReceiptId = result.RequestId;
        teacherDecisionAuthority = AuthorityLabel(result.Authority);
        teacherDecisionModelId = result.ModelId;
        teacherDecisionTiming = result.Timing;
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
        if (Time.unscaledTime < nextTeacherSettlementProbeAt
            || WitchCombatInteractionRuntime.HasActivePrompt
            || !IsStablePlayerGate())
        {
            return;
        }
        teacherSettlementCaptureCount++;
        nextTeacherSettlementProbeAt = Time.unscaledTime
                                       + SettlementProbeInterval(
                                           teacherSettlementCaptureCount,
                                           settings.DecisionIntervalMs);
        if (!TryCapturePlayerState(out var after, out _)
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
        CombatStateObservation? after = null,
        string battleOutcomeOverride = "")
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
            policyVisibleToHuman: teacherPolicyVisibleToHuman,
            receiptId: teacherDecisionReceiptId,
            decisionPurpose: "teacher",
            decisionAuthority: teacherDecisionAuthority,
            decisionModelId: teacherDecisionModelId,
            observationRevision: teacherBeforeAction.Sequence,
            decisionTiming: teacherDecisionTiming,
            battleOutcomeOverride: battleOutcomeOverride));
    }

    private void ObserveSettlement()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (Time.unscaledTime < nextSettlementProbeAt
            || WitchCombatInteractionRuntime.HasActivePrompt
            || !IsStablePlayerGate())
        {
            return;
        }
        settlementCaptureCount++;
        nextSettlementProbeAt = Time.unscaledTime
                                + SettlementProbeInterval(
                                    settlementCaptureCount,
                                    settings.DecisionIntervalMs);
        if (!TryCapturePlayerState(out var after, out _))
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
            if (string.Equals(
                    pendingDecisionOwner,
                    "model",
                    StringComparison.Ordinal))
            {
                technicalFallback.ReportModelProgress();
                currentDecisionOwner = "model";
            }
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
            if (string.Equals(
                    pendingDecisionOwner,
                    "model",
                    StringComparison.Ordinal))
            {
                technicalFallback.ReportModelProgress();
                currentDecisionOwner = "model";
            }
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

        if (CombatActionExecutionPolicy.OpensFollowUpInteraction(
                pendingDecision.Action))
        {
            transaction.HandOff(
                "interactive action did not reach a terminal native prompt state before the transaction deadline");
            RecordPendingTrainingSample(
                CombatActionTransactionState.HandedOff.ToString(),
                transaction.TerminalReason,
                terminal: false,
                after);
            ClearPendingAction();
            transaction.Reset();
            DeactivateWithReason(
                "交互动作在超时前未完成，已交还玩家，未将其误判为无效果动作");
            return true;
        }

        SuppressNoEffectAction(
            after,
            "action transaction reached the no-effect timeout");
        return true;
    }

    private bool RetryTransientNoAction(
        CombatStateObservation state,
        CombatDecision decision)
    {
        var fingerprint = state?.Fingerprint ?? "";
        if (!string.Equals(
                noActionFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            noActionFingerprint = fingerprint;
            noActionSince = Time.unscaledTime;
        }
        if (noActionSince < 0f)
        {
            noActionSince = Time.unscaledTime;
        }

        var timeout = Math.Max(
            1f,
            AuraToolsConfigService.MatchExperience.AutoBattle
                .ActionTimeoutSeconds);
        if (Time.unscaledTime - noActionSince >= timeout)
        {
            return false;
        }

        AuraToolsLog.Debug(
            "[AutoBattle][TransientNoAction] retrying governance/search path="
            + decision.DecisionPath
            + " governance=" + decision.GovernanceDecision
            + " reason=" + decision.GovernanceReason
            + " elapsed="
            + (Time.unscaledTime - noActionSince).ToString("0.00")
            + "/" + timeout.ToString("0.00"));
        ClearDecisionCache();
        nextDecisionAt = Time.unscaledTime + 0.10f;
        return true;
    }

    private void ResetNoActionWatchdog()
    {
        noActionFingerprint = "";
        noActionSince = -1f;
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
        persistentNoEffectActionKeys.Add(
            FailedActionStateKey(suppressionState, failedAction));
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
            var key = FailedActionStateKey(state, action);
            if (!failedActionStateKeys.ContainsKey(key)
                && !persistentNoEffectActionKeys.Contains(key))
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
        CombatStateObservation? after = null,
        string battleOutcomeOverride = "")
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
        var sample = CombatTrainingSampleBuilder.Create(
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
            demonstrator: string.Equals(
                pendingDecisionOwner,
                "emergency-baseline",
                StringComparison.Ordinal)
                ? "emergency-baseline"
                : "policy",
            recommendedCandidateId: pendingDecision.Action.CandidateId,
            receiptId: pendingDecisionReceiptId,
            decisionPurpose: "execution",
            decisionAuthority: pendingDecisionAuthority,
            decisionModelId: pendingDecisionModelId,
            fallbackKind: string.Equals(
                pendingDecisionOwner,
                "emergency-baseline",
                StringComparison.Ordinal)
                ? technicalFallback.LastReason
                : "",
            observationRevision: beforeAction.Sequence,
            decisionTiming: pendingDecisionTiming,
            battleOutcomeOverride: battleOutcomeOverride);
        sample.Interaction = CombatInteractionBroker.ConsumeCompletedTrace(
            pendingDecision.Action.ActionToken);
        CombatAiRegistry.RecordTrainingSample(sample);
    }

    private void ClearPendingAction()
    {
        beforeAction = null;
        pendingDecision = null;
        pendingDecisionReceiptId = 0L;
        pendingDecisionAuthority = "rule-baseline";
        pendingDecisionModelId = "none";
        pendingDecisionTiming = null;
        pendingDecisionOwner = "none";
        pendingSampleRecorded = false;
        lastInteractionDiagnostic = "";
        settlementCaptureCount = 0;
        nextSettlementProbeAt = 0f;
        ClearPredictionMarkers();
    }

    private void ClearTeacherAction()
    {
        teacherBeforeAction = null;
        teacherDecision = null;
        teacherActualAction = null;
        teacherDecisionReceiptId = 0L;
        teacherDecisionAuthority = "rule-baseline";
        teacherDecisionModelId = "none";
        teacherDecisionTiming = null;
        teacherRecommendedCandidateId = "";
        teacherPolicyVisibleToHuman = false;
        teacherStartedAt = 0f;
        teacherSettlementCaptureCount = 0;
        nextTeacherSettlementProbeAt = 0f;
        pendingTeacherDecisionRequestId = 0L;
    }

    private void UpdateShadowPrediction()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var shadowEvaluation = string.Equals(
            trainedModelMode,
            "shadow",
            StringComparison.OrdinalIgnoreCase)
                               && modelAvailable;
        if (Active
            || !AuraToolsAutoBattleRuntime.ModuleEnabled
            || (!settings.ShowPredictionMarkers && !shadowEvaluation)
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

        QueuePredictionDecision(state);
    }

    private void QueuePredictionDecision(CombatStateObservation state)
    {
        if (predictionDecisionPending
            && string.Equals(
                pendingPredictionFingerprint,
                state.Fingerprint,
                StringComparison.Ordinal))
        {
            return;
        }

        var profile = BuildProfile();
        var engine = SelectPredictionEngine(out var learned);
        if (learned)
        {
            ConfigureModelAuthority(profile);
        }
        var fingerprint = state.Fingerprint;
        var capturedSequence = state.Sequence;
        var preparedState = engine
            .PrepareNormalizedOwnedStateForIsolatedWorker(state);
        var worker = learned ? trainedDecisionWorker : baselineDecisionWorker;
        var requestGeneration = decisionWorkGeneration;
        predictionDecisionPending = true;
        pendingPredictionFingerprint = fingerprint;
        try
        {
            pendingPredictionDecisionRequestId = decisionLane.Submit(
                new CombatLiveDecisionRequest
                {
                    BattleSessionId = state.BattleSessionId,
                    Generation = requestGeneration,
                    ObservationRevision = capturedSequence,
                    StateFingerprint = fingerprint,
                    Purpose = learned
                              && string.Equals(
                                  trainedModelMode,
                                  "shadow",
                                  StringComparison.OrdinalIgnoreCase)
                        ? CombatLiveDecisionPurpose.Shadow
                        : CombatLiveDecisionPurpose.Prediction,
                    Authority = learned
                        ? CombatLiveDecisionAuthority.Model
                        : CombatLiveDecisionAuthority.RuleBaseline,
                    Priority = CombatLiveDecisionPriority.Opportunistic,
                    ModelId = learned ? trainedModelId : "baseline",
                    State = preparedState,
                    Profile = profile,
                    Worker = worker,
                    HardDeadlineMilliseconds = Math.Max(
                        2000,
                        AuraToolsConfigService.MatchExperience.AutoBattle
                            .DecisionTimeBudgetMs * 8)
                });
        }
        catch (Exception ex)
        {
            predictionDecisionPending = false;
            pendingPredictionFingerprint = "";
            pendingPredictionDecisionRequestId = 0L;
            nextPredictionAt = Time.unscaledTime + 0.1f;
            AuraToolsLog.Warn(
                "[AutoBattle] prediction submission failed: " + ex.Message);
        }
    }

    private void ApplyPredictionDecisionReceipt(
        CombatLiveDecisionReceipt result)
    {
        if (result.RequestId != pendingPredictionDecisionRequestId)
        {
            return;
        }
        predictionDecisionPending = false;
        pendingPredictionFingerprint = "";
        pendingPredictionDecisionRequestId = 0L;
        if (result.Generation != decisionWorkGeneration
            || Active
            || !AuraToolsAutoBattleRuntime.ModuleEnabled)
        {
            return;
        }
        if (result.Status != CombatLiveDecisionReceiptStatus.Completed)
        {
            if (result.Status is not (
                    CombatLiveDecisionReceiptStatus.Cancelled
                    or CombatLiveDecisionReceiptStatus.Superseded))
            {
                AuraToolsLog.Warn(
                    "[AutoBattle] prediction ended: " + result.Status
                    + " " + result.Reason);
                if (result.Authority == CombatLiveDecisionAuthority.Model)
                {
                    HandleLearnedInferenceFailure(
                        "prediction",
                        new InvalidOperationException(result.Reason));
                }
            }
            return;
        }

        RecordDecisionTiming(
            result.Timing.ComputeMilliseconds,
            result.Authority == CombatLiveDecisionAuthority.Model
                ? "prediction-learned-live-lane"
                : "prediction-baseline-live-lane");
        if (!TryCapturePlayerState(out var currentState, out _)
            || !string.Equals(
                currentState.Fingerprint,
                result.StateFingerprint,
                StringComparison.Ordinal)
            || !CombatDecisionExecutionBindingProtocol.TryBindToObservation(
                result.Decision,
                currentState,
                out var bound,
                out _))
        {
            ClearPredictionMarkers();
            nextPredictionAt = 0f;
            return;
        }
        currentDecisionOwner = result.Authority
                               == CombatLiveDecisionAuthority.Model
            ? string.Equals(
                trainedModelMode,
                "shadow",
                StringComparison.OrdinalIgnoreCase)
                ? "model-shadow"
                : "model"
            : "baseline";
        UpdateButtonLabel();
        if (result.Purpose == CombatLiveDecisionPurpose.Shadow)
        {
            AuraToolsLog.Info(
                "[AutoBattle][ModelShadow] stateId="
                + CompactFingerprint(result.StateFingerprint)
                + " model=" + result.ModelId
                + " proposed=" + DecisionLabel(bound)
                + " " + ScoreBreakdown(bound));
        }
        PresentPrediction(
            currentState,
            bound,
            result.Authority,
            result.Purpose == CombatLiveDecisionPurpose.Shadow);
    }

    private CombatDecisionEngine SelectPredictionEngine(out bool learned)
    {
        learned = (AuraToolsAutoBattleRuntime
                       .IsModelApplicationMode(trainedModelMode)
                   || string.Equals(
                       trainedModelMode,
                       "shadow",
                       StringComparison.OrdinalIgnoreCase))
                  && modelAvailable
                  && !modelLoadPending
                  && !technicalFallback.IsolatedForBattle
                  && !string.Equals(
                      trainedModelId,
                      "none",
                      StringComparison.Ordinal);
        return learned ? trainedDecisionEngine : baselineDecisionEngine;
    }

    private void PresentPrediction(
        CombatStateObservation state,
        CombatDecision decision,
        CombatLiveDecisionAuthority authority,
        bool shadow)
    {
        if (Active
            || !AuraToolsAutoBattleRuntime.ModuleEnabled
            || !AuraToolsConfigService.MatchExperience.AutoBattle
                .ShowPredictionMarkers)
        {
            return;
        }

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
                target,
                authority,
                shadow) != true)
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

    private static bool IsStablePlayerGate()
    {
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        return fightUi != null
               && WitchCombatRuntime.IsPlayerActionWindow(fightUi)
               && !WitchCombatRuntime.IsUiBusy(fightUi);
    }

    private static float SettlementProbeInterval(
        int captureCount,
        int decisionIntervalMilliseconds)
    {
        var configured = Math.Max(
            0.10f,
            decisionIntervalMilliseconds / 1000f);
        if (captureCount <= 3)
        {
            return configured;
        }
        if (captureCount <= 8)
        {
            return Math.Max(configured, 0.25f);
        }
        return Math.Max(configured, 0.50f);
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
            trainedModelMode = "full";
            baselineDecisionEngine = new CombatDecisionEngine();
            trainedDecisionEngine = new CombatDecisionEngine(
                validationResidual,
                validationGuidance,
                policyValueModel: validationPolicyValue);
            baselineDecisionWorker = new CombatDecisionEngineWorker(
                baselineDecisionEngine);
            trainedDecisionWorker = new CombatDecisionEngineWorker(
                trainedDecisionEngine);
            trainedModelId = validationModelId;
            modelAvailable = !string.Equals(
                validationPolicyValue.ModelId,
                "none",
                StringComparison.Ordinal);
            modelLoadPending = false;
            modelLoadFailureReason = modelAvailable
                ? ""
                : "游戏主体验证模型缺少策略价值网络";
            if (modelAvailable)
            {
                technicalFallback.ModelRecovered();
            }
            ClearDecisionCache();
            lastModelDiagnostic = "游戏主体验证专用加载=" + validationModelId;
            AuraToolsLog.Info("[AutoBattle] " + lastModelDiagnostic);
            return;
        }
        AuraToolsAutoBattleGameParameterRuntime.ResolvePresetReferences(
            settings);
        var configuredMode = AuraToolsAutoBattleRuntime.ModuleEnabled
            ? settings.TrainedModelMode
            : "off";
        var profile = settings.Profile;
        var selectedModelId = settings.SelectedModelId ?? "";
        var riskFallbackReason = "";
        if (AuraToolsAutoBattleRuntime.IsModelApplicationMode(configuredMode)
            && !AuraToolsAutoBattleModelRuntime
                .FoundationActiveUseRiskAcknowledged(
                    profile,
                    selectedModelId,
                    out var riskReason))
        {
            configuredMode = "shadow";
            riskFallbackReason = "尚未确认模型使用风险，暂时只进行观察："
                                 + riskReason;
        }
        var configurationKey = configuredMode
                               + "\n"
                               + profile
                               + "\n"
                               + selectedModelId
                               + "\n"
                               + string.Join(",", settings.ModelRiskAcknowledgements);
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
        baselineDecisionWorker = new CombatDecisionEngineWorker(
            baselineDecisionEngine);
        trainedDecisionWorker = new CombatDecisionEngineWorker(
            trainedDecisionEngine);
        trainedModelId = "none";
        modelAvailable = false;
        modelLoadPending = false;
        modelLoadFailureReason = "";
        ClearDecisionCache();
        if (string.Equals(
                configuredMode,
                "off",
                StringComparison.OrdinalIgnoreCase))
        {
            trainedModelMode = "off";
            AuraToolsAutoBattleModelRuntime.UnloadResidentModels();
            lastModelDiagnostic = "模型应用已关闭，驻留权重已卸载";
            currentDecisionOwner = "baseline";
            AuraToolsLog.Info("[AutoBattle] " + lastModelDiagnostic);
            return;
        }
        trainedModelMode = configuredMode;
        modelLoadPending = true;
        lastModelDiagnostic = string.IsNullOrWhiteSpace(riskFallbackReason)
            ? "模型正在后台加载"
            : riskFallbackReason + "；模型正在后台加载";
        var queued = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<AutoBattleResidentModelSet>
            {
                OwnerId = AuraToolsIds.ModId + ".AutoBattle",
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
                    modelLoadPending = false;
                    modelAvailable = false;
                    modelLoadFailureReason = ex.Message;
                    if (AuraToolsAutoBattleRuntime
                        .IsModelApplicationMode(configuredMode))
                    {
                        technicalFallback.ReportFailure(
                            "model-load-failed",
                            ex.Message,
                            isolateImmediately: true);
                        currentDecisionOwner = "emergency-baseline";
                    }
                    AuraToolsLog.Warn("[AutoBattle] " + lastModelDiagnostic);
                }
            });
        if (!queued)
        {
            lastModelDiagnostic = "模型后台加载任务未能提交";
            modelLoadPending = false;
            modelAvailable = false;
            modelLoadFailureReason = lastModelDiagnostic;
            if (AuraToolsAutoBattleRuntime
                .IsModelApplicationMode(configuredMode))
            {
                technicalFallback.ReportFailure(
                    "model-load-failed",
                    lastModelDiagnostic,
                    isolateImmediately: true);
                currentDecisionOwner = "emergency-baseline";
            }
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
        trainedDecisionWorker = new CombatDecisionEngineWorker(
            trainedDecisionEngine);
        trainedModelId = loaded.ModelId;
        modelLoadPending = false;
        modelAvailable = loaded.PolicyValueLoaded;
        modelLoadFailureReason = modelAvailable
            ? ""
            : "所选模型没有成功加载策略价值网络";
        var diagnostic = loaded.Diagnostic;
        if (modelAvailable)
        {
            technicalFallback.ModelRecovered();
            currentDecisionOwner = AuraToolsAutoBattleRuntime
                .IsModelApplicationMode(configuredMode)
                ? "model"
                : "baseline";
        }
        else if (AuraToolsAutoBattleRuntime
                 .IsModelApplicationMode(configuredMode))
        {
            technicalFallback.ReportFailure(
                "model-load-failed",
                modelLoadFailureReason,
                isolateImmediately: true);
            currentDecisionOwner = "emergency-baseline";
            diagnostic += "；已进入技术兜底：" + modelLoadFailureReason;
        }
        ClearDecisionCache();
        if (!string.Equals(lastModelDiagnostic, diagnostic, StringComparison.Ordinal))
        {
            lastModelDiagnostic = diagnostic;
            AuraToolsLog.Info("[AutoBattle] " + diagnostic);
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

    private void ClearDecisionCache()
    {
        decisionCacheKey = "";
    }

    private void InvalidateDecisionWork()
    {
        var battleSessionId = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        if (decisionLane != null && battleSessionId > 0)
        {
            decisionLane.CancelSession(
                battleSessionId,
                "live-session-invalidated");
        }
        decisionWorkGeneration++;
        activeDecisionPending = false;
        pendingActiveDecisionKey = "";
        pendingActiveDecisionRequestId = 0L;
        predictionDecisionPending = false;
        pendingPredictionFingerprint = "";
        pendingPredictionDecisionRequestId = 0L;
        pendingTeacherDecisionRequestId = 0L;
    }

    private void HandleLearnedInferenceFailure(
        string source,
        Exception exception)
    {
        if (AuraToolsAutoBattleRuntime
            .IsModelApplicationMode(trainedModelMode))
        {
            ReportTechnicalModelFailure(
                "inference-exception",
                exception.Message);
            return;
        }

        modelAvailable = false;
        modelLoadFailureReason = exception.Message;
        lastModelDiagnostic = "影子模型推理异常：" + exception.Message;
        AuraToolsLog.Warn(
            "[AutoBattle][ModelFailure] source="
            + source
            + "；"
            + lastModelDiagnostic);
    }

    private void ReportTechnicalModelFailure(string kind, string detail)
    {
        technicalFallback.ReportFailure(kind, detail);
        currentDecisionOwner = "emergency-baseline";
        lastModelDiagnostic = "模型技术故障，已启用可用性兜底："
                              + technicalFallback.LastReason;
        ClearDecisionCache();
        AuraToolsLog.Warn(
            "[AutoBattle][TechnicalFallback] reason="
            + technicalFallback.LastReason
            + " consecutive=" + technicalFallback.ConsecutiveFailures
            + " isolated=" + technicalFallback.IsolatedForBattle);
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

    private static void ConfigureModelAuthority(
        CombatDecisionProfile profile)
    {
        profile.ModelOwnsActionSelection = true;
        profile.UseLowConfidenceFallback = false;
        profile.PreferDominantFreeSetup = false;
    }

    private static string AuthorityLabel(
        CombatLiveDecisionAuthority authority)
    {
        return authority switch
        {
            CombatLiveDecisionAuthority.Model => "model",
            CombatLiveDecisionAuthority.EmergencyBaseline =>
                "emergency-baseline",
            _ => "rule-baseline"
        };
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
        if (Active)
        {
            return currentDecisionOwner switch
            {
                "model" => "自动战斗：模型",
                "emergency-baseline" => "自动战斗：兜底",
                _ => "自动战斗：规则"
            };
        }
        return string.Equals(
                   trainedModelMode,
                   "shadow",
                   StringComparison.OrdinalIgnoreCase)
               && modelAvailable
            ? "影子模型：观察"
            : "自动战斗：关";
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

    private void OnDestroy()
    {
        try
        {
            decisionLane?.Dispose();
            baselineDecisionWorker?.ReleaseRetainedMemory();
            trainedDecisionWorker?.ReleaseRetainedMemory();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn(
                "[AutoBattle][LiveDecision] lane shutdown failed: "
                + ex.Message);
        }
    }
}

internal sealed class AuraToolsAutoBattleTrainingSink :
    ICombatTrainingSampleSink,
    IDisposable
{
    private const long MaximumBufferedBytes = 32L * 1024L * 1024L;
    private const long ControlReserveBytes = 64L * 1024L;
    private const long MaximumPayloadBytes =
        MaximumBufferedBytes - ControlReserveBytes;
    private const long BattleFinalizationBytes = 512L;
    private const long MaximumSessionBytes = 8L * 1024L * 1024L;
    private const int MaximumSessionFrames = 2048;
    private static readonly object StorageGate = new();
    private static int storageGeneration;
    private readonly object queueGate = new();
    private readonly object metadataGate = new();
    private readonly Dictionary<long, TrainingMetadata> metadataBySession =
        new();
    private readonly BlockingCollection<QueuedTrainingSample> queue = new();
    private Thread writerThread = null!;
    private long bufferedBytes;
    private long droppedSamples;
    private int writerState = (int)TrainingWriterState.Running;
    private string writerDiagnostic = "";

    public bool Faulted => (TrainingWriterState)Volatile.Read(
        ref writerState) == TrainingWriterState.Faulted;

    public string Diagnostic
    {
        get
        {
            lock (queueGate)
            {
                return writerDiagnostic;
            }
        }
    }

    public AuraToolsAutoBattleTrainingSink()
    {
        writerThread = StartWriterThread();
        Application.quitting += Shutdown;
    }

    public void Record(CombatTrainingSample sample)
    {
        if (!AuraToolsConfigService.MatchExperience.AutoBattle.CaptureTrainingSamples)
        {
            return;
        }

        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var metadata = ResolveTrainingMetadata(
            sample.BattleSessionId,
            settings);
        sample.ContentSetHash = metadata.ContentSetHash;
        sample.OwnerModSetHash = metadata.OwnerModSetHash;
        sample.BaseModelId = metadata.BaseModelId;
        sample.ActiveAdapterIds = metadata.ActiveAdapterIds;
        foreach (var candidate in sample.Candidates
                     ?? new List<CombatTrainingCandidate>())
        {
            candidate.OwnerModId =
                AuraToolsCombatContentRuntime.ResolveOwnerModId(
                    candidate.SourceId);
        }

        var estimatedBytes = EstimateRetainedBytes(sample);
        var queued = false;
        lock (queueGate)
        {
            TryRecoverWriterNoLock();
            if ((TrainingWriterState)writerState == TrainingWriterState.Running
                && !queue.IsAddingCompleted
                && estimatedBytes <= MaximumPayloadBytes
                && bufferedBytes + estimatedBytes <= MaximumPayloadBytes)
            {
                bufferedBytes += estimatedBytes;
                queued = queue.TryAdd(new QueuedTrainingSample
                {
                    Generation = Volatile.Read(ref storageGeneration),
                    Sample = sample,
                    EstimatedBytes = estimatedBytes
                });
                if (!queued)
                {
                    bufferedBytes = Math.Max(
                        0L,
                        bufferedBytes - estimatedBytes);
                }
            }
            if (!queued)
            {
                droppedSamples++;
            }
        }
        if (!queued)
        {
            AuraToolsLog.Warn(
                Faulted
                    ? "[AutoBattle] training writer is faulted; sample rejected"
                    : "[AutoBattle] training sample byte budget is full; sample dropped");
            return;
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

    public bool RecordJourney(CombatJourneyTrainingEpisode episode)
    {
        if (episode == null
            || !AuraToolsConfigService.MatchExperience.AutoBattle
                .CaptureTrainingSamples)
        {
            return false;
        }
        var estimatedBytes = EstimateRetainedBytes(episode);
        lock (queueGate)
        {
            TryRecoverWriterNoLock();
            if ((TrainingWriterState)writerState != TrainingWriterState.Running
                || queue.IsAddingCompleted
                || estimatedBytes > MaximumPayloadBytes
                || bufferedBytes + estimatedBytes > MaximumPayloadBytes)
            {
                droppedSamples++;
                return false;
            }
            bufferedBytes += estimatedBytes;
            if (queue.TryAdd(new QueuedTrainingSample
                {
                    Generation = Volatile.Read(ref storageGeneration),
                    Journey = episode,
                    EstimatedBytes = estimatedBytes
                }))
            {
                return true;
            }
            bufferedBytes = Math.Max(0L, bufferedBytes - estimatedBytes);
            droppedSamples++;
            return false;
        }
    }

    public bool FinalizeBattle(
        long battleSessionId,
        string outcome,
        string reason)
    {
        if (battleSessionId <= 0)
        {
            return false;
        }
        lock (metadataGate)
        {
            metadataBySession.Remove(battleSessionId);
        }
        lock (queueGate)
        {
            TryRecoverWriterNoLock();
            if ((TrainingWriterState)writerState != TrainingWriterState.Running
                || queue.IsAddingCompleted
                || bufferedBytes + BattleFinalizationBytes
                   > MaximumBufferedBytes)
            {
                droppedSamples++;
                return false;
            }
            bufferedBytes += BattleFinalizationBytes;
            if (queue.TryAdd(new QueuedTrainingSample
                {
                    Generation = Volatile.Read(ref storageGeneration),
                    Finalization = new BattleFinalization
                    {
                        BattleSessionId = battleSessionId,
                        Outcome = outcome?.Trim().ToLowerInvariant() ?? "",
                        Reason = reason?.Trim() ?? ""
                    },
                    EstimatedBytes = BattleFinalizationBytes
                }))
            {
                return true;
            }
            bufferedBytes = Math.Max(
                0L,
                bufferedBytes - BattleFinalizationBytes);
            droppedSamples++;
            return false;
        }
    }

    private TrainingMetadata ResolveTrainingMetadata(
        long battleSessionId,
        AutoBattleSettings settings)
    {
        AuraToolsCombatContentRuntime.SnapshotContentIdentity(
            out var contentSetHash,
            out var ownerModSetHash,
            out var contentRevision);
        var profile = settings.Profile ?? "balanced";
        var baseModelId = settings.SelectedModelId ?? "";
        lock (metadataGate)
        {
            if (battleSessionId > 0
                && metadataBySession.TryGetValue(
                    battleSessionId,
                    out var cached)
                && cached.ContentRevision == contentRevision
                && string.Equals(
                    cached.Profile,
                    profile,
                    StringComparison.Ordinal)
                && string.Equals(
                    cached.BaseModelId,
                    baseModelId,
                    StringComparison.Ordinal))
            {
                return cached;
            }
            var resolved = new TrainingMetadata
            {
                ContentRevision = contentRevision,
                ContentSetHash = contentSetHash,
                OwnerModSetHash = ownerModSetHash,
                Profile = profile,
                BaseModelId = baseModelId,
                ActiveAdapterIds = AuraToolsAutoBattleModelRuntime
                    .SnapshotActiveAdapterIds(profile, baseModelId)
                    .ToList()
            };
            if (battleSessionId > 0)
            {
                metadataBySession[battleSessionId] = resolved;
                if (metadataBySession.Count > 64)
                {
                    metadataBySession.Remove(metadataBySession.Keys.First());
                }
            }
            return resolved;
        }
    }

    public void Dispose()
    {
        Shutdown();
    }

    private void WriteLoop()
    {
        var sessionsByContentSet = new Dictionary<
            string,
            Dictionary<long, LiveSessionBuffer>>(
            StringComparer.Ordinal);
        try
        {
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
                    var currentGeneration = Volatile.Read(
                        ref storageGeneration);
                    if (sessionGeneration != currentGeneration)
                    {
                        ReleaseAllSessionBytes(sessionsByContentSet);
                        sessionsByContentSet.Clear();
                        sessionGeneration = currentGeneration;
                    }
                    var staleItems = batch
                        .Where(item => item.Generation != currentGeneration)
                        .ToList();
                    ReleaseBufferedBytes(staleItems);
                    var currentBatch = batch
                        .Where(item => item.Generation == currentGeneration)
                        .ToList();
                    foreach (var journeyBatch in currentBatch
                                 .Where(item => item.Journey != null)
                                 .GroupBy(
                                     item => string.IsNullOrWhiteSpace(
                                         item.Journey!.ContentSetHash)
                                         ? CombatContentSetProtocol
                                             .EmptyContentSetHash
                                         : item.Journey.ContentSetHash,
                                     StringComparer.Ordinal))
                    {
                        var path = Path.Combine(
                            AuraToolsCombatContentRuntime
                                .LiveDatasetDirectory(journeyBatch.Key),
                            "journey-episodes-v1.jsonl");
                        using var journeyWriter = new StreamWriter(
                            path,
                            append: true);
                        foreach (var item in journeyBatch)
                        {
                            journeyWriter.WriteLine(
                                AuraSharedJson.SerializeCompact(
                                    item.Journey));
                            ReleaseBufferedBytes(item.EstimatedBytes);
                        }
                    }
                    foreach (var contentBatch in currentBatch
                                 .Where(item => item.Sample != null)
                                 .GroupBy(
                                     item => string.IsNullOrWhiteSpace(
                                         item.Sample!.ContentSetHash)
                                         ? CombatContentSetProtocol
                                             .EmptyContentSetHash
                                         : item.Sample.ContentSetHash,
                                     StringComparer.Ordinal))
                    {
                        var directory = AuraToolsCombatContentRuntime
                            .LiveDatasetDirectory(contentBatch.Key);
                        var path = Path.Combine(
                            directory,
                            "auto-battle-training-v9.jsonl");
                        var episodesPath = Path.Combine(
                            directory,
                            "live-combat-episodes-v5.jsonl");
                        if (!sessionsByContentSet.TryGetValue(
                                contentBatch.Key,
                                out var sessions))
                        {
                            sessions = new Dictionary<long, LiveSessionBuffer>();
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
                            RetainLiveSample(
                                item.Sample!,
                                item.EstimatedBytes,
                                sessions,
                                episodeWriter);
                        }
                    }
                    foreach (var item in currentBatch.Where(item =>
                                 item.Finalization != null))
                    {
                        FinalizeLiveSession(
                            item.Finalization!,
                            sessionsByContentSet);
                        ReleaseBufferedBytes(item.EstimatedBytes);
                    }
                    foreach (var item in currentBatch.Where(item =>
                                 item.Sample == null
                                 && item.Journey == null
                                 && item.Finalization == null))
                    {
                        ReleaseBufferedBytes(item.EstimatedBytes);
                    }
                }
                lock (queueGate)
                {
                    if ((TrainingWriterState)writerState
                        == TrainingWriterState.Running)
                    {
                        writerDiagnostic = "";
                    }
                }
            }
            if (sessionsByContentSet.Count > 0)
            {
                var abandonedBytes = ReleaseAllSessionBytes(
                    sessionsByContentSet);
                sessionsByContentSet.Clear();
                if (abandonedBytes > 0)
                {
                    AuraToolsLog.Warn(
                        "[AutoBattle][Training] writer stopped with unfinished live sessions; releasedBytes="
                        + abandonedBytes);
                }
            }
            Volatile.Write(
                ref writerState,
                (int)TrainingWriterState.Stopped);
        }
        catch (Exception ex)
        {
            var abandoned = 0L;
            foreach (var content in sessionsByContentSet.Values)
            {
                abandoned += content.Values.Sum(session =>
                    Math.Max(0L, session.EstimatedBytes));
            }
            sessionsByContentSet.Clear();
            lock (queueGate)
            {
                writerDiagnostic = ex.GetType().Name + ": " + ex.Message;
                writerState = (int)TrainingWriterState.Faulted;
                bufferedBytes = 0L;
            }
            while (queue.TryTake(out _))
            {
                Interlocked.Increment(ref droppedSamples);
            }
            AuraToolsLog.Warn(
                "[AutoBattle] training sample writer faulted; buffered work was released and the next record attempt may restart it: "
                + ex.Message
                + "; abandonedSessionBytes="
                + abandoned);
        }
    }

    private Thread StartWriterThread()
    {
        var started = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "AuraTools.AutoBattleTrainingWriter"
        };
        started.Start();
        return started;
    }

    private bool TryRecoverWriterNoLock()
    {
        if ((TrainingWriterState)writerState != TrainingWriterState.Faulted)
        {
            return (TrainingWriterState)writerState
                   == TrainingWriterState.Running;
        }
        if (queue.IsAddingCompleted || writerThread.IsAlive)
        {
            return false;
        }
        writerDiagnostic = "正在重启训练样本 writer；上一故障："
                           + writerDiagnostic;
        writerState = (int)TrainingWriterState.Running;
        writerThread = StartWriterThread();
        AuraToolsLog.Info(
            "[AutoBattle][Training] writer restarted after a visible fault");
        return true;
    }

    private void RetainLiveSample(
        CombatTrainingSample sample,
        long sampleBytes,
        IDictionary<long, LiveSessionBuffer> sessions,
        TextWriter episodeWriter)
    {
        if (sample == null || sample.BattleSessionId <= 0)
        {
            ReleaseBufferedBytes(sampleBytes);
            return;
        }
        if (!sessions.TryGetValue(sample.BattleSessionId, out var session))
        {
            session = new LiveSessionBuffer();
            sessions[sample.BattleSessionId] = session;
        }
        if (session.Samples.Count >= MaximumSessionFrames
            || session.EstimatedBytes + sampleBytes > MaximumSessionBytes)
        {
            sessions.Remove(sample.BattleSessionId);
            ReleaseBufferedBytes(session.EstimatedBytes + sampleBytes);
            AuraToolsLog.Warn(
                "[AutoBattle][Training] live session exceeded its byte/frame budget and was abandoned: battleSession="
                + sample.BattleSessionId);
            return;
        }
        session.Samples.Add(sample);
        session.EstimatedBytes += sampleBytes;
        if (!sample.Terminal)
        {
            ReleaseBufferedBytes(PruneSessions(sessions));
            return;
        }
        CompleteLiveEpisode(
            sample.BattleSessionId,
            sample.BattleOutcome,
            sample.TerminalReason,
            session,
            episodeWriter);
        sessions.Remove(sample.BattleSessionId);
        ReleaseBufferedBytes(session.EstimatedBytes);
    }

    private void FinalizeLiveSession(
        BattleFinalization finalization,
        IDictionary<string, Dictionary<long, LiveSessionBuffer>>
            sessionsByContentSet)
    {
        foreach (var content in sessionsByContentSet.ToArray())
        {
            if (!content.Value.TryGetValue(
                    finalization.BattleSessionId,
                    out var session))
            {
                continue;
            }
            if (string.Equals(
                    finalization.Outcome,
                    "victory",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    finalization.Outcome,
                    "defeat",
                    StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(
                    AuraToolsCombatContentRuntime
                        .LiveDatasetDirectory(content.Key),
                    "live-combat-episodes-v5.jsonl");
                using var episodeWriter = new StreamWriter(path, append: true);
                CompleteLiveEpisode(
                    finalization.BattleSessionId,
                    finalization.Outcome,
                    finalization.Reason,
                    session,
                    episodeWriter);
            }
            content.Value.Remove(finalization.BattleSessionId);
            ReleaseBufferedBytes(session.EstimatedBytes);
            if (content.Value.Count == 0)
            {
                sessionsByContentSet.Remove(content.Key);
            }
            return;
        }
    }

    private static void CompleteLiveEpisode(
        long battleSessionId,
        string outcome,
        string reason,
        LiveSessionBuffer session,
        TextWriter episodeWriter)
    {
        if (!string.Equals(outcome, "victory", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(outcome, "defeat", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var terminal = session.Samples
            .Where(sample => string.Equals(
                sample.CompletionState,
                CombatActionTransactionState.Completed.ToString(),
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(sample => sample.DecisionIndex)
            .ThenByDescending(sample => sample.Sequence)
            .ThenByDescending(sample => sample.CreatedUtc)
            .FirstOrDefault();
        if (terminal == null)
        {
            return;
        }
        terminal.Terminal = true;
        terminal.BattleOutcome = outcome.Trim().ToLowerInvariant();
        terminal.TerminalReason = string.IsNullOrWhiteSpace(reason)
            ? "battle finalized"
            : reason.Trim();
        terminal.RewardComponents ??= new CombatTrainingReward();
        var terminalBonus = string.Equals(
            outcome,
            "victory",
            StringComparison.OrdinalIgnoreCase)
            ? 50d
            : -50d;
        terminal.Reward += terminalBonus
                           - terminal.RewardComponents.TerminalBonus;
        terminal.RewardComponents.TerminalBonus = terminalBonus;
        if (!CombatLiveEpisodeAssembler.TryAssemble(
                battleSessionId,
                session.Samples,
                out var episode))
        {
            AuraToolsLog.Warn(
                "[AutoBattle][Training] terminal live session could not be assembled: battleSession="
                + battleSessionId);
            return;
        }

        episodeWriter.WriteLine(AuraSharedJson.SerializeCompact(episode));
        AuraToolsLog.Info(
            "[AutoBattle][Training] 已聚合完整实战轨迹：battleSession="
            + battleSessionId
            + "，outcome="
            + episode.Outcome
            + "，frames="
            + episode.Frames.Count);
    }

    private long ReleaseAllSessionBytes(
        IDictionary<string, Dictionary<long, LiveSessionBuffer>>
            sessionsByContentSet)
    {
        var released = sessionsByContentSet.Values
            .SelectMany(sessions => sessions.Values)
            .Sum(session => Math.Max(0L, session.EstimatedBytes));
        ReleaseBufferedBytes(released);
        return released;
    }

    public static void ClearPersistedData()
    {
        lock (StorageGate)
        {
            Interlocked.Increment(ref storageGeneration);
            foreach (var fileName in new[]
                      {
                          "auto-battle-training-v7.jsonl",
                          "auto-battle-training-v9.jsonl",
                          "live-combat-episodes-v5.jsonl",
                          "journey-episodes-v1.jsonl"
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
                   "auto-battle-training-v9.jsonl",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   fileName,
                   "auto-battle-training-v7.jsonl",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   fileName,
                   "live-combat-episodes-v5.jsonl",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   fileName,
                   "journey-episodes-v1.jsonl",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static long PruneSessions(
        IDictionary<long, LiveSessionBuffer> sessions)
    {
        const int maximumBufferedSessions = 64;
        if (sessions.Count <= maximumBufferedSessions)
        {
            return 0L;
        }
        var oldest = sessions
            .OrderBy(pair => pair.Value.Samples.Count == 0
                ? DateTime.MinValue
                : pair.Value.Samples.Min(sample => sample.CreatedUtc))
            .First();
        sessions.Remove(oldest.Key);
        return Math.Max(0L, oldest.Value.EstimatedBytes);
    }

    private void Shutdown()
    {
        Application.quitting -= Shutdown;
        lock (queueGate)
        {
            if ((TrainingWriterState)writerState == TrainingWriterState.Running)
            {
                writerState = (int)TrainingWriterState.Draining;
            }
        }
        queue.CompleteAdding();
        if (!writerThread.Join(5000))
        {
            AuraToolsLog.Warn(
                "[AutoBattle] training writer did not prove queue drain before shutdown; bufferedBytes="
                + Volatile.Read(ref bufferedBytes));
        }
    }

    private void ReleaseBufferedBytes(
        IEnumerable<QueuedTrainingSample> samples)
    {
        long released = 0L;
        foreach (var sample in samples)
        {
            released += Math.Max(0L, sample.EstimatedBytes);
        }
        ReleaseBufferedBytes(released);
    }

    private void ReleaseBufferedBytes(long released)
    {
        lock (queueGate)
        {
            bufferedBytes = Math.Max(0L, bufferedBytes - Math.Max(0L, released));
        }
    }

    private static long EstimateRetainedBytes(CombatTrainingSample sample)
    {
        if (sample == null) return 0L;
        long bytes = 4096L
                     + (sample.StateFeatures?.Count ?? 0) * 96L
                     + (sample.Features?.Count ?? 0) * 96L
                     + (sample.Plan?.Count ?? 0) * 192L;
        bytes += EstimateStrings(
            sample.ModelProtocol,
            sample.GameBuild,
            sample.SharedBuild,
            sample.OwnerModSetHash,
            sample.ContentSetHash,
            sample.BaseModelId,
            sample.StateFingerprint,
            sample.NextStateFingerprint,
            sample.DecisionProfile,
            sample.PlanSummary,
            sample.SearchAlgorithm,
            sample.SearchBudgetTier,
            sample.BattleOutcome,
            sample.CompletionState,
            sample.TerminalReason);
        foreach (var adapterId in sample.ActiveAdapterIds
                     ?? new List<string>())
        {
            bytes += EstimateString(adapterId);
        }
        var selection = sample.Selection;
        if (selection != null)
        {
            bytes += EstimateStrings(
                selection.Protocol,
                selection.DecisionPurpose,
                selection.DecisionAuthority,
                selection.DecisionModelId,
                selection.FallbackKind,
                selection.ExecutedBy,
                selection.LabelKind,
                selection.ExecutedCandidateId,
                selection.ExecutedDisplayName,
                selection.PolicyPreselectedCandidateId,
                selection.PolicyPreselectedDisplayName);
        }
        foreach (var step in sample.Plan ?? new List<CombatPlanStep>())
        {
            if (step == null) continue;
            bytes += EstimateStrings(
                step.CandidateId,
                step.SourceId,
                step.DisplayName);
        }
        foreach (var candidate in sample.Candidates
                     ?? new List<CombatTrainingCandidate>())
        {
            if (candidate == null) continue;
            bytes += 1024L
                     + (candidate.Features?.Count ?? 0) * 96L
                     + (candidate.Semantics?.StateChanges?.Count ?? 0) * 96L
                     + (candidate.Semantics?.TargetEffects?.Count ?? 0) * 160L
                     + (candidate.SearchReturnQuantiles?.Count ?? 0)
                     * sizeof(double);
            bytes += EstimateStrings(
                candidate.CandidateId,
                candidate.SourceId,
                candidate.OwnerModId,
                candidate.DisplayName,
                candidate.ActionKind,
                candidate.TargetKind,
                candidate.RejectionReason);
        }
        return Math.Max(1024L, bytes);
    }

    private static long EstimateRetainedBytes(
        CombatJourneyTrainingEpisode episode)
    {
        if (episode == null) return 0L;
        return Math.Max(
            2048L,
            4096L
            + (episode.InitialDeck?.Count ?? 0) * 96L
            + (episode.FinalDeck?.Count ?? 0) * 96L
            + (episode.Battles?.Count ?? 0) * 256L
            + (episode.Rewards?.Count ?? 0) * 768L
            + (episode.ActiveAdapterIds?.Count ?? 0) * 128L
            + EstimateStrings(
                episode.JourneyRunId,
                episode.JourneyId,
                episode.ModeId,
                episode.Source,
                episode.PolicyId,
                episode.OwnerModSetHash,
                episode.ContentSetHash,
                episode.BaseModelId));
    }

    private static long EstimateStrings(params string[] values)
    {
        long bytes = 0L;
        foreach (var value in values)
        {
            bytes += EstimateString(value);
        }
        return bytes;
    }

    private static long EstimateString(string value)
    {
        return 32L + (value?.Length ?? 0) * 2L;
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

        public CombatTrainingSample? Sample { get; set; }

        public CombatJourneyTrainingEpisode? Journey { get; set; }

        public BattleFinalization? Finalization { get; set; }

        public long EstimatedBytes { get; set; }
    }

    private sealed class BattleFinalization
    {
        public long BattleSessionId { get; set; }

        public string Outcome { get; set; } = "abandoned";

        public string Reason { get; set; } = "";
    }

    private sealed class TrainingMetadata
    {
        public long ContentRevision { get; set; }

        public string ContentSetHash { get; set; } = "";

        public string OwnerModSetHash { get; set; } = "";

        public string Profile { get; set; } = "balanced";

        public string BaseModelId { get; set; } = "";

        public List<string> ActiveAdapterIds { get; set; } = new();
    }

    private sealed class LiveSessionBuffer
    {
        public List<CombatTrainingSample> Samples { get; } = new();

        public long EstimatedBytes { get; set; }
    }

    private enum TrainingWriterState
    {
        Running,
        Draining,
        Faulted,
        Stopped
    }
}
