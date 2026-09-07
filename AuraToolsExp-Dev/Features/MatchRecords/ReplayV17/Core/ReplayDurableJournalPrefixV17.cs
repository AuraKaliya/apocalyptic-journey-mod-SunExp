using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

/// <summary>
/// Computes the append-only durability watermark for incremental replay batches.
/// Events owned by an open transaction, and presentation events whose observed
/// tracks are still being sampled, remain in memory until their final shape is
/// immutable. Later events are held behind the same global sequence watermark.
/// </summary>
internal static class ReplayDurableJournalPrefixV17
{
    internal static long LastDurableSequence(long lastSequence,
        IEnumerable<long> openFirstSequences, IEnumerable<long> mutablePresentationSequences)
    {
        var firstUnsafe = openFirstSequences.Concat(mutablePresentationSequences)
            .Where(value => value > 0).DefaultIfEmpty(long.MaxValue).Min();
        return firstUnsafe == long.MaxValue ? lastSequence : Math.Min(lastSequence, firstUnsafe - 1);
    }
    internal static long LastDurableSequence(
        ReplayDocumentV17 document,
        IEnumerable<string>? openTransactionIds,
        IEnumerable<long>? mutablePresentationSequences)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        var all = document.TruthEvents
            .Concat(document.PresentationEvents)
            .ToList();
        if (all.Count == 0) return 0L;

        var open = (openTransactionIds ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);
        return LastDurableSequence(all.Max(item => item.Sequence), all
            .Where(item => open.Contains(item.TransactionId ?? ""))
            .Select(item => item.Sequence), mutablePresentationSequences ?? Array.Empty<long>());
    }
}
