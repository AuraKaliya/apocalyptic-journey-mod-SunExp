using System;

namespace AudioArbiter.Shared;

internal readonly struct AudioNetworkSenderSnapshot
{
    public AudioNetworkSenderSnapshot(bool isAvailable, bool isLobbyMember, bool isLobbyHost, string playerId)
    {
        IsAvailable = isAvailable;
        IsLobbyMember = isLobbyMember;
        IsLobbyHost = isLobbyHost;
        PlayerId = playerId ?? "";
    }

    public bool IsAvailable { get; }

    public bool IsLobbyMember { get; }

    public bool IsLobbyHost { get; }

    public string PlayerId { get; }
}

internal static class AudioNetworkPolicy
{
    public static bool IsCardUsePresentation(SoundPlaybackRequest? request)
    {
        return request != null
               && string.Equals(request.Kind, SoundEventKinds.CardUse, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExpiredPresentation(SoundPlaybackRequest? request, long nowUtcTicks)
    {
        if (!IsCardUsePresentation(request)
            || request!.CreatedAtUtcTicks <= 0
            || request.MaxAgeMilliseconds <= 0)
        {
            return false;
        }

        var elapsedTicks = nowUtcTicks - request.CreatedAtUtcTicks;
        return elapsedTicks > TimeSpan.TicksPerMillisecond * request.MaxAgeMilliseconds;
    }

    public static string PresentationDedupeKey(SoundPlaybackRequest request)
    {
        return (request.FightToken ?? "")
               + "|" + (request.IssuerPlayerId ?? "")
               + "|" + (request.EventId ?? "");
    }

    public static string ValidateServerCardUsePresentation(
        SoundPlaybackRequest? request,
        AudioNetworkSenderSnapshot sender,
        Func<string, string, bool> senderOwnsStatus,
        long nowUtcTicks = 0L)
    {
        if (!IsCardUsePresentation(request)) return "invalid event kind";
        if (!string.Equals(request!.Stage, AudioSignalStages.PresentationCommitted, StringComparison.OrdinalIgnoreCase))
            return "invalid event stage";
        if (!sender.IsAvailable) return "missing sender";
        if (!sender.IsLobbyMember) return "sender outside lobby: " + sender.PlayerId;
        if (string.IsNullOrWhiteSpace(request.EventId)) return "missing event id";
        if (request.EventId.Length > 160) return "event id too long";
        if (string.IsNullOrWhiteSpace(request.FightToken)) return "missing fight token";
        if (request.FightToken.Length > 96) return "fight token too long";
        if (string.IsNullOrWhiteSpace(request.CardId)) return "missing card id";
        if (string.IsNullOrWhiteSpace(request.StatusInstanceId)) return "missing owner status";
        if (!string.IsNullOrWhiteSpace(request.IssuerPlayerId)
            && !string.Equals(request.IssuerPlayerId, sender.PlayerId, StringComparison.Ordinal))
        {
            return "issuer mismatch";
        }

        if (!senderOwnsStatus(sender.PlayerId, request.StatusInstanceId)) return "owner mismatch";
        if (request.MaxAgeMilliseconds < 0
            || request.MaxAgeMilliseconds > SoundPlaybackRequest.DefaultPresentationMaxAgeMilliseconds)
        {
            return "invalid max age";
        }
        if (nowUtcTicks > 0 && IsExpiredPresentation(request, nowUtcTicks)) return "expired presentation";

        return "";
    }

    public static string ValidateLocalPresentationIdentity(
        SoundPlaybackRequest request,
        bool isMultiplayerSession)
    {
        if (!isMultiplayerSession || !IsCardUsePresentation(request)) return "";
        if (string.IsNullOrWhiteSpace(request.IssuerPlayerId)) return "missing local issuer";
        if (string.IsNullOrWhiteSpace(request.StatusInstanceId)) return "missing local owner status";
        return "";
    }
}
