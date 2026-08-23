using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Tracks the native UI instances that existed before the replay view.
/// Anything created afterwards belongs to the replay view and must not survive it.
/// </summary>
internal sealed class MatchReplayManagedUiOwnership
{
    private readonly HashSet<int> baseline = new();

    internal bool IsCaptured { get; private set; }

    internal int BaselineCount => baseline.Count;

    internal void Capture(IEnumerable<int>? instanceIds)
    {
        baseline.Clear();
        foreach (var instanceId in instanceIds ?? Enumerable.Empty<int>())
        {
            baseline.Add(instanceId);
        }

        IsCaptured = true;
    }

    internal bool IsReplayPresentationOwned(int instanceId)
    {
        return IsCaptured && !baseline.Contains(instanceId);
    }

    internal void Reset()
    {
        baseline.Clear();
        IsCaptured = false;
    }
}
