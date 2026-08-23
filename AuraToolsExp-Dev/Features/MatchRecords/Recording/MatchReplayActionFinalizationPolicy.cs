using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal enum MatchReplayActionFinalizationDecision
{
    Observe,
    FinalizeStable,
    FinalizeDeadline
}

/// <summary>
/// Decides when an action's authoritative projection has stopped changing.
/// This policy intentionally knows nothing about the game's ActionQueue: that
/// collection is the round's actor roster, not a queue of pending commands.
/// </summary>
internal sealed class MatchReplayActionConvergenceTracker
{
    internal const int MinimumObservations = 3;
    internal const int StableObservationsRequired = 2;
    internal const int MaximumObservations = 8;

    private string previousStateHash = "";
    private int stableObservations;

    internal int ObservationCount { get; private set; }

    internal void Reset()
    {
        previousStateHash = "";
        stableObservations = 0;
        ObservationCount = 0;
    }

    internal MatchReplayActionFinalizationDecision Observe(string stateHash)
    {
        var normalized = stateHash ?? "";
        ObservationCount++;
        if (ObservationCount == 1
            || !string.Equals(previousStateHash, normalized, StringComparison.Ordinal))
        {
            previousStateHash = normalized;
            stableObservations = 1;
        }
        else
        {
            stableObservations++;
        }

        if (ObservationCount >= MinimumObservations
            && stableObservations >= StableObservationsRequired)
        {
            return MatchReplayActionFinalizationDecision.FinalizeStable;
        }

        return ObservationCount >= MaximumObservations
            ? MatchReplayActionFinalizationDecision.FinalizeDeadline
            : MatchReplayActionFinalizationDecision.Observe;
    }

    internal MatchReplayActionFinalizationDecision Observe(MatchReplayRevisionProbe probe)
    {
        var decision = Observe(probe.ConvergenceKey);
        if (probe.PendingWriters <= 0)
        {
            return decision;
        }

        return ObservationCount >= MaximumObservations
            ? MatchReplayActionFinalizationDecision.FinalizeDeadline
            : MatchReplayActionFinalizationDecision.Observe;
    }
}

internal readonly struct MatchReplayRevisionProbe
{
    internal MatchReplayRevisionProbe(long version, ulong fingerprint, int pendingWriters)
    {
        Version = version;
        Fingerprint = fingerprint;
        PendingWriters = Math.Max(0, pendingWriters);
    }

    internal long Version { get; }
    internal ulong Fingerprint { get; }
    internal int PendingWriters { get; }

    internal string ConvergenceKey => Version.ToString("x16")
                                      + ":" + Fingerprint.ToString("x16")
                                      + ":" + PendingWriters.ToString("x8");
}
