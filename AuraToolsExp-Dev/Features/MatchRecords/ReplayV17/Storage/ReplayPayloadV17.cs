using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;

internal static class ReplayPayloadV17
{
    internal const int DefaultMaximumDecodedBytes = 64 * 1024 * 1024;
    internal static byte[] Encode<T>(T value)
    {
        var canonical = ReplayCanonicalJsonV17.SerializeUtf8(value!);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(canonical, 0, canonical.Length);
        return output.ToArray();
    }

    internal static T Decode<T>(byte[] payload, int maximumDecodedBytes = DefaultMaximumDecodedBytes)
    {
        if (payload == null || payload.Length == 0)
            throw new InvalidDataException("Replay v17 payload is empty.");
        if (maximumDecodedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));
        using var input = new MemoryStream(payload, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > maximumDecodedBytes)
                throw new InvalidDataException("Replay v17 decoded payload exceeds its size budget.");
        }
        try
        {
            return ReplayCanonicalJsonV17.DeserializeStrict<T>(Encoding.UTF8.GetString(output.ToArray()));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Replay v17 payload could not be decoded strictly.", ex);
        }
    }
}

internal static class ReplayJournalChunkerV17
{
    internal const int DefaultTargetBytes = 256 * 1024;
    internal const int MinimumTargetBytes = 32 * 1024;
    internal const int MaximumTargetBytes = 1024 * 1024;

    internal static IReadOnlyList<ReplayJournalChunkV17> Build(
        string lane,
        IReadOnlyList<ReplayJournalEventV17>? events,
        int targetBytes = DefaultTargetBytes)
    {
        if (!string.Equals(lane, ReplayJournalLanesV17.Truth, StringComparison.Ordinal)
            && !string.Equals(lane, ReplayJournalLanesV17.Presentation, StringComparison.Ordinal))
            throw new ArgumentException("Replay lane is invalid.", nameof(lane));
        var source = (events ?? Array.Empty<ReplayJournalEventV17>())
            .Where(item => item != null)
            .OrderBy(item => item.Sequence)
            .ToList();
        if (source.Any(item => !string.Equals(item.Lane, lane, StringComparison.Ordinal)))
            throw new InvalidDataException("Replay chunk input mixes journal lanes.");
        var target = Math.Max(MinimumTargetBytes, Math.Min(MaximumTargetBytes, targetBytes));
        var result = new List<ReplayJournalChunkV17>();
        var pending = new List<ReplayJournalEventV17>();
        var pendingBytes = 0;
        foreach (var value in source)
        {
            var estimated = ReplayCanonicalJsonV17.SerializeUtf8(value).Length + 32;
            if (pending.Count > 0 && pendingBytes + estimated > target)
            {
                result.Add(Create(lane, result.Count, pending, result.LastOrDefault()?.Sha256 ?? ""));
                pending = new List<ReplayJournalEventV17>();
                pendingBytes = 0;
            }
            pending.Add(value);
            pendingBytes += estimated;
        }
        if (pending.Count > 0)
            result.Add(Create(lane, result.Count, pending, result.LastOrDefault()?.Sha256 ?? ""));
        return result;
    }

    internal static IReadOnlyList<ReplayJournalEventV17> Decode(
        string lane,
        IEnumerable<ReplayJournalChunkV17>? chunks)
    {
        var result = new List<ReplayJournalEventV17>();
        var expectedChunk = 0;
        var previousChunkHash = "";
        var lastSequence = 0L;
        foreach (var chunk in (chunks ?? Array.Empty<ReplayJournalChunkV17>()).OrderBy(item => item.ChunkIndex))
        {
            if (chunk.ChunkIndex != expectedChunk++
                || !string.Equals(chunk.Lane, lane, StringComparison.Ordinal)
                || !string.Equals(chunk.PreviousChunkSha256, previousChunkHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Replay v17 chunk chain is invalid at " + chunk.ChunkIndex + ".");
            if (!string.Equals(ChunkHash(chunk), chunk.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Replay v17 chunk hash mismatch at " + chunk.ChunkIndex + ".");
            var values = ReplayPayloadV17.Decode<List<ReplayJournalEventV17>>(chunk.Payload);
            if (values.Count == 0
                || values[0].Sequence != chunk.FirstSequence
                || values[values.Count - 1].Sequence != chunk.LastSequence
                || values[0].TimeTicks != chunk.FirstTimeTicks
                || values[values.Count - 1].TimeTicks != chunk.LastTimeTicks)
                throw new InvalidDataException("Replay v17 chunk index does not match its payload.");
            foreach (var value in values)
            {
                if (!string.Equals(value.Lane, lane, StringComparison.Ordinal) || value.Sequence <= lastSequence)
                    throw new InvalidDataException("Replay v17 lane event order is invalid at " + value.Sequence + ".");
                lastSequence = value.Sequence;
            }
            result.AddRange(values);
            previousChunkHash = chunk.Sha256;
        }
        return result;
    }

    private static ReplayJournalChunkV17 Create(
        string lane,
        int index,
        IReadOnlyList<ReplayJournalEventV17> events,
        string previousChunkHash)
    {
        var payload = ReplayPayloadV17.Encode(events);
        var result = new ReplayJournalChunkV17
        {
            Lane = lane,
            ChunkIndex = index,
            FirstSequence = events[0].Sequence,
            LastSequence = events[events.Count - 1].Sequence,
            FirstTimeTicks = events[0].TimeTicks,
            LastTimeTicks = events[events.Count - 1].TimeTicks,
            PreviousChunkSha256 = previousChunkHash ?? "",
            Payload = payload
        };
        result.Sha256 = ChunkHash(result);
        return result;
    }

    private static string ChunkHash(ReplayJournalChunkV17 chunk)
    {
        return ReplayCanonicalJsonV17.Sha256(new ReplayJournalChunkHashPayloadV17
        {
            Lane = chunk.Lane,
            ChunkIndex = chunk.ChunkIndex,
            FirstSequence = chunk.FirstSequence,
            LastSequence = chunk.LastSequence,
            FirstTimeTicks = chunk.FirstTimeTicks,
            LastTimeTicks = chunk.LastTimeTicks,
            PreviousChunkSha256 = chunk.PreviousChunkSha256,
            PayloadSha256 = ReplayCanonicalJsonV17.Sha256(chunk.Payload)
        });
    }

    private sealed class ReplayJournalChunkHashPayloadV17
    {
        public string Lane { get; set; } = "";
        public int ChunkIndex { get; set; }
        public long FirstSequence { get; set; }
        public long LastSequence { get; set; }
        public long FirstTimeTicks { get; set; }
        public long LastTimeTicks { get; set; }
        public string PreviousChunkSha256 { get; set; } = "";
        public string PayloadSha256 { get; set; } = "";
    }
}
