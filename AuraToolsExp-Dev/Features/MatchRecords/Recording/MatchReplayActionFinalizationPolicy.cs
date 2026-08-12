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
}
