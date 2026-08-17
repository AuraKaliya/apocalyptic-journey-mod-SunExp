using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Tracks the native UI instances that existed before the replay local host.
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

    internal bool IsReplayOwned(int instanceId, string? uiName)
    {
        if (IsTransportSupport(uiName))
        {
            return true;
        }

        return IsCaptured && !baseline.Contains(instanceId);
    }

    internal bool IsReplayPresentationOwned(int instanceId, string? uiName)
    {
        return !IsTransportSupport(uiName) && IsReplayOwned(instanceId, uiName);
    }

    internal static bool IsTransportSupport(string? uiName)
    {
        // The native local-host join flow sends RpcSendChat immediately after
        // PlayerManager.CreateChatPanel. ChatUI must remain valid until Mirror
        // is fully stopped, so it is not ordinary replay presentation UI.
        return string.Equals(uiName, "ChatUI", StringComparison.Ordinal);
    }

    internal static bool IsSelfClosingPresentation(string? uiName)
    {
        // TitleUI owns its own short presentation lifetime. Requesting Close()
        // while that coroutine is already completing can race its terminal
        // callbacks; the replay menu barrier still tracks and force-cleans it
        // if the native lifecycle fails to finish.
        return string.Equals(uiName, "TitleUI", StringComparison.Ordinal);
    }

    internal void Reset()
    {
        baseline.Clear();
        IsCaptured = false;
    }
}
