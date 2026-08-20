using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal sealed class ReplayValidationResultV10
{
    internal List<string> Errors { get; } = new();

    internal bool IsValid => Errors.Count == 0;

    internal string Message => IsValid ? "Replay Document v10 is valid." : string.Join("; ", Errors);
}

internal static class ReplayDocumentFinalizerV10
{
    internal static ReplayValidationResultV10 FinalizeAndValidate(ReplayDocumentV10 document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        document.Header.DocumentVersion = ReplayProtocolV10.DocumentVersion;
        document.Header.MinimumReadableDocumentVersion = ReplayProtocolV10.MinimumReadableDocumentVersion;
        document.Header.TimebaseTicksPerSecond = ReplayProtocolV10.TimebaseTicksPerSecond;
        document.Header.InitialLogicalStateSha256 = ReplayProjectionStateV10.Hash(document.InitialState);
        document.Header.ContentManifestSha256 = ReplayCanonicalJsonV10.Sha256(document.Content);

        var engine = new ReplayProjectionEngine();
        engine.Reset(document.InitialState);
        var chain = "";
        foreach (var value in document.Events.OrderBy(item => item.Sequence))
        {
            engine.Apply(value, verifyHash: false);
            value.StateHashAfter = ReplayProjectionStateV10.Hash(engine.Current);
            chain = ReplayCanonicalJsonV10.EventChainHash(chain, value);
            value.EventChainHashAfter = chain;
        }

        document.Header.FinalLogicalStateSha256 = ReplayProjectionStateV10.Hash(engine.Current);
        document.Header.FinalEventChainSha256 = chain;
        document.Header.TimelineRootSha256 = chain;
        NormalizeCheckpoints(document, engine);
        document.Header.DocumentSha256 = ReplayCanonicalJsonV10.DocumentHash(document);
        return ReplayDocumentValidatorV10.Validate(document);
    }

    private static void NormalizeCheckpoints(ReplayDocumentV10 document, ReplayProjectionEngine scratch)
    {
        var requested = new HashSet<long>((document.Checkpoints ?? new List<ReplayCheckpointV10>())
            .Select(item => item.EventSequence));
        requested.Add(0);
        requested.Add(document.Events.Count == 0 ? 0 : document.Events.Max(item => item.Sequence));
        var result = new List<ReplayCheckpointV10>();
        scratch.Reset(document.InitialState);
        var initialChain = "";
        if (requested.Contains(0))
        {
            result.Add(CreateCheckpoint(0, 0, scratch.Current, initialChain));
        }

        foreach (var value in document.Events.OrderBy(item => item.Sequence))
        {
            scratch.Apply(value);
            if (requested.Contains(value.Sequence))
            {
                result.Add(CreateCheckpoint(
                    value.Sequence,
                    value.TimeTicks,
                    scratch.Current,
                    value.EventChainHashAfter));
            }
        }

        document.Checkpoints = result.OrderBy(item => item.EventSequence).ToList();
    }

    private static ReplayCheckpointV10 CreateCheckpoint(
        long sequence,
        long timeTicks,
        ReplayLogicalStateV10 state,
        string eventChain)
    {
        var snapshot = ReplayProjectionStateV10.Clone(state);
        return new ReplayCheckpointV10
        {
            EventSequence = sequence,
            TimeTicks = Math.Max(0, timeTicks),
            State = snapshot,
            LogicalStateSha256 = ReplayProjectionStateV10.Hash(snapshot),
            EventChainSha256 = eventChain ?? ""
        };
    }
}

internal static class ReplayDocumentValidatorV10
{
    internal static ReplayValidationResultV10 Validate(ReplayDocumentV10 document)
    {
        var result = new ReplayValidationResultV10();
        if (document == null)
        {
            result.Errors.Add("document is missing");
            return result;
        }

        ValidateHeader(document, result);
        ValidateContent(document, result);
        ValidateIdentities(document, result);
        ValidateTimeline(document, result);
        ValidateCheckpoints(document, result);
        if (result.IsValid)
        {
            var actualDocumentHash = ReplayCanonicalJsonV10.DocumentHash(document);
            if (!string.Equals(actualDocumentHash, document.Header.DocumentSha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("document hash mismatch");
            }
        }

        return result;
    }

    private static void ValidateHeader(ReplayDocumentV10 document, ReplayValidationResultV10 result)
    {
        var header = document.Header ?? new ReplayDocumentHeaderV10();
        if (header.DocumentVersion != ReplayProtocolV10.DocumentVersion
            || header.MinimumReadableDocumentVersion != ReplayProtocolV10.MinimumReadableDocumentVersion)
        {
            result.Errors.Add("only Replay Document v10 is readable");
        }

        if (header.TimebaseTicksPerSecond != ReplayProtocolV10.TimebaseTicksPerSecond)
        {
            result.Errors.Add("unsupported replay timebase");
        }

        if (string.IsNullOrWhiteSpace(header.RecordId)) result.Errors.Add("record id is missing");
        if (string.IsNullOrWhiteSpace(header.DocumentSha256)) result.Errors.Add("document hash is missing");
        var initialHash = ReplayProjectionStateV10.Hash(document.InitialState);
        if (!string.Equals(initialHash, header.InitialLogicalStateSha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("initial state hash mismatch");
        }
    }

    private static void ValidateContent(ReplayDocumentV10 document, ReplayValidationResultV10 result)
    {
        var contentHash = ReplayCanonicalJsonV10.Sha256(document.Content ?? new ReplayContentManifestV10());
        if (!string.Equals(contentHash, document.Header.ContentManifestSha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("content manifest hash mismatch");
        }

        var definitions = document.Content?.Definitions ?? new List<ReplayContentDefinitionV10>();
        if (definitions.Any(item => string.IsNullOrWhiteSpace(item.Content?.OwnerModId)
                                    || string.IsNullOrWhiteSpace(item.Content?.ContentKind)
                                    || string.IsNullOrWhiteSpace(item.Content?.StableContentId)))
        {
            result.Errors.Add("content definition has no stable id");
        }

        if (definitions.GroupBy(item => item.Content.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            result.Errors.Add("content definition ids are not unique");
        }
        var knownContent = new HashSet<string>(definitions.Select(item => item.Content.Key), StringComparer.Ordinal);
        var stateContent = StateContent(document.InitialState)
            .Concat((document.Events ?? new List<ReplayTimelineEventV10>())
                .Where(item => item.Delta != null)
                .SelectMany(item => DeltaContent(item.Delta!)));
        foreach (var reference in stateContent)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.StableContentId)
                || !knownContent.Contains(reference.Key))
            {
                result.Errors.Add("logical state references undefined content: " + (reference?.Key ?? "<missing>"));
            }
        }

        var attachments = document.Attachments ?? new List<ReplayAttachmentV10>();
        if (attachments.GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            result.Errors.Add("attachment hashes are not unique");
        }

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.Sha256)
                || attachment.ByteLength < 0
                || attachment.Payload.Length > 0
                   && (!string.Equals(
                           ReplayCanonicalJsonV10.Sha256(attachment.Payload),
                           attachment.Sha256,
                           StringComparison.OrdinalIgnoreCase)
                       || attachment.Payload.LongLength != attachment.ByteLength))
            {
                result.Errors.Add("attachment metadata or hash is invalid: " + attachment.Sha256);
            }
        }

        var knownAssets = new HashSet<string>(attachments.Select(item => item.Sha256), StringComparer.OrdinalIgnoreCase);
        var assetsByHash = attachments
            .Where(item => !string.IsNullOrWhiteSpace(item.Sha256))
            .GroupBy(item => item.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        foreach (var hash in definitions.SelectMany(DisplayAssets).Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!knownAssets.Contains(hash)) result.Errors.Add("display attachment is missing: " + hash);
        }
        foreach (var cue in (document.Events ?? new List<ReplayTimelineEventV10>())
                     .SelectMany(item => item.Audio ?? new List<ReplayAudioCueV10>()))
        {
            if (string.IsNullOrWhiteSpace(cue.AssetSha256) || !knownAssets.Contains(cue.AssetSha256))
            {
                result.Errors.Add("audio attachment is missing: " + cue.AssetSha256);
            }
            if (cue.StartSample < 0 || cue.SourceOffsetSample < 0 || cue.DurationSamples < 0)
            {
                result.Errors.Add("audio cue contains a negative sample position");
            }
            if (cue.PlaybackRateQ16 <= 0
                || cue.LoopStartSample < 0
                || cue.LoopEndSample < cue.LoopStartSample
                || cue.FadeInSamples < 0
                || cue.FadeOutSamples < 0)
            {
                result.Errors.Add("audio cue contains an invalid rate, loop, or fade");
            }
            if (assetsByHash.TryGetValue(cue.AssetSha256, out var audioAsset)
                && audioAsset.SampleFrames > 0
                && (cue.SourceOffsetSample > audioAsset.SampleFrames
                    || cue.LoopEndSample > audioAsset.SampleFrames))
            {
                result.Errors.Add("audio cue exceeds its attachment sample range");
            }
        }
    }

    private static void ValidateTimeline(ReplayDocumentV10 document, ReplayValidationResultV10 result)
    {
        var engine = new ReplayProjectionEngine();
        engine.Reset(document.InitialState);
        var expectedSequence = 1L;
        var previousTime = 0L;
        var previousChain = "";
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in document.Events ?? new List<ReplayTimelineEventV10>())
        {
            if (value.Sequence != expectedSequence++) result.Errors.Add("event sequence is not contiguous");
            if (value.TimeTicks < previousTime) result.Errors.Add("event time moved backwards at " + value.Sequence);
            previousTime = value.TimeTicks;
            if (string.IsNullOrWhiteSpace(value.EventId) || !eventIds.Add(value.EventId))
            {
                result.Errors.Add("event id is missing or duplicated at " + value.Sequence);
            }

            if (!string.IsNullOrWhiteSpace(value.CauseEventId) && !eventIds.Contains(value.CauseEventId))
            {
                result.Errors.Add("event cause is not an earlier event at " + value.Sequence);
            }

            if (!ReplayEventTypesV10.Supported.Contains(value.EventType))
            {
                result.Errors.Add("unsupported required event type: " + value.EventType);
            }

            try
            {
                engine.Apply(value);
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                break;
            }

            var chain = ReplayCanonicalJsonV10.EventChainHash(previousChain, value);
            if (!string.Equals(chain, value.EventChainHashAfter, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("event chain hash mismatch at " + value.Sequence);
            }
            previousChain = chain;
        }

        if (!string.Equals(previousChain, document.Header.FinalEventChainSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previousChain, document.Header.TimelineRootSha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("final event chain hash mismatch");
        }

        var finalHash = ReplayProjectionStateV10.Hash(engine.Current);
        if (!string.Equals(finalHash, document.Header.FinalLogicalStateSha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("final logical state hash mismatch");
        }
    }

    private static void ValidateIdentities(ReplayDocumentV10 document, ReplayValidationResultV10 result)
    {
        if (HasDuplicate(document.InitialState.Actors.Select(item => item.InstanceId))
            || HasDuplicate(document.InitialState.Cards.Select(item => item.InstanceId))
            || HasDuplicate(document.InitialState.Intents.Select(item => item.InstanceId)))
        {
            result.Errors.Add("initial logical state contains missing or duplicate instance ids");
        }
        foreach (var delta in document.Events.Where(item => item.Delta != null).Select(item => item.Delta!))
        {
            if (HasDuplicate(delta.ActorUpserts.Select(item => item.InstanceId))
                || HasDuplicate(delta.CardUpserts.Select(item => item.InstanceId))
                || HasDuplicate(delta.IntentUpserts.Select(item => item.InstanceId)))
            {
                result.Errors.Add("state delta contains missing or duplicate upsert ids");
                break;
            }
        }
    }

    private static bool HasDuplicate(IEnumerable<string> values)
    {
        var normalized = values.Select(item => item ?? "").ToList();
        return normalized.Any(string.IsNullOrWhiteSpace)
               || normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Count;
    }

    private static void ValidateCheckpoints(ReplayDocumentV10 document, ReplayValidationResultV10 result)
    {
        var events = (document.Events ?? new List<ReplayTimelineEventV10>())
            .ToDictionary(item => item.Sequence, item => item, EqualityComparer<long>.Default);
        var checkpoints = document.Checkpoints ?? new List<ReplayCheckpointV10>();
        if (checkpoints.Count == 0 || checkpoints[0].EventSequence != 0)
        {
            result.Errors.Add("initial checkpoint is missing");
            return;
        }

        foreach (var checkpoint in checkpoints)
        {
            var stateHash = ReplayProjectionStateV10.Hash(checkpoint.State);
            if (!string.Equals(stateHash, checkpoint.LogicalStateSha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("checkpoint state hash mismatch at " + checkpoint.EventSequence);
            }

            var expectedChain = checkpoint.EventSequence == 0
                ? ""
                : events.TryGetValue(checkpoint.EventSequence, out var value)
                    ? value.EventChainHashAfter
                    : null;
            if (expectedChain == null
                || !string.Equals(expectedChain, checkpoint.EventChainSha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("checkpoint event chain mismatch at " + checkpoint.EventSequence);
            }
        }
    }

    private static IEnumerable<string> DisplayAssets(ReplayContentDefinitionV10 definition)
    {
        var display = definition?.Display ?? new ReplayDisplaySnapshotV10();
        yield return display.IconAssetSha256;
        yield return display.PortraitAssetSha256;
        yield return display.ArtworkAssetSha256;
        yield return display.BackgroundAssetSha256;
    }

    private static IEnumerable<ReplayContentRefV10> StateContent(ReplayLogicalStateV10 state)
    {
        foreach (var actor in state?.Actors ?? new List<ReplayActorStateV10>())
        {
            yield return actor.Content;
            foreach (var buff in actor.Buffs ?? new List<ReplayBuffStateV10>()) yield return buff.Content;
        }
        foreach (var card in state?.Cards ?? new List<ReplayCardStateV10>()) yield return card.Content;
        foreach (var intent in state?.Intents ?? new List<ReplayIntentStateV10>()) yield return intent.Content;
    }

    private static IEnumerable<ReplayContentRefV10> DeltaContent(ReplayStateDeltaV10 delta)
    {
        foreach (var actor in delta.ActorUpserts ?? new List<ReplayActorStateV10>())
        {
            yield return actor.Content;
            foreach (var buff in actor.Buffs ?? new List<ReplayBuffStateV10>()) yield return buff.Content;
        }
        foreach (var card in delta.CardUpserts ?? new List<ReplayCardStateV10>()) yield return card.Content;
        foreach (var intent in delta.IntentUpserts ?? new List<ReplayIntentStateV10>()) yield return intent.Content;
    }
}
