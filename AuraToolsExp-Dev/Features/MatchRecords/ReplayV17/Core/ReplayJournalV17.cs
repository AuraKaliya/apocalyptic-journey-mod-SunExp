using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal sealed class ReplayJournalBuilderV17
{
    // Capture batches have their own durable hash. Event/state hash chains are
    // sealed once by the background finalizer after mutable tracks are closed.
    private readonly ReplayStateReducerV17 reducer = new(computeHashes: false);
    private readonly Dictionary<string, int> steps = new(StringComparer.Ordinal);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayCausalTransactionV17> transactions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> firstSequences = new(StringComparer.Ordinal);
    private long sequence;
    private int transactionSequence;
    private long lastTruthTimeTicks;

    internal ReplayJournalBuilderV17(ReplayDocumentHeaderCoreV17 header, ReplayVisibleStateV17 initialState)
    {
        Document = new ReplayDocumentV17
        {
            Header = ReplayCanonicalJsonV17.Clone(header ?? new ReplayDocumentHeaderCoreV17()),
            InitialState = ReplayStateReducerV17.Normalize(initialState)
        };
        reducer.Reset(Document.InitialState);
    }

    internal ReplayDocumentV17 Document { get; }

    internal ReplayVisibleStateV17 CurrentState => reducer.Current;

    internal long LastSequence => sequence;
    internal ReplayStateDiffV17 CreateObservedDiff(ReplayVisibleStateV17 observed) => reducer.Diff(observed);

    internal long LastDurableSequence(IEnumerable<string> openIds, IEnumerable<long> mutableSequences) =>
        ReplayDurableJournalPrefixV17.LastDurableSequence(sequence,
            openIds.Select(id => firstSequences.TryGetValue(id, out var first) ? first
                : throw new InvalidOperationException("Unknown open journal transaction: " + id)), mutableSequences);

    internal IReadOnlyDictionary<string, ReplayCausalTransactionV17> Transactions => transactions;

    internal string StartTransaction(
        string kind,
        long timeTicks,
        int roundSequence,
        int actorTurnSequence,
        string actorId = "",
        string sourceInstanceId = "",
        string sourceDescriptorId = "",
        string label = "",
        string issuerPlayerId = "",
        string sourceToken = "",
        string parentTransactionId = "",
        string authorityKind = "Host")
    {
        if (!ReplayTransactionKindsV17.Supported.Contains(kind ?? ""))
            throw new InvalidOperationException("Unsupported replay transaction kind: " + kind);
        if (!string.IsNullOrWhiteSpace(parentTransactionId)
            && (!transactions.ContainsKey(parentTransactionId) || completed.Contains(parentTransactionId)))
            throw new InvalidOperationException("Replay parent transaction is missing or completed: " + parentTransactionId);
        var id = "transaction-" + (++transactionSequence).ToString("D8");
        var transaction = new ReplayCausalTransactionV17
        {
            Kind = kind ?? "",
            SourceToken = sourceToken ?? "",
            IssuerPlayerId = issuerPlayerId ?? "",
            ActorId = actorId ?? "",
            SourceInstanceId = sourceInstanceId ?? "",
            SourceDescriptorId = sourceDescriptorId ?? "",
            Label = label ?? ""
        };
        transactions.Add(id, transaction);
        firstSequences.Add(id, sequence + 1);
        steps.Add(id, 0);
        AppendTruth(new ReplayJournalEventV17
        {
            TransactionId = id,
            ParentTransactionId = parentTransactionId ?? "",
            RoundSequence = Math.Max(0, roundSequence),
            ActorTurnSequence = Math.Max(0, actorTurnSequence),
            TimeTicks = Math.Max(0, timeTicks),
            AuthorityKind = authorityKind ?? "Host",
            IssuerPlayerId = issuerPlayerId ?? "",
            ActorId = actorId ?? "",
            EventType = ReplayEventTypesV17.TransactionStarted,
            Transaction = ReplayFastCloneV17.Transaction(transaction)
        });
        return id;
    }

    internal ReplayJournalEventV17 AddTruthMarker(
        string transactionId,
        string eventType,
        long timeTicks,
        string actorId = "")
    {
        RequireOpen(transactionId);
        if (!ReplayEventTypesV17.Truth.Contains(eventType ?? ""))
            throw new InvalidOperationException("Unsupported replay truth marker: " + eventType);
        return AppendTruth(new ReplayJournalEventV17
        {
            TransactionId = transactionId,
            RoundSequence = reducer.RoundSequence,
            ActorTurnSequence = reducer.ActorTurnSequence,
            TimeTicks = Math.Max(0, timeTicks),
            ActorId = actorId ?? "",
            EventType = eventType ?? ""
        });
    }

    internal IReadOnlyList<ReplayJournalEventV17> ApplyObservedState(
        string transactionId,
        ReplayVisibleStateV17 observed,
        long timeTicks,
        ReplayStateDiffV17? preparedDiff = null)
    {
        RequireOpen(transactionId);
        var diff = preparedDiff ?? reducer.Diff(observed);
        if (diff.SourceStateVersion != reducer.StateVersion)
            throw new InvalidOperationException("A prepared replay diff belongs to an earlier visible state.");
        var result = new List<ReplayJournalEventV17>();
        foreach (var entity in diff.Despawned.OrderBy(item => item.EntityId, StringComparer.Ordinal)
                     .ThenBy(item => item.SpawnGeneration))
            result.Add(AppendTruth(new ReplayJournalEventV17
            {
                TransactionId = transactionId,
                RoundSequence = observed.RoundSequence,
                ActorTurnSequence = observed.ActorTurnSequence,
                TimeTicks = Math.Max(0, timeTicks),
                ActorId = entity.EntityId,
                EventType = ReplayEventTypesV17.EntityDespawned,
                EntityId = entity.EntityId,
                SpawnGeneration = entity.SpawnGeneration
            }));
        foreach (var entity in diff.Spawned.OrderBy(item => item.EntityId, StringComparer.Ordinal)
                     .ThenBy(item => item.SpawnGeneration))
            result.Add(AppendTruth(new ReplayJournalEventV17
            {
                TransactionId = transactionId,
                RoundSequence = observed.RoundSequence,
                ActorTurnSequence = observed.ActorTurnSequence,
                TimeTicks = Math.Max(0, timeTicks),
                ActorId = entity.EntityId,
                EventType = ReplayEventTypesV17.EntitySpawned,
                Entity = ReplayStateReducerV17.Clone(entity)
            }));
        if (diff.Delta.Operations.Count > 0)
            result.Add(AppendTruth(new ReplayJournalEventV17
            {
                TransactionId = transactionId,
                RoundSequence = observed.RoundSequence,
                ActorTurnSequence = observed.ActorTurnSequence,
                TimeTicks = Math.Max(0, timeTicks),
                ActorId = observed.ActiveActorId ?? "",
                EventType = ReplayEventTypesV17.StateDeltaApplied,
                Delta = ReplayFastCloneV17.Delta(diff.Delta)
            }));
        return result;
    }

    internal ReplayJournalEventV17 AddPresentation(
        string transactionId,
        string eventType,
        ReplayPresentationMessageV17 message,
        long timeTicks,
        string actorId = "")
    {
        RequireOpen(transactionId);
        if (!ReplayEventTypesV17.Presentation.Contains(eventType ?? ""))
            throw new InvalidOperationException("Unsupported replay presentation event: " + eventType);
        return AppendPresentation(new ReplayJournalEventV17
        {
            TransactionId = transactionId,
            RoundSequence = reducer.RoundSequence,
            ActorTurnSequence = reducer.ActorTurnSequence,
            TimeTicks = Math.Max(0, timeTicks),
            ActorId = actorId ?? message?.ActorId ?? "",
            EventType = eventType ?? "",
            Presentation = ReplayFastCloneV17.Presentation(message ?? new ReplayPresentationMessageV17())
        });
    }

    internal ReplayJournalEventV17 CompleteTransaction(string transactionId, long timeTicks)
    {
        RequireOpen(transactionId);
        var value = AppendTruth(new ReplayJournalEventV17
        {
            TransactionId = transactionId,
            RoundSequence = reducer.RoundSequence,
            ActorTurnSequence = reducer.ActorTurnSequence,
            TimeTicks = Math.Max(0, timeTicks),
            ActorId = transactions[transactionId].ActorId,
            EventType = ReplayEventTypesV17.TransactionCompleted
        });
        completed.Add(transactionId);
        return value;
    }

    internal ReplayJournalEventV17 AbortTransaction(string transactionId, long timeTicks, string reason)
    {
        RequireOpen(transactionId);
        var value = AppendTruth(new ReplayJournalEventV17
        {
            TransactionId = transactionId,
            RoundSequence = reducer.RoundSequence,
            ActorTurnSequence = reducer.ActorTurnSequence,
            TimeTicks = Math.Max(0, timeTicks),
            ActorId = transactions[transactionId].ActorId,
            EventType = ReplayEventTypesV17.TransactionAborted,
            Transaction = new ReplayCausalTransactionV17
            {
                Kind = transactions[transactionId].Kind,
                Label = reason ?? ""
            }
        });
        completed.Add(transactionId);
        return value;
    }

    internal bool IsOpen(string transactionId) => transactions.ContainsKey(transactionId) && !completed.Contains(transactionId);

    internal IReadOnlyList<string> OpenTransactions() => transactions.Keys.Where(IsOpen).ToList();

    private ReplayJournalEventV17 AppendTruth(ReplayJournalEventV17 value)
    {
        value.Lane = ReplayJournalLanesV17.Truth;
        value.TimeTicks = Math.Max(0L, value.TimeTicks);
        if (value.TimeTicks < lastTruthTimeTicks)
            throw new InvalidOperationException(
                "Replay truth logical time moved backwards at event " + (sequence + 1) + ".");
        PrepareEvent(value);
        lastTruthTimeTicks = value.TimeTicks;
        reducer.Apply(value, verifyHashes: false);
        Document.TruthEvents.Add(value);
        return value;
    }

    private ReplayJournalEventV17 AppendPresentation(ReplayJournalEventV17 value)
    {
        value.Lane = ReplayJournalLanesV17.Presentation;
        PrepareEvent(value);
        value.StateHashBefore = "";
        value.StateHashAfter = "";
        Document.PresentationEvents.Add(value);
        return value;
    }

    private void PrepareEvent(ReplayJournalEventV17 value)
    {
        var transactionId = value.TransactionId ?? "";
        value.TransactionId = transactionId;
        value.Sequence = ++sequence;
        value.EventId = "event-" + value.Sequence.ToString("D10");
        if (transactions.TryGetValue(transactionId, out var transaction))
        {
            if (string.IsNullOrWhiteSpace(value.IssuerPlayerId)) value.IssuerPlayerId = transaction.IssuerPlayerId;
            if (string.IsNullOrWhiteSpace(value.ActorId)) value.ActorId = transaction.ActorId;
        }
        value.TimeTicks = Math.Max(0, value.TimeTicks);
        value.StepOrdinal = NextStep(transactionId);
        if (value.RoundSequence <= 0) value.RoundSequence = reducer.RoundSequence;
        if (value.ActorTurnSequence <= 0) value.ActorTurnSequence = reducer.ActorTurnSequence;
    }

    private int NextStep(string transactionId)
    {
        var key = transactionId ?? "";
        if (!steps.TryGetValue(key, out var current))
            throw new InvalidOperationException("Replay transaction step owner is missing: " + transactionId);
        steps[key] = current + 1;
        return current;
    }

    private void RequireOpen(string transactionId)
    {
        if (!IsOpen(transactionId))
            throw new InvalidOperationException("Replay transaction is missing or completed: " + transactionId);
    }
}

internal static class ReplayDocumentFinalizerV17
{
    internal static ReplayValidationResultV17 FinalizeAndValidate(ReplayDocumentEnvelopeV17 envelope)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));
        Finalize(envelope.Document);
        envelope.DeclaredDocumentRoot = ReplayCanonicalJsonV17.DocumentRoot(envelope.Document.Header);
        return ReplayDocumentValidatorV17.Validate(envelope);
    }

    internal static void Finalize(ReplayDocumentV17 document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        document.Header.DocumentVersion = ReplayProtocolV17.DocumentVersion;
        document.Header.MinimumReadableDocumentVersion = ReplayProtocolV17.MinimumReadableDocumentVersion;
        document.Header.PackageVersion = ReplayProtocolV17.PackageVersion;
        document.Header.PresentationAbi = ReplayProtocolV17.PresentationAbi;
        document.Header.TimebaseTicksPerSecond = ReplayProtocolV17.TimebaseTicksPerSecond;
        document.Header.RequiredCapabilities = ReplayCapabilitiesV17.RequiredFor(document).OrderBy(item => item, StringComparer.Ordinal).ToList();
        document.Header.OptionalCapabilities = ReplayCapabilitiesV17.Optional.OrderBy(item => item, StringComparer.Ordinal).ToList();
        document.InitialState = ReplayStateReducerV17.Normalize(document.InitialState);
        document.Presentation = ReplayCanonicalJsonV17.NormalizePresentation(document.Presentation);
        document.Assets = PruneAssets(document);
        RebuildEventHashes(document);
        RebuildCheckpoints(document);
        document.Header.InitialVisibleStateSha256 = ReplayCanonicalJsonV17.StateHash(document.InitialState);
        var reducer = new ReplayStateReducerV17();
        reducer.Reset(document.InitialState);
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence)) reducer.Apply(value);
        document.Header.FinalVisibleStateSha256 = ReplayCanonicalJsonV17.StateHash(reducer.Current);
        document.Header.TruthEventCount = document.TruthEvents.Count;
        document.Header.PresentationEventCount = document.PresentationEvents.Count;
        document.Header.TruthCheckpointCount = document.TruthCheckpoints.Count;
        document.Header.PresentationCheckpointCount = document.PresentationCheckpoints.Count;
        document.Header.AssetCount = document.Assets.Count;
        document.Header.TruthRoot = ReplayCanonicalJsonV17.TruthRoot(document);
        document.Header.PresentationRoot = ReplayCanonicalJsonV17.PresentationRoot(document);
    }

    private static void RebuildEventHashes(ReplayDocumentV17 document)
    {
        var reducer = new ReplayStateReducerV17();
        reducer.Reset(document.InitialState);
        var previousTruth = "";
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence))
        {
            value.Lane = ReplayJournalLanesV17.Truth;
            value.PreviousLaneEventHash = previousTruth;
            value.StateHashBefore = reducer.CurrentStateHash;
            reducer.Apply(value, verifyHashes: false);
            value.StateHashAfter = reducer.CurrentStateHash;
            value.EventHash = ReplayCanonicalJsonV17.EventHash(value);
            previousTruth = value.EventHash;
        }
        var previousPresentation = "";
        foreach (var value in document.PresentationEvents.OrderBy(item => item.Sequence))
        {
            value.Lane = ReplayJournalLanesV17.Presentation;
            value.StateHashBefore = "";
            value.StateHashAfter = "";
            value.PreviousLaneEventHash = previousPresentation;
            value.EventHash = ReplayCanonicalJsonV17.EventHash(value);
            previousPresentation = value.EventHash;
        }
    }

    internal static void RebuildCheckpoints(ReplayDocumentV17 document)
    {
        document.TruthCheckpoints.Clear();
        document.PresentationCheckpoints.Clear();
        var reducer = new ReplayStateReducerV17();
        reducer.Reset(document.InitialState);
        var bindings = new Dictionary<string, ReplayEntityPresentationBindingV17>(StringComparer.Ordinal);
        var views = new Dictionary<string, ReplayEntityViewStateV17>(StringComparer.Ordinal);
        var lastTruthHash = "";
        var lastPresentationHash = "";
        var completedTransactions = 0;
        var checkpointTransactions = new HashSet<string>(StringComparer.Ordinal);
        var all = document.TruthEvents.Concat(document.PresentationEvents).OrderBy(item => item.Sequence).ToList();
        foreach (var value in all)
        {
            if (string.Equals(value.Lane, ReplayJournalLanesV17.Truth, StringComparison.Ordinal))
            {
                reducer.Apply(value);
                lastTruthHash = value.EventHash;
                if (value.EventType == ReplayEventTypesV17.EntityDespawned)
                {
                    var key = EntityKey(value.EntityId, value.SpawnGeneration);
                    bindings.Remove(key);
                    views.Remove(key);
                }
                if (value.EventType == ReplayEventTypesV17.BattleMaterialized
                    || value.EventType == ReplayEventTypesV17.RoundStarted
                    || value.EventType == ReplayEventTypesV17.EntitySpawned
                    || value.EventType == ReplayEventTypesV17.EntityDespawned
                    || value.EventType == ReplayEventTypesV17.BattleFinalized)
                    checkpointTransactions.Add(value.TransactionId);
                if (value.EventType == ReplayEventTypesV17.TransactionCompleted) completedTransactions++;
            }
            else
            {
                ApplyPresentationProjection(value, bindings, views);
                lastPresentationHash = value.EventHash;
            }

            if (!ShouldCheckpoint(value, completedTransactions, checkpointTransactions)) continue;
            var truth = new ReplayTruthCheckpointV17
            {
                EventSequence = value.Sequence,
                TimeTicks = value.TimeTicks,
                LastTruthEventHash = lastTruthHash,
                State = reducer.Current
            };
            truth.StateSha256 = ReplayCanonicalJsonV17.StateHash(truth.State);
            truth.CheckpointSha256 = ReplayCanonicalJsonV17.TruthCheckpointHash(truth);
            document.TruthCheckpoints.Add(truth);
            var presentation = new ReplayPresentationCheckpointV17
            {
                EventSequence = value.Sequence,
                TimeTicks = value.TimeTicks,
                LastPresentationEventHash = lastPresentationHash,
                SceneDescriptorId = document.Presentation.Scene.DescriptorId,
                EntityBindings = bindings.Values.Select(ReplayCanonicalJsonV17.Clone).ToList(),
                EntityViews = ProjectViewsAt(document, bindings, views, value.TimeTicks)
            };
            presentation.CheckpointSha256 = ReplayCanonicalJsonV17.PresentationCheckpointHash(presentation);
            document.PresentationCheckpoints.Add(presentation);
        }
        if (all.Count > 0 && (document.TruthCheckpoints.Count == 0
                              || document.TruthCheckpoints[document.TruthCheckpoints.Count - 1].EventSequence != all[all.Count - 1].Sequence))
        {
            var value = all[all.Count - 1];
            var truth = new ReplayTruthCheckpointV17
            {
                EventSequence = value.Sequence,
                TimeTicks = value.TimeTicks,
                LastTruthEventHash = lastTruthHash,
                State = reducer.Current
            };
            truth.StateSha256 = ReplayCanonicalJsonV17.StateHash(truth.State);
            truth.CheckpointSha256 = ReplayCanonicalJsonV17.TruthCheckpointHash(truth);
            document.TruthCheckpoints.Add(truth);
            var presentation = new ReplayPresentationCheckpointV17
            {
                EventSequence = value.Sequence,
                TimeTicks = value.TimeTicks,
                LastPresentationEventHash = lastPresentationHash,
                SceneDescriptorId = document.Presentation.Scene.DescriptorId,
                EntityBindings = bindings.Values.Select(ReplayCanonicalJsonV17.Clone).ToList(),
                EntityViews = ProjectViewsAt(document, bindings, views, value.TimeTicks)
            };
            presentation.CheckpointSha256 = ReplayCanonicalJsonV17.PresentationCheckpointHash(presentation);
            document.PresentationCheckpoints.Add(presentation);
        }
    }

    private static bool ShouldCheckpoint(
        ReplayJournalEventV17 value,
        int completedTransactions,
        ISet<string> checkpointTransactions)
    {
        if (!string.Equals(value.Lane, ReplayJournalLanesV17.Truth, StringComparison.Ordinal)) return false;
        if (value.EventType != ReplayEventTypesV17.TransactionCompleted) return false;
        return checkpointTransactions.Contains(value.TransactionId)
               || completedTransactions > 0
                  && completedTransactions % ReplayProtocolV17.DefaultCheckpointTransactionInterval == 0;
    }

    private static void ApplyPresentationProjection(
        ReplayJournalEventV17 value,
        IDictionary<string, ReplayEntityPresentationBindingV17> bindings,
        IDictionary<string, ReplayEntityViewStateV17> views)
    {
        var message = value.Presentation;
        if (message?.EntityBinding != null
            && (value.EventType == ReplayEventTypesV17.EntityPresented
                || value.EventType == ReplayEventTypesV17.EntityPresentationChanged))
        {
            var binding = ReplayCanonicalJsonV17.Clone(message.EntityBinding);
            bindings[EntityKey(binding.EntityId, binding.SpawnGeneration)] = binding;
        }
        if (value.EventType == ReplayEventTypesV17.ActorAnimationPresented
            || value.EventType == ReplayEventTypesV17.HitReactionPresented)
        {
            var entityId = message?.ActorId ?? value.ActorId;
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                var binding = bindings.Values.LastOrDefault(item => string.Equals(item.EntityId, entityId, StringComparison.Ordinal));
                if (binding != null)
                    views[EntityKey(binding.EntityId, binding.SpawnGeneration)] = new ReplayEntityViewStateV17
                    {
                        EntityId = binding.EntityId,
                        SpawnGeneration = binding.SpawnGeneration,
                        AnimationState = string.IsNullOrWhiteSpace(message?.AnimationState) ? "Idle" : message!.AnimationState,
                        FrameIndex = 0,
                        AnimationStartedTicks = ReplayPresentationTimingV17.EffectiveTimeTicks(value),
                        AnimationEndsTicks = message?.DurationTicks > 0
                            ? ReplayPresentationTimingV17.EffectiveTimeTicks(value) + message.DurationTicks
                            : 0L
                    };
            }
        }
    }

    private static List<ReplayEntityViewStateV17> ProjectViewsAt(
        ReplayDocumentV17 document,
        IReadOnlyDictionary<string, ReplayEntityPresentationBindingV17> bindings,
        IReadOnlyDictionary<string, ReplayEntityViewStateV17> views,
        long timeTicks)
    {
        var descriptors = document.Presentation.Entities.ToDictionary(item => item.DescriptorId, StringComparer.Ordinal);
        var result = new List<ReplayEntityViewStateV17>();
        foreach (var pair in views.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!bindings.TryGetValue(pair.Key, out var binding)
                || !descriptors.TryGetValue(binding.DescriptorId, out var descriptor)) continue;
            var value = ReplayCanonicalJsonV17.Clone(pair.Value);
            if (value.AnimationStartedTicks > timeTicks)
            {
                value.AnimationState = "Idle";
                value.AnimationStartedTicks = timeTicks;
                value.AnimationEndsTicks = 0L;
            }
            if (value.AnimationEndsTicks > 0 && timeTicks >= value.AnimationEndsTicks)
            {
                value.AnimationState = "Idle";
                value.AnimationStartedTicks = value.AnimationEndsTicks;
                value.AnimationEndsTicks = 0;
            }
            var animation = descriptor.Animations.FirstOrDefault(item =>
                                string.Equals(item.State, value.AnimationState, StringComparison.OrdinalIgnoreCase))
                            ?? descriptor.Animations.FirstOrDefault(item =>
                                string.Equals(item.State, "Idle", StringComparison.OrdinalIgnoreCase));
            var frameCount = animation == null
                ? 0
                : animation.Frames.Count > 0 ? animation.Frames.Count : animation.FrameNames.Count;
            if (animation != null && frameCount > 0)
            {
                var elapsed = Math.Max(0L, timeTicks - value.AnimationStartedTicks);
                var frame = elapsed / Math.Max(1L, animation.FrameDurationTicks);
                value.FrameIndex = animation.Loop
                    ? (int)(frame % frameCount)
                    : (int)Math.Min(frameCount - 1, frame);
            }
            result.Add(value);
        }
        return result;
    }

    private static List<ReplayAssetV17> PruneAssets(ReplayDocumentV17 document)
    {
        var required = ReplayPresentationReachabilityV17.AssetHashes(document);
        return (document.Assets ?? new List<ReplayAssetV17>())
            .Where(item => item != null && required.Contains(item.Sha256 ?? ""))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Sha256, StringComparer.Ordinal)
            .ToList();
    }

    private static string EntityKey(string entityId, int generation) => entityId + "|" + generation;
}

internal static class ReplayPresentationReachabilityV17
{
    internal static HashSet<string> AssetHashes(ReplayDocumentV17 document)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string value) { if (!string.IsNullOrWhiteSpace(value)) result.Add(value); }
        var presentation = document?.Presentation ?? new ReplayPresentationCapsuleV17();
        Add(presentation.Scene.BackgroundAssetSha256);
        foreach (var entity in presentation.Entities)
            foreach (var animation in entity.Animations)
                foreach (var frame in animation.Frames) Add(frame.AssetSha256);
        foreach (var card in presentation.Cards)
        {
            Add(card.ArtworkAssetSha256);
            Add(card.FrameAssetSha256);
        }
        foreach (var buff in presentation.Buffs) Add(buff.IconAssetSha256);
        foreach (var intent in presentation.Intents) Add(intent.IconAssetSha256);
        foreach (var effect in presentation.Effects)
            foreach (var frame in effect.Frames) Add(frame.AssetSha256);
        foreach (var value in document?.PresentationEvents ?? new List<ReplayJournalEventV17>())
            Add(value.Presentation?.Audio?.AssetSha256 ?? "");
        return result;
    }
}

internal static class ReplayDocumentValidatorV17
{
    internal static ReplayValidationResultV17 Validate(ReplayDocumentEnvelopeV17 envelope)
    {
        var result = new ReplayValidationResultV17();
        if (envelope?.Document == null)
        {
            result.Errors.Add("document-missing");
            return result;
        }
        var document = envelope.Document;
        if (document.Header == null
            || document.InitialState == null
            || document.TruthEvents == null
            || document.PresentationEvents == null
            || document.TruthCheckpoints == null
            || document.PresentationCheckpoints == null
            || document.Presentation == null
            || document.Presentation.Scene == null
            || document.Presentation.Entities == null
            || document.Presentation.Cards == null
            || document.Presentation.Buffs == null
            || document.Presentation.Intents == null
            || document.Presentation.Effects == null
            || document.Presentation.Modules == null
            || document.Assets == null)
        {
            result.Errors.Add("document-shape-invalid");
            return result;
        }
        var header = document.Header ?? new ReplayDocumentHeaderCoreV17();
        try
        {
            ValidateLimits(document, result);
            if (!result.IsValid) return result;
            if (header.DocumentVersion != ReplayProtocolV17.DocumentVersion
                || header.MinimumReadableDocumentVersion != ReplayProtocolV17.MinimumReadableDocumentVersion
                || header.PackageVersion != ReplayProtocolV17.PackageVersion
                || header.TimebaseTicksPerSecond != ReplayProtocolV17.TimebaseTicksPerSecond)
                result.Errors.Add("version-invalid");
            if (string.IsNullOrWhiteSpace(header.RecordId)
                || string.IsNullOrWhiteSpace(header.BattleSessionId)
                || string.IsNullOrWhiteSpace(header.PerspectivePlayerId)
                || !string.Equals(header.PerspectiveKind, "Player", StringComparison.Ordinal))
                result.Errors.Add("identity-missing");
            if (!string.Equals(header.PresentationAbi, ReplayProtocolV17.PresentationAbi, StringComparison.Ordinal))
                result.Errors.Add("presentation-abi-unsupported");
            var requiredCapabilities = header.RequiredCapabilities ?? new List<string>();
            if (requiredCapabilities.Count != requiredCapabilities.Distinct(StringComparer.Ordinal).Count()
                || !requiredCapabilities.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(ReplayCapabilitiesV17.RequiredFor(document)))
                result.Errors.Add("required-capability-invalid");
            var optionalCapabilities = header.OptionalCapabilities ?? new List<string>();
            if (optionalCapabilities.Count != optionalCapabilities.Distinct(StringComparer.Ordinal).Count())
                result.Errors.Add("optional-capability-invalid");
            if (!string.Equals(ReplayCanonicalJsonV17.StateHash(document.InitialState), header.InitialVisibleStateSha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("initial-state-hash-invalid");
            if (!string.Equals(document.InitialState.PerspectivePlayerId, header.PerspectivePlayerId, StringComparison.Ordinal))
                result.Errors.Add("perspective-identity-invalid");

            ValidateEvents(document, result);
            ValidatePresentation(document, result);
            ReplayHandLifecycleContractV17.Validate(document, result.Errors);
            ValidateCheckpoints(document, result);
            if (!string.Equals(ReplayCanonicalJsonV17.TruthRoot(document), header.TruthRoot, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("truth-root-invalid");
            if (!string.Equals(ReplayCanonicalJsonV17.PresentationRoot(document), header.PresentationRoot, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("presentation-root-invalid");
            var documentRoot = ReplayCanonicalJsonV17.DocumentRoot(header);
            if (!string.Equals(documentRoot, envelope.DeclaredDocumentRoot, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("document-root-invalid");
            if (header.TruthEventCount != document.TruthEvents.Count
                || header.PresentationEventCount != document.PresentationEvents.Count
                || header.TruthCheckpointCount != document.TruthCheckpoints.Count
                || header.PresentationCheckpointCount != document.PresentationCheckpoints.Count
                || header.AssetCount != document.Assets.Count)
                result.Errors.Add("header-count-invalid");
        }
        catch (Exception)
        {
            result.Errors.Add("document-shape-invalid");
        }
        return result;
    }

    private static void ValidateLimits(ReplayDocumentV17 document, ReplayValidationResultV17 result)
    {
        var header = document.Header;
        var headerText = new[]
        {
            header.RecordId, header.AdventureId, header.BattleSessionId, header.PerspectivePlayerId,
            header.PerspectiveKind, header.LevelId, header.BattleTitle,
            header.StartedUtc, header.EndedUtc, header.Result, header.GameBuildProvenance, header.RecorderBuild
        };
        if (headerText.Any(item => (item?.Length ?? 0) > ReplayLimitsV17.MaximumTextLength))
            result.Errors.Add("header-text-budget-exceeded");
        if (document.TruthEvents.Count > ReplayLimitsV17.MaximumEventsPerLane
            || document.PresentationEvents.Count > ReplayLimitsV17.MaximumEventsPerLane)
            result.Errors.Add("event-budget-exceeded");
        if (document.TruthCheckpoints.Count > ReplayLimitsV17.MaximumCheckpoints
            || document.PresentationCheckpoints.Count > ReplayLimitsV17.MaximumCheckpoints)
            result.Errors.Add("checkpoint-budget-exceeded");
        if (document.Presentation.Entities.Count > ReplayLimitsV17.MaximumDescriptorsPerKind
            || document.Presentation.Cards.Count > ReplayLimitsV17.MaximumDescriptorsPerKind
            || document.Presentation.Buffs.Count > ReplayLimitsV17.MaximumDescriptorsPerKind
            || document.Presentation.Intents.Count > ReplayLimitsV17.MaximumDescriptorsPerKind
            || document.Presentation.Effects.Count > ReplayLimitsV17.MaximumDescriptorsPerKind
            || document.Presentation.Modules.Count > 1024)
            result.Errors.Add("descriptor-budget-exceeded");
        long assetBytes = 0;
        foreach (var asset in document.Assets)
        {
            try { assetBytes = checked(assetBytes + Math.Max(0L, asset.ByteLength)); }
            catch (OverflowException) { assetBytes = long.MaxValue; break; }
        }
        if (document.Assets.Count > ReplayLimitsV17.MaximumAssets || assetBytes > ReplayLimitsV17.MaximumAssetBytes)
            result.Errors.Add("asset-budget-exceeded");
        foreach (var state in new[] { document.InitialState }.Concat(document.TruthCheckpoints.Select(item => item.State)))
            if (state.Entities.Count > ReplayLimitsV17.MaximumEntitiesPerState
                || state.Cards.Count > ReplayLimitsV17.MaximumCardsPerState
                || state.Intents.Count > ReplayLimitsV17.MaximumIntentsPerState
                || state.Resources.Count > ReplayLimitsV17.MaximumIntentsPerState
                || state.Extensions.Count > ReplayLimitsV17.MaximumIntentsPerState)
            {
                result.Errors.Add("state-budget-exceeded");
                break;
            }
        var descriptorText = document.Presentation.Entities.SelectMany(item => new[] { item.Name, item.Subtitle })
            .Concat(document.Presentation.Cards.SelectMany(item => new[] { item.Name, item.Description, item.Tag }))
            .Concat(document.Presentation.Buffs.SelectMany(item => new[] { item.Name, item.Description }))
            .Concat(document.Presentation.Intents.SelectMany(item => new[] { item.Name, item.Description }));
        if (descriptorText.Any(item => (item?.Length ?? 0) > ReplayLimitsV17.MaximumTextLength))
            result.Errors.Add("descriptor-text-budget-exceeded");
        if (new[] { document.InitialState }.Concat(document.TruthCheckpoints.Select(item => item.State))
            .SelectMany(item => item.Extensions)
            .Any(item => (item.DisplayText?.Length ?? 0) > ReplayLimitsV17.MaximumTextLength))
            result.Errors.Add("extension-text-budget-exceeded");
        if (document.TruthEvents.Any(item =>
                (item.Delta?.Operations.Count ?? 0) > ReplayLimitsV17.MaximumOperationsPerTransaction))
            result.Errors.Add("state-operation-budget-exceeded");
    }

    private static void ValidateEvents(ReplayDocumentV17 document, ReplayValidationResultV17 result)
    {
        var all = document.TruthEvents.Concat(document.PresentationEvents).OrderBy(item => item.Sequence).ToList();
        if (all.Count == 0) result.Errors.Add("journal-empty");
        var expected = 1L;
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        var lastStep = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in all)
        {
            if (value.Sequence != expected++) result.Errors.Add("global-sequence-invalid:" + value.Sequence);
            if (value.TimeTicks < 0 || value.TimeTicks > ReplayLimitsV17.MaximumTimelineTicks)
                result.Errors.Add("logical-time-invalid:" + value.Sequence);
            if (!string.IsNullOrWhiteSpace(value.CauseEventId) && !eventIds.Contains(value.CauseEventId))
                result.Errors.Add("cause-event-invalid:" + value.Sequence);
            if (string.IsNullOrWhiteSpace(value.EventId) || !eventIds.Add(value.EventId))
                result.Errors.Add("event-id-invalid:" + value.Sequence);
            if (string.IsNullOrWhiteSpace(value.TransactionId)) result.Errors.Add("transaction-id-missing:" + value.Sequence);
            ValidateStep(value, lastStep, result);
        }
        ValidateEntityReferences(document, all, result);
        var started = new Dictionary<string, ReplayJournalEventV17>(StringComparer.Ordinal);
        var ended = new HashSet<string>(StringComparer.Ordinal);
        var endedAt = new Dictionary<string, long>(StringComparer.Ordinal);
        var previousTruth = "";
        var lastTruthTimeTicks = 0L;
        var reducer = new ReplayStateReducerV17();
        reducer.Reset(document.InitialState);
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence))
        {
            if (value.TimeTicks < lastTruthTimeTicks)
                result.Errors.Add("truth-logical-time-regressed:" + value.Sequence);
            lastTruthTimeTicks = Math.Max(lastTruthTimeTicks, value.TimeTicks);
            if (!string.Equals(value.Lane, ReplayJournalLanesV17.Truth, StringComparison.Ordinal)
                || !ReplayEventTypesV17.Truth.Contains(value.EventType ?? ""))
                result.Errors.Add("truth-event-type-invalid:" + value.Sequence);
            if (!string.Equals(value.PreviousLaneEventHash, previousTruth, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(value.EventHash, ReplayCanonicalJsonV17.EventHash(value), StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("truth-event-hash-invalid:" + value.Sequence);
            previousTruth = value.EventHash;
            if (value.EventType == ReplayEventTypesV17.TransactionStarted)
            {
                if (value.Transaction == null || !ReplayTransactionKindsV17.Supported.Contains(value.Transaction.Kind ?? "")
                    || started.ContainsKey(value.TransactionId))
                    result.Errors.Add("transaction-start-invalid:" + value.Sequence);
                else
                {
                    if (!string.IsNullOrWhiteSpace(value.ParentTransactionId)
                        && (!started.ContainsKey(value.ParentTransactionId) || ended.Contains(value.ParentTransactionId)))
                        result.Errors.Add("parent-transaction-invalid:" + value.Sequence);
                    started[value.TransactionId] = value;
                }
            }
            else if (!started.ContainsKey(value.TransactionId))
                result.Errors.Add("transaction-not-started:" + value.Sequence);
            if (started.TryGetValue(value.TransactionId, out var owner)
                && !string.Equals(value.IssuerPlayerId, owner.Transaction?.IssuerPlayerId ?? "", StringComparison.Ordinal))
                result.Errors.Add("transaction-issuer-mismatch:" + value.Sequence);
            if (ended.Contains(value.TransactionId)) result.Errors.Add("transaction-event-after-end:" + value.Sequence);
            if (value.EventType == ReplayEventTypesV17.TransactionCompleted
                || value.EventType == ReplayEventTypesV17.TransactionAborted)
            {
                ended.Add(value.TransactionId);
                endedAt[value.TransactionId] = value.Sequence;
                if (value.EventType == ReplayEventTypesV17.TransactionAborted)
                    result.Errors.Add("transaction-aborted:" + value.TransactionId);
            }
            try { reducer.Apply(value); }
            catch (Exception ex) { result.Errors.Add("truth-state-invalid:" + value.Sequence + ":" + ex.Message); }
        }
        foreach (var id in started.Keys.Where(id => !ended.Contains(id))) result.Errors.Add("transaction-open:" + id);
        foreach (var child in started.Values.Where(item => !string.IsNullOrWhiteSpace(item.ParentTransactionId)))
            if (!endedAt.TryGetValue(child.TransactionId, out var childEnd)
                || !endedAt.TryGetValue(child.ParentTransactionId, out var parentEnd)
                || childEnd >= parentEnd)
                result.Errors.Add("nested-transaction-order-invalid:" + child.TransactionId);
        if (document.TruthEvents.Count(item => item.EventType == ReplayEventTypesV17.BattleMaterialized) != 1)
            result.Errors.Add("battle-materialized-missing");
        if (document.TruthEvents.Count(item => item.EventType == ReplayEventTypesV17.FightStartSignaled) != 1)
            result.Errors.Add("fight-start-signal-invalid");
        if (!document.TruthEvents.Any(item => item.EventType == ReplayEventTypesV17.RoundStarted))
            result.Errors.Add("round-start-missing");
        if (document.TruthEvents.Count(item => item.EventType == ReplayEventTypesV17.OutcomeEntering) != 1)
            result.Errors.Add("outcome-entering-invalid");
        if (document.TruthEvents.Count(item => item.EventType == ReplayEventTypesV17.BattleFinalized) != 1)
            result.Errors.Add("battle-finalized-missing");
        if (!string.Equals(document.InitialState.LevelId, document.Header.LevelId, StringComparison.Ordinal)
            || !string.Equals(document.InitialState.BattlePhase, "Materialized", StringComparison.Ordinal))
            result.Errors.Add("initial-battle-state-invalid");
        var finalState = reducer.Current;
        if (!string.Equals(finalState.BattlePhase, "Finalized", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(finalState.Outcome)
            || !string.Equals(finalState.Outcome, document.Header.Result, StringComparison.Ordinal))
            result.Errors.Add("final-battle-state-invalid");
        if (!string.Equals(ReplayCanonicalJsonV17.StateHash(finalState), document.Header.FinalVisibleStateSha256, StringComparison.OrdinalIgnoreCase))
            result.Errors.Add("final-state-hash-invalid");

        var previousPresentation = "";
        foreach (var value in document.PresentationEvents.OrderBy(item => item.Sequence))
        {
            if (!string.Equals(value.Lane, ReplayJournalLanesV17.Presentation, StringComparison.Ordinal)
                || !ReplayEventTypesV17.Presentation.Contains(value.EventType ?? ""))
                result.Errors.Add("presentation-event-type-invalid:" + value.Sequence);
            if (!started.TryGetValue(value.TransactionId, out var start) || value.Sequence <= start.Sequence)
                result.Errors.Add("presentation-transaction-invalid:" + value.Sequence);
            else if (!string.Equals(value.IssuerPlayerId, start.Transaction?.IssuerPlayerId ?? "", StringComparison.Ordinal))
                result.Errors.Add("presentation-issuer-mismatch:" + value.Sequence);
            if (endedAt.TryGetValue(value.TransactionId, out var endSequence) && value.Sequence >= endSequence)
                result.Errors.Add("presentation-after-transaction-end:" + value.Sequence);
            if (string.IsNullOrWhiteSpace(value.StateHashBefore) == false
                || string.IsNullOrWhiteSpace(value.StateHashAfter) == false)
                result.Errors.Add("presentation-mutates-state:" + value.Sequence);
            if (!string.Equals(value.PreviousLaneEventHash, previousPresentation, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(value.EventHash, ReplayCanonicalJsonV17.EventHash(value), StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("presentation-event-hash-invalid:" + value.Sequence);
            previousPresentation = value.EventHash;
        }
        var byTransaction = document.PresentationEvents.GroupBy(item => item.TransactionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var cardSourceDescriptors = document.Presentation.Cards
            .Select(item => item.DescriptorId)
            .ToHashSet(StringComparer.Ordinal);
        var intentSourceDescriptors = document.Presentation.Intents
            .Select(item => item.DescriptorId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var pair in started)
        {
            var kind = pair.Value.Transaction?.Kind ?? "";
            if (kind != ReplayTransactionKindsV17.Card
                && kind != ReplayTransactionKindsV17.Skill
                && kind != ReplayTransactionKindsV17.Intent
                && kind != ReplayTransactionKindsV17.ImplicitObserved) continue;
            var presentation = byTransaction.TryGetValue(pair.Key, out var values)
                ? values
                : new List<ReplayJournalEventV17>();
            var transaction = pair.Value.Transaction!;
            var sourceIsCard = cardSourceDescriptors.Contains(transaction.SourceDescriptorId ?? "");
            var sourceIsIntent = intentSourceDescriptors.Contains(transaction.SourceDescriptorId ?? "");
            var requiresIntent = kind == ReplayTransactionKindsV17.Intent;
            if (requiresIntent
                    ? !sourceIsIntent || sourceIsCard
                    : !sourceIsCard || sourceIsIntent)
                result.Errors.Add("action-source-descriptor-kind-invalid:" + pair.Key + ":" + kind);
            if (string.IsNullOrWhiteSpace(transaction.ActorId)
                || string.IsNullOrWhiteSpace(transaction.SourceDescriptorId)
                || !presentation.Any(item => item.EventType == ReplayEventTypesV17.SourcePresented
                                              && string.Equals(item.Presentation?.ActorId, transaction.ActorId, StringComparison.Ordinal)
                                              && string.Equals(item.Presentation?.DescriptorId, transaction.SourceDescriptorId, StringComparison.Ordinal)
                                              && string.Equals(item.Presentation?.SourceInstanceId, transaction.SourceInstanceId, StringComparison.Ordinal)))
                result.Errors.Add("action-source-presentation-missing:" + pair.Key);
            if (!presentation.Any(item => item.EventType == ReplayEventTypesV17.SourcePresented
                                          && item.Presentation?.Phase == ReplayPresentationPhasesV17.SourceFocus)
                || !presentation.Any(item => item.EventType == ReplayEventTypesV17.ActorAnimationPresented
                                              && item.Presentation?.Phase == ReplayPresentationPhasesV17.ActorFocus))
                result.Errors.Add("action-phase-presentation-missing:" + pair.Key);
            var stateDeltas = document.TruthEvents.Where(item => item.TransactionId == pair.Key
                                                                 && item.EventType == ReplayEventTypesV17.StateDeltaApplied)
                .Select(item => item.Sequence).ToHashSet();
            var commits = presentation.Where(item => item.EventType == ReplayEventTypesV17.VisualStateCommitted
                                                      && item.Presentation?.TruthEventSequence > 0)
                .Select(item => item.Presentation!.TruthEventSequence).ToHashSet();
            if (!stateDeltas.SetEquals(commits)) result.Errors.Add("visual-state-commit-mismatch:" + pair.Key);
            var phased = presentation.Where(item => !string.IsNullOrWhiteSpace(item.Presentation?.Phase)).ToList();
            if (phased.Any(item => !ValidActionPhase(item)))
                result.Errors.Add("presentation-phase-order-invalid:" + pair.Key);
            if (!presentation.Any(item => item.EventType == ReplayEventTypesV17.ActorAnimationPresented
                                          && string.Equals(item.Presentation?.ActorId, transaction.ActorId, StringComparison.Ordinal)))
                result.Errors.Add("action-animation-presentation-missing:" + pair.Key);
            if (string.IsNullOrWhiteSpace(pair.Value.ParentTransactionId)
                && (document.TruthEvents.Count(item => item.TransactionId == pair.Key
                                                       && item.EventType == ReplayEventTypesV17.ActorTurnStarted) != 1
                    || document.TruthEvents.Count(item => item.TransactionId == pair.Key
                                                         && item.EventType == ReplayEventTypesV17.ActorTurnCompleted) != 1))
                result.Errors.Add("action-turn-boundary-invalid:" + pair.Key);
        }
    }

    private static void ValidateStep(
        ReplayJournalEventV17 value,
        IDictionary<string, int> lastStep,
        ReplayValidationResultV17 result)
    {
        if (value.StepOrdinal < 0) result.Errors.Add("step-negative:" + value.Sequence);
        if (!lastStep.ContainsKey(value.TransactionId ?? "") && value.StepOrdinal != 0)
            result.Errors.Add("step-start-invalid:" + value.Sequence);
        if (lastStep.TryGetValue(value.TransactionId ?? "", out var previous) && value.StepOrdinal <= previous)
            result.Errors.Add("step-order-invalid:" + value.Sequence);
        lastStep[value.TransactionId ?? ""] = value.StepOrdinal;
    }

    private static void ValidateEntityReferences(
        ReplayDocumentV17 document,
        IEnumerable<ReplayJournalEventV17> events,
        ReplayValidationResultV17 result)
    {
        var active = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var lastGeneration = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entity in document.InitialState.Entities)
        {
            var entityId = entity.EntityId ?? "";
            if (!ValidEntity(entity)
                || active.ContainsKey(entityId)
                || !AddEntity(active, entityId, entity.SpawnGeneration))
                result.Errors.Add("initial-entity-invalid:" + entityId);
            else lastGeneration[entityId] = entity.SpawnGeneration;
        }
        foreach (var value in events)
        {
            if (value.EventType == ReplayEventTypesV17.EntitySpawned && value.Entity != null)
            {
                var entityId = value.Entity.EntityId ?? "";
                var previousGeneration = lastGeneration.TryGetValue(entityId, out var known) ? known : 0;
                if (!ValidEntity(value.Entity)
                    || HasEntity(active, entityId)
                    || value.Entity.SpawnGeneration <= previousGeneration
                    || !AddEntity(active, entityId, value.Entity.SpawnGeneration))
                    result.Errors.Add("spawn-entity-invalid:" + value.Sequence);
                else lastGeneration[entityId] = value.Entity.SpawnGeneration;
            }
            if (value.EventType == ReplayEventTypesV17.TransactionStarted
                && value.Transaction != null
                && (value.Transaction.Kind == ReplayTransactionKindsV17.Card
                    || value.Transaction.Kind == ReplayTransactionKindsV17.Skill
                    || value.Transaction.Kind == ReplayTransactionKindsV17.Intent
                    || value.Transaction.Kind == ReplayTransactionKindsV17.ImplicitObserved)
                && !HasEntity(active, value.Transaction.ActorId))
                result.Errors.Add("action-actor-missing:" + value.Sequence);
            var message = value.Presentation;
            if (message?.EntityBinding != null
                && !HasEntity(active, message.EntityBinding.EntityId, message.EntityBinding.SpawnGeneration))
                result.Errors.Add("presentation-entity-missing:" + value.Sequence);
            if (message?.EntityBinding?.CustomPresentation is { } custom
                && custom.PresentationMode == "OwnerAttachedProxy"
                && !HasEntity(active, custom.OwnerEntityId))
                result.Errors.Add("presentation-owner-entity-missing:" + value.Sequence);
            if (message != null
                && (value.EventType == ReplayEventTypesV17.SourcePresented
                    || value.EventType == ReplayEventTypesV17.ActorAnimationPresented
                    || value.EventType == ReplayEventTypesV17.HitReactionPresented)
                && !HasEntity(active, message.ActorId))
                result.Errors.Add("presentation-actor-missing:" + value.Sequence);
            if (message != null && (value.EventType == ReplayEventTypesV17.EffectPresented
                                    || value.EventType == ReplayEventTypesV17.HitReactionPresented))
                foreach (var target in message.TargetIds.Where(item => !string.IsNullOrWhiteSpace(item)))
                    if (!HasEntity(active, target)) result.Errors.Add("presentation-target-missing:" + value.Sequence + ":" + target);
            if (value.EventType == ReplayEventTypesV17.EntityDespawned
                && (!active.TryGetValue(value.EntityId ?? "", out var generations)
                    || !generations.Remove(value.SpawnGeneration)))
                result.Errors.Add("despawn-entity-invalid:" + value.Sequence);
        }
    }

    private static bool AddEntity(IDictionary<string, HashSet<int>> active, string entityId, int generation)
    {
        if (!active.TryGetValue(entityId ?? "", out var generations))
        {
            generations = new HashSet<int>();
            active[entityId ?? ""] = generations;
        }
        return generations.Add(generation);
    }

    private static bool HasEntity(IReadOnlyDictionary<string, HashSet<int>> active, string entityId, int? generation = null)
    {
        return active.TryGetValue(entityId ?? "", out var generations)
               && (generation == null ? generations.Count > 0 : generations.Contains(generation.Value));
    }

    private static bool ValidEntity(ReplayEntityStateV17 entity) => entity != null
        && !string.IsNullOrWhiteSpace(entity.EntityId)
        && entity.SpawnGeneration > 0
        && entity.SlotIndex >= 0
        && (entity.Team == ReplayTeamsV17.Friendly
            || entity.Team == ReplayTeamsV17.Enemy
            || entity.Team == ReplayTeamsV17.Neutral)
        && entity.MaxHp >= 0;

    private static void ValidatePresentation(ReplayDocumentV17 document, ReplayValidationResultV17 result)
    {
        var assets = document.Assets.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Sha256))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (assets.Count != document.Assets.Count) result.Errors.Add("asset-id-duplicate-or-empty");
        foreach (var asset in assets.Values)
        {
            var assetError = ReplayAssetContractV17.Validate(asset, requirePayload: false);
            if (assetError.Length > 0) result.Errors.Add("asset-invalid:" + asset.Sha256 + ":" + assetError);
        }
        var reachable = ReplayPresentationReachabilityV17.AssetHashes(document);
        foreach (var hash in reachable.Where(hash => !assets.ContainsKey(hash))) result.Errors.Add("asset-missing:" + hash);
        foreach (var hash in assets.Keys.Where(hash => !reachable.Contains(hash))) result.Errors.Add("asset-unreachable:" + hash);

        var entityDescriptors = document.Presentation.Entities.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var cardDescriptors = document.Presentation.Cards.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var buffDescriptors = document.Presentation.Buffs.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var intentDescriptors = document.Presentation.Intents.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var effectDescriptors = document.Presentation.Effects.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var moduleDescriptors = document.Presentation.Modules
            .Select(item => (item.OwnerModId ?? "") + "|" + (item.TypeId ?? ""))
            .ToHashSet(StringComparer.Ordinal);
        if (entityDescriptors.Count != document.Presentation.Entities.Count
            || cardDescriptors.Count != document.Presentation.Cards.Count
            || buffDescriptors.Count != document.Presentation.Buffs.Count
            || intentDescriptors.Count != document.Presentation.Intents.Count
            || effectDescriptors.Count != document.Presentation.Effects.Count
            || moduleDescriptors.Count != document.Presentation.Modules.Count
            || entityDescriptors.Any(string.IsNullOrWhiteSpace)
            || cardDescriptors.Any(string.IsNullOrWhiteSpace)
            || buffDescriptors.Any(string.IsNullOrWhiteSpace)
            || intentDescriptors.Any(string.IsNullOrWhiteSpace)
            || effectDescriptors.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(document.Presentation.Scene.DescriptorId))
            result.Errors.Add("presentation-descriptor-duplicate-or-empty");
        foreach (var module in document.Presentation.Modules)
            if (!ValidExtensionIdentity(module.OwnerModId)
                || !ValidExtensionIdentity(module.TypeId)
                || module.SchemaVersion <= 0
                || module.Portability is not "Portable" and not "ProviderRequired"
                || module.Portability == "ProviderRequired" && string.IsNullOrWhiteSpace(module.RendererCapability))
                result.Errors.Add("presentation-module-invalid:" + module.OwnerModId + ":" + module.TypeId);
        if (document.Presentation.Scene.ReferenceWidth <= 0
            || document.Presentation.Scene.ReferenceHeight <= 0
            || document.Presentation.Scene.CameraOrthographicSizeQ16 <= 0
            || string.IsNullOrWhiteSpace(document.Presentation.Scene.SceneResourceId)
            || !ValidResourcePath(document.Presentation.Scene.SceneResourcePath)
            || !OptionalImageAsset(document.Presentation.Scene.BackgroundAssetSha256, assets))
            result.Errors.Add("scene-descriptor-invalid");
        var ui = document.Presentation.Ui;
        if (ui == null
            || !ValidResourcePath(ui.FightUiResourcePath)
            || !ValidResourcePath(ui.StatusBarResourcePath)
            || !ValidResourcePath(ui.HpItemResourcePath)
            || !ValidResourcePath(ui.BuffBarResourcePath)
            || !ValidResourcePath(ui.BuffItemResourcePath)
            || !ValidResourcePath(ui.ActionContentResourcePath)
            || !ValidResourcePath(ui.ActionItemResourcePath)
            || !ValidResourcePath(ui.CardItemResourcePath)
            || !string.Equals(ui.CloneMode, "NativePrefabSanitized", StringComparison.Ordinal))
            result.Errors.Add("ui-template-descriptor-invalid");
        foreach (var descriptor in document.Presentation.Entities)
        {
            if (descriptor.Provenance == null
                || !ReplayEntityArchetypes(descriptor.Archetype)
                || descriptor.SafeActionProfile is not "default" and not "static"
                || descriptor.Animations.Count == 0
                || descriptor.Animations.Select(item => item.State).Distinct(StringComparer.Ordinal).Count()
                   != descriptor.Animations.Count
                || !ValidOptionalResourcePath(descriptor.NativePrefabResourcePath)
                || !ValidOptionalResourcePath(descriptor.IdleResourcePath)
                || !ValidOptionalResourcePath(descriptor.PortraitResourcePath)
                || string.IsNullOrWhiteSpace(descriptor.NativePrefabResourcePath)
                   && string.IsNullOrWhiteSpace(descriptor.PortraitResourcePath)
                   && descriptor.Animations.All(item => string.IsNullOrWhiteSpace(item.ResourcePath)
                                                         && item.Frames.Count == 0))
                result.Errors.Add("entity-descriptor-invalid:" + descriptor.DescriptorId);
            foreach (var animation in descriptor.Animations)
            {
                var animationError = ValidateAnimationDescriptor(animation, assets);
                if (animationError.Length > 0)
                    result.Errors.Add("entity-animation-invalid:" + descriptor.DescriptorId
                                      + ":" + (animation?.State ?? "<null>") + ":" + animationError);
            }
        }
        foreach (var descriptor in document.Presentation.Cards)
            if (descriptor.Provenance == null
                || !ValidResourceOrImage(descriptor.IconResourcePath, descriptor.ArtworkAssetSha256, assets)
                || !ValidResourceOrImage(descriptor.FrameResourcePath, descriptor.FrameAssetSha256, assets)
                || string.IsNullOrWhiteSpace(descriptor.NativeCardType)
                || !ValidResourcePath(descriptor.NativeResourcePath)
                || !descriptor.NativeVisualTemplateRequired
                || !ValidOptionalResourcePath(descriptor.ResolvedSkinFrameResourcePath)
                || !ValidOptionalResourcePath(descriptor.ResolvedSkinBackgroundResourcePath)
                || !ValidCanonicalJson(descriptor.DynamicEffectParametersJson))
                result.Errors.Add("card-descriptor-invalid:" + descriptor.DescriptorId);
        foreach (var descriptor in document.Presentation.Buffs)
            if (descriptor.Provenance == null
                || descriptor.SortOrder < 0
                || !ValidResourceOrImage(descriptor.IconResourcePath, descriptor.IconAssetSha256, assets))
                result.Errors.Add("buff-descriptor-invalid:" + descriptor.DescriptorId);
        foreach (var descriptor in document.Presentation.Intents)
            if (descriptor.Provenance == null
                || !ValidResourceOrImage(descriptor.IconResourcePath, descriptor.IconAssetSha256, assets)
                || !ValidResourcePath(descriptor.BackIconResourcePath))
                result.Errors.Add("intent-descriptor-invalid:" + descriptor.DescriptorId);
        foreach (var descriptor in document.Presentation.Effects)
            if (descriptor.Primitive is not "Flash" and not "SpriteSequence" and not "NativeResource"
                || descriptor.DurationTicks <= 0
                || descriptor.FramesPerSecondQ16 <= 0
                || descriptor.Primitive == "SpriteSequence" && descriptor.Frames.Count == 0
                || descriptor.Primitive == "NativeResource" && !ValidResourcePath(descriptor.ResourcePath)
                || descriptor.Frames.Any(frame => !ValidFrame(frame, assets)))
                result.Errors.Add("effect-descriptor-invalid:" + descriptor.DescriptorId);
        foreach (var value in document.PresentationEvents)
        {
            var message = value.Presentation;
            if (message == null) result.Errors.Add("presentation-payload-missing:" + value.Sequence);
            if (message?.VisualInstanceId != null
                && (string.IsNullOrWhiteSpace(message.VisualInstanceId) || message.VisualInstanceId.Length > 256))
                result.Errors.Add("presentation-visual-identity-invalid:" + value.Sequence);
            if (message != null
                && (message.DelayTicks < 0
                    || message.DelayTicks > ReplayLimitsV17.MaximumTimelineTicks
                    || message.DurationTicks < 0
                    || message.DurationTicks > ReplayLimitsV17.MaximumTimelineTicks
                    || value.TimeTicks > ReplayLimitsV17.MaximumTimelineTicks - message.DelayTicks
                    || value.TimeTicks + message.DelayTicks > ReplayLimitsV17.MaximumTimelineTicks - message.DurationTicks))
                result.Errors.Add("presentation-time-invalid:" + value.Sequence);
            if (message != null
                && (message.TransformSamples == null
                    || message.TransformSamples.Count > ReplayLimitsV17.MaximumPresentationSamplesPerEvent
                    || message.TransformSamples.Any(sample => sample.OffsetTicks < 0
                                                             || sample.AlphaQ16 < 0
                                                             || sample.AlphaQ16 > 65_536)
                    || message.TransformSamples.Zip(
                            message.TransformSamples.Skip(1),
                            (left, right) => right.OffsetTicks < left.OffsetTicks)
                        .Any(backwards => backwards)))
                result.Errors.Add("presentation-transform-track-invalid:" + value.Sequence);
            if (message != null
                && (message.WorldTransformSamples == null
                    || message.WorldTransformSamples.Count > ReplayLimitsV17.MaximumPresentationSamplesPerEvent
                    || message.WorldTransformSamples.Any(sample => sample.OffsetTicks < 0
                                                                 || string.IsNullOrWhiteSpace(sample.SortingLayerName)
                                                                 || !ValidAttachmentBounds(sample.AttachmentBounds))
                    || message.WorldTransformSamples.Zip(
                            message.WorldTransformSamples.Skip(1),
                            (left, right) => right.OffsetTicks < left.OffsetTicks)
                        .Any(backwards => backwards)))
                result.Errors.Add("presentation-world-track-invalid:" + value.Sequence);
            if (message?.HasCameraState == true && message.CameraOrthographicSizeQ16 <= 0)
                result.Errors.Add("presentation-camera-state-invalid:" + value.Sequence);
            if (value.EventType == ReplayEventTypesV17.CardMotionPresented
                && (message?.TransformSamples == null || message.TransformSamples.Count < 2))
                result.Errors.Add("card-motion-track-missing:" + value.Sequence);
            if ((value.EventType == ReplayEventTypesV17.ActorAnimationPresented
                 || value.EventType == ReplayEventTypesV17.HitReactionPresented)
                && message?.AnimationState is "Attack" or "Skill" or "Hit" or "Defend"
                && (message.WorldTransformSamples == null || message.WorldTransformSamples.Count < 1))
                result.Errors.Add("actor-motion-track-missing:" + value.Sequence);
            if ((value.EventType == ReplayEventTypesV17.EntityPresented
                 || value.EventType == ReplayEventTypesV17.EntityPresentationChanged)
                && (message?.EntityBinding == null
                    || !entityDescriptors.Contains(message.EntityBinding.DescriptorId)
                    || string.IsNullOrWhiteSpace(message.EntityBinding.EntityId)
                    || message.EntityBinding.SpawnGeneration <= 0
                    || !ValidMeasuredBinding(message.EntityBinding)))
                result.Errors.Add("entity-descriptor-missing:" + value.Sequence);
            var descriptorId = message?.DescriptorId ?? "";
            if (value.EventType == ReplayEventTypesV17.SourcePresented
                && !string.IsNullOrWhiteSpace(descriptorId)
                && !cardDescriptors.Contains(descriptorId)
                && !intentDescriptors.Contains(descriptorId))
                result.Errors.Add("source-descriptor-missing:" + value.Sequence);
            var effectDescriptorId = message?.EffectDescriptorId ?? "";
            if (value.EventType == ReplayEventTypesV17.EffectPresented
                && (string.IsNullOrWhiteSpace(effectDescriptorId)
                    || !effectDescriptors.Contains(effectDescriptorId)))
                result.Errors.Add("effect-descriptor-missing:" + value.Sequence);
            if (value.EventType == ReplayEventTypesV17.AudioPresented)
            {
                var cue = message?.Audio;
                var embedded = cue != null
                               && !string.IsNullOrWhiteSpace(cue.AssetSha256)
                               && assets.TryGetValue(cue.AssetSha256, out var audioAsset)
                               && ValidAudioCue(cue, audioAsset);
                var referenced = cue != null && !string.IsNullOrWhiteSpace(cue.ResourcePath);
                if (!embedded && !referenced) result.Errors.Add("audio-resource-missing:" + value.Sequence);
            }
            if (value.EventType == ReplayEventTypesV17.ExtensionPresented
                && (message == null
                    || !ValidExtensionIdentity(message.ExtensionOwnerModId)
                    || !ValidExtensionIdentity(message.ExtensionTypeId)
                    || message.ExtensionSchemaVersion <= 0
                    || string.IsNullOrWhiteSpace(message.ExtensionEventId)
                    || !ValidCanonicalJson(message.ExtensionPayloadJson)))
                result.Errors.Add("presentation-extension-invalid:" + value.Sequence);
            if (value.EventType == ReplayEventTypesV17.ExtensionPresented
                && message != null
                && !document.Presentation.Modules.Any(module =>
                    string.Equals(module.OwnerModId, message.ExtensionOwnerModId, StringComparison.Ordinal)
                    && string.Equals(module.TypeId, message.ExtensionTypeId, StringComparison.Ordinal)
                    && module.SchemaVersion == message.ExtensionSchemaVersion))
                result.Errors.Add("presentation-extension-module-missing:" + value.Sequence);
            if (value.EventType == ReplayEventTypesV17.VisualStateCommitted)
            {
                var truthSequence = message?.TruthEventSequence ?? 0;
                var truth = document.TruthEvents.FirstOrDefault(item => item.Sequence == truthSequence);
                if (message == null
                    || message.Phase != ReplayPresentationPhasesV17.StateCommit
                    || truth == null
                    || truth.EventType != ReplayEventTypesV17.StateDeltaApplied
                    || !string.Equals(truth.TransactionId, value.TransactionId, StringComparison.Ordinal))
                    result.Errors.Add("visual-state-commit-invalid:" + value.Sequence);
            }
        }
        foreach (var checkpoint in document.PresentationCheckpoints)
            if (checkpoint.EntityBindings.Any(binding => !ValidMeasuredBinding(binding)))
                result.Errors.Add("checkpoint-entity-layout-invalid:" + checkpoint.EventSequence);
        var states = new[] { document.InitialState }.Concat(document.TruthCheckpoints.Select(item => item.State)).ToList();
        foreach (var card in states.SelectMany(item => item.Cards))
            if (!cardDescriptors.Contains(card.DescriptorId)) result.Errors.Add("visible-card-descriptor-missing:" + card.CardInstanceId);
            else if (string.Equals(card.Zone, "Hand", StringComparison.OrdinalIgnoreCase) && !ValidMeasuredCard(card))
                result.Errors.Add("visible-card-layout-missing:" + card.CardInstanceId);
        foreach (var card in document.TruthEvents
                     .SelectMany(item => item.Delta?.Operations ?? new List<ReplayStateOperationV17>())
                     .Select(item => item.Card)
                     .Where(item => item != null && string.Equals(item.Zone, "Hand", StringComparison.OrdinalIgnoreCase)))
            if (!ValidMeasuredCard(card!)) result.Errors.Add("visible-card-layout-missing:" + card!.CardInstanceId);
        foreach (var buff in states.SelectMany(item => item.Entities).SelectMany(item => item.Buffs))
            if (!buffDescriptors.Contains(buff.DescriptorId)) result.Errors.Add("visible-buff-descriptor-missing:" + buff.InstanceId);
        foreach (var intent in states.SelectMany(item => item.Intents))
            if (!intentDescriptors.Contains(intent.DescriptorId)) result.Errors.Add("visible-intent-descriptor-missing:" + intent.IntentInstanceId);
        foreach (var entity in states.SelectMany(item => item.Entities))
            if (!entityDescriptors.Contains(entity.DescriptorId)) result.Errors.Add("visible-entity-descriptor-missing:" + entity.EntityId);
        foreach (var extension in states.SelectMany(item => item.Extensions))
            if (!ValidExtensionIdentity(extension.OwnerModId)
                || !ValidExtensionIdentity(extension.TypeId)
                || string.IsNullOrWhiteSpace(extension.InstanceId)
                || extension.SchemaVersion <= 0
                || !ValidCanonicalJson(extension.PayloadJson))
                result.Errors.Add("visible-extension-invalid:" + extension.OwnerModId + ":" + extension.InstanceId);
        foreach (var state in states)
            if (state.Extensions.Select(item => item.OwnerModId + "|" + item.TypeId + "|" + item.InstanceId)
                .Distinct(StringComparer.Ordinal).Count() != state.Extensions.Count)
                result.Errors.Add("visible-extension-duplicate");
        var requiredEntities = document.InitialState.Entities.Select(item => EntityGenerationKey(item.EntityId, item.SpawnGeneration))
            .Concat(document.TruthEvents.Where(item => item.EventType == ReplayEventTypesV17.EntitySpawned && item.Entity != null)
                .Select(item => EntityGenerationKey(item.Entity!.EntityId, item.Entity.SpawnGeneration)))
            .ToHashSet(StringComparer.Ordinal);
        var boundEntities = document.PresentationEvents
            .Where(item => item.Presentation?.EntityBinding != null)
            .Select(item => EntityGenerationKey(item.Presentation!.EntityBinding!.EntityId, item.Presentation.EntityBinding.SpawnGeneration))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in requiredEntities.Where(key => !boundEntities.Contains(key)))
            result.Errors.Add("entity-presentation-missing:" + key);
    }

    private static void ValidateCheckpoints(ReplayDocumentV17 document, ReplayValidationResultV17 result)
    {
        if (document.TruthCheckpoints.Count != document.PresentationCheckpoints.Count)
        {
            result.Errors.Add("checkpoint-pair-count-invalid");
            return;
        }
        var expected = new ReplayDocumentV17
        {
            InitialState = document.InitialState,
            TruthEvents = document.TruthEvents,
            PresentationEvents = document.PresentationEvents,
            Presentation = document.Presentation
        };
        ReplayDocumentFinalizerV17.RebuildCheckpoints(expected);
        if (document.TruthCheckpoints.Count != expected.TruthCheckpoints.Count)
        {
            result.Errors.Add("checkpoint-schedule-invalid");
            return;
        }
        for (var index = 0; index < document.TruthCheckpoints.Count; index++)
        {
            var truth = document.TruthCheckpoints[index];
            var presentation = document.PresentationCheckpoints[index];
            if (truth.EventSequence != presentation.EventSequence) result.Errors.Add("checkpoint-pair-sequence-invalid:" + index);
            if (!string.Equals(ReplayCanonicalJsonV17.StateHash(truth.State), truth.StateSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ReplayCanonicalJsonV17.TruthCheckpointHash(truth), truth.CheckpointSha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("truth-checkpoint-invalid:" + truth.EventSequence);
            if (!string.Equals(ReplayCanonicalJsonV17.PresentationCheckpointHash(presentation), presentation.CheckpointSha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("presentation-checkpoint-invalid:" + presentation.EventSequence);
            if (!string.Equals(truth.CheckpointSha256, expected.TruthCheckpoints[index].CheckpointSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(presentation.CheckpointSha256, expected.PresentationCheckpoints[index].CheckpointSha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("checkpoint-projection-invalid:" + truth.EventSequence);
        }
    }

    private static string EntityGenerationKey(string entityId, int generation) => (entityId ?? "") + "|" + generation;

    private static bool ReplayEntityArchetypes(string value) => value == ReplayEntityArchetypesV17.PlayerCombatant
        || value == ReplayEntityArchetypesV17.EnemyCombatant
        || value == ReplayEntityArchetypesV17.AlliedCombatant
        || value == ReplayEntityArchetypesV17.NeutralCombatant;

    private static bool IsImageAsset(string sha256, IReadOnlyDictionary<string, ReplayAssetV17> assets) =>
        assets.TryGetValue(sha256 ?? "", out var asset) && asset.MediaType == "image/png";

    private static bool OptionalImageAsset(
        string sha256,
        IReadOnlyDictionary<string, ReplayAssetV17> assets) =>
        string.IsNullOrWhiteSpace(sha256) || IsImageAsset(sha256, assets);

    private static bool ValidResourceOrImage(
        string resourcePath,
        string assetSha256,
        IReadOnlyDictionary<string, ReplayAssetV17> assets) =>
        ValidResourcePath(resourcePath)
        || IsImageAsset(assetSha256, assets);

    private static bool ValidResourceOrFrames(
        string resourcePath,
        IReadOnlyCollection<ReplaySpriteFrameV17> frames,
        IReadOnlyDictionary<string, ReplayAssetV17> assets) =>
        ValidResourcePath(resourcePath)
        || frames.Count > 0 && frames.All(frame => ValidFrame(frame, assets));

    private static string ValidateAnimationDescriptor(
        ReplayAnimationDescriptorV17? animation,
        IReadOnlyDictionary<string, ReplayAssetV17> assets)
    {
        if (animation == null) return "animation-null";
        if (string.IsNullOrWhiteSpace(animation.State)) return "animation-state-empty";
        if (animation.FrameDurationTicks <= 0) return "frame-duration-invalid";
        if (animation.FrameDurationTicks > ReplayProtocolV17.TimebaseTicksPerSecond * 10)
            return "frame-duration-exceeded";
        if (animation.Direction is not "Left" and not "Right") return "direction-invalid";
        if (animation.TargetScaleQ16 <= 0) return "target-scale-invalid";
        var embeddedFrames = animation.Frames ?? new List<ReplaySpriteFrameV17>();
        var frameNameError = ReplayFrameSequenceContractV17.ValidateNames(
            animation.FrameNames,
            required: embeddedFrames.Count == 0);
        if (frameNameError.Length > 0) return frameNameError;
        if (!ValidOptionalResourcePath(animation.SoundResourcePath)) return "sound-resource-invalid";
        if (!ValidResourceOrFrames(animation.ResourcePath, embeddedFrames, assets)) return "frame-resource-invalid";
        return "";
    }

    private static bool ValidOptionalResourcePath(string path) =>
        string.IsNullOrWhiteSpace(path) || ValidResourcePath(path);

    private static bool ValidMeasuredBinding(ReplayEntityPresentationBindingV17? value)
    {
        if (value == null
            || !value.HasMeasuredLayout
            || !NonZeroScale(value.RootScale)
            || !NonZeroScale(value.BodyLocalScale)
            || value.StatusBarSize == null
            || value.StatusBarSize.X <= 0
            || value.StatusBarSize.Y <= 0
            || value.HudScaleQ16 <= 0
            || string.IsNullOrWhiteSpace(value.SortingLayerName)
            || !ValidCustomPresentation(value.CustomPresentation)
            || !ValidAttachmentBounds(value.AttachmentBounds)) return false;
        return (value.HeadLocalPosition?.X ?? 0) != (value.BottomLocalPosition?.X ?? 0)
               || (value.HeadLocalPosition?.Y ?? 0) != (value.BottomLocalPosition?.Y ?? 0);
    }

    private static bool ValidCustomPresentation(ReplayCustomEntityPresentationV17? value)
    {
        if (value == null) return true;
        if (!ValidExtensionIdentity(value.OwnerModId)
            || value.SchemaVersion <= 0
            || value.PresentationMode is not "WorldEntity" and not "OwnerAttachedProxy"
            || value.HudMode is not "NativeHorizontal" and not "DetachedRightVertical"
            || value.HudScaleQ16 <= 0
            || !ValidOptionalResourcePath(value.BadgeIconResourcePath)) return false;
        if (value.PresentationMode == "OwnerAttachedProxy")
            return !string.IsNullOrWhiteSpace(value.OwnerEntityId)
                   && value.ReferenceHeightPixels > 0
                   && value.HorizontalOverlapQ16 is >= 0 and <= 65_536
                   && value.AttackFocusTravelPixels >= 0
                   && value.InterferenceFocusTravelPixels >= 0
                   && value.SupportFocusTravelPixels >= 0;
        return true;
    }

    private static bool ValidAttachmentBounds(ReplayBoundsQ16V17? value) => value == null
        || value.Center != null && value.Size != null && value.Size.X > 0 && value.Size.Y > 0 && value.Size.Z >= 0;

    private static bool ValidActionPhase(ReplayJournalEventV17 value)
    {
        var message = value.Presentation;
        if (message == null) return false;
        return value.EventType switch
        {
            ReplayEventTypesV17.SourcePresented =>
                message.Phase == ReplayPresentationPhasesV17.SourceFocus && message.PhaseOrdinal == 0,
            ReplayEventTypesV17.CardMotionPresented =>
                message.Phase == ReplayPresentationPhasesV17.CardTravel && message.PhaseOrdinal == 1,
            ReplayEventTypesV17.ActorAnimationPresented =>
                message.Phase == ReplayPresentationPhasesV17.ActorFocus && message.PhaseOrdinal == 2,
            ReplayEventTypesV17.EffectPresented or ReplayEventTypesV17.HitReactionPresented
                or ReplayEventTypesV17.DamageTextPresented or ReplayEventTypesV17.ExtensionPresented =>
                message.Phase == ReplayPresentationPhasesV17.Impact && message.PhaseOrdinal == 3,
            ReplayEventTypesV17.VisualStateCommitted =>
                message.Phase == ReplayPresentationPhasesV17.StateCommit && message.PhaseOrdinal == 4,
            _ => false
        };
    }

    private static bool ValidMeasuredCard(ReplayVisibleCardStateV17 value) => value.HasMeasuredLayout
        && value.CanvasSize != null
        && value.CanvasSize.X > 0
        && value.CanvasSize.Y > 0
        && ValidOptionalResourcePath(value.EnchantIconResourcePath)
        && NonZeroScale(value.LocalScale);

    private static bool NonZeroScale(ReplayVector3Q16V17? value) => value != null
        && value.X != 0
        && value.Y != 0
        && value.Z != 0
        && Math.Abs((long)value.X) <= 64L * 65_536L
        && Math.Abs((long)value.Y) <= 64L * 65_536L
        && Math.Abs((long)value.Z) <= 64L * 65_536L;

    private static bool ValidResourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > ReplayLimitsV17.MaximumTextLength) return false;
        var normalized = path.Replace('\\', '/');
        return normalized.IndexOf('\0') < 0
               && !normalized.Split('/').Any(segment => segment == "..");
    }

    private static bool ValidCanonicalJson(string payload)
    {
        if ((payload?.Length ?? 0) > ReplayLimitsV17.MaximumTextLength) return false;
        if (!ReplayCanonicalJsonV17.TryCanonicalizeJsonPayload(payload ?? "", out var canonical)) return false;
        return string.IsNullOrWhiteSpace(payload)
               || string.Equals(payload, canonical, StringComparison.Ordinal);
    }

    private static bool ValidExtensionIdentity(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character => char.IsLetterOrDigit(character)
                                  || character is '.' or '-' or '_');

    private static bool ValidFrame(
        ReplaySpriteFrameV17 frame,
        IReadOnlyDictionary<string, ReplayAssetV17> assets)
    {
        if (frame == null || !assets.TryGetValue(frame.AssetSha256 ?? "", out var asset)) return false;
        return asset.MediaType == "image/png"
               && frame.RectX >= 0
               && frame.RectY >= 0
               && frame.RectWidth > 0
               && frame.RectHeight > 0
               && (long)frame.RectX + frame.RectWidth <= asset.Width
               && (long)frame.RectY + frame.RectHeight <= asset.Height
               && frame.PivotXQ16 is >= 0 and <= 65_536
               && frame.PivotYQ16 is >= 0 and <= 65_536
               && frame.PixelsPerUnitQ16 > 0
               && frame.Border.X >= 0
               && frame.Border.Y >= 0
               && frame.Border.Z >= 0
               && frame.Border.W >= 0;
    }

    private static bool ValidAudioCue(ReplayAudioCueV17 cue, ReplayAssetV17 asset)
    {
        const long maximumTimelineSamples = 48_000L * 24L * 60L * 60L;
        var loopDisabled = cue.LoopStartSample == 0 && cue.LoopEndSample == 0;
        var loopValid = cue.LoopStartSample >= 0
                        && cue.LoopEndSample > cue.LoopStartSample
                        && cue.LoopEndSample <= asset.SampleFrames;
        return asset.MediaType == "audio/wav"
               && cue.StartSample >= 0
               && cue.StartSample <= maximumTimelineSamples
               && cue.SourceOffsetSample >= 0
               && cue.SourceOffsetSample < asset.SampleFrames
               && cue.DurationSamples > 0
               && cue.DurationSamples <= maximumTimelineSamples
               && cue.GainQ16 is >= 0 and <= 262_144
               && cue.PanQ16 is >= -65_536 and <= 65_536
               && cue.PlaybackRateQ16 is >= 4_096 and <= 262_144
               && (loopDisabled || loopValid)
               && cue.FadeInSamples >= 0
               && cue.FadeOutSamples >= 0
               && cue.FadeInSamples <= cue.DurationSamples
               && cue.FadeOutSamples <= cue.DurationSamples;
    }
}
