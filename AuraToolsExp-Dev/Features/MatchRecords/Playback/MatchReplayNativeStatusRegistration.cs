using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Registers a concrete native StatusManager before resolving a FightObject's
/// virtual Status property. FightPlayer.Status reads this dictionary, so reversing
/// the order creates a deterministic null cycle.
/// </summary>
internal static class MatchReplayNativeStatusRegistration
{
    internal static T Register<T>(
        IDictionary<string, T> statuses,
        string instanceId,
        T concreteStatus,
        Func<T?> resolveVirtualStatus)
        where T : class
    {
        if (statuses == null) throw new ArgumentNullException(nameof(statuses));
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Replay status instance id is required.", nameof(instanceId));
        if (concreteStatus == null) throw new ArgumentNullException(nameof(concreteStatus));
        if (resolveVirtualStatus == null) throw new ArgumentNullException(nameof(resolveVirtualStatus));

        statuses[instanceId] = concreteStatus;
        var resolved = resolveVirtualStatus();
        if (resolved == null || !ReferenceEquals(resolved, concreteStatus))
        {
            throw new InvalidOperationException(
                "Native replay status registration did not resolve through the FightObject contract: "
                + instanceId);
        }

        return resolved;
    }
}
