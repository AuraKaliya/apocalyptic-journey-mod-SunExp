using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AuraMode.Shared;
using AuraReplay.Presentation.Shared;
using AuraShared.Core;
using AudioArbiter.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Network;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using DG.Tweening;
using UnityEngine;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static class MatchReplayRecorder
{
    private static readonly object Gate = new();
    private static readonly List<string> Diagnostics = new();
    private static readonly ReplayNativeAudioCallTracker NativeAudioCalls = new();
    private static readonly MatchReplayBaselineGate BaselineGate = new();
    private static readonly MatchReplayTerminalGate TerminalGate = new();
    private static readonly ReplayTransactionLedgerV17 Ledger = new();
    private static readonly ReplayStableBarrierCoordinatorV17 StableBarrier = new();
    private static readonly List<string> ContextStack = new();
    private static readonly Stack<bool> CardLifecycleScopes = new();
    private static readonly Dictionary<string, CaptureTransaction> Transactions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> RemoteTransactions = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RemoteCardCommands = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RemoteActionAnimations = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ImplicitPresentationTransactions = new(StringComparer.Ordinal);
    private static readonly HashSet<string> PresentedEntities = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> EntityGenerations = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingPresentationTiming> PendingActionPresentations = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PendingPresentationTiming> PendingCardMotions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ActionPresentationObservation> PendingActionObservations = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CardMotionObservation> PendingCardMotionObservations = new(StringComparer.Ordinal);
    private static readonly ReplayDeferredObservationQueueV17<AuraReplayCapturedPresentationEvent>
        PendingSharedPresentations = new(256, item => item.CaptureSequence);
    private static readonly FieldInfo? ActiveActionAnimationCountsField = typeof(FightUI).GetField(
        "activeActionAnimationCounts",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static MatchRecord? activeRecord;
    private static ReplayDocumentHeaderCoreV17? pendingHeader;
    private static ReplayJournalBuilderV17? builder;
    private static ReplayCaptureCatalogV17? catalog;
    private static long startedTimestamp;
    private static long stateWatermark;
    private static long sourceSequence;
    private static int roundSequence = 1;
    private static int actorTurnSequence;
    private static bool firstRoundSeen;
    private static bool preBaselineActivityMissed;
    private static int lastBgmClipInstanceId;
    private static ReplayAudioCueV17? activeBgmCue;
    private static long captureGeneration;
    private static int stableBarrierRequests;
    private static int stableBarrierRuns;
    private static int stableBarrierStateChanges;
    private static double stableBarrierTotalMilliseconds;
    private static double stableBarrierMaximumMilliseconds;
    private static int persistedTruthEventCount;
    private static int persistedPresentationEventCount;
    private static int captureBatchIndex;
    private static int persistedCatalogRevision = -1;
    private static bool capturePersistenceStarted;
    private static IDisposable? sharedPresentationCapture;

    internal static bool IsRecording
    {
        get
        {
            lock (Gate) return pendingHeader != null || builder != null;
        }
    }

    internal static void Start(object[]? arguments)
    {
        if (!AuraToolsMatchRecordsRuntime.ReplayEnabled || MatchReplaySessionState.IsPlayback) return;
        var levelId = Argument<string>(arguments, 0) ?? FightManager.Instance?.level ?? "";
        if (ReplayNetworkAuthorityV17.IsMultiplayer && !ReplayNetworkAuthorityV17.IsHost)
        {
            ReplayNetworkAuthorityV17.AnnounceCapability(levelId);
            lock (Gate) ResetNoLock();
            return;
        }
        lock (Gate)
        {
            ResetNoLock();
            var recordId = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow.ToString("O");
            activeRecord = new MatchRecord
            {
                RecordId = recordId,
                SessionId = recordId,
                AdventureId = DamageMeter.Network.DamageMeterNetworkRuntime.CurrentAdventureId,
                LevelId = levelId,
                BattleTitle = AuraToolsPlayerDisplay.LevelName(levelId),
                StartedUtc = now,
                Collection = MatchRecordCollections.Auto,
                ReplayProtocol = ReplayProtocolV17.DocumentVersion,
                GameBuild = ReplayResourceCompatibilityApi.CurrentGameBuild,
                ToolBuild = typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
                ModFingerprint = "",
                RequiredCapabilities = ReplayCapabilitiesV17.Required.ToList(),
                OptionalCapabilities = ReplayCapabilitiesV17.Optional.ToList(),
                InitialState = new MatchReplayInitialState
                {
                    LevelId = levelId
                }
            };
            pendingHeader = new ReplayDocumentHeaderCoreV17
            {
                RecordId = recordId,
                AdventureId = activeRecord.AdventureId,
                BattleSessionId = recordId,
                LevelId = levelId,
                BattleTitle = activeRecord.BattleTitle,
                StartedUtc = now,
                GameBuildProvenance = activeRecord.GameBuild,
                RecorderBuild = activeRecord.ToolBuild,
                PerspectivePlayerId = RoleTable.Instance?.Id ?? "single-player",
                PerspectiveKind = "Player"
            };
            catalog = new ReplayCaptureCatalogV17();
            sharedPresentationCapture = AuraReplayPresentationRuntime.BeginCapture(
                recordId,
                CaptureSharedPresentation);
            BaselineGate.Arm();
            AudioArbiterRuntime.ResolvedPlayback += OnResolvedPlayback;
            ReplayNetworkAuthorityV17.CapabilityChanged += OnReplayCapabilityChanged;
        }
        ReplayNetworkAuthorityV17.AnnounceCapability(levelId);
    }

    private static void CaptureSharedPresentation(AuraReplayCapturedPresentationEvent captured)
    {
        if (captured == null) return;
        lock (Gate)
        {
            if (activeRecord == null
                || !string.Equals(activeRecord.RecordId, captured.BattleSessionId, StringComparison.Ordinal)) return;
            if (builder == null || !CanBindSharedPresentationNoLock(captured))
            {
                if (!PendingSharedPresentations.TryEnqueue(captured, item =>
                        item.CaptureSequence == captured.CaptureSequence
                        && string.Equals(item.BattleSessionId, captured.BattleSessionId, StringComparison.Ordinal)))
                {
                    AddDiagnosticNoLock("shared-presentation-prebaseline-overflow");
                    preBaselineActivityMissed = true;
                    return;
                }
                if (builder != null)
                    RequestStableBarrierNoLock("shared-presentation-entity-pending", needsStateCapture: true);
                return;
            }
            try
            {
                AppendSharedPresentationNoLock(captured);
                QueueCaptureBatchNoLock();
            }
            catch (Exception ex)
            {
                AddDiagnosticNoLock("shared-presentation-append-failed:"
                                    + captured.CaptureSequence + ":" + ex.GetType().Name + ":" + ex.Message);
                if (!PendingSharedPresentations.TryEnqueue(captured, item =>
                        item.CaptureSequence == captured.CaptureSequence
                        && string.Equals(item.BattleSessionId, captured.BattleSessionId, StringComparison.Ordinal)))
                    AddDiagnosticNoLock("shared-presentation-append-retry-overflow");
                RequestStableBarrierNoLock("shared-presentation-append-retry", needsStateCapture: true);
            }
        }
    }

    private static void AppendSharedPresentationNoLock(AuraReplayCapturedPresentationEvent captured)
    {
        if (builder == null || captured?.Event == null) return;
        var value = captured.Event;
        catalog?.ObservePresentationModule(value.OwnerModId, value.TypeId);
        var transactionId = ContextStack.LastOrDefault(id =>
            Transactions.TryGetValue(id, out var transaction)
            && (string.IsNullOrWhiteSpace(value.ActorEntityId)
                || string.Equals(transaction.Source.ActorId, value.ActorEntityId, StringComparison.Ordinal)));
        var ownsTransaction = string.IsNullOrWhiteSpace(transactionId) || !builder.IsOpen(transactionId);
        if (ownsTransaction)
            transactionId = BeginSystemTransactionNoLock(
                ReplayTransactionKindsV17.Passive,
                "SharedPresentation:" + value.OwnerModId + ":" + value.TypeId,
                value.ActorEntityId);
        try
        {
            var ticks = CapturedPresentationTicks(captured);
            builder.AddPresentation(
                transactionId,
                ReplayEventTypesV17.ExtensionPresented,
                new ReplayPresentationMessageV17
                {
                    Kind = value.Kind,
                    ActorId = value.ActorEntityId,
                    OwnerEntityId = value.OwnerEntityId,
                    TargetIds = value.TargetEntityIds?.ToList() ?? new List<string>(),
                    DisplayText = value.DisplayText,
                    ResourcePath = value.ResourcePath,
                    ExtensionOwnerModId = value.OwnerModId,
                    ExtensionTypeId = value.TypeId,
                    ExtensionSchemaVersion = value.SchemaVersion,
                    ExtensionPayloadJson = value.PayloadJson,
                    ExtensionEventId = value.EventId,
                    Phase = ReplayPresentationPhasesV17.Impact,
                    PhaseOrdinal = 3,
                    DurationTicks = Math.Max(1L, value.DurationMicroseconds),
                    Persistent = value.Persistent
                },
                ticks,
                value.ActorEntityId);
            if (ownsTransaction) MarkAndCompleteSystemTransactionNoLock(transactionId);
        }
        catch
        {
            if (ownsTransaction) RollbackFailedSystemTransactionNoLock(transactionId, "shared-presentation-append-failed");
            throw;
        }
    }

    private static bool CanBindSharedPresentationNoLock(AuraReplayCapturedPresentationEvent captured)
    {
        var actor = captured?.Event?.ActorEntityId ?? "";
        return builder != null
               && (string.IsNullOrWhiteSpace(actor)
                   || builder.CurrentState.Entities.Any(item =>
                       string.Equals(item.EntityId, actor, StringComparison.Ordinal)));
    }

    private static long CapturedPresentationTicks(AuraReplayCapturedPresentationEvent captured)
    {
        if (startedTimestamp == 0 || captured.StopwatchTimestamp <= startedTimestamp) return 0L;
        var elapsed = captured.StopwatchTimestamp - startedTimestamp;
        return Math.Max(0L, (long)(elapsed * (double)ReplayProtocolV17.TimebaseTicksPerSecond / Stopwatch.Frequency));
    }

    private static void DrainPendingSharedPresentationsNoLock()
    {
        if (builder == null || PendingSharedPresentations.Count == 0) return;
        foreach (var pending in PendingSharedPresentations.Ready(CanBindSharedPresentationNoLock))
        {
            try
            {
                AppendSharedPresentationNoLock(pending);
                if (!PendingSharedPresentations.Commit(pending))
                    throw new InvalidOperationException(
                        "Replay shared presentation obligation disappeared before commit: "
                        + pending.CaptureSequence + ".");
            }
            catch (Exception ex)
            {
                AddDiagnosticNoLock("shared-presentation-drain-failed:"
                                    + pending.CaptureSequence + ":" + ex.GetType().Name + ":" + ex.Message);
                break;
            }
        }
    }

    internal static void CommitMaterializedBaseline()
    {
        if (FightManager.Instance == null) return;
        lock (Gate)
        {
            BaselineGate.MarkMaterialized();
            TryCommitDeferredBaselineNoLock("battle-materialized");
        }
    }

    private static void OnReplayCapabilityChanged()
    {
        long generation;
        string recordId;
        lock (Gate)
        {
            if (pendingHeader == null || activeRecord == null) return;
            generation = captureGeneration;
            recordId = activeRecord.RecordId;
        }
        AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "match-replay-capability-ready:" + recordId,
            Source = "MatchRecords.ReplayV17.CapabilityReady",
            Phase = AuraSharedFramePhase.CriticalLifecycle,
            Priority = 200,
            Action = () =>
            {
                lock (Gate)
                {
                    if (generation == captureGeneration) TryCommitDeferredBaselineNoLock("capability-ready");
                }
            },
            OnFailed = (_, exception) => MarkCaptureFailure("capability-ready", exception)
        });
    }

    private static void TryCommitDeferredBaselineNoLock(string source)
    {
        if (pendingHeader == null
            || preBaselineActivityMissed
            || !BaselineGate.MaterializationObserved
            || FightManager.Instance == null
            || !ReplayNetworkAuthorityV17.CanHostRecord(pendingHeader.LevelId, out _)
            || !BaselineGate.TryCommit(CaptureMaterializedBaselineGuardedNoLock))
            return;
        AuraToolsLog.Debug("[MatchRecords] v17 materialized baseline committed from " + source + ".");
    }

    internal static void BeginCardAction(object? target)
    {
        if (target == null) return;
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("card-action")) return;
            FlushStableBarrierNoLock("before-card-action");
            var source = ReplayFactCaptureV17.CaptureActionSource(target, catalog!);
            var duplicate = FindOpenSourceTransactionNoLock(source);
            CardLifecycleScopes.Push(string.IsNullOrWhiteSpace(duplicate));
            if (string.IsNullOrWhiteSpace(duplicate)) BeginSourceTransactionNoLock(source);
        }
    }

    internal static void EndCardAction(object? target)
    {
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            if (CardLifecycleScopes.Count == 0 || !CardLifecycleScopes.Pop()) return;
            var source = target == null ? null : ReplayFactCaptureV17.CaptureActionSource(target, catalog!);
            EndCurrentSourceNoLock(source);
        }
    }

    internal static void AbortCardAction(object? target, string reason)
    {
        lock (Gate)
        {
            if (!CanCaptureNoLock() || CardLifecycleScopes.Count == 0 || !CardLifecycleScopes.Pop()) return;
            if (ContextStack.Count == 0) return;
            var transactionId = ContextStack[ContextStack.Count - 1];
            ContextStack.RemoveAt(ContextStack.Count - 1);
            var source = target == null ? null : ReplayFactCaptureV17.CaptureActionSource(target, catalog!);
            if (source != null
                && Transactions.TryGetValue(transactionId, out var transaction)
                && (!string.Equals(transaction.Source.ActorId, source.ActorId, StringComparison.Ordinal)
                    || !string.Equals(transaction.Source.SourceInstanceId, source.SourceInstanceId, StringComparison.Ordinal)))
            {
                AddDiagnosticNoLock("action-abort-owner-mismatch:" + transactionId);
            }
            if (builder!.IsOpen(transactionId))
                builder.AbortTransaction(transactionId, ElapsedTicks(), reason ?? "native-action-aborted");
            Ledger.Abort(transactionId);
            Transactions.Remove(transactionId);
            AddDiagnosticNoLock("action-aborted:" + transactionId + ":" + (reason ?? ""));
            RequestStableBarrierNoLock("action-aborted", needsStateCapture: true);
        }
    }

    internal static void BeginEnemyIntentAction(object? target, object[]? arguments)
    {
        if (target is not Enemy enemy || enemy.Status == null) return;
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("enemy-intent")) return;
            FlushStableBarrierNoLock("before-enemy-intent");
            var slot = arguments != null && arguments.Length > 0 && arguments[0] is int value ? Math.Max(0, value) : 0;
            var card = enemy.FightAction?.TryGetCard();
            if (card == null && enemy.ActionCards != null && slot < enemy.ActionCards.Count) card = enemy.ActionCards[slot];
            var config = card?.dataConfig;
            if (config == null) return;
            var stableId = ReplayCaptureCatalogV17.First(
                ReplayCaptureCatalogV17.Read(config.data, "Id"),
                ReplayCaptureCatalogV17.Read(config.Vars, "Id"));
            var descriptor = catalog!.RegisterIntent(config, stableId);
            var source = new ReplayCapturedActionSourceV17
            {
                Kind = ReplayTransactionKindsV17.Intent,
                IssuerPlayerId = RoleTable.Instance?.Id ?? "",
                ActorId = enemy.Status.InstanceId ?? enemy.InstanceId ?? "",
                SourceInstanceId = config.InstanceID ?? enemy.Status.InstanceId + "|intent|" + slot,
                DescriptorId = descriptor.DescriptorId,
                Label = descriptor.Name,
                AnimationState = ReplayCaptureCatalogV17.First(
                    ReplayCaptureCatalogV17.Read(config.Vars, "Action"),
                    ReplayCaptureCatalogV17.Read(config.data, "Action"),
                    "Idle"),
                EffectDescriptorId = catalog.RegisterEffect(ReplayCaptureCatalogV17.First(
                    ReplayCaptureCatalogV17.Read(config.Vars, "Effects"),
                    ReplayCaptureCatalogV17.Read(config.data, "Effects"))),
                SourceZone = "Intent",
                SourceSlot = slot
            };
            BeginSourceTransactionNoLock(source);
        }
    }

    internal static void EndEnemyIntentAction(object? target)
    {
        lock (Gate)
        {
            if (!CanCaptureNoLock() || ContextStack.Count == 0) return;
            var current = Transactions[ContextStack[ContextStack.Count - 1]];
            if (target is Enemy enemy
                && !string.Equals(current.Source.ActorId, enemy.Status?.InstanceId ?? enemy.InstanceId ?? "", StringComparison.Ordinal))
                return;
            EndCurrentSourceNoLock(null);
        }
    }

    internal static void CaptureRemoteCommand(AuraRemoteCombatActionContext context)
    {
        if (context == null) return;
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("remote-command")) return;
            if (string.Equals(context.Kind, AuraRemoteCombatActionKinds.CardUse, StringComparison.Ordinal)
                && context.CardData != null)
            {
                var remoteKey = RemoteKey(context.ActorId ?? "", context.CommandSequence);
                if (!RemoteCardCommands.Add(remoteKey)) return;
                FlushStableBarrierNoLock("before-remote-command");
                var config = context.CardData;
                var stableId = ReplayCaptureCatalogV17.Read(config.data, "Id");
                var descriptor = catalog!.RegisterCard(config, stableId);
                var actorEntityId = ResolveActorEntityIdNoLock(context.ActorId);
                var source = new ReplayCapturedActionSourceV17
                {
                    Kind = ReplayTransactionKindsV17.Card,
                    IssuerPlayerId = context.ActorId ?? "",
                    ActorId = actorEntityId,
                    SourceInstanceId = config is DataConfig concrete
                        ? concrete.InstanceID ?? ""
                        : ReplayCaptureCatalogV17.Read(config.Vars, "InstanceID"),
                    DescriptorId = descriptor.DescriptorId,
                    Label = descriptor.Name,
                    AnimationState = ReplayCaptureCatalogV17.First(
                        ReplayCaptureCatalogV17.Read(config.Vars, "Action"),
                        ReplayCaptureCatalogV17.Read(config.data, "Action"),
                        "Idle"),
                    EffectDescriptorId = catalog.RegisterEffect(context.EffectName),
                    SourceZone = "RemoteHand"
                };
                var startedTransactionId = FindOpenSourceTransactionNoLock(source);
                if (string.IsNullOrWhiteSpace(startedTransactionId))
                    startedTransactionId = BeginSourceTransactionNoLock(source, pushContext: false);
                RemoteTransactions[remoteKey] = startedTransactionId;
                return;
            }
            if (!string.Equals(context.Kind, AuraRemoteCombatActionKinds.ActionAnimation, StringComparison.Ordinal)) return;
            var actionKey = RemoteKey(context.ActorId ?? "", context.CommandSequence);
            if (!RemoteActionAnimations.Add(actionKey)) return;
            var transactionId = ResolveRemoteTransactionNoLock(context);
            var transactionActorId = Transactions[transactionId].Source.ActorId;
            var actionEvent = RequireActorPresentationNoLock(transactionId, transactionActorId);
            var actionTicks = ElapsedTicks();
            var effectDescriptorId = catalog!.RegisterEffect(context.EffectName);
            actionEvent.Presentation!.Kind = "Action";
            actionEvent.TimeTicks = actionTicks;
            actionEvent.Presentation.DelayTicks = 0L;
            CaptureCameraStateNoLock(actionEvent.Presentation);
            actionEvent.Presentation.ActorId = transactionActorId;
            actionEvent.Presentation.AnimationState = Transactions[transactionId].Source.AnimationState;
            actionEvent.Presentation.EffectDescriptorId = effectDescriptorId;
            actionEvent.Presentation.TargetIds = context.AnimationTargets.Select(item => item.StatusInstanceId ?? "")
                .Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            actionEvent.Presentation.Phase = ReplayPresentationPhasesV17.ActorFocus;
            actionEvent.Presentation.PhaseOrdinal = 2;
            actionEvent.Presentation.DurationTicks = 1;
            if (!string.IsNullOrWhiteSpace(effectDescriptorId))
                Transactions[transactionId].TimedPresentationEvents.Add(
                    builder!.AddPresentation(
                        transactionId,
                        ReplayEventTypesV17.EffectPresented,
                        new ReplayPresentationMessageV17
                        {
                            Kind = "Effect",
                            ActorId = transactionActorId,
                            EffectDescriptorId = effectDescriptorId,
                            TargetIds = actionEvent.Presentation.TargetIds.ToList(),
                            Phase = ReplayPresentationPhasesV17.Impact,
                            PhaseOrdinal = 3,
                            DurationTicks = 1
                        },
                        actionTicks,
                        transactionActorId));
            foreach (var target in context.AnimationTargets)
                Transactions[transactionId].TimedPresentationEvents.Add(
                    builder!.AddPresentation(transactionId, ReplayEventTypesV17.HitReactionPresented, new ReplayPresentationMessageV17
                    {
                        Kind = "Hit",
                        ActorId = target.StatusInstanceId ?? "",
                        AnimationState = target.AnimationState ?? "Idle",
                        Phase = ReplayPresentationPhasesV17.Impact,
                        PhaseOrdinal = 3,
                        DurationTicks = 1
                    }, actionTicks, target.StatusInstanceId ?? ""));
            ApplyCurrentStateNoLock(transactionId);
            if (!ContextStack.Contains(transactionId, StringComparer.Ordinal))
            {
                MarkSourceCompletedNoLock(transactionId);
                RequestStableBarrierNoLock("remote-action-complete", needsStateCapture: true);
            }
        }
    }

    internal static void ObserveAuthoritativeStatus(AuraAuthoritativeStatusContext context)
    {
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("authoritative-status")) return;
            stateWatermark = Math.Max(stateWatermark, context?.Version ?? 0);
            RequestStableBarrierNoLock("authoritative-status", needsStateCapture: true);
        }
    }

    internal static void CaptureActionPresentation(object? hookTarget, object[]? arguments)
    {
        if (MatchReplaySessionState.IsPlayback
            || arguments == null
            || arguments.Length == 0
            || arguments[0] is not IScriptExecutor executor
            || executor.Self is not StatusManager actor)
            return;
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("action-presentation")) return;
            var sourceInstanceId = executor.dataConfig?.InstanceID
                                   ?? ReplayCaptureCatalogV17.Read(executor.dataConfig?.Vars, "InstanceID");
            var transactionId = ContextStack.LastOrDefault();
            if (!PresentationMatchesTransactionNoLock(transactionId, actor.InstanceId ?? "", sourceInstanceId)
                && !Ledger.TryBindActionPresentation(
                    actor.InstanceId ?? "",
                    sourceInstanceId,
                    out transactionId,
                    out var rejection))
            {
                if (string.Equals(rejection, "ambiguous-causal-ownership", StringComparison.Ordinal))
                {
                    AddDiagnosticNoLock(rejection + ":presentation:" + (actor.InstanceId ?? "") + ":" + sourceInstanceId);
                    return;
                }
                transactionId = BeginImplicitTransactionNoLock(executor, actor, sourceInstanceId);
            }
            var captured = MatchReplayActionPresentationCapture.Capture(executor);
            if (captured == null) return;
            var effectDescriptorId = catalog!.RegisterEffect(captured.EffectName);
            var observedTicks = ElapsedTicks();
            var presentationTicks = observedTicks;
            var actionEvent = RequireActorPresentationNoLock(transactionId, actor.InstanceId ?? "");
            actionEvent.TimeTicks = presentationTicks;
            actionEvent.Presentation!.Kind = "Action";
            CaptureCameraStateNoLock(actionEvent.Presentation);
            actionEvent.Presentation.ActorId = actor.InstanceId ?? "";
            actionEvent.Presentation.SourceInstanceId = sourceInstanceId;
            actionEvent.Presentation.AnimationState = string.IsNullOrWhiteSpace(captured.ActorAnimationState)
                ? "Idle"
                : captured.ActorAnimationState;
            actionEvent.Presentation.EffectDescriptorId = effectDescriptorId;
            actionEvent.Presentation.Phase = ReplayPresentationPhasesV17.ActorFocus;
            actionEvent.Presentation.PhaseOrdinal = 2;
            actionEvent.Presentation.TargetIds = captured.Targets.Select(item => item.TargetId)
                .Distinct(StringComparer.Ordinal).ToList();
            actionEvent.Presentation.DurationTicks = 1;
            actionEvent.Presentation.DelayTicks = 0L;
            var timingKey = ImplicitKey(actor.InstanceId ?? "", sourceInstanceId);
            PendingActionPresentations[timingKey] = new PendingPresentationTiming(observedTicks, actionEvent);
            PendingActionObservations[timingKey] = new ActionPresentationObservation(
                transactionId,
                presentationTicks,
                hookTarget as FightUI,
                new[] { actor }.Concat(executor.Object?.OfType<StatusManager>()
                                       ?? Enumerable.Empty<StatusManager>())
                    .Where(item => item != null)
                    .Distinct()
                    .ToList());
            CaptureActionPresentationSamplesNoLock(
                PendingActionObservations[timingKey],
                presentationTicks);
            if (!string.IsNullOrWhiteSpace(effectDescriptorId))
                Transactions[transactionId].TimedPresentationEvents.Add(
                    builder!.AddPresentation(transactionId, ReplayEventTypesV17.EffectPresented, new ReplayPresentationMessageV17
                    {
                        Kind = "Effect",
                        ActorId = actor.InstanceId ?? "",
                        EffectDescriptorId = effectDescriptorId,
                        Phase = ReplayPresentationPhasesV17.Impact,
                        PhaseOrdinal = 3,
                        TargetIds = captured.Targets.Select(item => item.TargetId).Distinct(StringComparer.Ordinal).ToList(),
                        DelayTicks = 0,
                        DurationTicks = 1
                    }, presentationTicks, actor.InstanceId ?? ""));
            foreach (var target in captured.Targets)
                Transactions[transactionId].TimedPresentationEvents.Add(
                    builder!.AddPresentation(transactionId, ReplayEventTypesV17.HitReactionPresented, new ReplayPresentationMessageV17
                    {
                        Kind = "Hit",
                        ActorId = target.TargetId,
                        AnimationState = target.AnimationState,
                        Phase = ReplayPresentationPhasesV17.Impact,
                        PhaseOrdinal = 3,
                        DurationTicks = 1
                    }, presentationTicks, target.TargetId));
        }
    }

    internal static void CompleteActionPresentation(object? hookTarget, object[]? arguments)
    {
        if (arguments == null || arguments.Length == 0 || arguments[0] is not IScriptExecutor executor
            || executor.Self is not StatusManager actor)
            return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            var key = ImplicitKey(actor.InstanceId, executor.dataConfig?.InstanceID
                ?? ReplayCaptureCatalogV17.Read(executor.dataConfig?.Vars, "InstanceID"));
            if (PendingActionObservations.TryGetValue(key, out var observation))
            {
                observation.FightUi ??= hookTarget as FightUI;
                ScheduleActionPresentationObservationNoLock(key, observation);
            }
            else CompletePresentationTimingNoLock(PendingActionPresentations, key);
            var implicitTransaction = ImplicitPresentationTransactions.TryGetValue(key, out var transactionId);
            if (!implicitTransaction)
            {
                var sourceInstanceId = executor.dataConfig?.InstanceID
                                       ?? ReplayCaptureCatalogV17.Read(executor.dataConfig?.Vars, "InstanceID");
                transactionId = ContextStack.LastOrDefault();
                if (!PresentationMatchesTransactionNoLock(transactionId, actor.InstanceId ?? "", sourceInstanceId)
                && !Ledger.TryBindActionPresentation(
                        actor.InstanceId ?? "",
                        sourceInstanceId,
                        out transactionId,
                        out var rejection))
                {
                    if (string.Equals(rejection, "ambiguous-causal-ownership", StringComparison.Ordinal))
                        AddDiagnosticNoLock(rejection + ":presentation-complete:" + (actor.InstanceId ?? "") + ":" + sourceInstanceId);
                    return;
                }
            }
            ApplyCurrentStateNoLock(transactionId);
            if (implicitTransaction)
            {
                MarkSourceCompletedNoLock(transactionId);
                ImplicitPresentationTransactions.Remove(key);
                RequestStableBarrierNoLock("implicit-presentation-complete", needsStateCapture: true);
            }
        }
    }

    private static void ObserveScheduledStableBarrier(long expectedGeneration)
    {
        lock (Gate)
        {
            if (expectedGeneration != captureGeneration)
            {
                return;
            }
            FlushStableBarrierNoLock("scheduled");
        }
    }

    internal static void BeginNativeCardMotion(object? hookTarget, object[]? arguments)
    {
        if (MatchReplaySessionState.IsPlayback || arguments == null || arguments.Length == 0) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            var config = NativeCardData(arguments[0]);
            if (config == null) return;
            var sourceId = config.InstanceID ?? "";
            var currentBuilder = builder;
            if (currentBuilder == null) return;
            var transactionId = ContextStack.LastOrDefault();
            if (string.IsNullOrWhiteSpace(transactionId)
                || !currentBuilder.IsOpen(transactionId))
                transactionId = Transactions.Values
                    .Where(item => !item.SourceCompleted
                                   && string.Equals(item.Source.SourceInstanceId, sourceId, StringComparison.Ordinal))
                    .OrderByDescending(item => item.TransactionId, StringComparer.Ordinal)
                    .Select(item => item.TransactionId)
                    .FirstOrDefault() ?? "";
            if (transactionId.Length == 0 || !currentBuilder.IsOpen(transactionId)) return;
            var ticks = ElapsedTicks();
            var motion = RequireCardMotionPresentationNoLock(transactionId, sourceId, ticks);
            if (motion.Presentation == null) return;
            motion.TimeTicks = ticks;
            motion.Presentation.DelayTicks = 0L;
            motion.Presentation.Kind = NativeCardMotionKind(arguments);
            motion.Presentation.Value = NativeDisplayedCost(config);
            PendingCardMotions[sourceId] = new PendingPresentationTiming(ticks, motion);
            var fightUi = hookTarget as FightUI;
            PendingCardMotionObservations[sourceId] = new CardMotionObservation(
                ticks,
                fightUi,
                fightUi == null
                    ? new HashSet<int>()
                    : fightUi.GetComponentsInChildren<CardItem>(includeInactive: true)
                        .Where(item => item != null)
                        .Select(item => item.GetInstanceID())
                        .ToHashSet());
        }
    }

    internal static void EndNativeCardMotion(object? hookTarget, object[]? arguments)
    {
        if (arguments == null || arguments.Length == 0) return;
        lock (Gate)
        {
            var config = NativeCardData(arguments[0]);
            if (config == null) return;
            var sourceId = config.InstanceID ?? "";
            if (!PendingCardMotionObservations.TryGetValue(sourceId, out var observation))
            {
                CompletePresentationTimingNoLock(PendingCardMotions, sourceId);
                return;
            }
            observation.FightUi ??= hookTarget as FightUI;
            observation.Visual = observation.FightUi?
                .GetComponentsInChildren<CardItem>(includeInactive: true)
                .Where(item => item != null && !observation.ExistingInstanceIds.Contains(item.GetInstanceID()))
                .OrderByDescending(item => string.Equals(item.dataConfig?.InstanceID, sourceId, StringComparison.Ordinal))
                .ThenByDescending(item => item.GetInstanceID())
                .FirstOrDefault();
            if (observation.Visual == null)
            {
                PendingCardMotionObservations.Remove(sourceId);
                AddDiagnosticNoLock("native-card-motion-visual-missing:" + sourceId);
                CompletePresentationTimingNoLock(PendingCardMotions, sourceId);
                return;
            }
            CaptureCardMotionSampleNoLock(sourceId, observation, ElapsedTicks());
            ScheduleCardMotionObservationNoLock(sourceId, observation);
        }
    }

    internal static void ObserveNativeCardExitMotion(object? hookTarget, string kind)
    {
        if (MatchReplaySessionState.IsPlayback || hookTarget is not CardItem card || card.dataConfig == null) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock() || builder == null) return;
            var sourceId = card.dataConfig.InstanceID ?? "";
            var transactionId = ContextStack.LastOrDefault(id =>
                Transactions.TryGetValue(id, out var transaction)
                && string.Equals(transaction.Source.SourceInstanceId, sourceId, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(transactionId))
                transactionId = Transactions.Values
                    .Where(item => !item.SourceCompleted
                                   && string.Equals(item.Source.SourceInstanceId, sourceId, StringComparison.Ordinal))
                    .OrderByDescending(item => item.TransactionId, StringComparer.Ordinal)
                    .Select(item => item.TransactionId)
                    .FirstOrDefault() ?? "";
            var ownsTransaction = transactionId.Length == 0 || !builder.IsOpen(transactionId);
            if (ownsTransaction)
            {
                var source = ReplayFactCaptureV17.CaptureActionSource(card, catalog!);
                source.Kind = ReplayTransactionKindsV17.Passive;
                source.Label = "CardExit:" + (kind ?? "");
                transactionId = BeginSourceTransactionNoLock(source, pushContext: false);
            }
            var ticks = ElapsedTicks();
            var motion = RequireCardMotionPresentationNoLock(transactionId, sourceId, ticks);
            if (motion.Presentation == null) return;
            motion.TimeTicks = ticks;
            motion.Presentation.DelayTicks = 0L;
            var motionKind = (kind ?? "").Trim();
            motion.Presentation.Kind = motionKind.Length == 0 ? "CardExit" : motionKind;
            motion.Presentation.Value = NativeDisplayedCost(card.dataConfig);
            PendingCardMotions[sourceId] = new PendingPresentationTiming(ticks, motion);
            var observation = new CardMotionObservation(ticks, card.GetComponentInParent<FightUI>(), new HashSet<int>())
            {
                Visual = card,
                OwnsTransaction = ownsTransaction,
                TransactionId = transactionId
            };
            PendingCardMotionObservations[sourceId] = observation;
            CaptureCardMotionSampleNoLock(sourceId, observation, ticks);
            ScheduleCardMotionObservationNoLock(sourceId, observation);
        }
    }

    internal static void ObserveCardPresentationReset(AuraCardPresentationContext context)
    {
        if (MatchReplaySessionState.IsPlayback || context?.Root == null) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            var resetRootInstanceId = context.Root.GetInstanceID();
            var resetSourceInstanceId = context.Config?.InstanceID
                                        ?? context.Card?.dataConfig?.InstanceID
                                        ?? "";
            var completed = false;
            var now = ElapsedTicks();
            foreach (var pair in PendingCardMotionObservations.ToList())
            {
                var visual = pair.Value.Visual;
                var observedRootInstanceId = visual != null && visual.transform != null
                    ? visual.transform.GetInstanceID()
                    : 0;
                if (!ReplayCardVisualLifecycleV17.ResetMatches(
                        observedRootInstanceId,
                        pair.Key,
                        resetRootInstanceId,
                        resetSourceInstanceId)) continue;
                CompleteCardMotionObservationNoLock(
                    pair.Key,
                    pair.Value,
                    now,
                    ReplayCardVisualLifecycleV17.SharedReset);
                completed = true;
            }
            if (completed) FlushStableBarrierNoLock("card-visual-shared-reset");
        }
    }

    internal static void CaptureNativeDamagePopup(object[]? arguments)
    {
        if (MatchReplaySessionState.IsPlayback || arguments == null || arguments.Length < 4) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            var transactionId = ContextStack.LastOrDefault();
            var ownsTransaction = string.IsNullOrWhiteSpace(transactionId) || !builder!.IsOpen(transactionId);
            if (ownsTransaction)
                transactionId = BeginSystemTransactionNoLock(
                    ReplayTransactionKindsV17.SystemPhase,
                    "ObservedNativeDamagePopup");
            var position = arguments[3] switch
                {
                    Vector2 vector => vector,
                    Vector3 vector => new Vector2(vector.x, vector.y),
                    _ => Vector2.zero
                };
            var target = arguments[2] as StatusManager;
            builder!.AddPresentation(transactionId, ReplayEventTypesV17.DamageTextPresented,
                new ReplayPresentationMessageV17
                {
                    Kind = arguments[0]?.ToString() ?? "DamageText",
                    ActorId = target?.InstanceId ?? "",
                    DisplayText = arguments[1]?.ToString() ?? "",
                    FinalDisplayText = arguments.Length > 4 ? arguments[4]?.ToString() ?? "" : "",
                    ScreenPosition = new ReplayVector2Q16V17
                    {
                        X = (int)Math.Round(position.x * 65_536d),
                        Y = (int)Math.Round(position.y * 65_536d)
                    },
                    DurationTicks = 2_500_000L
                }, ElapsedTicks(), target?.InstanceId ?? "");
            if (ownsTransaction) MarkAndCompleteSystemTransactionNoLock(transactionId);
        }
    }

    internal static void BeginNativeAudioCapture(object[]? arguments, string bus)
    {
        if (arguments?.OfType<AudioClip>().FirstOrDefault() != null) return;
        lock (Gate)
        {
            if (CanCaptureNoLock()) NativeAudioCalls.BeginSymbolic(bus, arguments?.OfType<string>().ToList() ?? new List<string>());
        }
    }

    internal static void EndNativeAudioCapture(object[]? arguments, string bus)
    {
        var clip = arguments?.OfType<AudioClip>().FirstOrDefault();
        lock (Gate)
        {
            if (clip != null && CanCaptureNoLock())
            {
                var observed = NativeAudioCalls.ObserveClip(bus, clip.name, clip.GetInstanceID());
                RecordAudioNoLock(clip, bus, bus, "Native." + bus, observed.ResourceId);
            }
            NativeAudioCalls.EndSymbolic(bus);
        }
    }

    internal static void CaptureNativeBgm(object? target, object[]? arguments)
    {
        var manager = target as AudioManager ?? AudioManager.Instance;
        var clip = manager?.bgmSource?.clip;
        if (clip == null) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock() || clip.GetInstanceID() == lastBgmClipInstanceId) return;
            lastBgmClipInstanceId = clip.GetInstanceID();
            LimitActiveBgmNoLock();
            activeBgmCue = RecordAudioNoLock(clip, "Bgm", "BattleBgm", "BattleBgm", manager?.NowBGMName ?? clip.name);
        }
    }

    internal static void SignalFightStart()
    {
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("fight-start")) return;
            FlushStableBarrierNoLock("fight-start");
            var transactionId = BeginSystemTransactionNoLock(ReplayTransactionKindsV17.SystemPhase, "FightStart");
            builder!.AddTruthMarker(
                transactionId,
                ReplayEventTypesV17.FightStartSignaled,
                ElapsedTicks(),
                builder.CurrentState.ActiveActorId);
            ApplyCurrentStateNoLock(transactionId);
            MarkAndCompleteSystemTransactionNoLock(transactionId);
        }
    }

    internal static void StartTurn()
    {
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("round-start")) return;
            FlushStableBarrierNoLock("round-start");
            if (firstRoundSeen) roundSequence++;
            else firstRoundSeen = true;
            var transactionId = BeginSystemTransactionNoLock(ReplayTransactionKindsV17.SystemPhase, "RoundStart");
            ApplyCurrentStateNoLock(transactionId);
            builder!.AddTruthMarker(transactionId, ReplayEventTypesV17.RoundStarted, ElapsedTicks(), builder.CurrentState.ActiveActorId);
            builder.AddPresentation(transactionId, ReplayEventTypesV17.TurnTransitionPresented,
                new ReplayPresentationMessageV17
                {
                    Kind = "RoundStart",
                    ActorId = builder.CurrentState.ActiveActorId,
                    DisplayText = "Round " + roundSequence,
                    DurationTicks = 1
                }, ElapsedTicks(), builder.CurrentState.ActiveActorId);
            MarkAndCompleteSystemTransactionNoLock(transactionId);
        }
    }

    internal static void PrepareCompletion(string result)
    {
        lock (Gate)
        {
            if (builder == null || activeRecord == null || TerminalGate.SettlementPrepared) return;
            FlushStableBarrierNoLock("battle-settling");
            TerminalGate.Prepare(result);
        }
    }

    internal static void CompleteAfterCleanup(string result)
    {
        CompletionSnapshot? completion = null;
        MatchRecord? rejectedSummary = null;
        lock (Gate)
        {
            if (activeRecord == null || TerminalGate.TerminalFrameSealed) return;
            if (builder == null)
            {
                activeRecord.Result = string.IsNullOrWhiteSpace(result) ? "Ended" : result;
                activeRecord.EndedUtc = DateTime.UtcNow.ToString("O");
                activeRecord.ReplayState = MatchReplayStates.Rejected;
                activeRecord.StatisticsJson = AuraSharedJson.SerializeCompact(AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
                activeRecord.CaptureDiagnostics = Diagnostics.Concat(new[]
                    {
                        ReplayNetworkAuthorityV17.CanHostRecord(activeRecord.LevelId, out var rejection)
                            ? "materialized-baseline-unavailable"
                            : "network-authority-negotiation-failed:" + rejection
                    })
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                rejectedSummary = activeRecord;
                ResetNoLock();
            }
            else
            {
                if (!TerminalGate.SettlementPrepared) TerminalGate.Prepare(result);
                SealPendingPresentationTimingsNoLock();
                var terminalSealed = Ledger.SealSourcesAtTerminal(stateWatermark);
                if (terminalSealed.Count > 0)
                    AuraToolsLog.Debug("[MatchRecords] terminal source boundary sealed transactions: "
                                       + string.Join(",", terminalSealed) + ".");
                FlushStableBarrierNoLock("battle-finalized");
                var outcome = BeginSystemTransactionNoLock(ReplayTransactionKindsV17.Outcome, "Outcome");
                var finalState = ReplayFactCaptureV17.CaptureVisibleState(
                    roundSequence, actorTurnSequence, catalog!, activeRecord.RecordId);
                AssignEntityGenerationsNoLock(finalState);
                finalState.BattlePhase = "Finalized";
                finalState.Outcome = TerminalGate.Result.Length == 0 ? result : TerminalGate.Result;
                ApplyObservedStateNoLock(outcome, finalState);
                DrainPendingSharedPresentationsNoLock();
                if (PendingSharedPresentations.Count > 0)
                    throw new InvalidOperationException(
                        "Replay terminal frame has undrained shared presentation obligations: "
                        + string.Join(",", PendingSharedPresentations.Snapshot.Select(item => item.Event.EventId)) + ".");
                builder.AddTruthMarker(outcome, ReplayEventTypesV17.OutcomeEntering, ElapsedTicks(), finalState.ActiveActorId);
                builder.AddTruthMarker(outcome, ReplayEventTypesV17.BattleFinalized, ElapsedTicks(), finalState.ActiveActorId);
                MarkAndCompleteSystemTransactionNoLock(outcome);
                LimitActiveBgmNoLock();
                TerminalGate.SealTerminalFrame(result);
                AbortUndrainedNoLock();
                completion = DetachCompletionNoLock();
            }
        }
        if (rejectedSummary != null)
        {
            try
            {
                MatchRecordStorage.Database.SaveSummaryV17(rejectedSummary, null, rejected: true);
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[MatchRecords] rejected multiplayer replay summary could not be saved: " + ex.Message);
            }
            return;
        }
        if (completion != null) QueueFinalization(completion);
    }

    internal static void Abort()
    {
        lock (Gate) ResetNoLock();
    }

    internal static void MarkCaptureFailure(string stage, Exception exception)
    {
        MatchRecord? terminalSummary = null;
        lock (Gate)
        {
            if (activeRecord == null && pendingHeader == null) return;
            if (builder == null) preBaselineActivityMissed = true;
            var message = exception?.Message ?? "";
            if (message.Length > 256) message = message.Substring(0, 256);
            AddDiagnosticNoLock("capture-failed:" + (stage ?? "unknown") + ":"
                                + (exception?.GetType().Name ?? "Exception") + ":" + message);
            if (string.Equals(stage, "battle-finalized", StringComparison.Ordinal) && activeRecord != null)
            {
                activeRecord.EndedUtc = DateTime.UtcNow.ToString("O");
                if (string.IsNullOrWhiteSpace(activeRecord.Result)) activeRecord.Result = "Ended";
                activeRecord.ReplayState = MatchReplayStates.Rejected;
                activeRecord.CaptureDiagnostics = Diagnostics.ToList();
                activeRecord.StatisticsJson = AuraSharedJson.SerializeCompact(AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
                terminalSummary = activeRecord;
                ResetNoLock();
            }
        }
        if (terminalSummary != null)
        {
            try
            {
                MatchRecordStorage.Database.SaveSummaryV17(
                    terminalSummary,
                    MatchAnalysisBuilder.BuildSummary(terminalSummary),
                    rejected: true);
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[MatchRecords] terminal capture failure summary could not be saved: " + ex.Message);
            }
        }
    }

    internal static string CurrentRuntimeFingerprint()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(item => !item.IsDynamic)
            .Select(item => item.GetName().Name + "|" + item.GetName().Version)
            .OrderBy(item => item, StringComparer.Ordinal);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", assemblies)))
            .Select(item => item.ToString("x2")));
    }

    private static bool CaptureMaterializedBaselineNoLock()
    {
        if (pendingHeader == null || activeRecord == null || catalog == null || FightManager.Instance == null) return false;
        var baselineStarted = Stopwatch.GetTimestamp();
        catalog.CaptureBackground(GameApp.Instance?.NowBackground);
        var baselineMilliseconds = (Stopwatch.GetTimestamp() - baselineStarted) * 1000d / Stopwatch.Frequency;
        if (baselineMilliseconds >= 8d)
            AuraToolsLog.Warn("[MatchRecords:perf] visible baseline capture was slow: elapsedMs="
                              + baselineMilliseconds.ToString("0.###") + ".");
        var initial = ReplayFactCaptureV17.CaptureVisibleState(
            roundSequence, actorTurnSequence, catalog, activeRecord.RecordId);
        if (initial.Entities.Count == 0) return false;
        initial.BattlePhase = "Materialized";
        foreach (var entity in initial.Entities)
        {
            entity.SpawnGeneration = 1;
            EntityGenerations[entity.EntityId] = 1;
        }
        builder = new ReplayJournalBuilderV17(pendingHeader, initial);
        pendingHeader = null;
        startedTimestamp = Stopwatch.GetTimestamp();
        var bootstrap = builder.StartTransaction(
            ReplayTransactionKindsV17.Bootstrap,
            0,
            roundSequence,
            actorTurnSequence,
            initial.ActiveActorId);
        builder.AddTruthMarker(bootstrap, ReplayEventTypesV17.BattleMaterialized, 0, initial.ActiveActorId);
        foreach (var entity in initial.Entities)
        {
            var binding = ReplayFactCaptureV17.CaptureBinding(entity, catalog);
            builder.AddPresentation(bootstrap, ReplayEventTypesV17.EntityPresented, new ReplayPresentationMessageV17
            {
                Kind = "Entity",
                ActorId = entity.EntityId,
                AnimationState = "Idle",
                EntityBinding = binding
            }, 0, entity.EntityId);
            PresentedEntities.Add(EntityKey(entity.EntityId, entity.SpawnGeneration));
        }
        builder.CompleteTransaction(bootstrap, 1);
        DrainPendingSharedPresentationsNoLock();
        BeginCapturePersistenceNoLock();
        CaptureNativeBgm(AudioManager.Instance, Array.Empty<object>());
        return true;
    }

    private static bool CaptureMaterializedBaselineGuardedNoLock()
    {
        try
        {
            return CaptureMaterializedBaselineNoLock();
        }
        catch (Exception ex)
        {
            preBaselineActivityMissed = true;
            AddDiagnosticNoLock("materialized-baseline-capture-failed:"
                                + ex.GetType().Name + ":" + (ex.Message ?? "").Substring(0, Math.Min(256, ex.Message?.Length ?? 0)));
            return false;
        }
    }

    private static void BeginCapturePersistenceNoLock()
    {
        if (capturePersistenceStarted || activeRecord == null || builder == null || catalog == null) return;
        var firstBatch = CreateCaptureBatchNoLock()
                         ?? throw new InvalidOperationException("Replay baseline produced no durable journal batch.");
        activeRecord.ReplayState = MatchReplayStates.Recording;
        MatchRecordStorage.Database.BeginCaptureV17(
            activeRecord,
            builder.Document.Header,
            builder.Document.InitialState,
            firstBatch);
        capturePersistenceStarted = true;
    }

    private static void QueueCaptureBatchNoLock()
    {
        if (!capturePersistenceStarted || activeRecord == null) return;
        var batch = CreateCaptureBatchNoLock();
        if (batch == null) return;
        var recordId = activeRecord.RecordId;
        var database = MatchRecordStorage.Database;
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<bool>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV17.CaptureBatch." + recordId + "." + batch.BatchIndex,
            Source = "MatchRecords.ReplayV17.CaptureBatch",
            Kind = AuraSharedBackgroundWorkKind.Io,
            Work = _ => database.AppendCaptureBatchV17(recordId, batch),
            ApplyOnMainThread = stored =>
            {
                if (stored) return;
                lock (Gate)
                    if (string.Equals(activeRecord?.RecordId, recordId, StringComparison.Ordinal))
                        AuraToolsLog.Warn("[MatchRecords] incremental capture batch session was unavailable: record="
                                          + recordId + ", batch=" + batch.BatchIndex + ".");
            },
            OnFailedOnMainThread = exception =>
            {
                lock (Gate)
                    if (string.Equals(activeRecord?.RecordId, recordId, StringComparison.Ordinal))
                        AuraToolsLog.Warn("[MatchRecords] incremental capture batch write failed: record="
                                          + recordId + ", batch=" + batch.BatchIndex
                                          + ", error=" + exception.GetType().Name + ".");
            }
        });
        if (!accepted && !database.AppendCaptureBatchV17(recordId, batch))
            AuraToolsLog.Warn("[MatchRecords] incremental capture batch scheduler rejected and session was unavailable: record="
                              + recordId + ", batch=" + batch.BatchIndex + ".");
    }

    private static ReplayCaptureBatchV17? CreateCaptureBatchNoLock()
    {
        if (builder == null || catalog == null) return null;
        var mutablePresentationSequences = PendingActionPresentations.Values
            .Select(item => item.Event.Sequence)
            .Concat(PendingCardMotions.Values.Select(item => item.Event.Sequence))
            .Where(item => item > 0L)
            .ToList();
        var lastDurableSequence = ReplayDurableJournalPrefixV17.LastDurableSequence(
            builder.Document,
            Ledger.OpenEntries.Select(item => item.TransactionId),
            mutablePresentationSequences);
        var lastPersistedSequence = Math.Max(
            persistedTruthEventCount > 0
                ? builder.Document.TruthEvents[persistedTruthEventCount - 1].Sequence
                : 0L,
            persistedPresentationEventCount > 0
                ? builder.Document.PresentationEvents[persistedPresentationEventCount - 1].Sequence
                : 0L);
        if (lastDurableSequence < lastPersistedSequence)
            throw new InvalidOperationException(
                "Replay durability watermark moved behind an already persisted event: "
                + lastDurableSequence + " < " + lastPersistedSequence + ".");
        var truth = builder.Document.TruthEvents.Skip(persistedTruthEventCount)
            .TakeWhile(item => item.Sequence <= lastDurableSequence)
            .Select(ReplayFastCloneV17.Event).ToList();
        var presentation = builder.Document.PresentationEvents.Skip(persistedPresentationEventCount)
            .TakeWhile(item => item.Sequence <= lastDurableSequence)
            .Select(ReplayFastCloneV17.Event).ToList();
        var all = truth.Concat(presentation).OrderBy(item => item.Sequence).ToList();
        if (all.Count == 0) return null;
        var catalogChanged = catalog.Revision != persistedCatalogRevision;
        var batch = new ReplayCaptureBatchV17
        {
            BatchIndex = captureBatchIndex++,
            FirstSequence = all[0].Sequence,
            LastSequence = all[all.Count - 1].Sequence,
            TruthEvents = truth,
            PresentationEvents = presentation,
            Presentation = catalogChanged ? catalog.Capsule : null,
            Assets = catalogChanged ? catalog.SnapshotAssets() : new List<ReplayAssetV17>()
        };
        persistedTruthEventCount += truth.Count;
        persistedPresentationEventCount += presentation.Count;
        persistedCatalogRevision = catalog.Revision;
        return batch;
    }

    private static string BeginSourceTransactionNoLock(ReplayCapturedActionSourceV17 source, bool pushContext = true)
    {
        var parent = ContextStack.LastOrDefault() ?? "";
        var ownsActorTurn = string.IsNullOrWhiteSpace(parent) && IsActorActionKind(source.Kind);
        if (ownsActorTurn)
        {
            actorTurnSequence++;
            var turnState = builder!.CurrentState;
            turnState.RoundSequence = roundSequence;
            turnState.ActorTurnSequence = actorTurnSequence;
            turnState.ActiveActorId = source.ActorId;
            var turnPrelude = BeginSystemTransactionNoLock(
                ReplayTransactionKindsV17.SystemPhase,
                "ActorTurnPrelude",
                source.ActorId);
            ApplyObservedStateNoLock(turnPrelude, turnState);
            MarkAndCompleteSystemTransactionNoLock(turnPrelude);
        }
        var transactionId = builder!.StartTransaction(
            source.Kind,
            ElapsedTicks(),
            roundSequence,
            actorTurnSequence,
            source.ActorId,
            source.SourceInstanceId,
            source.DescriptorId,
            source.Label,
            string.IsNullOrWhiteSpace(source.IssuerPlayerId) ? RoleTable.Instance?.Id ?? "" : source.IssuerPlayerId,
            (activeRecord?.SessionId ?? "battle") + "|"
            + (string.IsNullOrWhiteSpace(source.IssuerPlayerId) ? RoleTable.Instance?.Id ?? "" : source.IssuerPlayerId)
            + "|" + (++sourceSequence).ToString("D10"),
            parent);
        Ledger.Begin(transactionId, source.Kind, source.ActorId, source.SourceInstanceId, parent);
        Transactions[transactionId] = new CaptureTransaction
        {
            TransactionId = transactionId,
            Source = source,
            OwnsActorTurn = ownsActorTurn
        };
        if (ownsActorTurn)
        {
            builder.AddTruthMarker(transactionId, ReplayEventTypesV17.ActorTurnStarted, ElapsedTicks(), source.ActorId);
        }
        var sourceTicks = ElapsedTicks();
        var sourcePresentation = builder.AddPresentation(transactionId, ReplayEventTypesV17.SourcePresented, new ReplayPresentationMessageV17
        {
            Kind = source.Kind,
            DescriptorId = source.DescriptorId,
            ActorId = source.ActorId,
            SourceInstanceId = source.SourceInstanceId,
            SourceZone = source.SourceZone,
            SourceSlot = source.SourceSlot,
            Phase = ReplayPresentationPhasesV17.SourceFocus,
            PhaseOrdinal = 0,
            DurationTicks = 1L
        }, sourceTicks, source.ActorId);
        Transactions[transactionId].TimedPresentationEvents.Add(sourcePresentation);
        if (IsActorActionKind(source.Kind))
        {
            var actorPresentation = builder.AddPresentation(
                transactionId,
                ReplayEventTypesV17.ActorAnimationPresented,
                new ReplayPresentationMessageV17
                {
                    Kind = "Action",
                    ActorId = source.ActorId,
                    SourceInstanceId = source.SourceInstanceId,
                    AnimationState = string.IsNullOrWhiteSpace(source.AnimationState) ? "Idle" : source.AnimationState,
                    EffectDescriptorId = source.EffectDescriptorId,
                    Phase = ReplayPresentationPhasesV17.ActorFocus,
                    PhaseOrdinal = 2,
                    DurationTicks = 1L
                },
                sourceTicks,
                source.ActorId);
            Transactions[transactionId].TimedPresentationEvents.Add(actorPresentation);
        }
        if (pushContext) ContextStack.Add(transactionId);
        return transactionId;
    }

    private static string FindOpenSourceTransactionNoLock(ReplayCapturedActionSourceV17 source)
    {
        return Transactions.Values
            .Where(item => !item.SourceCompleted
                           && Ledger.OpenEntries.Any(entry =>
                               string.Equals(entry.TransactionId, item.TransactionId, StringComparison.Ordinal)))
            .Where(item => string.Equals(item.Source.ActorId, source.ActorId, StringComparison.Ordinal)
                           && string.Equals(item.Source.SourceInstanceId, source.SourceInstanceId, StringComparison.Ordinal))
            .OrderByDescending(item => item.TransactionId, StringComparer.Ordinal)
            .Select(item => item.TransactionId)
            .FirstOrDefault() ?? "";
    }

    private static void CaptureCameraStateNoLock(ReplayPresentationMessageV17 message)
    {
        var camera = Camera.main;
        if (message == null || camera == null) return;
        message.HasCameraState = true;
        message.CameraPosition = Quantized(camera.transform.position);
        message.CameraRotation = Quantized(camera.transform.eulerAngles);
        message.CameraOrthographicSizeQ16 = (int)Math.Round(camera.orthographicSize * 65_536d);
    }

    private static ReplayVector3Q16V17 Quantized(Vector3 value) => new()
    {
        X = (int)Math.Round(value.x * 65_536d),
        Y = (int)Math.Round(value.y * 65_536d),
        Z = (int)Math.Round(value.z * 65_536d)
    };

    private static bool PresentationMatchesTransactionNoLock(
        string transactionId,
        string actorId,
        string sourceInstanceId)
    {
        return !string.IsNullOrWhiteSpace(transactionId)
               && Transactions.TryGetValue(transactionId, out var transaction)
               && string.Equals(transaction.Source.ActorId, actorId ?? "", StringComparison.Ordinal)
               && (string.IsNullOrWhiteSpace(sourceInstanceId)
                   || string.Equals(transaction.Source.SourceInstanceId, sourceInstanceId, StringComparison.Ordinal));
    }

    private static string BeginImplicitTransactionNoLock(IScriptExecutor executor, StatusManager actor, string sourceInstanceId)
    {
        FlushStableBarrierNoLock("before-implicit-presentation");
        var config = executor.dataConfig;
        var classification = ReplayActionSourceClassifierV17.Classify(config?.Type.ToString() ?? "");
        if (!classification.Supported)
            throw new InvalidOperationException(
                "Replay implicit action source is unsupported: " + classification.FailureReason + ".");
        var stableId = ReplayCaptureCatalogV17.Read(config?.data, "Id");
        var descriptorIdentity = ReplayActionSourceClassifierV17.RouteDescriptor(
            classification,
            () =>
            {
                var descriptor = catalog!.RegisterCard(config, stableId);
                return new ReplayActionSourceDescriptorIdentityV17
                {
                    DescriptorId = descriptor.DescriptorId,
                    Name = descriptor.Name
                };
            },
            () =>
            {
                var descriptor = catalog!.RegisterIntent(config, stableId);
                return new ReplayActionSourceDescriptorIdentityV17
                {
                    DescriptorId = descriptor.DescriptorId,
                    Name = descriptor.Name
                };
            });
        AuraToolsLog.Debug("[MatchRecords] implicit action source classified: dataType="
                           + classification.NativeDataType + ", descriptor="
                           + classification.DescriptorKind + ", id=" + stableId + ".");
        var source = new ReplayCapturedActionSourceV17
        {
            Kind = classification.TransactionKind,
            IssuerPlayerId = builder!.CurrentState.Entities.LastOrDefault(item =>
                string.Equals(item.EntityId, actor.InstanceId ?? "", StringComparison.Ordinal))?.OwnerPlayerId ?? "",
            ActorId = actor.InstanceId ?? "",
            SourceInstanceId = sourceInstanceId ?? "",
            DescriptorId = descriptorIdentity.DescriptorId,
            Label = descriptorIdentity.Name,
            AnimationState = ReplayCaptureCatalogV17.First(
                ReplayCaptureCatalogV17.Read(config?.Vars, "Action"),
                ReplayCaptureCatalogV17.Read(config?.data, "Action"),
                "Idle"),
            EffectDescriptorId = catalog!.RegisterEffect(ReplayCaptureCatalogV17.First(
                ReplayCaptureCatalogV17.Read(config?.Vars, "Effects"),
                ReplayCaptureCatalogV17.Read(config?.data, "Effects"))),
            SourceZone = classification.SourceZone
        };
        var transactionId = BeginSourceTransactionNoLock(source, pushContext: false);
        ImplicitPresentationTransactions[ImplicitKey(source.ActorId, source.SourceInstanceId)] = transactionId;
        return transactionId;
    }

    private static void EndCurrentSourceNoLock(ReplayCapturedActionSourceV17? latest)
    {
        if (ContextStack.Count == 0) return;
        var transactionId = ContextStack[ContextStack.Count - 1];
        ContextStack.RemoveAt(ContextStack.Count - 1);
        if (latest != null) Transactions[transactionId].Source = latest;
        ApplyCurrentStateNoLock(transactionId);
        MarkSourceCompletedNoLock(transactionId);
        RequestStableBarrierNoLock("source-completed", needsStateCapture: true);
    }

    private static void ApplyCurrentStateNoLock(string transactionId)
    {
        var observed = ReplayFactCaptureV17.CaptureVisibleState(
            roundSequence, actorTurnSequence, catalog!, activeRecord?.RecordId ?? "");
        if (Transactions.TryGetValue(transactionId, out var transaction)
            && !string.IsNullOrWhiteSpace(transaction.Source.ActorId))
            observed.ActiveActorId = transaction.Source.ActorId;
        AssignEntityGenerationsNoLock(observed);
        ApplyObservedStateNoLock(transactionId, observed);
    }

    private static void ApplyObservedStateNoLock(string transactionId, ReplayVisibleStateV17 observed)
    {
        var added = builder!.ApplyObservedState(transactionId, observed, ElapsedTicks());
        stateWatermark++;
        foreach (var delta in added.Where(item => item.EventType == ReplayEventTypesV17.StateDeltaApplied))
        {
            var commit = builder.AddPresentation(
                transactionId,
                ReplayEventTypesV17.VisualStateCommitted,
                new ReplayPresentationMessageV17
                {
                    Kind = "VisibleStateCommit",
                    ActorId = observed.ActiveActorId ?? "",
                    Phase = ReplayPresentationPhasesV17.StateCommit,
                    PhaseOrdinal = 4,
                    TruthEventSequence = delta.Sequence,
                    DurationTicks = 1
                },
                delta.TimeTicks,
                observed.ActiveActorId ?? "");
            if (Transactions.TryGetValue(transactionId, out var captureTransaction))
                captureTransaction.TimedPresentationEvents.Add(commit);
        }
        foreach (var spawn in added.Where(item => item.EventType == ReplayEventTypesV17.EntitySpawned && item.Entity != null))
        {
            var entity = spawn.Entity!;
            var key = EntityKey(entity.EntityId, entity.SpawnGeneration);
            if (!PresentedEntities.Add(key)) continue;
            var binding = ReplayFactCaptureV17.CaptureBinding(entity, catalog!);
            builder.AddPresentation(transactionId, ReplayEventTypesV17.EntityPresented, new ReplayPresentationMessageV17
            {
                Kind = "Entity",
                ActorId = entity.EntityId,
                AnimationState = "Idle",
                EntityBinding = binding
            }, ElapsedTicks(), entity.EntityId);
        }
        foreach (var despawn in added.Where(item => item.EventType == ReplayEventTypesV17.EntityDespawned))
            PresentedEntities.Remove(EntityKey(despawn.EntityId, despawn.SpawnGeneration));
        DrainPendingSharedPresentationsNoLock();
    }

    private static void AssignEntityGenerationsNoLock(ReplayVisibleStateV17 observed)
    {
        var active = builder?.CurrentState.Entities
            .GroupBy(item => item.EntityId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.SpawnGeneration).First(),
                StringComparer.Ordinal)
            ?? new Dictionary<string, ReplayEntityStateV17>(StringComparer.Ordinal);
        var pending = new List<ReplayEntityStateV17>();
        var usedSlots = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
        {
            [ReplayTeamsV17.Friendly] = new HashSet<int>(),
            [ReplayTeamsV17.Enemy] = new HashSet<int>(),
            [ReplayTeamsV17.Neutral] = new HashSet<int>()
        };
        foreach (var entity in observed.Entities)
        {
            if (active.TryGetValue(entity.EntityId, out var activeEntity)
                && string.Equals(activeEntity.Team, entity.Team, StringComparison.Ordinal)
                && string.Equals(activeEntity.OwnerPlayerId, entity.OwnerPlayerId, StringComparison.Ordinal))
            {
                entity.SpawnGeneration = activeEntity.SpawnGeneration;
                entity.SlotIndex = activeEntity.SlotIndex;
                EntityGenerations[entity.EntityId] = Math.Max(
                    EntityGenerations.TryGetValue(entity.EntityId, out var known) ? known : 0,
                    activeEntity.SpawnGeneration);
                SlotsFor(usedSlots, entity.Team).Add(entity.SlotIndex);
                continue;
            }
            pending.Add(entity);
        }
        foreach (var entity in pending)
        {
            var next = (EntityGenerations.TryGetValue(entity.EntityId, out var previous) ? previous : 0) + 1;
            EntityGenerations[entity.EntityId] = next;
            entity.SpawnGeneration = next;
            var used = SlotsFor(usedSlots, entity.Team);
            var preferred = Math.Max(0, entity.SlotIndex);
            while (used.Contains(preferred)) preferred++;
            entity.SlotIndex = preferred;
            used.Add(preferred);
        }
    }

    private static HashSet<int> SlotsFor(IDictionary<string, HashSet<int>> slots, string team)
    {
        var key = string.IsNullOrWhiteSpace(team) ? ReplayTeamsV17.Neutral : team;
        if (!slots.TryGetValue(key, out var result))
        {
            result = new HashSet<int>();
            slots[key] = result;
        }
        return result;
    }

    private static void MarkSourceCompletedNoLock(string transactionId)
    {
        if (!Transactions.TryGetValue(transactionId, out var transaction) || transaction.SourceCompleted) return;
        transaction.SourceCompleted = true;
        Ledger.MarkSourceCompleted(transactionId, stateWatermark);
    }

    private static void RequestStableBarrierNoLock(string reason, bool needsStateCapture)
    {
        if (!CanCaptureNoLock()) return;
        stableBarrierRequests++;
        if (!StableBarrier.Request(reason, needsStateCapture)) return;

        var generation = captureGeneration;
        var recordId = activeRecord?.RecordId ?? generation.ToString();
        AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "match-replay-stable-barrier:" + recordId,
            Source = "MatchRecords.ReplayV17.StableBarrier",
            Phase = AuraSharedFramePhase.Reconcile,
            Priority = 100,
            EstimatedCost = 8,
            Action = () => ObserveScheduledStableBarrier(generation),
            OnFailed = (_, exception) => MarkCaptureFailure("stable-barrier", exception)
        });
    }

    private static void FlushStableBarrierNoLock(string reason)
    {
        if (!CanCaptureNoLock())
        {
            StableBarrier.Reset();
            return;
        }

        var completed = Ledger.OpenEntries
            .Where(item => item.SourceCompleted)
            .OrderBy(item => item.OpenSequence)
            .ToList();
        if (!StableBarrier.TryTake(out var batch) && completed.Count == 0) return;

        var started = Stopwatch.GetTimestamp();
        var stateCaptureMilliseconds = 0d;
        var drainMilliseconds = 0d;
        var batchSnapshotMilliseconds = 0d;
        ReplayVisibleStateV17? observed = null;
        var hasResidualState = false;
        if (batch.CaptureState)
        {
            var stateStarted = Stopwatch.GetTimestamp();
            observed = ReplayFactCaptureV17.CaptureVisibleState(
                roundSequence, actorTurnSequence, catalog!, activeRecord?.RecordId ?? "");
            AssignEntityGenerationsNoLock(observed);
            hasResidualState = ReplayStateReducerV17.CreateDiff(builder!.CurrentState, observed).HasChanges;
            stateCaptureMilliseconds = ElapsedMilliseconds(stateStarted);
        }

        var drainStarted = Stopwatch.GetTimestamp();
        while (true)
        {
            var ready = Ledger.ObserveStableBarrier(stateWatermark);
            if (ready.Count == 0) break;
            foreach (var transactionId in ready) CompleteSourceTransactionAtBarrierNoLock(transactionId);
        }
        drainMilliseconds = ElapsedMilliseconds(drainStarted);

        if (hasResidualState && observed != null)
        {
            var passive = BeginSystemTransactionNoLock(
                ReplayTransactionKindsV17.Passive,
                batch.Label + ":" + (string.IsNullOrWhiteSpace(reason) ? "reconcile" : reason));
            ApplyObservedStateNoLock(passive, observed);
            MarkAndCompleteSystemTransactionNoLock(passive);
            stableBarrierStateChanges++;
        }

        var batchStarted = Stopwatch.GetTimestamp();
        QueueCaptureBatchNoLock();
        batchSnapshotMilliseconds = ElapsedMilliseconds(batchStarted);
        stableBarrierRuns++;
        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        stableBarrierTotalMilliseconds += elapsedMilliseconds;
        stableBarrierMaximumMilliseconds = Math.Max(stableBarrierMaximumMilliseconds, elapsedMilliseconds);
        if (elapsedMilliseconds >= 8d)
            AuraToolsLog.Warn("[MatchRecords:perf] stable barrier was slow: elapsedMs="
                              + elapsedMilliseconds.ToString("0.###")
                              + ", completed=" + completed.Count
                              + ", residual=" + hasResidualState
                              + ", stateMs=" + stateCaptureMilliseconds.ToString("0.###")
                              + ", drainMs=" + drainMilliseconds.ToString("0.###")
                              + ", batchMs=" + batchSnapshotMilliseconds.ToString("0.###")
                              + ", reasons=" + string.Join(",", batch.Reasons) + ".");
    }

    private static void CompleteSourceTransactionAtBarrierNoLock(string transactionId)
    {
        if (Transactions.TryGetValue(transactionId, out var timedTransaction))
        {
            var completedTicks = ElapsedTicks();
            if (!HasPendingPresentationTimingNoLock(transactionId))
            {
                foreach (var value in timedTransaction.TimedPresentationEvents)
                    if (value.Presentation != null
                        && value.Presentation.DurationTicks <= 1
                        && (value.EventType == ReplayEventTypesV17.ActorAnimationPresented
                            || value.EventType == ReplayEventTypesV17.HitReactionPresented))
                        value.Presentation.DurationTicks = Math.Max(1L, completedTicks - value.TimeTicks);
            }
        }
        if (Transactions.TryGetValue(transactionId, out var transaction) && transaction.OwnsActorTurn)
            builder!.AddTruthMarker(
                transactionId,
                ReplayEventTypesV17.ActorTurnCompleted,
                ElapsedTicks(),
                transaction.Source.ActorId);
        if (builder!.IsOpen(transactionId)) builder.CompleteTransaction(transactionId, ElapsedTicks());
        Ledger.Complete(transactionId);
        Transactions.Remove(transactionId);
        foreach (var key in RemoteTransactions.Where(item => item.Value == transactionId).Select(item => item.Key).ToList())
            RemoteTransactions.Remove(key);
    }

    private static string BeginSystemTransactionNoLock(string kind, string label, string actorId = "")
    {
        var effectiveActorId = string.IsNullOrWhiteSpace(actorId)
            ? builder!.CurrentState.ActiveActorId
            : actorId;
        var transactionId = builder!.StartTransaction(
            kind,
            ElapsedTicks(),
            roundSequence,
            actorTurnSequence,
            effectiveActorId,
            label: label,
            issuerPlayerId: RoleTable.Instance?.Id ?? "",
            sourceToken: "system-" + (++sourceSequence).ToString("D10"));
        Ledger.Begin(transactionId, kind, effectiveActorId, "");
        Transactions[transactionId] = new CaptureTransaction
        {
            TransactionId = transactionId,
            Source = new ReplayCapturedActionSourceV17
            {
                Kind = kind,
                ActorId = effectiveActorId,
                Label = label
            }
        };
        return transactionId;
    }

    private static void MarkAndCompleteSystemTransactionNoLock(string transactionId)
    {
        MarkSourceCompletedNoLock(transactionId);
        var ready = Ledger.ObserveStableBarrier(stateWatermark);
        if (!ready.Contains(transactionId, StringComparer.Ordinal))
            throw new InvalidOperationException("Replay system transaction did not reach its explicit stable barrier: " + transactionId);
        builder!.CompleteTransaction(transactionId, ElapsedTicks());
        Ledger.Complete(transactionId);
        Transactions.Remove(transactionId);
    }

    private static void RollbackFailedSystemTransactionNoLock(string transactionId, string reason)
    {
        if (string.IsNullOrWhiteSpace(transactionId)) return;
        try
        {
            if (builder?.IsOpen(transactionId) == true)
                builder.AbortTransaction(transactionId, ElapsedTicks(), reason ?? "system-transaction-failed");
        }
        catch (Exception ex)
        {
            AddDiagnosticNoLock("system-transaction-journal-rollback-failed:"
                                + transactionId + ":" + ex.GetType().Name);
        }
        try
        {
            if (Ledger.OpenEntries.Any(item => string.Equals(
                    item.TransactionId,
                    transactionId,
                    StringComparison.Ordinal)))
                Ledger.Abort(transactionId);
        }
        catch (Exception ex)
        {
            AddDiagnosticNoLock("system-transaction-ledger-rollback-failed:"
                                + transactionId + ":" + ex.GetType().Name);
        }
        Transactions.Remove(transactionId);
    }

    private static string ResolveRemoteTransactionNoLock(AuraRemoteCombatActionContext context)
    {
        var exact = RemoteKey(context.ActorId, context.CommandSequence);
        if (RemoteTransactions.TryGetValue(exact, out var value)) return value;
        var openIds = Ledger.OpenEntries.Select(entry => entry.TransactionId).ToHashSet(StringComparer.Ordinal);
        var candidate = RemoteTransactions
            .Where(item => item.Key.StartsWith((context.ActorId ?? "") + "|", StringComparison.Ordinal))
            .Select(item => item.Value)
            .LastOrDefault(openIds.Contains);
        if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        var descriptor = catalog!.RegisterCard(
            null,
            ReplayCaptureCatalogV17.First(context.EffectName, "remote-action"));
        var actorEntityId = ResolveActorEntityIdNoLock(context.ActorId);
        var source = new ReplayCapturedActionSourceV17
        {
            Kind = ReplayTransactionKindsV17.ImplicitObserved,
            IssuerPlayerId = context.ActorId ?? "",
            ActorId = actorEntityId,
            SourceInstanceId = "remote-action|" + context.CommandSequence,
            DescriptorId = descriptor.DescriptorId,
            Label = "Remote action",
            AnimationState = "Idle",
            EffectDescriptorId = catalog.RegisterEffect(context.EffectName)
        };
        return BeginSourceTransactionNoLock(source, pushContext: false);
    }

    private static string ResolveActorEntityIdNoLock(string? actorOrPlayerId)
    {
        var identity = actorOrPlayerId ?? "";
        var state = builder?.CurrentState;
        if (state == null) return identity;
        return state.Entities.LastOrDefault(item =>
                   string.Equals(item.EntityId, identity, StringComparison.Ordinal))?.EntityId
               ?? state.Entities.LastOrDefault(item =>
                   string.Equals(item.OwnerPlayerId, identity, StringComparison.Ordinal))?.EntityId
               ?? identity;
    }

    private static ReplayAudioCueV17 RecordAudioNoLock(
        AudioClip clip,
        string bus,
        string kind,
        string usage,
        string provenance)
    {
        var standalone = ContextStack.Count == 0;
        var transactionId = standalone
            ? BeginSystemTransactionNoLock(ReplayTransactionKindsV17.SystemPhase, kind)
            : ContextStack[ContextStack.Count - 1];
        var cue = new ReplayAudioCueV17
        {
            Kind = kind ?? "Audio",
            Bus = string.IsNullOrWhiteSpace(bus) ? "Effect" : bus,
            StartSample = ElapsedTicks() * 48_000L / ReplayProtocolV17.TimebaseTicksPerSecond,
            DurationSamples = clip.frequency <= 0
                ? 0L
                : Math.Max(1L, clip.samples * 48_000L / clip.frequency),
            ResourcePath = string.IsNullOrWhiteSpace(provenance) ? clip.name ?? "" : provenance.Trim(),
            ProviderId = string.IsNullOrWhiteSpace(usage) ? "" : usage.Trim(),
            GainQ16 = 65_536
        };
        var audioEvent = builder!.AddPresentation(transactionId, ReplayEventTypesV17.AudioPresented, new ReplayPresentationMessageV17
        {
            Kind = kind ?? "Audio",
            ActorId = builder.CurrentState.ActiveActorId,
            Audio = cue
        }, ElapsedTicks(), builder.CurrentState.ActiveActorId);
        cue = audioEvent.Presentation?.Audio
              ?? throw new InvalidOperationException("Replay audio event did not retain its canonical cue.");
        if (standalone)
        {
            MarkSourceCompletedNoLock(transactionId);
            RequestStableBarrierNoLock("standalone-audio-recorded", needsStateCapture: false);
        }
        return cue;
    }

    private static void LimitActiveBgmNoLock()
    {
        if (activeBgmCue == null) return;
        var endSample = ElapsedTicks() * 48_000L / ReplayProtocolV17.TimebaseTicksPerSecond;
        var observedDuration = Math.Max(0L, endSample - activeBgmCue.StartSample);
        if (observedDuration > 0)
            activeBgmCue.DurationSamples = activeBgmCue.DurationSamples > 0
                ? Math.Min(activeBgmCue.DurationSamples, observedDuration)
                : observedDuration;
        activeBgmCue = null;
    }

    private static void OnResolvedPlayback(ResolvedSoundPlayback playback)
    {
        if (playback?.Clip is not AudioClip clip) return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            if (string.Equals(playback.Bus, "Bgm", StringComparison.OrdinalIgnoreCase))
            {
                CaptureNativeBgm(AudioManager.Instance, Array.Empty<object>());
                return;
            }
            RecordAudioNoLock(
                clip,
                playback.Bus,
                playback.Request?.Kind ?? "ResolvedAudio",
                playback.OwnerModId + ":" + playback.ProviderId,
                !string.IsNullOrWhiteSpace(playback.Request?.SourceName)
                    ? playback.Request!.SourceName
                    : !string.IsNullOrWhiteSpace(playback.ProviderId) ? playback.ProviderId : clip.name);
        }
    }

    private static CompletionSnapshot? DetachCompletionNoLock()
    {
        if (activeRecord == null || builder == null || catalog == null) return null;
        builder.Document.Presentation = catalog.DetachCapsule();
        var canonicalAssets = catalog.DetachAssets();
        builder.Document.Assets = canonicalAssets;
        builder.Document.Header.EndedUtc = DateTime.UtcNow.ToString("O");
        builder.Document.Header.Result = TerminalGate.Result.Length == 0 ? "Unknown" : TerminalGate.Result;
        activeRecord.Result = builder.Document.Header.Result;
        activeRecord.EndedUtc = builder.Document.Header.EndedUtc;
        activeRecord.TurnCount = Math.Max(1, roundSequence);
        activeRecord.EventCount = builder.Document.TruthEvents.Count + builder.Document.PresentationEvents.Count;
        activeRecord.StatisticsJson = AuraSharedJson.SerializeCompact(AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
        activeRecord.CaptureDiagnostics = Diagnostics.ToList();
        AuraToolsLog.Info("[MatchRecords:perf] stable barriers: requests=" + stableBarrierRequests
                          + ", runs=" + stableBarrierRuns
                          + ", stateChanges=" + stableBarrierStateChanges
                          + ", totalMs=" + stableBarrierTotalMilliseconds.ToString("0.###")
                          + ", maxMs=" + stableBarrierMaximumMilliseconds.ToString("0.###") + ".");
        var envelope = new ReplayDocumentEnvelopeV17 { Document = builder.Document };
        activeRecord.ReplayState = MatchReplayStates.Finalizing;
        MatchRecordStorage.Database.SaveFinalizingCaptureV17(
            activeRecord,
            envelope,
            Diagnostics);
        var result = new CompletionSnapshot(
            activeRecord,
            envelope,
            Diagnostics.ToList());
        ResetNoLock();
        return result;
    }

    private static void QueueFinalization(CompletionSnapshot completion)
    {
        var database = MatchRecordStorage.Database;
        var limit = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit;
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<FinalizationResult>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV17.Finalize." + completion.Record.RecordId,
            Source = "MatchRecords.ReplayV17.Finalize",
            Kind = AuraSharedBackgroundWorkKind.Io,
            Work = _ => FinalizeDetached(completion, database, limit),
            ApplyOnMainThread = LogFinalization,
            OnFailedOnMainThread = ex =>
            {
                AuraToolsLog.Warn("[MatchRecords] v17 background finalization failed: " + ex.Message);
                LogFinalization(FinalizeDetached(completion, database, limit));
            }
        });
        if (!accepted) LogFinalization(FinalizeDetached(completion, database, limit));
    }

    private static FinalizationResult FinalizeDetached(
        CompletionSnapshot completion,
        MatchRecordDatabase database,
        int limit)
    {
        try
        {
            var validation = ReplayDocumentFinalizerV17.FinalizeAndValidate(completion.Envelope);
            completion.Record.ContentSha256 = completion.Envelope.DeclaredDocumentRoot;
            completion.Record.EventCount = completion.Envelope.Document.TruthEvents.Count
                                           + completion.Envelope.Document.PresentationEvents.Count;
            var analysis = MatchAnalysisBuilder.BuildV17(completion.Record, completion.Envelope.Document);
            var diagnostics = completion.Diagnostics.Concat(validation.Errors).Distinct(StringComparer.Ordinal).ToList();
            if (!validation.IsValid || diagnostics.Count > 0)
            {
                completion.Record.CaptureDiagnostics = diagnostics;
                var saved = database.SaveSummaryV17(completion.Record, analysis, rejected: true);
                return new FinalizationResult
                {
                    Stored = saved,
                    RecordId = completion.Record.RecordId,
                    Message = saved
                        ? "对局摘要已保存；v17 结构化回放被拒绝：" + string.Join("; ", diagnostics)
                        : "v17 回放和摘要均未保存。"
                };
            }
            var stored = database.SaveV17(
                completion.Record,
                completion.Envelope,
                analysis,
                AuraToolsConfigService.MatchExperience.MatchRecords.Replay.ChunkTargetBytes);
            var removed = stored ? database.EnforceAutoLimit(limit) : 0;
            return new FinalizationResult
            {
                Stored = stored,
                ReplayReady = stored,
                RecordId = completion.Record.RecordId,
                Removed = removed,
                Message = stored ? "Replay Document v17 已验证并保存。" : "记录 ID 已存在，v17 回放未重复保存。",
                Record = stored ? completion.Record : null,
                Envelope = stored ? completion.Envelope : null,
                Analysis = stored ? analysis : null
            };
        }
        catch (Exception ex)
        {
            try
            {
                completion.Record.CaptureDiagnostics = completion.Diagnostics.Concat(new[] { "v17-finalization:" + ex.Message })
                    .Distinct(StringComparer.Ordinal).ToList();
                var analysis = MatchAnalysisBuilder.BuildV17(completion.Record, completion.Envelope.Document);
                var stored = database.SaveSummaryV17(completion.Record, analysis, rejected: true);
                return new FinalizationResult
                {
                    Stored = stored,
                    RecordId = completion.Record.RecordId,
                    Message = stored ? "仅保存了对局摘要：" + ex.Message : ex.Message
                };
            }
            catch (Exception fallback)
            {
                return new FinalizationResult
                {
                    RecordId = completion.Record.RecordId,
                    Message = ex.Message + "; summary storage failed: " + fallback.Message
                };
            }
        }
    }

    private static void LogFinalization(FinalizationResult result)
    {
        if (result.Stored && result.ReplayReady && result.Record != null && result.Envelope != null && result.Analysis != null)
            ReplayNetworkAuthorityV17.PublishCanonical(result.Record, result.Envelope);
        if (result.Stored)
            AuraToolsLog.Info("[MatchRecords] " + result.Message + " record=" + result.RecordId
                              + ", ready=" + result.ReplayReady
                              + (result.Removed > 0 ? ", retention-removed=" + result.Removed : "") + ".");
        else AuraToolsLog.Warn("[MatchRecords] " + result.Message + " record=" + result.RecordId + ".");
    }

    private static bool CanCaptureNoLock() =>
        BaselineGate.CanCaptureTimeline
        && builder != null
        && activeRecord != null
        && catalog != null
        && FightManager.Instance != null;

    private static bool RequireCaptureForActivityNoLock(string activity)
    {
        if (CanCaptureNoLock()) return true;
        if (pendingHeader != null && BaselineGate.AwaitingMaterializedCommit)
        {
            preBaselineActivityMissed = true;
            AddDiagnosticNoLock("pre-baseline-activity-missed:" + activity);
        }
        return false;
    }

    private static long ElapsedTicks()
    {
        if (startedTimestamp == 0) return 0;
        var elapsed = Stopwatch.GetTimestamp() - startedTimestamp;
        return Math.Max(0L, (long)(elapsed * (double)ReplayProtocolV17.TimebaseTicksPerSecond / Stopwatch.Frequency));
    }

    private static double ElapsedMilliseconds(long startedTimestampValue) =>
        (Stopwatch.GetTimestamp() - startedTimestampValue) * 1000d / Stopwatch.Frequency;

    private static void AddDiagnosticNoLock(string message)
    {
        if (Diagnostics.Count < 64 && !Diagnostics.Contains(message, StringComparer.Ordinal)) Diagnostics.Add(message);
    }

    private static void AbortUndrainedNoLock()
    {
        if (builder == null) return;
        var entries = Ledger.OpenEntries.ToDictionary(item => item.TransactionId, StringComparer.Ordinal);
        foreach (var transactionId in Ledger.AbortAll())
        {
            if (builder.IsOpen(transactionId)) builder.AbortTransaction(transactionId, ElapsedTicks(), "terminal-undrained");
            entries.TryGetValue(transactionId, out var entry);
            AddDiagnosticNoLock("transaction-undrained:" + transactionId
                                + ":kind=" + (entry?.Kind ?? "unknown")
                                + ":sourceCompleted=" + (entry?.SourceCompleted ?? false)
                                + ":terminalSealed=" + (entry?.TerminalSourceSealed ?? false)
                                + ":pendingAssets=" + (entry?.PendingAssets.Count ?? 0));
        }
    }

    private static void ResetNoLock()
    {
        try { sharedPresentationCapture?.Dispose(); }
        catch (Exception ex) { AuraToolsLog.Warn("[MatchRecords] shared presentation capture cleanup failed: " + ex.Message); }
        sharedPresentationCapture = null;
        AudioArbiterRuntime.ResolvedPlayback -= OnResolvedPlayback;
        ReplayNetworkAuthorityV17.CapabilityChanged -= OnReplayCapabilityChanged;
        NativeAudioCalls.Reset();
        BaselineGate.Reset();
        TerminalGate.Reset();
        Ledger.Reset();
        StableBarrier.Reset();
        ContextStack.Clear();
        CardLifecycleScopes.Clear();
        Transactions.Clear();
        RemoteTransactions.Clear();
        RemoteCardCommands.Clear();
        RemoteActionAnimations.Clear();
        ImplicitPresentationTransactions.Clear();
        PresentedEntities.Clear();
        EntityGenerations.Clear();
        PendingActionPresentations.Clear();
        PendingCardMotions.Clear();
        PendingActionObservations.Clear();
        PendingCardMotionObservations.Clear();
        PendingSharedPresentations.Clear();
        Diagnostics.Clear();
        activeRecord = null;
        pendingHeader = null;
        builder = null;
        catalog = null;
        startedTimestamp = 0;
        stateWatermark = 0;
        sourceSequence = 0;
        roundSequence = 1;
        actorTurnSequence = 0;
        firstRoundSeen = false;
        preBaselineActivityMissed = false;
        lastBgmClipInstanceId = 0;
        activeBgmCue = null;
        captureGeneration++;
        stableBarrierRequests = 0;
        stableBarrierRuns = 0;
        stableBarrierStateChanges = 0;
        stableBarrierTotalMilliseconds = 0d;
        stableBarrierMaximumMilliseconds = 0d;
        persistedTruthEventCount = 0;
        persistedPresentationEventCount = 0;
        captureBatchIndex = 0;
        persistedCatalogRevision = -1;
        capturePersistenceStarted = false;
    }

    private static T? Argument<T>(object[]? arguments, int index) =>
        arguments != null && index >= 0 && index < arguments.Length && arguments[index] is T value ? value : default;

    private static string EntityKey(string entityId, int generation) => entityId + "|" + generation;
    private static string RemoteKey(string actorId, long sequence) => (actorId ?? "") + "|" + sequence;
    private static string ImplicitKey(string actorId, string sourceId) => (actorId ?? "") + "|" + (sourceId ?? "");

    private static DataConfig? NativeCardData(object? value)
    {
        if (value is DataConfig config) return config;
        if (value == null) return null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = value.GetType();
        return type.GetField("cardData", flags)?.GetValue(value) as DataConfig
               ?? type.GetProperty("cardData", flags)?.GetValue(value) as DataConfig;
    }

    private static string NativeCardMotionKind(object[] arguments)
    {
        var cardUseData = arguments.Length > 0 ? arguments[0] : null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var burning = cardUseData != null
                      && ((cardUseData.GetType().GetField("isBurning", flags)?.GetValue(cardUseData) as bool?)
                          ?? (cardUseData.GetType().GetProperty("isBurning", flags)?.GetValue(cardUseData) as bool?)
                          ?? false);
        if (burning) return "NativeCardBurn";
        var toThrow = arguments.Length <= 1 || arguments[1] is not bool value || value;
        return toThrow ? "NativeCardDiscard" : "NativeCardReturn";
    }

    private static int NativeDisplayedCost(DataConfig config)
    {
        var value = ReplayCaptureCatalogV17.First(
            ReplayCaptureCatalogV17.Read(config?.Vars, "CurrentCost"),
            ReplayCaptureCatalogV17.Read(config?.Vars, "Expend"),
            ReplayCaptureCatalogV17.Read(config?.data, "Expend"),
            "0");
        return int.TryParse(value, out var result) ? Math.Max(0, result) : 0;
    }

    private static void ScheduleActionPresentationObservationNoLock(
        string key,
        ActionPresentationObservation observation)
    {
        var generation = captureGeneration;
        var poll = ++observation.PollSequence;
        if (AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "match-replay-action-visual:" + key + ":" + poll,
                Source = "MatchRecords.ReplayV17.ActionVisualCompletion",
                Phase = AuraSharedFramePhase.Reconcile,
                Priority = 110,
                EstimatedCost = 1,
                Action = () => ObserveActionPresentationCompletion(generation, key),
                OnFailed = (_, exception) => MarkCaptureFailure("action-visual-completion", exception)
            })) return;
        PendingActionObservations.Remove(key);
        AddDiagnosticNoLock("action-visual-observer-scheduling-failed:" + key);
        CompletePresentationTimingNoLock(PendingActionPresentations, key);
    }

    private static void ObserveActionPresentationCompletion(long generation, string key)
    {
        lock (Gate)
        {
            if (generation != captureGeneration
                || !PendingActionObservations.TryGetValue(key, out var observation)) return;
            var now = ElapsedTicks();
            CaptureActionPresentationSamplesNoLock(observation, now);
            if (IsActionPresentationActive(observation))
            {
                observation.SawActivity = true;
                observation.QuietFrames = 0;
                observation.FirstQuietTicks = 0;
            }
            else
            {
                if (observation.QuietFrames == 0) observation.FirstQuietTicks = now;
                observation.QuietFrames++;
            }
            if (now - observation.StartTicks > 30_000_000L)
            {
                PendingActionObservations.Remove(key);
                AddDiagnosticNoLock("action-visual-observer-timeout:" + key);
                CompletePresentationTimingNoLock(PendingActionPresentations, key, now);
                FlushStableBarrierNoLock("action-visual-timeout");
                return;
            }
            if (observation.QuietFrames >= 2)
            {
                PendingActionObservations.Remove(key);
                CompletePresentationTimingNoLock(
                    PendingActionPresentations,
                    key,
                    Math.Max(observation.StartTicks + 1, observation.FirstQuietTicks));
                FlushStableBarrierNoLock("action-visual-complete");
                return;
            }
            ScheduleActionPresentationObservationNoLock(key, observation);
        }
    }

    private static bool IsActionPresentationActive(ActionPresentationObservation observation)
    {
        if (observation.FightUi != null && observation.FightUi.NowAnimation) return true;
        var activeCounts = observation.FightUi == null
            ? null
            : ActiveActionAnimationCountsField?.GetValue(observation.FightUi) as IDictionary;
        foreach (var status in observation.Statuses.Where(item => item != null))
        {
            if (activeCounts?.Contains(status) == true
                && Convert.ToInt32(activeCounts[status]) > 0) return true;
            if (status.animatedState != IStatusManager.AnimatedState.Idle) return true;
            if (status.transform != null && DOTween.IsTweening(status.transform, alsoCheckIfIsPlaying: true)) return true;
            var body = status.transform?.Find("body");
            if (body != null && DOTween.IsTweening(body, alsoCheckIfIsPlaying: true)) return true;
        }
        return false;
    }

    private static void CaptureActionPresentationSamplesNoLock(
        ActionPresentationObservation observation,
        long now)
    {
        if (builder == null) return;
        foreach (var status in observation.Statuses.Where(item => item != null))
        {
            var presentation = builder.Document.PresentationEvents.LastOrDefault(item =>
                string.Equals(item.TransactionId, observation.TransactionId, StringComparison.Ordinal)
                && (item.EventType == ReplayEventTypesV17.ActorAnimationPresented
                    || item.EventType == ReplayEventTypesV17.HitReactionPresented)
                && string.Equals(item.Presentation?.ActorId, status.InstanceId ?? "", StringComparison.Ordinal));
            if (presentation?.Presentation == null) continue;
            var sample = ReplayFactCaptureV17.CaptureWorldTransformSample(
                status,
                Math.Max(0L, now - presentation.TimeTicks));
            if (sample == null) continue;
            var values = presentation.Presentation.WorldTransformSamples;
            var previous = values.LastOrDefault();
            if (previous != null
                && previous.WorldPosition.X == sample.WorldPosition.X
                && previous.WorldPosition.Y == sample.WorldPosition.Y
                && previous.WorldPosition.Z == sample.WorldPosition.Z
                && previous.RootScale.X == sample.RootScale.X
                && previous.RootScale.Y == sample.RootScale.Y
                && previous.RootScale.Z == sample.RootScale.Z
                && previous.BodyLocalPosition.X == sample.BodyLocalPosition.X
                && previous.BodyLocalPosition.Y == sample.BodyLocalPosition.Y
                && previous.BodyLocalPosition.Z == sample.BodyLocalPosition.Z
                && previous.BodyLocalScale.X == sample.BodyLocalScale.X
                && previous.BodyLocalScale.Y == sample.BodyLocalScale.Y
                && previous.BodyLocalScale.Z == sample.BodyLocalScale.Z
                && string.Equals(previous.SortingLayerName, sample.SortingLayerName, StringComparison.Ordinal)
                && previous.SortingOrder == sample.SortingOrder) continue;
            if (values.Count >= ReplayLimitsV17.MaximumPresentationSamplesPerEvent)
            {
                AddDiagnosticNoLock("action-motion-track-budget-exceeded:"
                                    + observation.TransactionId + ":" + status.InstanceId);
                continue;
            }
            values.Add(sample);
        }
    }

    private static void ScheduleCardMotionObservationNoLock(string key, CardMotionObservation observation)
    {
        var generation = captureGeneration;
        var poll = ++observation.PollSequence;
        if (AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
            {
                OwnerId = AuraToolsIds.ModId,
                Key = "match-replay-card-visual:" + key + ":" + poll,
                Source = "MatchRecords.ReplayV17.CardVisualCompletion",
                Phase = AuraSharedFramePhase.Reconcile,
                Priority = 110,
                EstimatedCost = 1,
                Action = () => ObserveCardMotionCompletion(generation, key),
                OnFailed = (_, exception) => MarkCaptureFailure("card-visual-completion", exception)
            })) return;
        AddDiagnosticNoLock("card-visual-observer-scheduling-failed:" + key);
        CompleteCardMotionObservationNoLock(
            key,
            observation,
            ElapsedTicks(),
            "SchedulingFailed");
        FlushStableBarrierNoLock("card-visual-scheduling-failed");
    }

    // Shared Reset closes retained/pooled views. Polling remains the generic
    // fallback for native center cards that are destroyed after their tween.
    private static void ObserveCardMotionCompletion(long generation, string key)
    {
        lock (Gate)
        {
            if (generation != captureGeneration
                || !PendingCardMotionObservations.TryGetValue(key, out var observation)) return;
            var now = ElapsedTicks();
            var visual = observation.Visual;
            var visualObject = visual == null ? null : visual.gameObject;
            var visualExists = visualObject != null;
            var activeInHierarchy = visualObject?.activeInHierarchy == true;
            var currentSourceInstanceId = visualExists ? visual?.dataConfig?.InstanceID ?? "" : "";
            var identityChanged = key.Length > 0
                                  && currentSourceInstanceId.Length > 0
                                  && !string.Equals(key, currentSourceInstanceId, StringComparison.Ordinal);
            var immediateReason = ReplayCardVisualLifecycleV17.CompletionReason(
                resetMatched: false,
                visualExists,
                activeInHierarchy,
                identityChanged,
                now - observation.StartTicks);
            if (immediateReason == ReplayCardVisualLifecycleV17.Destroyed
                || immediateReason == ReplayCardVisualLifecycleV17.Inactive
                || immediateReason == ReplayCardVisualLifecycleV17.Rebound)
            {
                CompleteCardMotionObservationNoLock(key, observation, now, immediateReason);
                FlushStableBarrierNoLock("card-visual-" + immediateReason.ToLowerInvariant());
                return;
            }

            CaptureCardMotionSampleNoLock(key, observation, now);
            var reason = ReplayCardVisualLifecycleV17.CompletionReason(
                resetMatched: false,
                visualExists,
                activeInHierarchy,
                identityChanged,
                now - observation.StartTicks);
            if (reason.Length > 0)
            {
                if (reason == ReplayCardVisualLifecycleV17.Timeout)
                {
                    AddDiagnosticNoLock("card-visual-observer-timeout:" + key);
                }
                CompleteCardMotionObservationNoLock(key, observation, now, reason);
                FlushStableBarrierNoLock("card-visual-" + reason.ToLowerInvariant());
                return;
            }
            ScheduleCardMotionObservationNoLock(key, observation);
        }
    }

    private static void CompleteCardMotionObservationNoLock(
        string key,
        CardMotionObservation observation,
        long completedTicks,
        string reason)
    {
        PendingCardMotionObservations.Remove(key);
        CompletePresentationTimingNoLock(PendingCardMotions, key, completedTicks);
        CompleteOwnedCardMotionTransactionNoLock(observation);
        AuraToolsLog.Debug("[MatchRecords] card visual observation completed: source="
                           + key + ", reason=" + (reason ?? "") + ".");
    }

    private static void CaptureCardMotionSampleNoLock(
        string key,
        CardMotionObservation observation,
        long now)
    {
        if (catalog == null
            || observation.Visual == null
            || !PendingCardMotions.TryGetValue(key, out var pending)
            || pending.Event.Presentation == null) return;
        var sample = ReplayFactCaptureV17.CaptureCardTransformSample(
            observation.Visual,
            Math.Max(0L, now - observation.StartTicks),
            catalog.Scene.ReferenceWidth,
            catalog.Scene.ReferenceHeight);
        if (sample == null) return;
        var values = pending.Event.Presentation.TransformSamples;
        var previous = values.LastOrDefault();
        if (previous != null
            && previous.CanvasPosition.X == sample.CanvasPosition.X
            && previous.CanvasPosition.Y == sample.CanvasPosition.Y
            && previous.CanvasSize.X == sample.CanvasSize.X
            && previous.CanvasSize.Y == sample.CanvasSize.Y
            && previous.LocalScale.X == sample.LocalScale.X
            && previous.LocalScale.Y == sample.LocalScale.Y
            && previous.LocalScale.Z == sample.LocalScale.Z
            && previous.RotationZQ16 == sample.RotationZQ16
            && previous.AlphaQ16 == sample.AlphaQ16
            && previous.HasMaterialFade == sample.HasMaterialFade
            && previous.MaterialFadeQ16 == sample.MaterialFadeQ16) return;
        if (values.Count >= ReplayLimitsV17.MaximumPresentationSamplesPerEvent)
        {
            AddDiagnosticNoLock("card-motion-track-budget-exceeded:" + key);
            return;
        }
        values.Add(sample);
    }

    private static void CompleteOwnedCardMotionTransactionNoLock(CardMotionObservation observation)
    {
        if (!observation.OwnsTransaction
            || string.IsNullOrWhiteSpace(observation.TransactionId)
            || !Transactions.ContainsKey(observation.TransactionId)) return;
        ApplyCurrentStateNoLock(observation.TransactionId);
        MarkSourceCompletedNoLock(observation.TransactionId);
        observation.OwnsTransaction = false;
    }

    private static void CompletePresentationTimingNoLock(
        IDictionary<string, PendingPresentationTiming> pending,
        string key,
        long? completedTicks = null)
    {
        if (string.IsNullOrWhiteSpace(key) || !pending.TryGetValue(key, out var timing)) return;
        pending.Remove(key);
        var duration = Math.Max(1L, (completedTicks ?? ElapsedTicks()) - timing.StartTicks);
        if (timing.Event.Presentation != null) timing.Event.Presentation.DurationTicks = duration;
        if (builder != null && timing.Event.EventType == ReplayEventTypesV17.ActorAnimationPresented)
        {
            var endTicks = timing.StartTicks + duration;
            foreach (var related in builder.Document.PresentationEvents.Where(item =>
                         string.Equals(item.TransactionId, timing.Event.TransactionId, StringComparison.Ordinal)
                         && item.EventType == ReplayEventTypesV17.HitReactionPresented
                         && item.Presentation != null
                         && item.Presentation.DurationTicks <= 1))
                related.Presentation!.DurationTicks = Math.Max(1L, endTicks - related.TimeTicks);
        }
    }

    private static ReplayJournalEventV17 RequireActorPresentationNoLock(string transactionId, string actorId) =>
        builder?.Document.PresentationEvents.LastOrDefault(item =>
            string.Equals(item.TransactionId, transactionId, StringComparison.Ordinal)
            && item.EventType == ReplayEventTypesV17.ActorAnimationPresented
            && string.Equals(item.Presentation?.ActorId, actorId ?? "", StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            "Replay action transaction has no guaranteed actor presentation: " + transactionId + ":" + actorId);

    private static ReplayJournalEventV17 RequireCardMotionPresentationNoLock(
        string transactionId,
        string sourceInstanceId,
        long observedTicks)
    {
        var existing = builder?.Document.PresentationEvents.LastOrDefault(item =>
            string.Equals(item.TransactionId, transactionId, StringComparison.Ordinal)
            && item.EventType == ReplayEventTypesV17.CardMotionPresented
            && string.Equals(item.Presentation?.SourceInstanceId, sourceInstanceId, StringComparison.Ordinal));
        if (existing != null) return existing;
        if (builder == null || !Transactions.TryGetValue(transactionId, out var transaction))
            throw new InvalidOperationException("Replay card motion has no owning transaction: " + transactionId + ".");
        var source = transaction.Source;
        var created = builder.AddPresentation(
            transactionId,
            ReplayEventTypesV17.CardMotionPresented,
            new ReplayPresentationMessageV17
            {
                Kind = "NativeCardTravel",
                DescriptorId = source.DescriptorId,
                ActorId = source.ActorId,
                SourceInstanceId = source.SourceInstanceId,
                SourceZone = source.SourceZone,
                SourceSlot = source.SourceSlot,
                Phase = ReplayPresentationPhasesV17.CardTravel,
                PhaseOrdinal = 1,
                DurationTicks = 1L
            },
            observedTicks,
            source.ActorId);
        transaction.TimedPresentationEvents.Add(created);
        return created;
    }

    private static bool HasPendingPresentationTimingNoLock(string transactionId) =>
        PendingActionPresentations.Values.Any(item =>
            string.Equals(item.Event.TransactionId, transactionId, StringComparison.Ordinal))
        || PendingCardMotions.Values.Any(item =>
            string.Equals(item.Event.TransactionId, transactionId, StringComparison.Ordinal));

    private static void SealPendingPresentationTimingsNoLock()
    {
        var terminalTicks = ElapsedTicks();
        foreach (var key in PendingActionPresentations.Keys.ToList())
            CompletePresentationTimingNoLock(PendingActionPresentations, key, terminalTicks);
        foreach (var key in PendingCardMotions.Keys.ToList())
            CompletePresentationTimingNoLock(PendingCardMotions, key, terminalTicks);
        PendingActionObservations.Clear();
        PendingCardMotionObservations.Clear();
    }

    private static bool IsActorActionKind(string kind) => kind == ReplayTransactionKindsV17.Card
                                                          || kind == ReplayTransactionKindsV17.Skill
                                                          || kind == ReplayTransactionKindsV17.Intent
                                                          || kind == ReplayTransactionKindsV17.ImplicitObserved;

    private sealed class CaptureTransaction
    {
        internal string TransactionId { get; set; } = "";
        internal ReplayCapturedActionSourceV17 Source { get; set; } = new();
        internal bool SourceCompleted { get; set; }
        internal bool OwnsActorTurn { get; set; }
        internal List<ReplayJournalEventV17> TimedPresentationEvents { get; } = new();
    }

    private sealed class ActionPresentationObservation
    {
        internal ActionPresentationObservation(
            string transactionId,
            long startTicks,
            FightUI? fightUi,
            List<StatusManager> statuses)
        {
            TransactionId = transactionId ?? "";
            StartTicks = startTicks;
            FightUi = fightUi;
            Statuses = statuses ?? new List<StatusManager>();
        }

        internal string TransactionId { get; }
        internal long StartTicks { get; }
        internal FightUI? FightUi { get; set; }
        internal List<StatusManager> Statuses { get; }
        internal int PollSequence { get; set; }
        internal int QuietFrames { get; set; }
        internal long FirstQuietTicks { get; set; }
        internal bool SawActivity { get; set; }
    }

    private sealed class CardMotionObservation
    {
        internal CardMotionObservation(
            long startTicks,
            FightUI? fightUi,
            HashSet<int> existingInstanceIds)
        {
            StartTicks = startTicks;
            FightUi = fightUi;
            ExistingInstanceIds = existingInstanceIds ?? new HashSet<int>();
        }

        internal long StartTicks { get; }
        internal FightUI? FightUi { get; set; }
        internal HashSet<int> ExistingInstanceIds { get; }
        internal CardItem? Visual { get; set; }
        internal int PollSequence { get; set; }
        internal string TransactionId { get; set; } = "";
        internal bool OwnsTransaction { get; set; }
    }

    private sealed class PendingPresentationTiming
    {
        internal PendingPresentationTiming(long startTicks, ReplayJournalEventV17 value)
        {
            StartTicks = startTicks;
            Event = value;
        }

        internal long StartTicks { get; }
        internal ReplayJournalEventV17 Event { get; }
    }

    private sealed class CompletionSnapshot
    {
        internal CompletionSnapshot(
            MatchRecord record,
            ReplayDocumentEnvelopeV17 envelope,
            IReadOnlyCollection<string> diagnostics)
        {
            Record = record;
            Envelope = envelope;
            Diagnostics = diagnostics;
        }

        internal MatchRecord Record { get; }
        internal ReplayDocumentEnvelopeV17 Envelope { get; }
        internal IReadOnlyCollection<string> Diagnostics { get; }
    }

    private sealed class FinalizationResult
    {
        internal bool Stored { get; set; }
        internal bool ReplayReady { get; set; }
        internal string RecordId { get; set; } = "";
        internal int Removed { get; set; }
        internal string Message { get; set; } = "";
        internal MatchRecord? Record { get; set; }
        internal ReplayDocumentEnvelopeV17? Envelope { get; set; }
        internal MatchAnalysisReport? Analysis { get; set; }
    }
}
