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
            var estimated = Math.Max(64, item.Payload?.Length ?? 0) + 160;
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
}
