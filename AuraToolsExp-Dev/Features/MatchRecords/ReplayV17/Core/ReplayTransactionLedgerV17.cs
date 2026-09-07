using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal sealed class ReplayTransactionLedgerEntryV17
{
    public string TransactionId { get; set; } = "";

    public string ParentTransactionId { get; set; } = "";

    public string Kind { get; set; } = "";

    public string ActorId { get; set; } = "";

    public string SourceInstanceId { get; set; } = "";

    public long OpenSequence { get; set; }

    public long RequiredStateWatermark { get; set; }

    public bool SourceCompleted { get; set; }

    public bool TerminalSourceSealed { get; set; }

    public bool StableBarrierObserved { get; set; }

    public bool Terminal { get; set; }

    public HashSet<string> PendingAssets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal bool CanComplete(long currentWatermark) =>
        !Terminal
        && SourceCompleted
        && StableBarrierObserved
        && currentWatermark >= RequiredStateWatermark
        && PendingAssets.Count == 0;
}

internal sealed class ReplayTransactionLedgerV17
{
    private readonly Dictionary<string, ReplayTransactionLedgerEntryV17> entries = new(StringComparer.Ordinal);
    private long openSequence;

    internal int OpenCount => entries.Values.Count(item => !item.Terminal);

    internal IReadOnlyList<ReplayTransactionLedgerEntryV17> OpenEntries => entries.Values
        .Where(item => !item.Terminal)
        .OrderBy(item => item.OpenSequence)
        .Select(CloneEntry)
        .ToList();

    private static ReplayTransactionLedgerEntryV17 CloneEntry(ReplayTransactionLedgerEntryV17 value) => new()
    {
        TransactionId = value.TransactionId, ParentTransactionId = value.ParentTransactionId,
        Kind = value.Kind, ActorId = value.ActorId, SourceInstanceId = value.SourceInstanceId,
        OpenSequence = value.OpenSequence, RequiredStateWatermark = value.RequiredStateWatermark,
        SourceCompleted = value.SourceCompleted, TerminalSourceSealed = value.TerminalSourceSealed,
        StableBarrierObserved = value.StableBarrierObserved, Terminal = value.Terminal,
        PendingAssets = new HashSet<string>(value.PendingAssets, StringComparer.OrdinalIgnoreCase)
    };

    internal void Begin(
        string transactionId,
        string kind,
        string actorId,
        string sourceInstanceId,
        string parentTransactionId = "")
    {
        if (string.IsNullOrWhiteSpace(transactionId) || entries.ContainsKey(transactionId))
            throw new InvalidOperationException("Replay transaction ledger id is empty or duplicated: " + transactionId);
        if (!string.IsNullOrWhiteSpace(parentTransactionId)
            && (!entries.TryGetValue(parentTransactionId, out var parent) || parent.Terminal))
            throw new InvalidOperationException("Replay transaction ledger parent is missing or terminal: " + parentTransactionId);
        entries.Add(transactionId, new ReplayTransactionLedgerEntryV17
        {
            TransactionId = transactionId,
            ParentTransactionId = parentTransactionId ?? "",
            Kind = kind ?? "",
            ActorId = actorId ?? "",
            SourceInstanceId = sourceInstanceId ?? "",
            OpenSequence = ++openSequence
        });
    }

    internal bool TryBindActionPresentation(
        string actorId,
        string sourceInstanceId,
        out string transactionId,
        out string rejection)
    {
        var candidates = entries.Values.Where(item => !item.Terminal)
            .Where(item => item.Kind == ReplayTransactionKindsV17.Card
                           || item.Kind == ReplayTransactionKindsV17.Skill
                           || item.Kind == ReplayTransactionKindsV17.Intent
                           || item.Kind == ReplayTransactionKindsV17.ImplicitObserved)
            .Where(item => string.IsNullOrWhiteSpace(actorId)
                           || string.Equals(item.ActorId, actorId, StringComparison.Ordinal))
            .Where(item => string.IsNullOrWhiteSpace(sourceInstanceId)
                           || string.Equals(item.SourceInstanceId, sourceInstanceId, StringComparison.Ordinal))
            .OrderByDescending(item => item.OpenSequence)
            .ToList();
        if (candidates.Count == 1)
        {
            transactionId = candidates[0].TransactionId;
            rejection = "";
            return true;
        }
        transactionId = "";
        rejection = candidates.Count == 0 ? "no-open-transaction" : "ambiguous-causal-ownership";
        return false;
    }

    internal void MarkSourceCompleted(string transactionId, long requiredStateWatermark)
    {
        var value = RequireOpen(transactionId);
        value.SourceCompleted = true;
        value.RequiredStateWatermark = Math.Max(value.RequiredStateWatermark, requiredStateWatermark);
    }

    internal IReadOnlyList<string> SealSourcesAtTerminal(long requiredStateWatermark)
    {
        var sealedTransactions = entries.Values
            .Where(item => !item.Terminal && !item.SourceCompleted)
            .OrderBy(item => item.OpenSequence)
            .ToList();
        foreach (var value in sealedTransactions)
        {
            value.SourceCompleted = true;
            value.TerminalSourceSealed = true;
            value.RequiredStateWatermark = Math.Max(value.RequiredStateWatermark, requiredStateWatermark);
        }
        return sealedTransactions.Select(item => item.TransactionId).ToList();
    }

    internal void RequireAsset(string transactionId, string sha256)
    {
        if (!string.IsNullOrWhiteSpace(sha256)) RequireOpen(transactionId).PendingAssets.Add(sha256);
    }

    internal void ResolveAsset(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return;
        foreach (var value in entries.Values.Where(item => !item.Terminal)) value.PendingAssets.Remove(sha256);
    }

    internal IReadOnlyList<string> ObserveStableBarrier(long currentStateWatermark)
    {
        foreach (var value in entries.Values.Where(item => !item.Terminal && item.SourceCompleted))
            value.StableBarrierObserved = true;
        return entries.Values
            .Where(item => item.CanComplete(currentStateWatermark)
                           && !entries.Values.Any(child => !child.Terminal
                               && string.Equals(child.ParentTransactionId, item.TransactionId, StringComparison.Ordinal)))
            .OrderByDescending(Depth)
            .ThenBy(item => item.OpenSequence)
            .Select(item => item.TransactionId)
            .ToList();
    }

    internal void Complete(string transactionId)
    {
        var value = RequireOpen(transactionId);
        if (!value.SourceCompleted || !value.StableBarrierObserved || value.PendingAssets.Count > 0)
            throw new InvalidOperationException("Replay transaction cannot complete before all ledger obligations drain: " + transactionId);
        if (entries.Values.Any(child => !child.Terminal
                                       && string.Equals(child.ParentTransactionId, transactionId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Replay parent transaction cannot complete before its children: " + transactionId);
        value.Terminal = true;
    }

    internal void Abort(string transactionId)
    {
        RequireOpen(transactionId).Terminal = true;
    }

    internal IReadOnlyList<string> AbortAll()
    {
        var result = entries.Values.Where(item => !item.Terminal)
            .OrderByDescending(Depth)
            .ThenBy(item => item.OpenSequence)
            .Select(item => item.TransactionId)
            .ToList();
        foreach (var id in result) entries[id].Terminal = true;
        return result;
    }

    internal void Reset()
    {
        entries.Clear();
        openSequence = 0;
    }

    private ReplayTransactionLedgerEntryV17 RequireOpen(string transactionId)
    {
        if (!entries.TryGetValue(transactionId ?? "", out var value) || value.Terminal)
            throw new InvalidOperationException("Replay transaction ledger entry is missing or terminal: " + transactionId);
        return value;
    }

    private int Depth(ReplayTransactionLedgerEntryV17 value)
    {
        var depth = 0;
        var parent = value.ParentTransactionId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(parent) && visited.Add(parent) && entries.TryGetValue(parent, out var next))
        {
            depth++;
            parent = next.ParentTransactionId;
        }
        return depth;
    }
}
