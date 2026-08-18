using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Model;

internal static class MatchReplayChunker
{
    internal static IReadOnlyList<MatchReplayChunk> Build(IReadOnlyList<MatchReplayEvent>? events, int targetBytes)
    {
        var source = (events ?? Array.Empty<MatchReplayEvent>())
            .Where(item => item != null)
            .OrderBy(item => item.Sequence)
            .ToList();
        var normalizedTarget = Math.Max(32 * 1024, Math.Min(1024 * 1024, targetBytes));
        var result = new List<MatchReplayChunk>();
        var pending = new List<MatchReplayEvent>();
        var pendingBytes = 0;

        foreach (var item in source)
        {
            var estimated = Estimate(item);
            if (pending.Count > 0 && pendingBytes + estimated > normalizedTarget)
            {
                result.Add(Create(result.Count, pending));
                pending = new List<MatchReplayEvent>();
                pendingBytes = 0;
            }

            pending.Add(item);
            pendingBytes += estimated;
        }

        if (pending.Count > 0)
        {
            result.Add(Create(result.Count, pending));
        }

        return result;
    }

    internal static IReadOnlyList<MatchReplayEvent> Decode(IEnumerable<MatchReplayChunk>? chunks)
    {
        var result = new List<MatchReplayEvent>();
        foreach (var chunk in (chunks ?? Array.Empty<MatchReplayChunk>()).OrderBy(item => item.ChunkIndex))
        {
            if (!string.Equals(MatchReplayPayload.Sha256(chunk.Payload), chunk.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Replay chunk checksum mismatch at index " + chunk.ChunkIndex + ".");
            }

            result.AddRange(MatchReplayPayload.Decode<List<MatchReplayEvent>>(chunk.Payload)
                            ?? new List<MatchReplayEvent>());
        }

        return result.OrderBy(item => item.Sequence).ToList();
    }

    private static MatchReplayChunk Create(int index, List<MatchReplayEvent> events)
    {
        var payload = MatchReplayPayload.Encode(events);
        return new MatchReplayChunk
        {
            ChunkIndex = index,
            FirstSequence = events[0].Sequence,
            LastSequence = events[events.Count - 1].Sequence,
            FirstTurnIndex = events.Min(item => item.TurnIndex),
            LastTurnIndex = events.Max(item => item.TurnIndex),
            Payload = payload,
            Sha256 = MatchReplayPayload.Sha256(payload)
        };
    }

    internal static int Estimate(MatchReplayEvent item)
    {
        if (item == null)
        {
            return 0;
        }

        long size = 256 + (item.Payload?.Length ?? 0);
        if (item.TurnFrame != null)
        {
            size += Estimate(item.TurnFrame.State);
        }

        if (item.SeekCheckpoint != null)
        {
            size += Estimate(item.SeekCheckpoint.State);
        }

        if (item.ActionFrame != null)
        {
            var frame = item.ActionFrame;
            size += 640 + Estimate(frame.SourcePresentation) + Estimate(frame.Delta);
            size += frame.IntentPresentation == null
                ? 0
                : Estimate(frame.IntentPresentation);
            size += frame.NativePresentation == null
                ? 0
                : 192L + (frame.NativePresentation.Targets?.Count ?? 0) * 96L;
            size += (frame.CardTransitions?.Count ?? 0) * 160L;
            size += (frame.Presentation?.Count ?? 0) * 240L;
            size += (frame.Semantics?.Count ?? 0) * 320L;
        }

        return (int)Math.Max(256, Math.Min(int.MaxValue, size));
    }

    private static long Estimate(MatchReplayStateSnapshot? state)
    {
        if (state == null)
        {
            return 0;
        }

        long size = 512 + (state.RoleTableJson?.Length ?? 0) * 2L;
        size += (state.Statuses ?? new List<MatchReplayStatusState>()).Sum(status =>
            256L
            + (status.DynamicVariables?.Count ?? 0) * 64L
            + (status.Buffs?.Count ?? 0) * 192L
            + (status.Buffs?.Sum(buff => buff.Vars?.Count ?? 0) ?? 0) * 64L);
        size += (state.Cards ?? new List<MatchReplayCardState>()).Sum(Estimate);
        size += (state.EnemyIntents ?? new List<MatchReplayEnemyIntentState>()).Sum(Estimate);
        return size;
    }

    private static long Estimate(MatchReplayStateDelta? delta)
    {
        if (delta == null)
        {
            return 0;
        }

        long size = 384 + (delta.RemovedStatusIds?.Count ?? 0) * 64L;
        size += (delta.StatusUpserts ?? new List<MatchReplayStatusState>()).Sum(status =>
            256L
            + (status.DynamicVariables?.Count ?? 0) * 64L
            + (status.Buffs?.Count ?? 0) * 192L
            + (status.Buffs?.Sum(buff => buff.Vars?.Count ?? 0) ?? 0) * 64L);
        size += (delta.Cards ?? new List<MatchReplayCardState>()).Sum(Estimate);
        size += (delta.CardUpserts ?? new List<MatchReplayCardState>()).Sum(Estimate);
        size += (delta.RemovedCardIds?.Count ?? 0) * 64L;
        size += (delta.EnemyIntents ?? new List<MatchReplayEnemyIntentState>()).Sum(Estimate);
        size += (delta.EnemyIntentUpserts ?? new List<MatchReplayEnemyIntentState>()).Sum(Estimate);
        size += (delta.RemovedEnemyIntentIds?.Count ?? 0) * 96L;
        return size;
    }

    private static long Estimate(MatchReplayEnemyIntentState? intent)
    {
        if (intent == null)
        {
            return 0;
        }

        return 384L
               + (intent.ActorId?.Length ?? 0) * 2L
               + (intent.IntentId?.Length ?? 0) * 2L
               + (intent.SourceInstanceId?.Length ?? 0) * 2L
               + (intent.Label?.Length ?? 0) * 2L
               + (intent.Description?.Length ?? 0) * 2L
               + (intent.Icon?.Length ?? 0) * 2L
               + (intent.BackIcon?.Length ?? 0) * 2L
               + (intent.DisplayValue?.Length ?? 0) * 2L
               + (intent.ActionState?.Length ?? 0) * 2L
               + (intent.EffectName?.Length ?? 0) * 2L
               + (intent.TargetIds?.Sum(id => 32L + (id?.Length ?? 0) * 2L) ?? 0L);
    }

    private static long Estimate(MatchReplayCardState? card)
    {
        if (card == null)
        {
            return 0;
        }

        return 320L
               + (card.Data ?? new List<MatchReplayStringValue>())
                   .Sum(value => 48L + (value.Key?.Length ?? 0) * 2L + (value.Value?.Length ?? 0) * 2L)
               + (card.Vars ?? new List<MatchReplayStringValue>())
                   .Sum(value => 48L + (value.Key?.Length ?? 0) * 2L + (value.Value?.Length ?? 0) * 2L);
    }
}
