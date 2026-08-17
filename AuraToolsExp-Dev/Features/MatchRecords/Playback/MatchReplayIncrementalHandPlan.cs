using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Converts recorded identity transitions into the smallest presentation mutation set.
/// Snapshot rebuilds are deliberately outside this contract and are reserved for bootstrap/seek.
/// </summary>
internal sealed class MatchReplayIncrementalHandPlan
{
    internal HashSet<string> AddedHandIds { get; } = new(StringComparer.Ordinal);

    internal HashSet<string> RemovedHandIds { get; } = new(StringComparer.Ordinal);

    internal HashSet<string> PresentationCandidateIds { get; } = new(StringComparer.Ordinal);

    internal HashSet<string> ContentChangedIds { get; } = new(StringComparer.Ordinal);

    internal HashSet<string> RuntimeCardIds { get; } = new(StringComparer.Ordinal);

    internal bool LayoutChanged { get; private set; }

    internal static MatchReplayIncrementalHandPlan Build(
        IEnumerable<MatchReplayCardTransition>? transitions)
    {
        var result = new MatchReplayIncrementalHandPlan();
        foreach (var transition in transitions ?? Enumerable.Empty<MatchReplayCardTransition>())
        {
            var id = (transition?.ReplayCardId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            result.RuntimeCardIds.Add(id);
            if (transition!.PresentationChanged
                || string.Equals(
                    transition.Disposition,
                    MatchReplayCardDispositionKinds.Update,
                    StringComparison.Ordinal))
            {
                result.ContentChangedIds.Add(id);
            }

            var fromHand = string.Equals(transition!.FromZone, "Hand", StringComparison.Ordinal);
            var toHand = string.Equals(transition.ToZone, "Hand", StringComparison.Ordinal);
            if (toHand && !fromHand)
            {
                result.AddedHandIds.Add(id);
            }

            if (fromHand && !toHand)
            {
                result.RemovedHandIds.Add(id);
            }

            if (toHand && result.ContentChangedIds.Contains(id))
            {
                result.PresentationCandidateIds.Add(id);
            }

            if (fromHand != toHand
                || (fromHand && toHand && transition.FromOrder != transition.ToOrder))
            {
                result.LayoutChanged = true;
            }
        }

        return result;
    }
}
