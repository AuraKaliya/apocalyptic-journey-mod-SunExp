using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Storage;

internal static class ReplayPayloadV10
{
    internal static byte[] Encode<T>(T value)
    {
        var canonical = ReplayCanonicalJsonV10.SerializeUtf8(value!);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(canonical, 0, canonical.Length);
        }

        return output.ToArray();
    }

    internal static T Decode<T>(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            throw new InvalidDataException("Replay v10 payload is empty.");
        }

        using var input = new MemoryStream(payload, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        return JsonConvert.DeserializeObject<T>(reader.ReadToEnd())
               ?? throw new InvalidDataException("Replay v10 payload could not be decoded.");
    }
}

internal static class ReplayTimelineChunkerV10
{
    internal const int DefaultTargetBytes = 256 * 1024;
    internal const int MinimumTargetBytes = 32 * 1024;
    internal const int MaximumTargetBytes = 1024 * 1024;

    internal static IReadOnlyList<ReplayTimelineChunkV10> Build(
        IReadOnlyList<ReplayTimelineEventV10>? events,
        int targetBytes = DefaultTargetBytes)
    {
        var source = (events ?? Array.Empty<ReplayTimelineEventV10>())
            .OrderBy(item => item.Sequence)
            .ToList();
        var target = Math.Max(MinimumTargetBytes, Math.Min(MaximumTargetBytes, targetBytes));
        var result = new List<ReplayTimelineChunkV10>();
        var pending = new List<ReplayTimelineEventV10>();
        var pendingBytes = 0;
        foreach (var value in source)
        {
            var estimated = ReplayCanonicalJsonV10.SerializeUtf8(value).Length + 32;
            if (pending.Count > 0 && pendingBytes + estimated > target)
            {
                result.Add(Create(result.Count, pending));
                pending = new List<ReplayTimelineEventV10>();
                pendingBytes = 0;
            }

            pending.Add(value);
            pendingBytes += estimated;
        }

        if (pending.Count > 0) result.Add(Create(result.Count, pending));
        return result;
    }

    internal static IReadOnlyList<ReplayTimelineEventV10> Decode(
        IEnumerable<ReplayTimelineChunkV10>? chunks)
    {
        var result = new List<ReplayTimelineEventV10>();
        var expectedChunk = 0;
        foreach (var chunk in (chunks ?? Array.Empty<ReplayTimelineChunkV10>())
                     .OrderBy(item => item.ChunkIndex))
        {
            if (chunk.ChunkIndex != expectedChunk++)
            {
                throw new InvalidDataException("Replay v10 chunks are not contiguous.");
            }

            if (!string.Equals(
                    ReplayCanonicalJsonV10.Sha256(chunk.Payload),
                    chunk.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Replay v10 chunk hash mismatch at " + chunk.ChunkIndex + ".");
            }

            var events = ReplayPayloadV10.Decode<List<ReplayTimelineEventV10>>(chunk.Payload);
            if (events.Count == 0
                || events[0].Sequence != chunk.FirstSequence
                || events[events.Count - 1].Sequence != chunk.LastSequence)
            {
                throw new InvalidDataException("Replay v10 chunk index does not match its payload.");
            }

            result.AddRange(events);
        }

        var expectedSequence = 1L;
        foreach (var value in result)
        {
            if (value.Sequence != expectedSequence++)
            {
                throw new InvalidDataException("Replay v10 event stream is not contiguous.");
            }
        }

        return result;
    }

    private static ReplayTimelineChunkV10 Create(int index, List<ReplayTimelineEventV10> events)
    {
        var payload = ReplayPayloadV10.Encode(events);
        return new ReplayTimelineChunkV10
        {
            ChunkIndex = index,
            FirstSequence = events[0].Sequence,
            LastSequence = events[events.Count - 1].Sequence,
            FirstTimeTicks = events[0].TimeTicks,
            LastTimeTicks = events[events.Count - 1].TimeTicks,
            Payload = payload,
            Sha256 = ReplayCanonicalJsonV10.Sha256(payload)
        };
    }
}
