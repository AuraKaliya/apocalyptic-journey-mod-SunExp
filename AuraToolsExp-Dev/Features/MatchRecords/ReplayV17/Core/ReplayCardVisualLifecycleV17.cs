using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayCardVisualLifecycleV17
{
    internal const long TimeoutTicks = 30_000_000L;

    internal const string SharedReset = "SharedReset";
    internal const string Destroyed = "Destroyed";
    internal const string Inactive = "Inactive";
    internal const string Rebound = "Rebound";
    internal const string Timeout = "Timeout";

    internal static bool ResetMatches(
        int observedRootInstanceId,
        string observedSourceInstanceId,
        int resetRootInstanceId,
        string resetSourceInstanceId)
    {
        if (observedRootInstanceId <= 0 || observedRootInstanceId != resetRootInstanceId) return false;
        return string.IsNullOrWhiteSpace(observedSourceInstanceId)
               || string.IsNullOrWhiteSpace(resetSourceInstanceId)
               || string.Equals(observedSourceInstanceId, resetSourceInstanceId, StringComparison.Ordinal);
    }

    internal static string CompletionReason(
        bool resetMatched,
        bool visualExists,
        bool activeInHierarchy,
        bool identityChanged,
        long elapsedTicks)
    {
        if (resetMatched) return SharedReset;
        if (!visualExists) return Destroyed;
        if (!activeInHierarchy) return Inactive;
        if (identityChanged) return Rebound;
        return elapsedTicks > TimeoutTicks ? Timeout : "";
    }
}
