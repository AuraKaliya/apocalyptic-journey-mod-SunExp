using System;

namespace AuraToolsExp.Dll.Features.ModSync;

public enum AuraToolsModSyncRequestMode
{
    Targeted,
    BroadcastFallback,
    LegacyBroadcastFallback
}

public static class AuraToolsModSyncProtocolPolicy
{
    public const int CurrentProtocolVersion = 2;
    public const int MinimumSupportedProtocolVersion = 1;

    public static int ProtocolVersionFor(AuraToolsModSyncRequestMode mode)
    {
        return mode == AuraToolsModSyncRequestMode.LegacyBroadcastFallback
            ? MinimumSupportedProtocolVersion
            : CurrentProtocolVersion;
    }

    public static bool TryNextFallback(
        AuraToolsModSyncRequestMode mode,
        out AuraToolsModSyncRequestMode next)
    {
        switch (mode)
        {
            case AuraToolsModSyncRequestMode.Targeted:
                next = AuraToolsModSyncRequestMode.BroadcastFallback;
                return true;
            case AuraToolsModSyncRequestMode.BroadcastFallback:
                next = AuraToolsModSyncRequestMode.LegacyBroadcastFallback;
                return true;
            default:
                next = AuraToolsModSyncRequestMode.LegacyBroadcastFallback;
                return false;
        }
    }
}

public sealed class AuraToolsModSyncRequestTracker
{
    public bool IsPending { get; private set; }

    public string RequestId { get; private set; } = "";

    public DateTime DeadlineUtc { get; private set; }

    public AuraToolsModSyncRequestMode Mode { get; private set; }

    public void Begin(string requestId, DateTime nowUtc, TimeSpan timeout, AuraToolsModSyncRequestMode mode = AuraToolsModSyncRequestMode.Targeted)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("A request id is required.", nameof(requestId));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        RequestId = requestId.Trim();
        DeadlineUtc = nowUtc + timeout;
        Mode = mode;
        IsPending = true;
    }

    public bool Matches(string responseRequestId)
    {
        return IsPending
               && (string.IsNullOrWhiteSpace(responseRequestId)
                   || string.Equals(RequestId, responseRequestId.Trim(), StringComparison.Ordinal));
    }

    public bool IsPendingRequest(string requestId)
    {
        return IsPending
               && !string.IsNullOrWhiteSpace(requestId)
               && string.Equals(RequestId, requestId.Trim(), StringComparison.Ordinal);
    }

    public bool IsExpired(DateTime nowUtc)
    {
        return IsPending && nowUtc >= DeadlineUtc;
    }

    public void Clear()
    {
        IsPending = false;
        RequestId = "";
        DeadlineUtc = default;
        Mode = AuraToolsModSyncRequestMode.Targeted;
    }
}
