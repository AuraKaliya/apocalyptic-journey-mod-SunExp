using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal sealed class MatchReplayWorkingBuffer : IDisposable
{
    private readonly int targetBytes;
    private readonly long memoryBudgetBytes;
    private readonly string workingDirectory;
    private readonly List<MatchReplayEvent> pending = new();
    private readonly List<BufferedChunk> chunks = new();
    private int pendingBytes;
    private long bufferedBytes;
    private bool completed;

    internal MatchReplayWorkingBuffer(int targetBytes, long memoryBudgetBytes, string workingDirectory)
    {
        this.targetBytes = Math.Max(32 * 1024, Math.Min(1024 * 1024, targetBytes));
        this.memoryBudgetBytes = Math.Max(this.targetBytes * 2L, memoryBudgetBytes);
        this.workingDirectory = Path.GetFullPath(workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory)));
    }

    internal int EventCount { get; private set; }

    internal int ChunkCount => chunks.Count + (pending.Count > 0 ? 1 : 0);

    internal long BufferedBytes => bufferedBytes + pendingBytes;

    internal void Add(MatchReplayEvent item)
    {
        if (completed) throw new InvalidOperationException("Replay working buffer is already complete.");
        if (item == null) throw new ArgumentNullException(nameof(item));
        var estimated = Math.Max(64, item.Payload?.Length ?? 0) + 256;
        if (pending.Count > 0 && pendingBytes + estimated > targetBytes)
        {
            FlushPending();
        }

        pending.Add(item);
        pendingBytes += estimated;
        EventCount++;
    }

    internal IReadOnlyList<MatchReplayChunk> Complete()
    {
        return ReadChunks().ToList();
    }

    internal IEnumerable<MatchReplayChunk> ReadChunks()
    {
        EnsureCompleted();
        foreach (var chunk in chunks) yield return Load(chunk);
    }

    internal IEnumerable<MatchReplayEvent> ReadEvents()
    {
        foreach (var chunk in ReadChunks())
        {
            foreach (var item in MatchReplayChunker.Decode(new[] { chunk }))
            {
                yield return item;
            }
        }
    }

    public void Dispose()
    {
        pending.Clear();
        chunks.Clear();
        TryDeleteDirectory(workingDirectory);
    }

    internal static void CleanupAbandoned(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory)) return;
        foreach (var directory in Directory.GetDirectories(rootDirectory, "recording-*", SearchOption.TopDirectoryOnly))
        {
            TryDeleteDirectory(directory);
        }
    }

    private void FlushPending()
    {
        if (pending.Count == 0) return;
        var chunk = MatchReplayChunker.Build(pending, targetBytes).Single();
        chunk.ChunkIndex = chunks.Count;
        var buffered = new BufferedChunk(chunk);
        chunks.Add(buffered);
        bufferedBytes += chunk.Payload.Length;
        pending.Clear();
        pendingBytes = 0;
        SpillUntilWithinBudget();
    }

    private void EnsureCompleted()
    {
        if (completed) return;
        FlushPending();
        completed = true;
    }

    private void SpillUntilWithinBudget()
    {
        if (bufferedBytes <= memoryBudgetBytes) return;
        Directory.CreateDirectory(workingDirectory);
        foreach (var chunk in chunks)
        {
            if (bufferedBytes <= memoryBudgetBytes) break;
            if (chunk.Payload == null) continue;
            var path = Path.Combine(workingDirectory, "chunk-" + chunk.Metadata.ChunkIndex.ToString("D6") + ".work");
            File.WriteAllBytes(path, chunk.Payload);
            bufferedBytes -= chunk.Payload.Length;
            chunk.Path = path;
            chunk.Payload = null;
        }
    }

    private static MatchReplayChunk Load(BufferedChunk source)
    {
        var payload = source.Payload ?? File.ReadAllBytes(source.Path ?? throw new InvalidDataException("Replay work chunk is unavailable."));
        if (!string.Equals(MatchReplayPayload.Sha256(payload), source.Metadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Replay work chunk checksum mismatch.");
        }

        return new MatchReplayChunk
        {
            ChunkIndex = source.Metadata.ChunkIndex,
            FirstSequence = source.Metadata.FirstSequence,
            LastSequence = source.Metadata.LastSequence,
            FirstTurnIndex = source.Metadata.FirstTurnIndex,
            LastTurnIndex = source.Metadata.LastTurnIndex,
            Sha256 = source.Metadata.Sha256,
            Payload = payload
        };
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class BufferedChunk
    {
        internal BufferedChunk(MatchReplayChunk source)
        {
            Metadata = new MatchReplayChunk
            {
                ChunkIndex = source.ChunkIndex,
                FirstSequence = source.FirstSequence,
                LastSequence = source.LastSequence,
                FirstTurnIndex = source.FirstTurnIndex,
                LastTurnIndex = source.LastTurnIndex,
                Sha256 = source.Sha256
            };
            Payload = source.Payload;
        }

        internal MatchReplayChunk Metadata { get; }
        internal byte[]? Payload { get; set; }
        internal string? Path { get; set; }
    }
}
