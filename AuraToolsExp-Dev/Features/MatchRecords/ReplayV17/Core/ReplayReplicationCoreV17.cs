using System;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal sealed class ReplayCanonicalChunkBufferV17
{
    private readonly byte[][] chunks;
    private readonly bool[] received;

    internal ReplayCanonicalChunkBufferV17(
        string documentRoot,
        string transferId,
        int chunkCount,
        int totalBytes,
        string sha256)
    {
        if (chunkCount <= 0 || totalBytes <= 0) throw new ArgumentOutOfRangeException(nameof(chunkCount));
        DocumentRoot = documentRoot ?? "";
        TransferId = transferId ?? "";
        ChunkCount = chunkCount;
        TotalBytes = totalBytes;
        Sha256 = sha256 ?? "";
        chunks = new byte[chunkCount][];
        received = new bool[chunkCount];
    }

    internal string DocumentRoot { get; }
    internal string TransferId { get; }
    internal int ChunkCount { get; }
    internal int TotalBytes { get; }
    internal string Sha256 { get; }
    internal DateTime CreatedUtc { get; } = DateTime.UtcNow;
    internal int ReceivedCount { get; private set; }
    internal bool IsComplete => ReceivedCount == ChunkCount;

    internal bool Accepts(string documentRoot, string transferId, int chunkCount, int totalBytes, string sha256)
    {
        return string.Equals(DocumentRoot, documentRoot ?? "", StringComparison.OrdinalIgnoreCase)
               && string.Equals(TransferId, transferId ?? "", StringComparison.Ordinal)
               && ChunkCount == chunkCount
               && TotalBytes == totalBytes
               && string.Equals(Sha256, sha256 ?? "", StringComparison.OrdinalIgnoreCase);
    }

    internal bool TrySet(int index, byte[] value, int maximumChunkBytes)
    {
        if (index < 0 || index >= ChunkCount || value == null || value.Length > maximumChunkBytes) return false;
        if (received[index]) return chunks[index].SequenceEqual(value);
        chunks[index] = (byte[])value.Clone();
        received[index] = true;
        ReceivedCount++;
        return true;
    }

    internal byte[] Join()
    {
        if (!IsComplete) throw new InvalidOperationException("Canonical replay transfer is incomplete.");
        var result = new byte[TotalBytes];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            var value = chunk ?? Array.Empty<byte>();
            if (offset + value.Length > result.Length) throw new InvalidOperationException("Canonical replay chunk overflow.");
            Buffer.BlockCopy(value, 0, result, offset, value.Length);
            offset += value.Length;
        }
        if (offset != result.Length) throw new InvalidOperationException("Canonical replay byte count mismatch.");
        return result;
    }
}
