using System;
using AuraShared.Core;
using Witch.Core;

namespace AudioArbiter.Shared;

internal sealed class AudioNetworkRuntime
{
    internal const int MaximumPlaybackClaims = 512;
    internal const float LocalPlayIdReuseSeconds = 0.15f;
    private readonly AudioNetworkSessionState session = new(MaximumPlaybackClaims);

    public string FightToken => session.FightToken;

    public void BeginFightSession()
    {
        session.ResetTransient();
        var playerManager = PlayerManager.Instance;
        if (playerManager == null || (!playerManager.isClient && !playerManager.isServer))
        {
            session.SetFightToken(CreateFightToken());
            return;
        }

        if (!playerManager.isServer)
        {
            return;
        }

        session.SetFightToken(CreateFightToken());
        try
        {
            var command = new RpcAudioFightSession(session.FightToken);
            command.BindServerSender(AuraRpcAuthorityRuntime.CreateLocalServerSender("AudioFightSession"));
            playerManager.SendRpcCommand(command);
        }
        catch (Exception ex)
        {
            Warn("Fight session broadcast failed: " + ex.Message);
        }
    }

    public bool ApplyFightSession(string token, string source)
    {
        token = (token ?? "").Trim();
        if (token.Length == 0 || token.Length > 96)
        {
            return false;
        }

        session.ApplyFightToken(token);
        AuraSharedLog.Info("AudioArbiter", "Fight session applied: token=" + token + ", source=" + source);
        return true;
    }

    public bool TryAcceptRemotePresentation(SoundPlaybackRequest request)
    {
        if (AudioNetworkPolicy.IsExpiredPresentation(request, DateTime.UtcNow.Ticks))
        {
            TraceRequest(request, "Discarded expired presentation event");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            request.EventId = Guid.NewGuid().ToString("N");
        }

        return TryClaimPresentation(request, "remote");
    }

    public bool TryPrepareAndRelayLocalPresentation(SoundPlaybackRequest request, bool presentationClaimed)
    {
        if (!AudioNetworkPolicy.IsCardUsePresentation(request) || request.IsRemote)
        {
            return true;
        }

        request.CreatedAtUtcTicks = request.CreatedAtUtcTicks > 0 ? request.CreatedAtUtcTicks : DateTime.UtcNow.Ticks;
        request.MaxAgeMilliseconds = request.MaxAgeMilliseconds > 0
            ? request.MaxAgeMilliseconds
            : SoundPlaybackRequest.DefaultPresentationMaxAgeMilliseconds;
        request.IssuerPlayerId = string.IsNullOrWhiteSpace(request.IssuerPlayerId)
            ? PlayerManager.Instance?.PlayerId ?? ""
            : request.IssuerPlayerId;
        var identityRejection = AudioNetworkPolicy.ValidateLocalPresentationIdentity(
            request,
            IsMultiplayerSession());
        if (!string.IsNullOrWhiteSpace(identityRejection))
        {
            Warn("Card-use presentation skipped: " + identityRejection + ". card=" + request.CardId);
            return false;
        }
        request.FightToken = string.IsNullOrWhiteSpace(request.FightToken) ? session.FightToken : request.FightToken;
        if (!presentationClaimed && !TryClaimPresentation(request, "local"))
        {
            return false;
        }

        RelayLocalCardUsePresentation(request);
        return true;
    }

    public void SyncRemote(
        SoundPlaybackRequest request,
        string providerId,
        string ownerModId,
        bool providerSync,
        bool syncRemote)
    {
        if (!syncRemote
            || request.DisableSync
            || request.IsRemote
            || !providerSync
            || AudioNetworkPolicy.IsCardUsePresentation(request))
        {
            return;
        }

        var playerManager = PlayerManager.Instance;
        if (playerManager == null)
        {
            return;
        }

        // Keep the RPC payload's bare ProviderId for older receivers; new receivers use OwnerModId to disambiguate.
        request.ProviderId = providerId;
        request.OwnerModId = ownerModId;
        try
        {
            playerManager.SendRpcCommandExcludeOwner(new RpcAudioEvent(request));
        }
        catch (Exception ex)
        {
            Warn("Remote sound sync failed: " + ex.Message);
        }
    }

    public void ApplyServerCardUsePresentation(
        SoundPlaybackRequest request,
        AuraRpcSender sender,
        Func<SoundPlaybackRequest, bool> receiveRemote)
    {
        var playerManager = PlayerManager.Instance;
        if (playerManager == null || !playerManager.isServer)
        {
            return;
        }

        var senderSnapshot = new AudioNetworkSenderSnapshot(
            sender.IsAvailable,
            sender.IsLobbyMember,
            sender.IsLobbyHost,
            sender.PlayerId);
        var rejection = AudioNetworkPolicy.ValidateServerCardUsePresentation(
            request,
            senderSnapshot,
            SenderOwnsStatus,
            DateTime.UtcNow.Ticks);
        if (!string.IsNullOrWhiteSpace(rejection))
        {
            Warn("Card-use presentation rejected: " + rejection);
            return;
        }

        request.IssuerPlayerId = sender.PlayerId;
        request.OwnerModId = "";
        request.ProviderId = "";
        request.CreatedAtUtcTicks = DateTime.UtcNow.Ticks;
        request.MaxAgeMilliseconds = SoundPlaybackRequest.DefaultPresentationMaxAgeMilliseconds;
        request.DisableSync = true;
        if (!receiveRemote(request))
        {
            return;
        }

        try
        {
            playerManager.SendRpcCommand(new RpcAudioEvent(request));
            TraceRequest(request, "Authorized client card-use presentation relayed");
        }
        catch (Exception ex)
        {
            Warn("Authorized card-use presentation relay failed: " + ex.Message);
        }
    }

    public string ReuseOrCreateLocalPlayId(
        string ownerInstanceId,
        string cardId,
        string action,
        string effects,
        float now)
    {
        var key = (ownerInstanceId ?? "") + "|" + (cardId ?? "") + "|"
                  + (action ?? "") + "|" + (effects ?? "");
        return session.ReuseOrCreateLocalPlayId(
            key,
            PlayerManager.Instance?.PlayerId ?? "solo",
            now,
            LocalPlayIdReuseSeconds);
    }

    private void RelayLocalCardUsePresentation(SoundPlaybackRequest request)
    {
        var playerManager = PlayerManager.Instance;
        if (playerManager == null)
        {
            return;
        }

        try
        {
            if (playerManager.isServer)
            {
                playerManager.SendRpcCommand(new RpcAudioEvent(request));
                TraceRequest(request, "Host local card-use presentation relayed");
            }
            else
            {
                playerManager.SendRpcCommand(new RpcAudioPresentationRequest(request));
                TraceRequest(request, "Client card-use presentation submitted to host");
            }
        }
        catch (Exception ex)
        {
            Warn("Card-use presentation submit failed: " + ex.Message);
        }
    }

    private bool TryClaimPresentation(SoundPlaybackRequest request, string source)
    {
        var result = session.TryClaimPresentation(request, IsMultiplayerSession());
        switch (result)
        {
            case AudioPresentationClaimResult.NotPresentation:
            case AudioPresentationClaimResult.Claimed:
                break;
            case AudioPresentationClaimResult.FightSessionNotReady:
                Warn("Card-use presentation skipped: fight session is not ready. source=" + source);
                return false;
            case AudioPresentationClaimResult.StaleFightSession:
                AuraSharedLog.Info("AudioArbiter", "Stale card-use presentation ignored: source=" + source
                    + ", eventId=" + request.EventId + ", requestFight=" + request.FightToken
                    + ", currentFight=" + session.FightToken);
                return false;
            case AudioPresentationClaimResult.Duplicate:
                AuraSharedLog.Info("AudioArbiter", "Duplicate card-use presentation ignored: source=" + source
                    + ", eventId=" + request.EventId + ", issuer=" + request.IssuerPlayerId
                    + ", fight=" + request.FightToken);
                return false;
            default:
                return false;
        }

        if (result == AudioPresentationClaimResult.Claimed)
        {
            AuraSharedLog.Info("AudioArbiter", "Card-use presentation claimed: source=" + source
                + ", eventId=" + request.EventId + ", issuer=" + request.IssuerPlayerId
                + ", fight=" + request.FightToken + ", cardId=" + request.CardId + ", roleId=" + request.RoleId);
        }

        return true;
    }

    private static bool SenderOwnsStatus(string playerId, string statusInstanceId)
    {
        if (string.Equals(playerId, statusInstanceId, StringComparison.Ordinal)) return true;
        try
        {
            var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            return map != null
                   && map.TryGetValue(playerId, out var statuses)
                   && statuses != null
                   && statuses.Contains(statusInstanceId);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMultiplayerSession()
    {
        var playerManager = PlayerManager.Instance;
        return playerManager != null && (playerManager.isClient || playerManager.isServer);
    }

    private static string CreateFightToken()
    {
        return "audio-" + Guid.NewGuid().ToString("N");
    }

    private static void TraceRequest(SoundPlaybackRequest request, string message)
    {
        AuraSharedLog.DebugLog("AudioArbiter", message + ": kind=" + request.Kind
            + ", owner=" + request.OwnerModId + ", provider=" + request.ProviderId
            + ", role=" + request.RoleId + ", status=" + request.StatusInstanceId
            + ", card=" + request.CardId + ", source=" + request.SourceName,
            false);
    }

    private static void Warn(string message)
    {
        AuraSharedLog.Warn("AudioArbiter", message);
    }
}
