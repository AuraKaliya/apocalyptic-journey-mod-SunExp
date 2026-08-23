using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal sealed class ReplayMaterializedBaselineMigrationResultV11
{
    internal bool Success { get; set; }

    internal bool Changed { get; set; }

    internal ReplayDocumentV11? Document { get; set; }

    internal long AnchorSequence { get; set; }

    internal int RemovedPreludeEvents { get; set; }

    internal int RemovedAttachments { get; set; }

    internal string Message { get; set; } = "";
}

internal static class ReplayMaterializedBaselineMigrationV11
{
    private const int OfflineAudioSampleRate = 48_000;

    internal static ReplayMaterializedBaselineMigrationResultV11 Rebase(ReplayDocumentV11 source)
    {
        if (source == null)
            return Failed("Replay Document v11 is missing.");

        if (ReplayPlayableBootstrapContractV11.ValidateState(source.InitialState).Count == 0)
        {
            return new ReplayMaterializedBaselineMigrationResultV11
            {
                Success = true,
                Document = source,
                Message = "Replay already has a materialized baseline."
            };
        }

        var document = Clone(source);
        var events = document.Events.OrderBy(value => value.Sequence).ToList();
        var projection = new ReplayProjectionEngine();
        projection.Reset(document.InitialState);
        ReplayTimelineEventV11? anchor = null;
        ReplayLogicalStateV11? materialized = null;
        try
        {
            foreach (var value in events)
            {
                projection.Apply(value, verifyHash: false);
                var candidate = projection.Current;
                if (ReplayPlayableBootstrapContractV11.ValidateState(candidate).Count != 0) continue;
                anchor = value;
                materialized = candidate;
                break;
            }
        }
        catch (Exception ex)
        {
            return Failed("Replay timeline cannot be projected for baseline migration: " + ex.Message);
        }

        if (anchor == null || materialized == null)
            return Failed("Replay never reaches a complete player-and-enemy presentation state.");
        if (!string.Equals(anchor.EventType, ReplayEventTypesV11.TurnChanged, StringComparison.Ordinal))
            return Failed("The first complete presentation state is not a player-round boundary.");

        var consumed = events.Where(value => value.Sequence <= anchor.Sequence).ToList();
        if (consumed.Any(value => value.Sequence < anchor.Sequence && !IsStateNeutralPrelude(value))
            || !IsMaterializationAnchor(anchor))
        {
            return Failed("Replay contains semantic actions before its first complete presentation state.");
        }

        var retained = events.Where(value => value.Sequence > anchor.Sequence).ToList();
        var retainedEventIds = new HashSet<string>(retained.Select(value => value.EventId), StringComparer.Ordinal);
        if (retained.Any(value => !string.IsNullOrWhiteSpace(value.CauseEventId)
                                  && !retainedEventIds.Contains(value.CauseEventId)))
        {
            return Failed("Replay contains a retained event whose cause belongs to the discarded prelude.");
        }

        var bgm = consumed.SelectMany(value => value.Audio ?? new List<ReplayAudioCueV11>())
            .Where(IsBgm)
            .OrderBy(value => value.StartSample)
            .LastOrDefault();
        var rebased = new List<ReplayTimelineEventV11>();
        if (bgm != null)
        {
            bgm.StartSample = 0;
            rebased.Add(new ReplayTimelineEventV11
            {
                TimeTicks = 0,
                TurnIndex = materialized.TurnIndex,
                EventType = ReplayEventTypesV11.StateChanged,
                ActorId = materialized.ActiveActorId,
                Audio = new List<ReplayAudioCueV11> { bgm }
            });
        }

        var removedTicks = Math.Max(0L, anchor.TimeTicks);
        var removedSamples = removedTicks * OfflineAudioSampleRate / ReplayProtocolV11.TimebaseTicksPerSecond;
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var orderedEvents = rebased.Concat(retained).ToList();
        for (var index = 0; index < orderedEvents.Count; index++)
        {
            var value = orderedEvents[index];
            var oldId = value.EventId ?? "";
            value.Sequence = index + 1L;
            value.EventId = "event-" + value.Sequence.ToString("D8");
            if (oldId.Length > 0) idMap[oldId] = value.EventId;
            value.TimeTicks = Math.Max(0L, value.TimeTicks - removedTicks);
            foreach (var cue in value.Audio ?? new List<ReplayAudioCueV11>())
            {
                if (ReferenceEquals(cue, bgm)) continue;
                cue.StartSample = Math.Max(0L, cue.StartSample - removedSamples);
            }
        }

        foreach (var value in retained)
        {
            if (string.IsNullOrWhiteSpace(value.CauseEventId)) continue;
            if (!idMap.TryGetValue(value.CauseEventId, out var mapped))
                return Failed("Replay event cause could not be rebased: " + value.CauseEventId);
            value.CauseEventId = mapped;
        }

        document.InitialState = ReplayProjectionStateV11.Clone(materialized);
        document.Events = orderedEvents;
        document.Checkpoints = document.Events
            .Where(value => value.Sequence % ReplayProtocolV11.DefaultCheckpointInterval == 0
                            || string.Equals(value.EventType, ReplayEventTypesV11.TurnChanged, StringComparison.Ordinal))
            .Select(value => new ReplayCheckpointV11 { EventSequence = value.Sequence })
            .ToList();

        var attachmentCount = document.Attachments.Count;
        var referenced = ReferencedAttachments(document);
        document.Attachments = document.Attachments
            .Where(value => referenced.Contains(value.Sha256))
            .ToList();

        ReplayCardPresentationContractV11.NormalizeDocument(document);
        var validation = ReplayDocumentFinalizerV11.FinalizeAndValidate(document);
        if (!validation.IsValid)
            return Failed("Rebased Replay Document v11 is not playable: " + validation.Message);

        return new ReplayMaterializedBaselineMigrationResultV11
        {
            Success = true,
            Changed = true,
            Document = document,
            AnchorSequence = anchor.Sequence,
            RemovedPreludeEvents = consumed.Count,
            RemovedAttachments = attachmentCount - document.Attachments.Count,
            Message = "Replay was rebased to materialized event " + anchor.Sequence + "."
        };
    }

    private static bool IsStateNeutralPrelude(ReplayTimelineEventV11 value)
    {
        return value.Delta == null
               && string.IsNullOrWhiteSpace(value.ActionId)
               && string.IsNullOrWhiteSpace(value.CauseEventId)
               && string.IsNullOrWhiteSpace(value.SourceInstanceId)
               && (value.Semantics?.Count ?? 0) == 0
               && (value.Presentation?.Count ?? 0) == 0
               && value.NativePresentation == null;
    }

    private static bool IsMaterializationAnchor(ReplayTimelineEventV11 value)
    {
        return value.Delta != null
               && string.IsNullOrWhiteSpace(value.ActionId)
               && string.IsNullOrWhiteSpace(value.CauseEventId)
               && (value.Semantics?.Count ?? 0) == 0
               && (value.Presentation?.Count ?? 0) == 0
               && value.NativePresentation == null;
    }

    private static bool IsBgm(ReplayAudioCueV11 value)
    {
        return value != null
               && (string.Equals(value.Bus, "Bgm", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value.Kind, "BattleBgm", StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ReferencedAttachments(ReplayDocumentV11 document)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in (document.Content?.Definitions ?? new List<ReplayContentDefinitionV11>())
                     .Select(value => value.Display ?? new ReplayDisplaySnapshotV11()))
        {
            Add(result, display.IconAssetSha256);
            Add(result, display.PortraitAssetSha256);
            Add(result, display.ArtworkAssetSha256);
            Add(result, display.BackgroundAssetSha256);
        }

        foreach (var cue in document.Events.SelectMany(value => value.Audio ?? new List<ReplayAudioCueV11>()))
            Add(result, cue.AssetSha256);
        return result;
    }

    private static void Add(ISet<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
    }

    private static ReplayDocumentV11 Clone(ReplayDocumentV11 source)
    {
        var json = Encoding.UTF8.GetString(ReplayCanonicalJsonV11.SerializeUtf8(source));
        var clone = JsonConvert.DeserializeObject<ReplayDocumentV11>(json)
                    ?? throw new InvalidOperationException("Replay Document v11 could not be cloned for migration.");
        var payloads = (source.Attachments ?? new List<ReplayAttachmentV11>())
            .Where(value => value.Payload != null && value.Payload.Length > 0)
            .GroupBy(value => value.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Payload, StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in clone.Attachments)
        {
            if (payloads.TryGetValue(attachment.Sha256, out var payload))
                attachment.Payload = (byte[])payload.Clone();
        }
        return clone;
    }

    private static ReplayMaterializedBaselineMigrationResultV11 Failed(string message)
    {
        return new ReplayMaterializedBaselineMigrationResultV11 { Message = message };
    }
}
