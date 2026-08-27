using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

internal sealed class ReplayJournalBuilderV12
{
    private readonly ReplayStateReducerV12 reducer = new();
    private readonly Dictionary<string, int> steps = new(StringComparer.Ordinal);
    private readonly HashSet<string> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReplayCausalTransactionV12> transactions = new(StringComparer.Ordinal);
    private long sequence;
    private int transactionSequence;
    private long lastTimeTicks;
    private string previousTruthHash = "";
    private string previousPresentationHash = "";

    internal ReplayJournalBuilderV12(ReplayDocumentHeaderCoreV12 header, ReplayPublicStateV12 initialState)
    {
        Document = new ReplayDocumentV12
        {
            Header = ReplayCanonicalJsonV12.Clone(header ?? new ReplayDocumentHeaderCoreV12()),
            InitialState = ReplayStateReducerV12.Normalize(initialState)
        };
        reducer.Reset(Document.InitialState);
    }

    internal ReplayDocumentV12 Document { get; }

    internal ReplayPublicStateV12 CurrentState => reducer.Current;

    internal long LastSequence => sequence;

    internal IReadOnlyDictionary<string, ReplayCausalTransactionV12> Transactions => transactions;

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
        if (!ReplayTransactionKindsV12.Supported.Contains(kind ?? ""))
            throw new InvalidOperationException("Unsupported replay transaction kind: " + kind);
        if (!string.IsNullOrWhiteSpace(parentTransactionId)
            && (!transactions.ContainsKey(parentTransactionId) || completed.Contains(parentTransactionId)))
            throw new InvalidOperationException("Replay parent transaction is missing or completed: " + parentTransactionId);
        var id = "transaction-" + (++transactionSequence).ToString("D8");
        var transaction = new ReplayCausalTransactionV12
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
        steps.Add(id, 0);
        AppendTruth(new ReplayJournalEventV12
        {
            TransactionId = id,
            ParentTransactionId = parentTransactionId ?? "",
            RoundSequence = Math.Max(0, roundSequence),
            ActorTurnSequence = Math.Max(0, actorTurnSequence),
            TimeTicks = Math.Max(0, timeTicks),
            AuthorityKind = authorityKind ?? "Host",
            IssuerPlayerId = issuerPlayerId ?? "",
            ActorId = actorId ?? "",
            EventType = ReplayEventTypesV12.TransactionStarted,
            Transaction = ReplayCanonicalJsonV12.Clone(transaction)
        });
        return id;
    }

    internal ReplayJournalEventV12 AddTruthMarker(
        string transactionId,
        string eventType,
        long timeTicks,
        string actorId = "")
    {
        RequireOpen(transactionId);
        if (!ReplayEventTypesV12.Truth.Contains(eventType ?? ""))
            throw new InvalidOperationException("Unsupported replay truth marker: " + eventType);
        return AppendTruth(new ReplayJournalEventV12
        {
            TransactionId = transactionId,
            RoundSequence = reducer.Current.RoundSequence,
            ActorTurnSequence = reducer.Current.ActorTurnSequence,
            TimeTicks = Math.Max(0, timeTicks),
            ActorId = actorId ?? "",
            EventType = eventType ?? ""
        });
    }

    internal IReadOnlyList<ReplayJournalEventV12> ApplyObservedState(
        string transactionId,
        ReplayPublicStateV12 observed,
        long timeTicks)
    {
        RequireOpen(transactionId);
        var diff = ReplayStateReducerV12.CreateDiff(reducer.Current, observed);
        var result = new List<ReplayJournalEventV12>();
        foreach (var entity in diff.Despawned.OrderBy(item => item.EntityId, StringComparer.Ordinal)
                     .ThenBy(item => item.SpawnGeneration))
            result.Add(AppendTruth(new ReplayJournalEventV12
            {
                TransactionId = transactionId,
                RoundSequence = observed.RoundSequence,
                ActorTurnSequence = observed.ActorTurnSequence,
                TimeTicks = Math.Max(0, timeTicks),
                ActorId = entity.EntityId,
                EventType = ReplayEventTypesV12.EntityDespawned,
                EntityId = entity.EntityId,
                SpawnGeneration = entity.SpawnGeneration
            }));
        foreach (var entity in diff.Spawned.OrderBy(item => item.EntityId, StringComparer.Ordinal)
                     .ThenBy(item => item.SpawnGeneration))
            result.Add(AppendTruth(new ReplayJournalEventV12
            {
                TransactionId = transactionId,
                RoundSequence = observed.RoundSequence,
                ActorTurnSequence = observed.ActorTurnSequence,
                TimeTicks = Math.Max(0, timeTicks),
                ActorId = entity.EntityId,
                EventType = ReplayEventTypesV12.EntitySpawned,
                Entity = ReplayStateReducerV12.Clone(entity)
            }));
        if (diff.Delta.Operations.Count > 0)
            result.Add(AppendTruth(new ReplayJournalEventV12
            {
                TransactionId = transactionId,
                RoundSequence = observed.RoundSequence,
                ActorTurnSequence = observed.ActorTurnSequence,
                TimeTicks = Math.Max(0, timeTicks),
                ActorId = observed.ActiveActorId ?? "",
                EventType = ReplayEventTypesV12.StateDeltaApplied,
                Delta = ReplayCanonicalJsonV12.Clone(diff.Delta)
            }));
        return result;
    }

    internal ReplayJournalEventV12 AddPresentation(
        string transactionId,
        string eventType,
        ReplayPresentationMessageV12 message,
        long timeTicks,
        string actorId = "")
    {
        RequireOpen(transactionId);
        if (!ReplayEventTypesV12.Presentation.Contains(eventType ?? ""))
            throw new InvalidOperationException("Unsupported replay presentation event: " + eventType);
        return AppendPresentation(new ReplayJournalEventV12
        {
            TransactionId = transactionId,
            RoundSequence = reducer.Current.RoundSequence,
            ActorTurnSequence = reducer.Current.ActorTurnSequence,
            TimeTicks = Math.Max(0, timeTicks),
            ActorId = actorId ?? message?.ActorId ?? "",
            EventType = eventType ?? "",
            Presentation = ReplayCanonicalJsonV12.Clone(message ?? new ReplayPresentationMessageV12())
        });
    }

    internal ReplayJournalEventV12 CompleteTransaction(string transactionId, long timeTicks)
    {
        RequireOpen(transactionId);
        var value = AppendTruth(new ReplayJournalEventV12
        {
            TransactionId = transactionId,
            RoundSequence = reducer.Current.RoundSequence,
            ActorTurnSequence = reducer.Current.ActorTurnSequence,
            TimeTicks = Math.Max(0, timeTicks),
            ActorId = transactions[transactionId].ActorId,
            EventType = ReplayEventTypesV12.TransactionCompleted
        });
        completed.Add(transactionId);
        return value;
    }

    internal ReplayJournalEventV12 AbortTransaction(string transactionId, long timeTicks, string reason)
    {
        RequireOpen(transactionId);
        var value = AppendTruth(new ReplayJournalEventV12
        {
            TransactionId = transactionId,
            RoundSequence = reducer.Current.RoundSequence,
            ActorTurnSequence = reducer.Current.ActorTurnSequence,
            TimeTicks = Math.Max(0, timeTicks),
            ActorId = transactions[transactionId].ActorId,
            EventType = ReplayEventTypesV12.TransactionAborted,
            Transaction = new ReplayCausalTransactionV12
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

    private ReplayJournalEventV12 AppendTruth(ReplayJournalEventV12 value)
    {
        value.Lane = ReplayJournalLanesV12.Truth;
        PrepareEvent(value);
        var before = ReplayCanonicalJsonV12.StateHash(reducer.Current);
        value.StateHashBefore = before;
        reducer.Apply(value, verifyHashes: false);
        value.StateHashAfter = ReplayCanonicalJsonV12.StateHash(reducer.Current);
        value.PreviousLaneEventHash = previousTruthHash;
        value.EventHash = ReplayCanonicalJsonV12.EventHash(value);
        previousTruthHash = value.EventHash;
        Document.TruthEvents.Add(value);
        return value;
    }

    private ReplayJournalEventV12 AppendPresentation(ReplayJournalEventV12 value)
    {
        value.Lane = ReplayJournalLanesV12.Presentation;
        PrepareEvent(value);
        value.StateHashBefore = "";
        value.StateHashAfter = "";
        value.PreviousLaneEventHash = previousPresentationHash;
        value.EventHash = ReplayCanonicalJsonV12.EventHash(value);
        previousPresentationHash = value.EventHash;
        Document.PresentationEvents.Add(value);
        return value;
    }

    private void PrepareEvent(ReplayJournalEventV12 value)
    {
        var transactionId = value.TransactionId ?? "";
        value.TransactionId = transactionId;
        if (value.TimeTicks < lastTimeTicks)
            throw new InvalidOperationException("Replay logical time moved backwards at event " + (sequence + 1) + ".");
        value.Sequence = ++sequence;
        value.EventId = "event-" + value.Sequence.ToString("D10");
        if (transactions.TryGetValue(transactionId, out var transaction))
        {
            if (string.IsNullOrWhiteSpace(value.IssuerPlayerId)) value.IssuerPlayerId = transaction.IssuerPlayerId;
            if (string.IsNullOrWhiteSpace(value.ActorId)) value.ActorId = transaction.ActorId;
        }
        value.TimeTicks = Math.Max(0, value.TimeTicks);
        lastTimeTicks = value.TimeTicks;
        value.StepOrdinal = NextStep(transactionId);
        if (value.RoundSequence <= 0) value.RoundSequence = reducer.Current.RoundSequence;
        if (value.ActorTurnSequence <= 0) value.ActorTurnSequence = reducer.Current.ActorTurnSequence;
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

internal static class ReplayDocumentFinalizerV12
{
    internal static ReplayValidationResultV12 FinalizeAndValidate(ReplayDocumentEnvelopeV12 envelope)
    {
        if (envelope == null) throw new ArgumentNullException(nameof(envelope));
        Finalize(envelope.Document);
        envelope.DeclaredDocumentRoot = ReplayCanonicalJsonV12.DocumentRoot(envelope.Document.Header);
        return ReplayDocumentValidatorV12.Validate(envelope);
    }

    internal static void Finalize(ReplayDocumentV12 document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        document.Header.DocumentVersion = ReplayProtocolV12.DocumentVersion;
        document.Header.MinimumReadableDocumentVersion = ReplayProtocolV12.MinimumReadableDocumentVersion;
        document.Header.PackageVersion = ReplayProtocolV12.PackageVersion;
        document.Header.PresentationAbi = ReplayProtocolV12.PresentationAbi;
        document.Header.TimebaseTicksPerSecond = ReplayProtocolV12.TimebaseTicksPerSecond;
        document.Header.RequiredCapabilities = ReplayCapabilitiesV12.Required.OrderBy(item => item, StringComparer.Ordinal).ToList();
        document.Header.OptionalCapabilities = ReplayCapabilitiesV12.Optional.OrderBy(item => item, StringComparer.Ordinal).ToList();
        document.InitialState = ReplayStateReducerV12.Normalize(document.InitialState);
        document.Presentation = ReplayCanonicalJsonV12.NormalizePresentation(document.Presentation);
        document.Assets = PruneAssets(document);
        RebuildEventHashes(document);
        RebuildCheckpoints(document);
        document.Header.InitialPublicStateSha256 = ReplayCanonicalJsonV12.StateHash(document.InitialState);
        var reducer = new ReplayStateReducerV12();
        reducer.Reset(document.InitialState);
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence)) reducer.Apply(value);
        document.Header.FinalPublicStateSha256 = ReplayCanonicalJsonV12.StateHash(reducer.Current);
        document.Header.TruthEventCount = document.TruthEvents.Count;
        document.Header.PresentationEventCount = document.PresentationEvents.Count;
        document.Header.TruthCheckpointCount = document.TruthCheckpoints.Count;
        document.Header.PresentationCheckpointCount = document.PresentationCheckpoints.Count;
        document.Header.AssetCount = document.Assets.Count;
        document.Header.TruthRoot = ReplayCanonicalJsonV12.TruthRoot(document);
        document.Header.PresentationRoot = ReplayCanonicalJsonV12.PresentationRoot(document);
    }

    private static void RebuildEventHashes(ReplayDocumentV12 document)
    {
        var reducer = new ReplayStateReducerV12();
        reducer.Reset(document.InitialState);
        var previousTruth = "";
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence))
        {
            value.Lane = ReplayJournalLanesV12.Truth;
            value.PreviousLaneEventHash = previousTruth;
            value.StateHashBefore = ReplayCanonicalJsonV12.StateHash(reducer.Current);
            reducer.Apply(value, verifyHashes: false);
            value.StateHashAfter = ReplayCanonicalJsonV12.StateHash(reducer.Current);
            value.EventHash = ReplayCanonicalJsonV12.EventHash(value);
            previousTruth = value.EventHash;
        }
        var previousPresentation = "";
        foreach (var value in document.PresentationEvents.OrderBy(item => item.Sequence))
        {
            value.Lane = ReplayJournalLanesV12.Presentation;
            value.StateHashBefore = "";
            value.StateHashAfter = "";
            value.PreviousLaneEventHash = previousPresentation;
            value.EventHash = ReplayCanonicalJsonV12.EventHash(value);
            previousPresentation = value.EventHash;
        }
    }

    internal static void RebuildCheckpoints(ReplayDocumentV12 document)
    {
        document.TruthCheckpoints.Clear();
        document.PresentationCheckpoints.Clear();
        var reducer = new ReplayStateReducerV12();
        reducer.Reset(document.InitialState);
        var bindings = new Dictionary<string, ReplayEntityPresentationBindingV12>(StringComparer.Ordinal);
        var views = new Dictionary<string, ReplayEntityViewStateV12>(StringComparer.Ordinal);
        var lastTruthHash = "";
        var lastPresentationHash = "";
        var completedTransactions = 0;
        var checkpointTransactions = new HashSet<string>(StringComparer.Ordinal);
        var all = document.TruthEvents.Concat(document.PresentationEvents).OrderBy(item => item.Sequence).ToList();
        foreach (var value in all)
        {
            if (string.Equals(value.Lane, ReplayJournalLanesV12.Truth, StringComparison.Ordinal))
            {
                reducer.Apply(value);
                lastTruthHash = value.EventHash;
                if (value.EventType == ReplayEventTypesV12.EntityDespawned)
                {
                    var key = EntityKey(value.EntityId, value.SpawnGeneration);
                    bindings.Remove(key);
                    views.Remove(key);
                }
                if (value.EventType == ReplayEventTypesV12.BattleMaterialized
                    || value.EventType == ReplayEventTypesV12.RoundStarted
                    || value.EventType == ReplayEventTypesV12.EntitySpawned
                    || value.EventType == ReplayEventTypesV12.EntityDespawned
                    || value.EventType == ReplayEventTypesV12.BattleFinalized)
                    checkpointTransactions.Add(value.TransactionId);
                if (value.EventType == ReplayEventTypesV12.TransactionCompleted) completedTransactions++;
            }
            else
            {
                ApplyPresentationProjection(value, bindings, views);
                lastPresentationHash = value.EventHash;
            }

            if (!ShouldCheckpoint(value, completedTransactions, checkpointTransactions)) continue;
            var truth = new ReplayTruthCheckpointV12
            {
                EventSequence = value.Sequence,
                TimeTicks = value.TimeTicks,
                LastTruthEventHash = lastTruthHash,
                State = reducer.Current
            };
            truth.StateSha256 = ReplayCanonicalJsonV12.StateHash(truth.State);
            truth.CheckpointSha256 = ReplayCanonicalJsonV12.TruthCheckpointHash(truth);
            document.TruthCheckpoints.Add(truth);
            var presentation = new ReplayPresentationCheckpointV12
            {
                EventSequence = value.Sequence,
                TimeTicks = value.TimeTicks,
                LastPresentationEventHash = lastPresentationHash,
                SceneDescriptorId = document.Presentation.Scene.DescriptorId,
                EntityBindings = bindings.Values.Select(ReplayCanonicalJsonV12.Clone).ToList(),
                EntityViews = ProjectViewsAt(document, bindings, views, value.TimeTicks)
            };
            presentation.CheckpointSha256 = ReplayCanonicalJsonV12.PresentationCheckpointHash(presentation);
            document.PresentationCheckpoints.Add(presentation);
        }
        if (all.Count > 0 && (document.TruthCheckpoints.Count == 0
                              || document.TruthCheckpoints[document.TruthCheckpoints.Count - 1].EventSequence != all[all.Count - 1].Sequence))
        {
            var value = all[all.Count - 1];
            var truth = new ReplayTruthCheckpointV12
            {
                EventSequence = value.Sequence,
                TimeTicks = value.TimeTicks,
                LastTruthEventHash = lastTruthHash,
                State = reducer.Current
            };
            truth.StateSha256 = ReplayCanonicalJsonV12.StateHash(truth.State);
            truth.CheckpointSha256 = ReplayCanonicalJsonV12.TruthCheckpointHash(truth);
            document.TruthCheckpoints.Add(truth);
            var presentation = new ReplayPresentationCheckpointV12
            {
                EventSequence = value.Sequence,
                TimeTicks = value.TimeTicks,
                LastPresentationEventHash = lastPresentationHash,
                SceneDescriptorId = document.Presentation.Scene.DescriptorId,
                EntityBindings = bindings.Values.Select(ReplayCanonicalJsonV12.Clone).ToList(),
                EntityViews = ProjectViewsAt(document, bindings, views, value.TimeTicks)
            };
            presentation.CheckpointSha256 = ReplayCanonicalJsonV12.PresentationCheckpointHash(presentation);
            document.PresentationCheckpoints.Add(presentation);
        }
    }

    private static bool ShouldCheckpoint(
        ReplayJournalEventV12 value,
        int completedTransactions,
        ISet<string> checkpointTransactions)
    {
        if (!string.Equals(value.Lane, ReplayJournalLanesV12.Truth, StringComparison.Ordinal)) return false;
        if (value.EventType != ReplayEventTypesV12.TransactionCompleted) return false;
        return checkpointTransactions.Contains(value.TransactionId)
               || completedTransactions > 0
                  && completedTransactions % ReplayProtocolV12.DefaultCheckpointTransactionInterval == 0;
    }

    private static void ApplyPresentationProjection(
        ReplayJournalEventV12 value,
        IDictionary<string, ReplayEntityPresentationBindingV12> bindings,
        IDictionary<string, ReplayEntityViewStateV12> views)
    {
        var message = value.Presentation;
        if (message?.EntityBinding != null
            && (value.EventType == ReplayEventTypesV12.EntityPresented
                || value.EventType == ReplayEventTypesV12.EntityPresentationChanged))
        {
            var binding = ReplayCanonicalJsonV12.Clone(message.EntityBinding);
            bindings[EntityKey(binding.EntityId, binding.SpawnGeneration)] = binding;
        }
        if (value.EventType == ReplayEventTypesV12.ActorAnimationPresented
            || value.EventType == ReplayEventTypesV12.HitReactionPresented)
        {
            var entityId = message?.ActorId ?? value.ActorId;
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                var binding = bindings.Values.LastOrDefault(item => string.Equals(item.EntityId, entityId, StringComparison.Ordinal));
                if (binding != null)
                    views[EntityKey(binding.EntityId, binding.SpawnGeneration)] = new ReplayEntityViewStateV12
                    {
                        EntityId = binding.EntityId,
                        SpawnGeneration = binding.SpawnGeneration,
                        AnimationState = string.IsNullOrWhiteSpace(message?.AnimationState) ? "Idle" : message!.AnimationState,
                        FrameIndex = 0,
                        AnimationStartedTicks = value.TimeTicks,
                        AnimationEndsTicks = message?.DurationTicks > 0 ? value.TimeTicks + message.DurationTicks : 0L
                    };
            }
        }
    }

    private static List<ReplayEntityViewStateV12> ProjectViewsAt(
        ReplayDocumentV12 document,
        IReadOnlyDictionary<string, ReplayEntityPresentationBindingV12> bindings,
        IReadOnlyDictionary<string, ReplayEntityViewStateV12> views,
        long timeTicks)
    {
        var descriptors = document.Presentation.Entities.ToDictionary(item => item.DescriptorId, StringComparer.Ordinal);
        var result = new List<ReplayEntityViewStateV12>();
        foreach (var pair in views.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!bindings.TryGetValue(pair.Key, out var binding)
                || !descriptors.TryGetValue(binding.DescriptorId, out var descriptor)) continue;
            var value = ReplayCanonicalJsonV12.Clone(pair.Value);
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
            if (animation?.Frames.Count > 0)
            {
                var elapsed = Math.Max(0L, timeTicks - value.AnimationStartedTicks);
                var frame = (long)Math.Floor(
                    elapsed / (double)ReplayProtocolV12.TimebaseTicksPerSecond
                    * Math.Max(1, animation.FramesPerSecondQ16) / 65_536d);
                value.FrameIndex = animation.Loop
                    ? (int)(frame % animation.Frames.Count)
                    : (int)Math.Min(animation.Frames.Count - 1, frame);
            }
            result.Add(value);
        }
        return result;
    }

    private static List<ReplayAssetV12> PruneAssets(ReplayDocumentV12 document)
    {
        var required = ReplayPresentationReachabilityV12.AssetHashes(document);
        return (document.Assets ?? new List<ReplayAssetV12>())
            .Where(item => item != null && required.Contains(item.Sha256 ?? ""))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Sha256, StringComparer.Ordinal)
            .ToList();
    }

    private static string EntityKey(string entityId, int generation) => entityId + "|" + generation;
}

internal static class ReplayPresentationReachabilityV12
{
    internal static HashSet<string> AssetHashes(ReplayDocumentV12 document)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string value) { if (!string.IsNullOrWhiteSpace(value)) result.Add(value); }
        var presentation = document?.Presentation ?? new ReplayPresentationCapsuleV12();
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
        foreach (var value in document?.PresentationEvents ?? new List<ReplayJournalEventV12>())
            Add(value.Presentation?.Audio?.AssetSha256 ?? "");
        return result;
    }
}

internal static class ReplayDocumentValidatorV12
{
    internal static ReplayValidationResultV12 Validate(ReplayDocumentEnvelopeV12 envelope)
    {
        var result = new ReplayValidationResultV12();
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
            || document.Assets == null)
        {
            result.Errors.Add("document-shape-invalid");
            return result;
        }
        var header = document.Header ?? new ReplayDocumentHeaderCoreV12();
        try
        {
            ValidateLimits(document, result);
            if (!result.IsValid) return result;
            if (header.DocumentVersion != ReplayProtocolV12.DocumentVersion
                || header.MinimumReadableDocumentVersion != ReplayProtocolV12.MinimumReadableDocumentVersion
                || header.PackageVersion != ReplayProtocolV12.PackageVersion
                || header.TimebaseTicksPerSecond != ReplayProtocolV12.TimebaseTicksPerSecond)
                result.Errors.Add("version-invalid");
            if (string.IsNullOrWhiteSpace(header.RecordId) || string.IsNullOrWhiteSpace(header.BattleSessionId))
                result.Errors.Add("identity-missing");
            if (!string.Equals(header.PresentationAbi, ReplayProtocolV12.PresentationAbi, StringComparison.Ordinal))
                result.Errors.Add("presentation-abi-unsupported");
            var requiredCapabilities = header.RequiredCapabilities ?? new List<string>();
            if (requiredCapabilities.Count != requiredCapabilities.Distinct(StringComparer.Ordinal).Count()
                || !requiredCapabilities.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(ReplayCapabilitiesV12.Required))
                result.Errors.Add("required-capability-invalid");
            var optionalCapabilities = header.OptionalCapabilities ?? new List<string>();
            if (optionalCapabilities.Count != optionalCapabilities.Distinct(StringComparer.Ordinal).Count())
                result.Errors.Add("optional-capability-invalid");
            if (!string.Equals(ReplayCanonicalJsonV12.StateHash(document.InitialState), header.InitialPublicStateSha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("initial-state-hash-invalid");

            ValidateEvents(document, result);
            ValidatePresentation(document, result);
            ValidateCheckpoints(document, result);
            if (!string.Equals(ReplayCanonicalJsonV12.TruthRoot(document), header.TruthRoot, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("truth-root-invalid");
            if (!string.Equals(ReplayCanonicalJsonV12.PresentationRoot(document), header.PresentationRoot, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("presentation-root-invalid");
            var documentRoot = ReplayCanonicalJsonV12.DocumentRoot(header);
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

    private static void ValidateLimits(ReplayDocumentV12 document, ReplayValidationResultV12 result)
    {
        var header = document.Header;
        var headerText = new[]
        {
            header.RecordId, header.AdventureId, header.BattleSessionId, header.LevelId, header.BattleTitle,
            header.StartedUtc, header.EndedUtc, header.Result, header.GameBuildProvenance, header.RecorderBuild
        };
        if (headerText.Any(item => (item?.Length ?? 0) > ReplayLimitsV12.MaximumTextLength))
            result.Errors.Add("header-text-budget-exceeded");
        if (document.TruthEvents.Count > ReplayLimitsV12.MaximumEventsPerLane
            || document.PresentationEvents.Count > ReplayLimitsV12.MaximumEventsPerLane)
            result.Errors.Add("event-budget-exceeded");
        if (document.TruthCheckpoints.Count > ReplayLimitsV12.MaximumCheckpoints
            || document.PresentationCheckpoints.Count > ReplayLimitsV12.MaximumCheckpoints)
            result.Errors.Add("checkpoint-budget-exceeded");
        if (document.Presentation.Entities.Count > ReplayLimitsV12.MaximumDescriptorsPerKind
            || document.Presentation.Cards.Count > ReplayLimitsV12.MaximumDescriptorsPerKind
            || document.Presentation.Buffs.Count > ReplayLimitsV12.MaximumDescriptorsPerKind
            || document.Presentation.Intents.Count > ReplayLimitsV12.MaximumDescriptorsPerKind
            || document.Presentation.Effects.Count > ReplayLimitsV12.MaximumDescriptorsPerKind)
            result.Errors.Add("descriptor-budget-exceeded");
        long assetBytes = 0;
        foreach (var asset in document.Assets)
        {
            try { assetBytes = checked(assetBytes + Math.Max(0L, asset.ByteLength)); }
            catch (OverflowException) { assetBytes = long.MaxValue; break; }
        }
        if (document.Assets.Count > ReplayLimitsV12.MaximumAssets || assetBytes > ReplayLimitsV12.MaximumAssetBytes)
            result.Errors.Add("asset-budget-exceeded");
        foreach (var state in new[] { document.InitialState }.Concat(document.TruthCheckpoints.Select(item => item.State)))
            if (state.Entities.Count > ReplayLimitsV12.MaximumEntitiesPerState
                || state.Cards.Count > ReplayLimitsV12.MaximumCardsPerState
                || state.Intents.Count > ReplayLimitsV12.MaximumIntentsPerState)
            {
                result.Errors.Add("state-budget-exceeded");
                break;
            }
        var descriptorText = document.Presentation.Entities.SelectMany(item => new[] { item.Name, item.Subtitle })
            .Concat(document.Presentation.Cards.SelectMany(item => new[] { item.Name, item.Description, item.Tag }))
            .Concat(document.Presentation.Buffs.SelectMany(item => new[] { item.Name, item.Description }))
            .Concat(document.Presentation.Intents.SelectMany(item => new[] { item.Name, item.Description }));
        if (descriptorText.Any(item => (item?.Length ?? 0) > ReplayLimitsV12.MaximumTextLength))
            result.Errors.Add("descriptor-text-budget-exceeded");
    }

    private static void ValidateEvents(ReplayDocumentV12 document, ReplayValidationResultV12 result)
    {
        var all = document.TruthEvents.Concat(document.PresentationEvents).OrderBy(item => item.Sequence).ToList();
        if (all.Count == 0) result.Errors.Add("journal-empty");
        var expected = 1L;
        var lastTimeTicks = 0L;
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        var lastStep = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in all)
        {
            if (value.Sequence != expected++) result.Errors.Add("global-sequence-invalid:" + value.Sequence);
            if (value.TimeTicks < 0 || value.TimeTicks > ReplayLimitsV12.MaximumTimelineTicks)
                result.Errors.Add("logical-time-invalid:" + value.Sequence);
            if (value.TimeTicks < lastTimeTicks) result.Errors.Add("logical-time-regressed:" + value.Sequence);
            lastTimeTicks = Math.Max(lastTimeTicks, value.TimeTicks);
            if (!string.IsNullOrWhiteSpace(value.CauseEventId) && !eventIds.Contains(value.CauseEventId))
                result.Errors.Add("cause-event-invalid:" + value.Sequence);
            if (string.IsNullOrWhiteSpace(value.EventId) || !eventIds.Add(value.EventId))
                result.Errors.Add("event-id-invalid:" + value.Sequence);
            if (string.IsNullOrWhiteSpace(value.TransactionId)) result.Errors.Add("transaction-id-missing:" + value.Sequence);
            ValidateStep(value, lastStep, result);
        }
        ValidateEntityReferences(document, all, result);
        var started = new Dictionary<string, ReplayJournalEventV12>(StringComparer.Ordinal);
        var ended = new HashSet<string>(StringComparer.Ordinal);
        var endedAt = new Dictionary<string, long>(StringComparer.Ordinal);
        var previousTruth = "";
        var reducer = new ReplayStateReducerV12();
        reducer.Reset(document.InitialState);
        foreach (var value in document.TruthEvents.OrderBy(item => item.Sequence))
        {
            if (!string.Equals(value.Lane, ReplayJournalLanesV12.Truth, StringComparison.Ordinal)
                || !ReplayEventTypesV12.Truth.Contains(value.EventType ?? ""))
                result.Errors.Add("truth-event-type-invalid:" + value.Sequence);
            if (!string.Equals(value.PreviousLaneEventHash, previousTruth, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(value.EventHash, ReplayCanonicalJsonV12.EventHash(value), StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("truth-event-hash-invalid:" + value.Sequence);
            previousTruth = value.EventHash;
            if (value.EventType == ReplayEventTypesV12.TransactionStarted)
            {
                if (value.Transaction == null || !ReplayTransactionKindsV12.Supported.Contains(value.Transaction.Kind ?? "")
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
            if (value.EventType == ReplayEventTypesV12.TransactionCompleted
                || value.EventType == ReplayEventTypesV12.TransactionAborted)
            {
                ended.Add(value.TransactionId);
                endedAt[value.TransactionId] = value.Sequence;
                if (value.EventType == ReplayEventTypesV12.TransactionAborted)
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
        if (document.TruthEvents.Count(item => item.EventType == ReplayEventTypesV12.BattleMaterialized) != 1)
            result.Errors.Add("battle-materialized-missing");
        if (document.TruthEvents.Count(item => item.EventType == ReplayEventTypesV12.FightStartSignaled) != 1)
            result.Errors.Add("fight-start-signal-invalid");
        if (!document.TruthEvents.Any(item => item.EventType == ReplayEventTypesV12.RoundStarted))
            result.Errors.Add("round-start-missing");
        if (document.TruthEvents.Count(item => item.EventType == ReplayEventTypesV12.OutcomeEntering) != 1)
            result.Errors.Add("outcome-entering-invalid");
        if (document.TruthEvents.Count(item => item.EventType == ReplayEventTypesV12.BattleFinalized) != 1)
            result.Errors.Add("battle-finalized-missing");
        if (!string.Equals(document.InitialState.LevelId, document.Header.LevelId, StringComparison.Ordinal)
            || !string.Equals(document.InitialState.BattlePhase, "Materialized", StringComparison.Ordinal))
            result.Errors.Add("initial-battle-state-invalid");
        var finalState = reducer.Current;
        if (!string.Equals(finalState.BattlePhase, "Finalized", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(finalState.Outcome)
            || !string.Equals(finalState.Outcome, document.Header.Result, StringComparison.Ordinal))
            result.Errors.Add("final-battle-state-invalid");
        if (!string.Equals(ReplayCanonicalJsonV12.StateHash(finalState), document.Header.FinalPublicStateSha256, StringComparison.OrdinalIgnoreCase))
            result.Errors.Add("final-state-hash-invalid");

        var previousPresentation = "";
        foreach (var value in document.PresentationEvents.OrderBy(item => item.Sequence))
        {
            if (!string.Equals(value.Lane, ReplayJournalLanesV12.Presentation, StringComparison.Ordinal)
                || !ReplayEventTypesV12.Presentation.Contains(value.EventType ?? ""))
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
                || !string.Equals(value.EventHash, ReplayCanonicalJsonV12.EventHash(value), StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("presentation-event-hash-invalid:" + value.Sequence);
            previousPresentation = value.EventHash;
        }
        var byTransaction = document.PresentationEvents.GroupBy(item => item.TransactionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var pair in started)
        {
            var kind = pair.Value.Transaction?.Kind ?? "";
            if (kind != ReplayTransactionKindsV12.Card
                && kind != ReplayTransactionKindsV12.Skill
                && kind != ReplayTransactionKindsV12.Intent
                && kind != ReplayTransactionKindsV12.ImplicitNative) continue;
            var presentation = byTransaction.TryGetValue(pair.Key, out var values)
                ? values
                : new List<ReplayJournalEventV12>();
            var transaction = pair.Value.Transaction!;
            if (string.IsNullOrWhiteSpace(transaction.ActorId)
                || string.IsNullOrWhiteSpace(transaction.SourceDescriptorId)
                || !presentation.Any(item => item.EventType == ReplayEventTypesV12.SourcePresented
                                              && string.Equals(item.Presentation?.ActorId, transaction.ActorId, StringComparison.Ordinal)
                                              && string.Equals(item.Presentation?.DescriptorId, transaction.SourceDescriptorId, StringComparison.Ordinal)
                                              && string.Equals(item.Presentation?.SourceInstanceId, transaction.SourceInstanceId, StringComparison.Ordinal)))
                result.Errors.Add("action-source-presentation-missing:" + pair.Key);
            if (!presentation.Any(item => item.EventType == ReplayEventTypesV12.ActorAnimationPresented
                                          && string.Equals(item.Presentation?.ActorId, transaction.ActorId, StringComparison.Ordinal)))
                result.Errors.Add("action-animation-presentation-missing:" + pair.Key);
            if (string.IsNullOrWhiteSpace(pair.Value.ParentTransactionId)
                && (document.TruthEvents.Count(item => item.TransactionId == pair.Key
                                                       && item.EventType == ReplayEventTypesV12.ActorTurnStarted) != 1
                    || document.TruthEvents.Count(item => item.TransactionId == pair.Key
                                                         && item.EventType == ReplayEventTypesV12.ActorTurnCompleted) != 1))
                result.Errors.Add("action-turn-boundary-invalid:" + pair.Key);
        }
    }

    private static void ValidateStep(
        ReplayJournalEventV12 value,
        IDictionary<string, int> lastStep,
        ReplayValidationResultV12 result)
    {
        if (value.StepOrdinal < 0) result.Errors.Add("step-negative:" + value.Sequence);
        if (!lastStep.ContainsKey(value.TransactionId ?? "") && value.StepOrdinal != 0)
            result.Errors.Add("step-start-invalid:" + value.Sequence);
        if (lastStep.TryGetValue(value.TransactionId ?? "", out var previous) && value.StepOrdinal <= previous)
            result.Errors.Add("step-order-invalid:" + value.Sequence);
        lastStep[value.TransactionId ?? ""] = value.StepOrdinal;
    }

    private static void ValidateEntityReferences(
        ReplayDocumentV12 document,
        IEnumerable<ReplayJournalEventV12> events,
        ReplayValidationResultV12 result)
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
            if (value.EventType == ReplayEventTypesV12.EntitySpawned && value.Entity != null)
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
            if (value.EventType == ReplayEventTypesV12.TransactionStarted
                && value.Transaction != null
                && (value.Transaction.Kind == ReplayTransactionKindsV12.Card
                    || value.Transaction.Kind == ReplayTransactionKindsV12.Skill
                    || value.Transaction.Kind == ReplayTransactionKindsV12.Intent
                    || value.Transaction.Kind == ReplayTransactionKindsV12.ImplicitNative)
                && !HasEntity(active, value.Transaction.ActorId))
                result.Errors.Add("action-actor-missing:" + value.Sequence);
            var message = value.Presentation;
            if (message?.EntityBinding != null
                && !HasEntity(active, message.EntityBinding.EntityId, message.EntityBinding.SpawnGeneration))
                result.Errors.Add("presentation-entity-missing:" + value.Sequence);
            if (message != null
                && (value.EventType == ReplayEventTypesV12.SourcePresented
                    || value.EventType == ReplayEventTypesV12.ActorAnimationPresented
                    || value.EventType == ReplayEventTypesV12.HitReactionPresented)
                && !HasEntity(active, message.ActorId))
                result.Errors.Add("presentation-actor-missing:" + value.Sequence);
            if (message != null && (value.EventType == ReplayEventTypesV12.EffectPresented
                                    || value.EventType == ReplayEventTypesV12.HitReactionPresented))
                foreach (var target in message.TargetIds.Where(item => !string.IsNullOrWhiteSpace(item)))
                    if (!HasEntity(active, target)) result.Errors.Add("presentation-target-missing:" + value.Sequence + ":" + target);
            if (value.EventType == ReplayEventTypesV12.EntityDespawned
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

    private static bool ValidEntity(ReplayEntityStateV12 entity) => entity != null
        && !string.IsNullOrWhiteSpace(entity.EntityId)
        && entity.SpawnGeneration > 0
        && entity.SlotIndex >= 0
        && (entity.Team == ReplayTeamsV12.Friendly
            || entity.Team == ReplayTeamsV12.Enemy
            || entity.Team == ReplayTeamsV12.Neutral)
        && entity.MaxHp >= 0;

    private static void ValidatePresentation(ReplayDocumentV12 document, ReplayValidationResultV12 result)
    {
        var assets = document.Assets.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Sha256))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (assets.Count != document.Assets.Count) result.Errors.Add("asset-id-duplicate-or-empty");
        foreach (var asset in assets.Values)
        {
            var assetError = ReplayAssetContractV12.Validate(asset, requirePayload: false);
            if (assetError.Length > 0) result.Errors.Add("asset-invalid:" + asset.Sha256 + ":" + assetError);
        }
        var reachable = ReplayPresentationReachabilityV12.AssetHashes(document);
        foreach (var hash in reachable.Where(hash => !assets.ContainsKey(hash))) result.Errors.Add("asset-missing:" + hash);
        foreach (var hash in assets.Keys.Where(hash => !reachable.Contains(hash))) result.Errors.Add("asset-unreachable:" + hash);

        var entityDescriptors = document.Presentation.Entities.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var cardDescriptors = document.Presentation.Cards.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var buffDescriptors = document.Presentation.Buffs.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var intentDescriptors = document.Presentation.Intents.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var effectDescriptors = document.Presentation.Effects.Select(item => item.DescriptorId).ToHashSet(StringComparer.Ordinal);
        var anchors = document.Presentation.Scene.Anchors.Select(item => item.AnchorId).ToHashSet(StringComparer.Ordinal);
        if (entityDescriptors.Count != document.Presentation.Entities.Count
            || cardDescriptors.Count != document.Presentation.Cards.Count
            || buffDescriptors.Count != document.Presentation.Buffs.Count
            || intentDescriptors.Count != document.Presentation.Intents.Count
            || effectDescriptors.Count != document.Presentation.Effects.Count
            || entityDescriptors.Any(string.IsNullOrWhiteSpace)
            || cardDescriptors.Any(string.IsNullOrWhiteSpace)
            || buffDescriptors.Any(string.IsNullOrWhiteSpace)
            || intentDescriptors.Any(string.IsNullOrWhiteSpace)
            || effectDescriptors.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(document.Presentation.Scene.DescriptorId)
            || anchors.Count != document.Presentation.Scene.Anchors.Count
            || anchors.Any(string.IsNullOrWhiteSpace))
            result.Errors.Add("presentation-descriptor-duplicate-or-empty");
        if (document.Presentation.Scene.ReferenceWidth <= 0
            || document.Presentation.Scene.ReferenceHeight <= 0
            || document.Presentation.Scene.CameraOrthographicSizeQ16 <= 0
            || string.IsNullOrWhiteSpace(document.Presentation.Scene.BackgroundAssetSha256)
            || !IsImageAsset(document.Presentation.Scene.BackgroundAssetSha256, assets))
            result.Errors.Add("scene-descriptor-invalid");
        foreach (var descriptor in document.Presentation.Entities)
        {
            if (descriptor.Provenance == null
                || !ReplayEntityArchetypes(descriptor.Archetype)
                || descriptor.SafeActionProfile is not "default" and not "static"
                || descriptor.Animations.Count == 0
                || descriptor.Animations.Select(item => item.State).Distinct(StringComparer.Ordinal).Count()
                   != descriptor.Animations.Count
                || descriptor.Animations.Any(item => string.IsNullOrWhiteSpace(item.State)
                                                     || item.FramesPerSecondQ16 <= 0
                                                     || item.Frames.Count == 0
                                                     || item.Frames.Any(frame => !ValidFrame(frame, assets))))
                result.Errors.Add("entity-descriptor-invalid:" + descriptor.DescriptorId);
        }
        foreach (var descriptor in document.Presentation.Cards)
            if (descriptor.Provenance == null
                || string.IsNullOrWhiteSpace(descriptor.ArtworkAssetSha256)
                || !IsImageAsset(descriptor.ArtworkAssetSha256, assets)
                || !string.IsNullOrWhiteSpace(descriptor.FrameAssetSha256)
                   && !IsImageAsset(descriptor.FrameAssetSha256, assets))
                result.Errors.Add("card-descriptor-invalid:" + descriptor.DescriptorId);
        foreach (var descriptor in document.Presentation.Buffs)
            if (descriptor.Provenance == null
                || string.IsNullOrWhiteSpace(descriptor.IconAssetSha256)
                || !IsImageAsset(descriptor.IconAssetSha256, assets))
                result.Errors.Add("buff-descriptor-invalid:" + descriptor.DescriptorId);
        foreach (var descriptor in document.Presentation.Intents)
            if (descriptor.Provenance == null
                || string.IsNullOrWhiteSpace(descriptor.IconAssetSha256)
                || !IsImageAsset(descriptor.IconAssetSha256, assets))
                result.Errors.Add("intent-descriptor-invalid:" + descriptor.DescriptorId);
        foreach (var descriptor in document.Presentation.Effects)
            if (descriptor.Primitive is not "Flash" and not "SpriteSequence"
                || descriptor.DurationTicks <= 0
                || descriptor.FramesPerSecondQ16 <= 0
                || descriptor.Primitive == "SpriteSequence" && descriptor.Frames.Count == 0
                || descriptor.Frames.Any(frame => !ValidFrame(frame, assets)))
                result.Errors.Add("effect-descriptor-invalid:" + descriptor.DescriptorId);
        foreach (var value in document.PresentationEvents)
        {
            var message = value.Presentation;
            if (message == null) result.Errors.Add("presentation-payload-missing:" + value.Sequence);
            if (message != null
                && (message.DelayTicks < 0
                    || message.DelayTicks > ReplayLimitsV12.MaximumTimelineTicks
                    || message.DurationTicks < 0
                    || message.DurationTicks > ReplayLimitsV12.MaximumTimelineTicks
                    || message.DelayTicks > 0 && value.EventType != ReplayEventTypesV12.EffectPresented
                    || value.TimeTicks > ReplayLimitsV12.MaximumTimelineTicks - message.DelayTicks
                    || value.TimeTicks + message.DelayTicks > ReplayLimitsV12.MaximumTimelineTicks - message.DurationTicks))
                result.Errors.Add("presentation-time-invalid:" + value.Sequence);
            if ((value.EventType == ReplayEventTypesV12.EntityPresented
                 || value.EventType == ReplayEventTypesV12.EntityPresentationChanged)
                && (message?.EntityBinding == null
                    || !entityDescriptors.Contains(message.EntityBinding.DescriptorId)
                    || string.IsNullOrWhiteSpace(message.EntityBinding.EntityId)
                    || message.EntityBinding.SpawnGeneration <= 0
                    || !anchors.Contains(message.EntityBinding.LayoutAnchor)))
                result.Errors.Add("entity-descriptor-missing:" + value.Sequence);
            var descriptorId = message?.DescriptorId ?? "";
            if (value.EventType == ReplayEventTypesV12.SourcePresented
                && !string.IsNullOrWhiteSpace(descriptorId)
                && !cardDescriptors.Contains(descriptorId)
                && !intentDescriptors.Contains(descriptorId))
                result.Errors.Add("source-descriptor-missing:" + value.Sequence);
            var effectDescriptorId = message?.EffectDescriptorId ?? "";
            if (value.EventType == ReplayEventTypesV12.EffectPresented
                && (string.IsNullOrWhiteSpace(effectDescriptorId)
                    || !effectDescriptors.Contains(effectDescriptorId)))
                result.Errors.Add("effect-descriptor-missing:" + value.Sequence);
            if (value.EventType == ReplayEventTypesV12.AudioPresented
                && (message?.Audio == null
                    || string.IsNullOrWhiteSpace(message.Audio.AssetSha256)
                    || !assets.TryGetValue(message.Audio.AssetSha256, out var audioAsset)
                    || !ValidAudioCue(message.Audio, audioAsset)))
                result.Errors.Add("audio-asset-missing:" + value.Sequence);
        }
        var states = new[] { document.InitialState }.Concat(document.TruthCheckpoints.Select(item => item.State)).ToList();
        foreach (var card in states.SelectMany(item => item.Cards))
            if (!cardDescriptors.Contains(card.DescriptorId)) result.Errors.Add("public-card-descriptor-missing:" + card.CardInstanceId);
        foreach (var buff in states.SelectMany(item => item.Entities).SelectMany(item => item.Buffs))
            if (!buffDescriptors.Contains(buff.DescriptorId)) result.Errors.Add("public-buff-descriptor-missing:" + buff.InstanceId);
        foreach (var intent in states.SelectMany(item => item.Intents))
            if (!intentDescriptors.Contains(intent.DescriptorId)) result.Errors.Add("public-intent-descriptor-missing:" + intent.IntentInstanceId);
        var requiredEntities = document.InitialState.Entities.Select(item => EntityGenerationKey(item.EntityId, item.SpawnGeneration))
            .Concat(document.TruthEvents.Where(item => item.EventType == ReplayEventTypesV12.EntitySpawned && item.Entity != null)
                .Select(item => EntityGenerationKey(item.Entity!.EntityId, item.Entity.SpawnGeneration)))
            .ToHashSet(StringComparer.Ordinal);
        var boundEntities = document.PresentationEvents
            .Where(item => item.Presentation?.EntityBinding != null)
            .Select(item => EntityGenerationKey(item.Presentation!.EntityBinding!.EntityId, item.Presentation.EntityBinding.SpawnGeneration))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in requiredEntities.Where(key => !boundEntities.Contains(key)))
            result.Errors.Add("entity-presentation-missing:" + key);
    }

    private static void ValidateCheckpoints(ReplayDocumentV12 document, ReplayValidationResultV12 result)
    {
        if (document.TruthCheckpoints.Count != document.PresentationCheckpoints.Count)
        {
            result.Errors.Add("checkpoint-pair-count-invalid");
            return;
        }
        var expected = new ReplayDocumentV12
        {
            InitialState = document.InitialState,
            TruthEvents = document.TruthEvents,
            PresentationEvents = document.PresentationEvents,
            Presentation = document.Presentation
        };
        ReplayDocumentFinalizerV12.RebuildCheckpoints(expected);
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
            if (!string.Equals(ReplayCanonicalJsonV12.StateHash(truth.State), truth.StateSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ReplayCanonicalJsonV12.TruthCheckpointHash(truth), truth.CheckpointSha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("truth-checkpoint-invalid:" + truth.EventSequence);
            if (!string.Equals(ReplayCanonicalJsonV12.PresentationCheckpointHash(presentation), presentation.CheckpointSha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("presentation-checkpoint-invalid:" + presentation.EventSequence);
            if (!string.Equals(truth.CheckpointSha256, expected.TruthCheckpoints[index].CheckpointSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(presentation.CheckpointSha256, expected.PresentationCheckpoints[index].CheckpointSha256, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add("checkpoint-projection-invalid:" + truth.EventSequence);
        }
    }

    private static string EntityGenerationKey(string entityId, int generation) => (entityId ?? "") + "|" + generation;

    private static bool ReplayEntityArchetypes(string value) => value == ReplayEntityArchetypesV12.PlayerCombatant
        || value == ReplayEntityArchetypesV12.EnemyCombatant
        || value == ReplayEntityArchetypesV12.AlliedCombatant
        || value == ReplayEntityArchetypesV12.NeutralCombatant;

    private static bool IsImageAsset(string sha256, IReadOnlyDictionary<string, ReplayAssetV12> assets) =>
        assets.TryGetValue(sha256 ?? "", out var asset) && asset.MediaType == "image/png";

    private static bool ValidFrame(
        ReplaySpriteFrameV12 frame,
        IReadOnlyDictionary<string, ReplayAssetV12> assets)
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
               && frame.PixelsPerUnitQ16 > 0;
    }

    private static bool ValidAudioCue(ReplayAudioCueV12 cue, ReplayAssetV12 asset)
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
