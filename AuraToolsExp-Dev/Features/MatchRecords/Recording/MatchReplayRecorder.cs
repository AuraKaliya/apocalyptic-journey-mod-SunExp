using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraMode.Shared;
using AuraShared.Core;
using AudioArbiter.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Network;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static class MatchReplayRecorder
{
    private static readonly object Gate = new();
    private static readonly List<string> Diagnostics = new();
    private static readonly ReplayAudioAssetCaptureV12 AudioCapture = new();
    private static readonly ReplayNativeAudioCallTracker NativeAudioCalls = new();
    private static readonly MatchReplayBaselineGate BaselineGate = new();
    private static readonly MatchReplayTerminalGate TerminalGate = new();
    private static readonly ReplayTransactionLedgerV12 Ledger = new();
    private static readonly List<string> ContextStack = new();
    private static readonly Stack<bool> CardLifecycleScopes = new();
    private static readonly Dictionary<string, CaptureTransaction> Transactions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> RemoteTransactions = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RemoteCardCommands = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RemoteActionAnimations = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ImplicitPresentationTransactions = new(StringComparer.Ordinal);
    private static readonly HashSet<string> PresentedEntities = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> EntityGenerations = new(StringComparer.Ordinal);

    private static MatchRecord? activeRecord;
    private static ReplayDocumentHeaderCoreV12? pendingHeader;
    private static ReplayJournalBuilderV12? builder;
    private static ReplayCaptureCatalogV12? catalog;
    private static ReplayCaptureCatalogV12? povCatalog;
    private static ReplayPovSidecarV12? pov;
    private static List<ReplayPublicCardStateV12> lastPrivateCards = new();
    private static long startedTimestamp;
    private static long stateWatermark;
    private static long sourceSequence;
    private static long povSequence;
    private static string previousPovHash = "";
    private static int roundSequence = 1;
    private static int actorTurnSequence;
    private static bool firstRoundSeen;
    private static bool preBaselineActivityMissed;
    private static int lastBgmClipInstanceId;
    private static ReplayAudioCueV12? activeBgmCue;

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
        ReplayNetworkAuthorityV12.AnnounceCapability(levelId);
        if (ReplayNetworkAuthorityV12.IsMultiplayer && !ReplayNetworkAuthorityV12.IsHost)
        {
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
                ReplayProtocol = ReplayProtocolV12.DocumentVersion,
                GameBuild = typeof(FightManager).Assembly.GetName().Version?.ToString() ?? "unknown",
                ToolBuild = typeof(AuraToolsMatchRecordsRuntime).Assembly.GetName().Version?.ToString() ?? "unknown",
                ModFingerprint = "",
                RequiredCapabilities = ReplayCapabilitiesV12.Required.ToList(),
                OptionalCapabilities = ReplayCapabilitiesV12.Optional.ToList(),
                InitialState = new MatchReplayInitialState
                {
                    LevelId = levelId
                }
            };
            pendingHeader = new ReplayDocumentHeaderCoreV12
            {
                RecordId = recordId,
                AdventureId = activeRecord.AdventureId,
                BattleSessionId = recordId,
                LevelId = levelId,
                BattleTitle = activeRecord.BattleTitle,
                StartedUtc = now,
                GameBuildProvenance = activeRecord.GameBuild,
                RecorderBuild = activeRecord.ToolBuild
            };
            catalog = new ReplayCaptureCatalogV12();
            povCatalog = new ReplayCaptureCatalogV12();
            pov = new ReplayPovSidecarV12 { PlayerId = RoleTable.Instance?.Id ?? "single-player" };
            BaselineGate.Arm();
            AudioArbiterRuntime.ResolvedPlayback += OnResolvedPlayback;
        }
    }

    internal static void CommitMaterializedBaseline()
    {
        if (FightManager.Instance == null) return;
        lock (Gate)
        {
            BaselineGate.MarkMaterialized();
            if (!ReplayNetworkAuthorityV12.CanHostRecord(pendingHeader?.LevelId ?? "", out _)) return;
            if (!BaselineGate.TryCommit(CaptureMaterializedBaselineGuardedNoLock)) return;
            AuraToolsLog.Debug("[MatchRecords] v12 materialized baseline committed.");
        }
    }

    internal static void BeginCardAction(object? target)
    {
        if (target == null) return;
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("card-action")) return;
            DrainStableBarrierNoLock(createPassiveTransaction: true);
            var source = ReplayFactCaptureV12.CaptureActionSource(target, catalog!);
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
            var source = target == null ? null : ReplayFactCaptureV12.CaptureActionSource(target, catalog!);
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
            var source = target == null ? null : ReplayFactCaptureV12.CaptureActionSource(target, catalog!);
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
        }
    }

    internal static void BeginEnemyIntentAction(object? target, object[]? arguments)
    {
        if (target is not Enemy enemy || enemy.Status == null) return;
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("enemy-intent")) return;
            DrainStableBarrierNoLock(createPassiveTransaction: true);
            var slot = arguments != null && arguments.Length > 0 && arguments[0] is int value ? Math.Max(0, value) : 0;
            var card = enemy.FightAction?.TryGetCard();
            if (card == null && enemy.ActionCards != null && slot < enemy.ActionCards.Count) card = enemy.ActionCards[slot];
            var config = card?.dataConfig;
            if (config == null) return;
            var stableId = ReplayCaptureCatalogV12.First(
                ReplayCaptureCatalogV12.Read(config.data, "Id"),
                ReplayCaptureCatalogV12.Read(config.Vars, "Id"));
            var descriptor = catalog!.RegisterIntent(config, stableId);
            var source = new ReplayCapturedActionSourceV12
            {
                Kind = ReplayTransactionKindsV12.Intent,
                IssuerPlayerId = RoleTable.Instance?.Id ?? "",
                ActorId = enemy.Status.InstanceId ?? enemy.InstanceId ?? "",
                SourceInstanceId = config.InstanceID ?? enemy.Status.InstanceId + "|intent|" + slot,
                DescriptorId = descriptor.DescriptorId,
                Label = descriptor.Name,
                AnimationState = ReplayCaptureCatalogV12.First(
                    ReplayCaptureCatalogV12.Read(config.Vars, "Action"),
                    ReplayCaptureCatalogV12.Read(config.data, "Action"),
                    "Idle"),
                EffectDescriptorId = catalog.RegisterEffect(ReplayCaptureCatalogV12.First(
                    ReplayCaptureCatalogV12.Read(config.Vars, "Effects"),
                    ReplayCaptureCatalogV12.Read(config.data, "Effects"))),
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
                DrainStableBarrierNoLock(createPassiveTransaction: true);
                var config = context.CardData;
                var stableId = ReplayCaptureCatalogV12.Read(config.data, "Id");
                var descriptor = catalog!.RegisterCard(config, stableId);
                var actorEntityId = ResolveActorEntityIdNoLock(context.ActorId);
                var source = new ReplayCapturedActionSourceV12
                {
                    Kind = ReplayTransactionKindsV12.Card,
                    IssuerPlayerId = context.ActorId ?? "",
                    ActorId = actorEntityId,
                    SourceInstanceId = config is DataConfig concrete
                        ? concrete.InstanceID ?? ""
                        : ReplayCaptureCatalogV12.Read(config.Vars, "InstanceID"),
                    DescriptorId = descriptor.DescriptorId,
                    Label = descriptor.Name,
                    AnimationState = ReplayCaptureCatalogV12.First(
                        ReplayCaptureCatalogV12.Read(config.Vars, "Action"),
                        ReplayCaptureCatalogV12.Read(config.data, "Action"),
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
            var message = new ReplayPresentationMessageV12
            {
                Kind = "Action",
                ActorId = transactionActorId,
                AnimationState = Transactions[transactionId].Source.AnimationState,
                EffectDescriptorId = catalog!.RegisterEffect(context.EffectName),
                TargetIds = context.AnimationTargets.Select(item => item.StatusInstanceId ?? "")
                    .Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).ToList(),
                DurationTicks = 1_040_000
            };
            builder!.AddPresentation(transactionId, ReplayEventTypesV12.ActorAnimationPresented, message, ElapsedTicks(), transactionActorId);
            if (!string.IsNullOrWhiteSpace(message.EffectDescriptorId))
                builder.AddPresentation(transactionId, ReplayEventTypesV12.EffectPresented, message, ElapsedTicks(), transactionActorId);
            foreach (var target in context.AnimationTargets)
                builder.AddPresentation(transactionId, ReplayEventTypesV12.HitReactionPresented, new ReplayPresentationMessageV12
                {
                    Kind = "Hit",
                    ActorId = target.StatusInstanceId ?? "",
                    AnimationState = target.AnimationState ?? "Idle",
                    DurationTicks = 360_000
                }, ElapsedTicks(), target.StatusInstanceId ?? "");
            ApplyCurrentStateNoLock(transactionId);
            if (!ContextStack.Contains(transactionId, StringComparer.Ordinal))
                MarkSourceCompletedNoLock(transactionId);
        }
    }

    internal static void ObserveAuthoritativeStatus(AuraAuthoritativeStatusContext context)
    {
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("authoritative-status")) return;
            stateWatermark = Math.Max(stateWatermark, context?.Version ?? 0);
            DrainStableBarrierNoLock(createPassiveTransaction: true);
        }
    }

    internal static void CaptureActionPresentation(object[]? arguments)
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
                                   ?? ReplayCaptureCatalogV12.Read(executor.dataConfig?.Vars, "InstanceID");
            var transactionId = ContextStack.LastOrDefault();
            if (!PresentationMatchesTransactionNoLock(transactionId, actor.InstanceId ?? "", sourceInstanceId)
                && !Ledger.TryBindPresentation(
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
            var presentationTicks = ElapsedTicks();
            builder!.AddPresentation(transactionId, ReplayEventTypesV12.ActorAnimationPresented, new ReplayPresentationMessageV12
            {
                Kind = "Action",
                ActorId = actor.InstanceId ?? "",
                SourceInstanceId = sourceInstanceId,
                AnimationState = string.IsNullOrWhiteSpace(captured.ActorAnimationState) ? "Idle" : captured.ActorAnimationState,
                EffectDescriptorId = effectDescriptorId,
                TargetIds = captured.Targets.Select(item => item.TargetId).Distinct(StringComparer.Ordinal).ToList(),
                DurationTicks = Math.Max(1, captured.PresentationDurationMilliseconds) * 1000L
            }, presentationTicks, actor.InstanceId ?? "");
            if (!string.IsNullOrWhiteSpace(effectDescriptorId))
                builder.AddPresentation(transactionId, ReplayEventTypesV12.EffectPresented, new ReplayPresentationMessageV12
                {
                    Kind = "Effect",
                    ActorId = actor.InstanceId ?? "",
                    EffectDescriptorId = effectDescriptorId,
                    TargetIds = captured.Targets.Select(item => item.TargetId).Distinct(StringComparer.Ordinal).ToList(),
                    DelayTicks = Math.Max(0, captured.EffectDelayMilliseconds) * 1000L,
                    DurationTicks = Math.Max(1, captured.PresentationDurationMilliseconds - captured.EffectDelayMilliseconds) * 1000L
                }, presentationTicks, actor.InstanceId ?? "");
            foreach (var target in captured.Targets)
                builder.AddPresentation(transactionId, ReplayEventTypesV12.HitReactionPresented, new ReplayPresentationMessageV12
                {
                    Kind = "Hit",
                    ActorId = target.TargetId,
                    AnimationState = target.AnimationState,
                    DurationTicks = 360_000
                }, presentationTicks, target.TargetId);
        }
    }

    internal static void CompleteActionPresentation(object[]? arguments)
    {
        if (arguments == null || arguments.Length == 0 || arguments[0] is not IScriptExecutor executor
            || executor.Self is not StatusManager actor)
            return;
        lock (Gate)
        {
            if (!CanCaptureNoLock()) return;
            var key = ImplicitKey(actor.InstanceId, executor.dataConfig?.InstanceID
                ?? ReplayCaptureCatalogV12.Read(executor.dataConfig?.Vars, "InstanceID"));
            var implicitTransaction = ImplicitPresentationTransactions.TryGetValue(key, out var transactionId);
            if (!implicitTransaction)
            {
                var sourceInstanceId = executor.dataConfig?.InstanceID
                                       ?? ReplayCaptureCatalogV12.Read(executor.dataConfig?.Vars, "InstanceID");
                transactionId = ContextStack.LastOrDefault();
                if (!PresentationMatchesTransactionNoLock(transactionId, actor.InstanceId ?? "", sourceInstanceId)
                    && !Ledger.TryBindPresentation(
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
            }
        }
    }

    internal static void ObserveStableBarrier()
    {
        lock (Gate)
        {
            if (builder == null
                && pendingHeader != null
                && !preBaselineActivityMissed
                && BaselineGate.MaterializationObserved
                && FightManager.Instance != null
                && ReplayNetworkAuthorityV12.CanHostRecord(pendingHeader.LevelId, out _)
                && BaselineGate.TryCommit(CaptureMaterializedBaselineGuardedNoLock))
                AuraToolsLog.Debug("[MatchRecords] deferred v12 materialized baseline committed after network negotiation.");
            if (CanCaptureNoLock()) DrainStableBarrierNoLock(createPassiveTransaction: true);
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

    internal static void CaptureCheckpointIfDue() => ObserveStableBarrier();

    internal static void SignalFightStart()
    {
        lock (Gate)
        {
            if (!RequireCaptureForActivityNoLock("fight-start")) return;
            DrainStableBarrierNoLock(createPassiveTransaction: true);
            var transactionId = BeginSystemTransactionNoLock(ReplayTransactionKindsV12.SystemPhase, "FightStart");
            builder!.AddTruthMarker(
                transactionId,
                ReplayEventTypesV12.FightStartSignaled,
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
            DrainStableBarrierNoLock(createPassiveTransaction: true);
            if (firstRoundSeen) roundSequence++;
            else firstRoundSeen = true;
            var transactionId = BeginSystemTransactionNoLock(ReplayTransactionKindsV12.SystemPhase, "RoundStart");
            ApplyCurrentStateNoLock(transactionId);
            builder!.AddTruthMarker(transactionId, ReplayEventTypesV12.RoundStarted, ElapsedTicks(), builder.CurrentState.ActiveActorId);
            MarkAndCompleteSystemTransactionNoLock(transactionId);
        }
    }

    internal static void PrepareCompletion(string result)
    {
        lock (Gate)
        {
            if (builder == null || activeRecord == null || TerminalGate.SettlementPrepared) return;
            DrainStableBarrierNoLock(createPassiveTransaction: true);
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
                        ReplayNetworkAuthorityV12.CanHostRecord(activeRecord.LevelId, out var rejection)
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
                DrainStableBarrierNoLock(createPassiveTransaction: true);
                var outcome = BeginSystemTransactionNoLock(ReplayTransactionKindsV12.Outcome, "Outcome");
                var finalState = ReplayFactCaptureV12.CapturePublicState(roundSequence, actorTurnSequence, catalog!);
                AssignEntityGenerationsNoLock(finalState);
                finalState.BattlePhase = "Finalized";
                finalState.Outcome = TerminalGate.Result.Length == 0 ? result : TerminalGate.Result;
                ApplyObservedStateNoLock(outcome, finalState);
                builder.AddTruthMarker(outcome, ReplayEventTypesV12.OutcomeEntering, ElapsedTicks(), finalState.ActiveActorId);
                builder.AddTruthMarker(outcome, ReplayEventTypesV12.BattleFinalized, ElapsedTicks(), finalState.ActiveActorId);
                MarkAndCompleteSystemTransactionNoLock(outcome);
                LimitActiveBgmNoLock();
                TerminalGate.SealTerminalFrame(result);
                if (!TerminalGate.CanDetach(AudioCapture.PendingCount))
                {
                    AudioCapture.Drained -= OnAudioCapturesDrained;
                    AudioCapture.Drained += OnAudioCapturesDrained;
                    return;
                }
                AbortUndrainedNoLock();
                completion = DetachCompletionNoLock();
            }
        }
        if (rejectedSummary != null)
        {
            try
            {
                MatchRecordStorage.Database.SaveSummaryV12(rejectedSummary, null, rejected: true);
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
                MatchRecordStorage.Database.SaveSummaryV12(
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
        catalog.CaptureBackground(GameApp.Instance?.NowBackground);
        var initial = ReplayFactCaptureV12.CapturePublicState(roundSequence, actorTurnSequence, catalog);
        if (initial.Entities.Count == 0) return false;
        initial.BattlePhase = "Materialized";
        foreach (var entity in initial.Entities)
        {
            entity.SpawnGeneration = 1;
            EntityGenerations[entity.EntityId] = 1;
        }
        builder = new ReplayJournalBuilderV12(pendingHeader, initial);
        pendingHeader = null;
        startedTimestamp = Stopwatch.GetTimestamp();
        var bootstrap = builder.StartTransaction(
            ReplayTransactionKindsV12.Bootstrap,
            0,
            roundSequence,
            actorTurnSequence,
            initial.ActiveActorId);
        builder.AddTruthMarker(bootstrap, ReplayEventTypesV12.BattleMaterialized, 0, initial.ActiveActorId);
        foreach (var entity in initial.Entities)
        {
            var binding = ReplayFactCaptureV12.CaptureBinding(entity, catalog);
            builder.AddPresentation(bootstrap, ReplayEventTypesV12.EntityPresented, new ReplayPresentationMessageV12
            {
                Kind = "Entity",
                ActorId = entity.EntityId,
                AnimationState = "Idle",
                EntityBinding = binding
            }, 0, entity.EntityId);
            PresentedEntities.Add(EntityKey(entity.EntityId, entity.SpawnGeneration));
        }
        builder.CompleteTransaction(bootstrap, 1);
        CapturePovNoLock();
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

    private static string BeginSourceTransactionNoLock(ReplayCapturedActionSourceV12 source, bool pushContext = true)
    {
        var parent = ContextStack.LastOrDefault() ?? "";
        var ownsActorTurn = string.IsNullOrWhiteSpace(parent) && IsActorActionKind(source.Kind);
        if (ownsActorTurn) actorTurnSequence++;
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
            var turnState = builder.CurrentState;
            turnState.RoundSequence = roundSequence;
            turnState.ActorTurnSequence = actorTurnSequence;
            turnState.ActiveActorId = source.ActorId;
            builder.ApplyObservedState(transactionId, turnState, ElapsedTicks());
            stateWatermark++;
            builder.AddTruthMarker(transactionId, ReplayEventTypesV12.ActorTurnStarted, ElapsedTicks(), source.ActorId);
        }
        builder.AddPresentation(transactionId, ReplayEventTypesV12.SourcePresented, new ReplayPresentationMessageV12
        {
            Kind = source.Kind,
            DescriptorId = source.DescriptorId,
            ActorId = source.ActorId,
            SourceInstanceId = source.SourceInstanceId,
            SourceZone = source.SourceZone,
            SourceSlot = source.SourceSlot,
            DurationTicks = 240_000
        }, ElapsedTicks(), source.ActorId);
        if (pushContext) ContextStack.Add(transactionId);
        return transactionId;
    }

    private static string FindOpenSourceTransactionNoLock(ReplayCapturedActionSourceV12 source)
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
        DrainStableBarrierNoLock(createPassiveTransaction: true);
        var config = executor.dataConfig;
        var descriptor = catalog!.RegisterCard(config, ReplayCaptureCatalogV12.Read(config?.data, "Id"));
        var source = new ReplayCapturedActionSourceV12
        {
            Kind = ReplayTransactionKindsV12.ImplicitNative,
            IssuerPlayerId = builder!.CurrentState.Entities.LastOrDefault(item =>
                string.Equals(item.EntityId, actor.InstanceId ?? "", StringComparison.Ordinal))?.OwnerPlayerId ?? "",
            ActorId = actor.InstanceId ?? "",
            SourceInstanceId = sourceInstanceId ?? "",
            DescriptorId = descriptor.DescriptorId,
            Label = descriptor.Name,
            AnimationState = ReplayCaptureCatalogV12.First(
                ReplayCaptureCatalogV12.Read(config?.Vars, "Action"),
                ReplayCaptureCatalogV12.Read(config?.data, "Action"),
                "Idle"),
            EffectDescriptorId = catalog.RegisterEffect(ReplayCaptureCatalogV12.First(
                ReplayCaptureCatalogV12.Read(config?.Vars, "Effects"),
                ReplayCaptureCatalogV12.Read(config?.data, "Effects")))
        };
        var transactionId = BeginSourceTransactionNoLock(source, pushContext: false);
        ImplicitPresentationTransactions[ImplicitKey(source.ActorId, source.SourceInstanceId)] = transactionId;
        return transactionId;
    }

    private static void EndCurrentSourceNoLock(ReplayCapturedActionSourceV12? latest)
    {
        if (ContextStack.Count == 0) return;
        var transactionId = ContextStack[ContextStack.Count - 1];
        ContextStack.RemoveAt(ContextStack.Count - 1);
        if (latest != null) Transactions[transactionId].Source = latest;
        ApplyCurrentStateNoLock(transactionId);
        MarkSourceCompletedNoLock(transactionId);
    }

    private static void ApplyCurrentStateNoLock(string transactionId)
    {
        var observed = ReplayFactCaptureV12.CapturePublicState(roundSequence, actorTurnSequence, catalog!);
        if (Transactions.TryGetValue(transactionId, out var transaction)
            && !string.IsNullOrWhiteSpace(transaction.Source.ActorId))
            observed.ActiveActorId = transaction.Source.ActorId;
        AssignEntityGenerationsNoLock(observed);
        ApplyObservedStateNoLock(transactionId, observed);
    }

    private static void ApplyObservedStateNoLock(string transactionId, ReplayPublicStateV12 observed)
    {
        var added = builder!.ApplyObservedState(transactionId, observed, ElapsedTicks());
        stateWatermark++;
        foreach (var spawn in added.Where(item => item.EventType == ReplayEventTypesV12.EntitySpawned && item.Entity != null))
        {
            var entity = spawn.Entity!;
            var key = EntityKey(entity.EntityId, entity.SpawnGeneration);
            if (!PresentedEntities.Add(key)) continue;
            var binding = ReplayFactCaptureV12.CaptureBinding(entity, catalog!);
            builder.AddPresentation(transactionId, ReplayEventTypesV12.EntityPresented, new ReplayPresentationMessageV12
            {
                Kind = "Entity",
                ActorId = entity.EntityId,
                AnimationState = "Idle",
                EntityBinding = binding
            }, ElapsedTicks(), entity.EntityId);
        }
        foreach (var despawn in added.Where(item => item.EventType == ReplayEventTypesV12.EntityDespawned))
            PresentedEntities.Remove(EntityKey(despawn.EntityId, despawn.SpawnGeneration));
        CapturePovNoLock();
    }

    private static void AssignEntityGenerationsNoLock(ReplayPublicStateV12 observed)
    {
        var active = builder?.CurrentState.Entities
            .GroupBy(item => item.EntityId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.SpawnGeneration).First(),
                StringComparer.Ordinal)
            ?? new Dictionary<string, ReplayEntityStateV12>(StringComparer.Ordinal);
        var pending = new List<ReplayEntityStateV12>();
        var usedSlots = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
        {
            [ReplayTeamsV12.Friendly] = new HashSet<int>(),
            [ReplayTeamsV12.Enemy] = new HashSet<int>(),
            [ReplayTeamsV12.Neutral] = new HashSet<int>()
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
        var key = string.IsNullOrWhiteSpace(team) ? ReplayTeamsV12.Neutral : team;
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

    private static void DrainStableBarrierNoLock(bool createPassiveTransaction)
    {
        if (!CanCaptureNoLock()) return;
        var pending = Ledger.OpenEntries.Where(item => item.SourceCompleted).OrderBy(item => item.OpenSequence).ToList();
        if (pending.Count > 0)
        {
            var stateOwner = ResolveStateOwnerNoLock(pending);
            if (stateOwner.Length > 0) ApplyCurrentStateNoLock(stateOwner);
            else AddDiagnosticNoLock("ambiguous-causal-ownership:state-barrier");
        }
        else if (createPassiveTransaction)
        {
            var observed = ReplayFactCaptureV12.CapturePublicState(roundSequence, actorTurnSequence, catalog!);
            AssignEntityGenerationsNoLock(observed);
            var diff = ReplayStateReducerV12.CreateDiff(builder!.CurrentState, observed);
            if (diff.HasChanges)
            {
                var passive = BeginSystemTransactionNoLock(ReplayTransactionKindsV12.Passive, "PassiveState");
                ApplyObservedStateNoLock(passive, observed);
                MarkAndCompleteSystemTransactionNoLock(passive);
            }
        }
        while (true)
        {
            var ready = Ledger.ObserveStableBarrier(stateWatermark);
            if (ready.Count == 0) break;
            foreach (var transactionId in ready)
            {
                if (Transactions.TryGetValue(transactionId, out var transaction) && transaction.OwnsActorTurn)
                    builder!.AddTruthMarker(
                        transactionId,
                        ReplayEventTypesV12.ActorTurnCompleted,
                        ElapsedTicks(),
                        transaction.Source.ActorId);
                if (builder!.IsOpen(transactionId)) builder.CompleteTransaction(transactionId, ElapsedTicks());
                Ledger.Complete(transactionId);
                Transactions.Remove(transactionId);
                foreach (var key in RemoteTransactions.Where(item => item.Value == transactionId).Select(item => item.Key).ToList())
                    RemoteTransactions.Remove(key);
            }
        }
    }

    private static string ResolveStateOwnerNoLock(IReadOnlyList<ReplayTransactionLedgerEntryV12> pending)
    {
        if (pending.Count == 1) return pending[0].TransactionId;
        var open = Ledger.OpenEntries.ToDictionary(item => item.TransactionId, StringComparer.Ordinal);
        var candidate = pending.OrderByDescending(item => item.OpenSequence).First();
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var parent = candidate.ParentTransactionId;
        while (!string.IsNullOrWhiteSpace(parent) && ancestors.Add(parent) && open.TryGetValue(parent, out var value))
            parent = value.ParentTransactionId;
        return pending.All(item => string.Equals(item.TransactionId, candidate.TransactionId, StringComparison.Ordinal)
                                   || ancestors.Contains(item.TransactionId))
            ? candidate.TransactionId
            : "";
    }

    private static string BeginSystemTransactionNoLock(string kind, string label)
    {
        var transactionId = builder!.StartTransaction(
            kind,
            ElapsedTicks(),
            roundSequence,
            actorTurnSequence,
            builder.CurrentState.ActiveActorId,
            label: label,
            issuerPlayerId: RoleTable.Instance?.Id ?? "",
            sourceToken: "system-" + (++sourceSequence).ToString("D10"));
        Ledger.Begin(transactionId, kind, builder.CurrentState.ActiveActorId, "");
        Transactions[transactionId] = new CaptureTransaction
        {
            TransactionId = transactionId,
            Source = new ReplayCapturedActionSourceV12
            {
                Kind = kind,
                ActorId = builder.CurrentState.ActiveActorId,
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
            ReplayCaptureCatalogV12.First(context.EffectName, "remote-action"));
        var actorEntityId = ResolveActorEntityIdNoLock(context.ActorId);
        var source = new ReplayCapturedActionSourceV12
        {
            Kind = ReplayTransactionKindsV12.ImplicitNative,
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

    private static ReplayAudioCueV12 RecordAudioNoLock(
        AudioClip clip,
        string bus,
        string kind,
        string usage,
        string provenance)
    {
        var standalone = ContextStack.Count == 0;
        var transactionId = standalone
            ? BeginSystemTransactionNoLock(ReplayTransactionKindsV12.SystemPhase, kind)
            : ContextStack[ContextStack.Count - 1];
        var cue = new ReplayAudioCueV12
        {
            Kind = kind ?? "Audio",
            Bus = string.IsNullOrWhiteSpace(bus) ? "Effect" : bus,
            StartSample = ElapsedTicks() * 48_000L / ReplayProtocolV12.TimebaseTicksPerSecond,
            GainQ16 = 65_536
        };
        var pendingToken = "audio-pending-" + Guid.NewGuid().ToString("N");
        Ledger.RequireAsset(transactionId, pendingToken);
        var audioEvent = builder!.AddPresentation(transactionId, ReplayEventTypesV12.AudioPresented, new ReplayPresentationMessageV12
        {
            Kind = kind ?? "Audio",
            ActorId = builder.CurrentState.ActiveActorId,
            Audio = cue
        }, ElapsedTicks(), builder.CurrentState.ActiveActorId);
        cue = audioEvent.Presentation?.Audio
              ?? throw new InvalidOperationException("Replay audio event did not retain its canonical cue.");
        AudioCapture.Request(clip, usage, result =>
        {
            lock (Gate)
            {
                if (builder == null || catalog == null || !Transactions.ContainsKey(transactionId)) return;
                if (!result.Success || result.Attachment == null)
                {
                    AddDiagnosticNoLock("required-audio-capture-failed:" + provenance + ":" + result.FailureCode);
                    Ledger.ResolveAsset(pendingToken);
                    if (standalone)
                    {
                        MarkSourceCompletedNoLock(transactionId);
                        DrainStableBarrierNoLock(createPassiveTransaction: false);
                    }
                    return;
                }
                var source = result.Attachment;
                cue.AssetSha256 = catalog.RegisterAsset(new ReplayAssetV12
                {
                    Sha256 = source.Sha256,
                    MediaType = source.MediaType,
                    Extension = source.Extension,
                    Usage = usage,
                    ByteLength = source.ByteLength,
                    SampleRate = source.SampleRate,
                    Channels = source.Channels,
                    SampleFrames = source.SampleFrames,
                    Required = true,
                    Payload = source.Payload
                });
                var capturedDuration = source.SampleRate <= 0
                    ? 0
                    : source.SampleFrames * 48_000L / source.SampleRate;
                cue.DurationSamples = cue.DurationSamples > 0 && capturedDuration > 0
                    ? Math.Min(cue.DurationSamples, capturedDuration)
                    : Math.Max(cue.DurationSamples, capturedDuration);
                Ledger.ResolveAsset(pendingToken);
                if (standalone)
                {
                    MarkSourceCompletedNoLock(transactionId);
                    DrainStableBarrierNoLock(createPassiveTransaction: false);
                }
            }
        });
        if (standalone && AudioCapture.PendingCount == 0)
        {
            MarkSourceCompletedNoLock(transactionId);
            DrainStableBarrierNoLock(createPassiveTransaction: false);
        }
        return cue;
    }

    private static void LimitActiveBgmNoLock()
    {
        if (activeBgmCue == null) return;
        var endSample = ElapsedTicks() * 48_000L / ReplayProtocolV12.TimebaseTicksPerSecond;
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
                "Resolved." + (playback.Request?.Kind ?? "Audio"),
                playback.ProviderId);
        }
    }

    private static void CapturePovNoLock()
    {
        if (pov == null || povCatalog == null || builder == null) return;
        var anchor = builder.Document.TruthEvents.Concat(builder.Document.PresentationEvents)
            .OrderByDescending(item => item.Sequence)
            .FirstOrDefault();
        if (anchor == null) return;
        var current = ReplayFactCaptureV12.CapturePrivateCards(povCatalog);
        var before = lastPrivateCards.ToDictionary(item => item.CardInstanceId, StringComparer.Ordinal);
        var after = current.ToDictionary(item => item.CardInstanceId, StringComparer.Ordinal);
        foreach (var id in before.Keys.Where(id => !after.ContainsKey(id)).OrderBy(item => item, StringComparer.Ordinal))
            AddPovEventNoLock(new ReplayPovEventV12
            {
                CanonicalSequence = anchor.Sequence,
                TransactionId = anchor.TransactionId,
                StepOrdinal = anchor.StepOrdinal,
                Kind = ReplayPovEventKindsV12.RemovePrivateCard,
                CardInstanceId = id
            });
        foreach (var pair in after.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!before.TryGetValue(pair.Key, out var previous)
                || ReplayCanonicalJsonV12.Sha256(previous) != ReplayCanonicalJsonV12.Sha256(pair.Value))
                AddPovEventNoLock(new ReplayPovEventV12
                {
                    CanonicalSequence = anchor.Sequence,
                    TransactionId = anchor.TransactionId,
                    StepOrdinal = anchor.StepOrdinal,
                    Kind = ReplayPovEventKindsV12.UpsertPrivateCard,
                    Card = ReplayCanonicalJsonV12.Clone(pair.Value),
                    CardInstanceId = pair.Key
                });
        }
        lastPrivateCards = current;
    }

    private static void AddPovEventNoLock(ReplayPovEventV12 value)
    {
        if (pov == null) return;
        value.Sequence = ++povSequence;
        value.PreviousEventHash = previousPovHash;
        value.EventHash = "";
        value.EventHash = ReplayCanonicalJsonV12.Sha256(value);
        previousPovHash = value.EventHash;
        pov.Events.Add(value);
    }

    private static CompletionSnapshot? DetachCompletionNoLock()
    {
        if (activeRecord == null || builder == null || catalog == null) return null;
        builder.Document.Presentation = catalog.Capsule;
        builder.Document.Assets = catalog.Assets;
        builder.Document.Header.EndedUtc = DateTime.UtcNow.ToString("O");
        builder.Document.Header.Result = TerminalGate.Result.Length == 0 ? "Unknown" : TerminalGate.Result;
        activeRecord.Result = builder.Document.Header.Result;
        activeRecord.EndedUtc = builder.Document.Header.EndedUtc;
        activeRecord.TurnCount = Math.Max(1, roundSequence);
        activeRecord.EventCount = builder.Document.TruthEvents.Count + builder.Document.PresentationEvents.Count;
        activeRecord.StatisticsJson = AuraSharedJson.SerializeCompact(AuraToolsDamageMeterRuntime.Ledger.CreateSnapshot());
        activeRecord.CaptureDiagnostics = Diagnostics.ToList();
        var result = new CompletionSnapshot(
            activeRecord,
            new ReplayDocumentEnvelopeV12 { Document = builder.Document },
            FinalizePovNoLock(pov, povCatalog),
            Diagnostics.ToList());
        ResetNoLock();
        return result;
    }

    private static ReplayPovSidecarV12? FinalizePovNoLock(ReplayPovSidecarV12? sidecar, ReplayCaptureCatalogV12? privateCatalog)
    {
        if (sidecar == null || privateCatalog == null || sidecar.Events.Count == 0) return null;
        sidecar.PrivateCards = privateCatalog.Capsule.Cards;
        var assetHashes = sidecar.PrivateCards
            .SelectMany(item => new[] { item.ArtworkAssetSha256, item.FrameAssetSha256 })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        sidecar.Assets = privateCatalog.Assets.Where(item => assetHashes.Contains(item.Sha256)).ToList();
        ReplayPovContractV12.Finalize(sidecar);
        return sidecar;
    }

    private static void OnAudioCapturesDrained()
    {
        CompletionSnapshot? completion;
        lock (Gate)
        {
            AudioCapture.Drained -= OnAudioCapturesDrained;
            if (CanCaptureNoLock()) DrainStableBarrierNoLock(createPassiveTransaction: false);
            AbortUndrainedNoLock();
            completion = TerminalGate.CanDetach(AudioCapture.PendingCount) ? DetachCompletionNoLock() : null;
        }
        if (completion != null) QueueFinalization(completion);
    }

    private static void QueueFinalization(CompletionSnapshot completion)
    {
        var database = MatchRecordStorage.Database;
        var limit = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit;
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(new AuraSharedBackgroundWorkRequest<FinalizationResult>
        {
            OwnerId = AuraToolsIds.ModId,
            Key = "ReplayV12.Finalize." + completion.Record.RecordId,
            Source = "MatchRecords.ReplayV12.Finalize",
            Kind = AuraSharedBackgroundWorkKind.Io,
            Work = _ => FinalizeDetached(completion, database, limit),
            ApplyOnMainThread = LogFinalization,
            OnFailedOnMainThread = ex =>
            {
                AuraToolsLog.Warn("[MatchRecords] v12 background finalization failed: " + ex.Message);
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
            var validation = ReplayDocumentFinalizerV12.FinalizeAndValidate(completion.Envelope);
            completion.Record.ContentSha256 = completion.Envelope.DeclaredDocumentRoot;
            completion.Record.EventCount = completion.Envelope.Document.TruthEvents.Count
                                           + completion.Envelope.Document.PresentationEvents.Count;
            var analysis = MatchAnalysisBuilder.BuildV12(completion.Record, completion.Envelope.Document);
            var diagnostics = completion.Diagnostics.Concat(validation.Errors).Distinct(StringComparer.Ordinal).ToList();
            if (!validation.IsValid || diagnostics.Count > 0)
            {
                completion.Record.CaptureDiagnostics = diagnostics;
                var saved = database.SaveSummaryV12(completion.Record, analysis, rejected: true);
                return new FinalizationResult
                {
                    Stored = saved,
                    RecordId = completion.Record.RecordId,
                    Message = saved
                        ? "对局摘要已保存；v12 结构化回放被拒绝：" + string.Join("; ", diagnostics)
                        : "v12 回放和摘要均未保存。"
                };
            }
            var stored = database.SaveV12(
                completion.Record,
                completion.Envelope,
                analysis,
                AuraToolsConfigService.MatchExperience.MatchRecords.Replay.ChunkTargetBytes);
            if (stored && completion.Sidecar != null)
            {
                completion.Sidecar.ParentDocumentRoot = completion.Envelope.DeclaredDocumentRoot;
                ReplayPovContractV12.Finalize(completion.Sidecar);
                database.SavePovV12(completion.Record.RecordId, completion.Sidecar);
            }
            var removed = stored ? database.EnforceAutoLimit(limit) : 0;
            return new FinalizationResult
            {
                Stored = stored,
                ReplayReady = stored,
                RecordId = completion.Record.RecordId,
                Removed = removed,
                Message = stored ? "Replay Document v12 已验证并保存。" : "记录 ID 已存在，v12 回放未重复保存。",
                Record = stored ? completion.Record : null,
                Envelope = stored ? completion.Envelope : null,
                Analysis = stored ? analysis : null
            };
        }
        catch (Exception ex)
        {
            try
            {
                completion.Record.CaptureDiagnostics = completion.Diagnostics.Concat(new[] { "v12-finalization:" + ex.Message })
                    .Distinct(StringComparer.Ordinal).ToList();
                var analysis = MatchAnalysisBuilder.BuildV12(completion.Record, completion.Envelope.Document);
                var stored = database.SaveSummaryV12(completion.Record, analysis, rejected: true);
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
            ReplayNetworkAuthorityV12.PublishCanonical(result.Record, result.Envelope);
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
        return Math.Max(0L, (long)(elapsed * (double)ReplayProtocolV12.TimebaseTicksPerSecond / Stopwatch.Frequency));
    }

    private static void AddDiagnosticNoLock(string message)
    {
        if (Diagnostics.Count < 64 && !Diagnostics.Contains(message, StringComparer.Ordinal)) Diagnostics.Add(message);
    }

    private static void AbortUndrainedNoLock()
    {
        if (builder == null) return;
        foreach (var transactionId in Ledger.AbortAll())
        {
            if (builder.IsOpen(transactionId)) builder.AbortTransaction(transactionId, ElapsedTicks(), "terminal-undrained");
            AddDiagnosticNoLock("transaction-undrained:" + transactionId);
        }
    }

    private static void ResetNoLock()
    {
        AudioArbiterRuntime.ResolvedPlayback -= OnResolvedPlayback;
        AudioCapture.Drained -= OnAudioCapturesDrained;
        AudioCapture.Cancel();
        NativeAudioCalls.Reset();
        BaselineGate.Reset();
        TerminalGate.Reset();
        Ledger.Reset();
        ContextStack.Clear();
        CardLifecycleScopes.Clear();
        Transactions.Clear();
        RemoteTransactions.Clear();
        RemoteCardCommands.Clear();
        RemoteActionAnimations.Clear();
        ImplicitPresentationTransactions.Clear();
        PresentedEntities.Clear();
        EntityGenerations.Clear();
        Diagnostics.Clear();
        activeRecord = null;
        pendingHeader = null;
        builder = null;
        catalog = null;
        povCatalog = null;
        pov = null;
        lastPrivateCards = new List<ReplayPublicCardStateV12>();
        startedTimestamp = 0;
        stateWatermark = 0;
        sourceSequence = 0;
        povSequence = 0;
        previousPovHash = "";
        roundSequence = 1;
        actorTurnSequence = 0;
        firstRoundSeen = false;
        preBaselineActivityMissed = false;
        lastBgmClipInstanceId = 0;
        activeBgmCue = null;
    }

    private static T? Argument<T>(object[]? arguments, int index) =>
        arguments != null && index >= 0 && index < arguments.Length && arguments[index] is T value ? value : default;

    private static string EntityKey(string entityId, int generation) => entityId + "|" + generation;
    private static string RemoteKey(string actorId, long sequence) => (actorId ?? "") + "|" + sequence;
    private static string ImplicitKey(string actorId, string sourceId) => (actorId ?? "") + "|" + (sourceId ?? "");

    private static bool IsActorActionKind(string kind) => kind == ReplayTransactionKindsV12.Card
                                                          || kind == ReplayTransactionKindsV12.Skill
                                                          || kind == ReplayTransactionKindsV12.Intent
                                                          || kind == ReplayTransactionKindsV12.ImplicitNative;

    private sealed class CaptureTransaction
    {
        internal string TransactionId { get; set; } = "";
        internal ReplayCapturedActionSourceV12 Source { get; set; } = new();
        internal bool SourceCompleted { get; set; }
        internal bool OwnsActorTurn { get; set; }
    }

    private sealed class CompletionSnapshot
    {
        internal CompletionSnapshot(
            MatchRecord record,
            ReplayDocumentEnvelopeV12 envelope,
            ReplayPovSidecarV12? sidecar,
            IReadOnlyCollection<string> diagnostics)
        {
            Record = record;
            Envelope = envelope;
            Sidecar = sidecar;
            Diagnostics = diagnostics;
        }

        internal MatchRecord Record { get; }
        internal ReplayDocumentEnvelopeV12 Envelope { get; }
        internal ReplayPovSidecarV12? Sidecar { get; }
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
        internal ReplayDocumentEnvelopeV12? Envelope { get; set; }
        internal MatchAnalysisReport? Analysis { get; set; }
    }
}
