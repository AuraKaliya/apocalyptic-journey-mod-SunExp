using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using UnityEngine;
using Witch.Core;

namespace AuraCg.Shared;

internal sealed class AuraCgNetworkRuntime
{
    internal const int MaximumEventsPerPlayback = 4;
    internal const int MaximumPayloadBytes = 8192;
    internal const int MaximumIdentifierLength = 160;
    private const int MaximumPlaybackClaims = 512;
    private readonly Func<SkillCgNetworkEvent, bool, SkillCgRequest?> registeredRequestResolver;
    private readonly AuraCgNetworkSessionState session = new(MaximumPlaybackClaims);
    private readonly AuraCgPendingPlaybackStore pendingPlaybacks = new();

    public AuraCgNetworkRuntime(Func<SkillCgNetworkEvent, bool, SkillCgRequest?> registeredRequestResolver)
    {
        this.registeredRequestResolver = registeredRequestResolver ?? throw new ArgumentNullException(nameof(registeredRequestResolver));
    }

    public void BeginFightSession(object? value, Action<string> clearTransientPlayback)
    {
        var request = value as SkillCgFightSessionRequest
                      ?? new SkillCgFightSessionRequest("AuraCgShared", "fight start");
        clearTransientPlayback(request.Reason);
        if (!IsMultiplayerSession())
        {
            session.SetFightToken(CreateFightToken());
            return;
        }

        var playerManager = PlayerManager.Instance;
        if (playerManager == null || !playerManager.isServer)
        {
            return;
        }

        session.SetFightToken(CreateFightToken());
        try
        {
            var command = new RpcSkillCgFightSession(request.OwnerModId, session.FightToken);
            command.BindServerSender(AuraCgRpcAuthorityRuntime.CreateLocalServerSender("SkillCgFightSession"));
            playerManager.SendRpcCommand(command);
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("fight-session-broadcast-failed", "Skill CG fight session broadcast failed once; later errors are suppressed. error=" + ex.Message);
        }
    }

    public void ApplyFightSession(object? value, Action<string> clearTransientPlayback)
    {
        if (value is not SkillCgFightSessionRequest request || !HasBoundedIdentifier(request.FightToken))
        {
            return;
        }

        clearTransientPlayback(request.Reason);
        session.SetFightToken(request.FightToken);
    }

    public void ResetTransient()
    {
        pendingPlaybacks.Clear();
        session.ResetTransient();
    }

    public bool TryPrepareLocalPlaybackBatch(
        IReadOnlyList<SkillCgRequest> requests,
        float duplicateWindowSeconds,
        out SkillCgPlaybackSnapshot playback)
    {
        playback = new SkillCgPlaybackSnapshot();
        var batch = (requests ?? Array.Empty<SkillCgRequest>())
            .Where(request => request != null)
            .ToList();
        if (batch.Count == 0)
        {
            return false;
        }

        foreach (var request in batch)
        {
            request.Normalize();
        }

        var first = batch[0];
        if (!TryValidateLocalPlaybackOwner(first, out var issuerPlayerId, out var rejection))
        {
            AuraCgLog.DebugLog("[SkillCG] local playback skipped: " + rejection);
            return false;
        }

        if (IsMultiplayerSession() && !HasBoundedIdentifier(session.FightToken))
        {
            AuraCgLog.WarnOnce("fight-session-not-ready", "Skill CG playback skipped: host fight session is not ready.");
            return false;
        }

        var playId = session.ReuseOrCreateLocalPlayId(
            issuerPlayerId,
            first.OwnerInstanceId,
            first.CardId,
            first.ActionSequence,
            first.EventToken,
            Time.unscaledTime,
            duplicateWindowSeconds);
        if (!TryClaimPlayback(issuerPlayerId, playId, "local"))
        {
            return false;
        }

        foreach (var request in batch)
        {
            request.IssuerPlayerId = issuerPlayerId;
            request.SkillCgPlayId = playId;
            request.EventToken = playId;
        }

        playback = CreatePlaybackSnapshot(issuerPlayerId, playId, first, batch);
        return AuraSharedPayloadBudget.FitsSoftLimit(playback, MaximumPayloadBytes, out _, out _);
    }

    public void RelayPlayback(SkillCgPlaybackSnapshot playback)
    {
        if (playback == null || playback.Events == null || playback.Events.Count == 0)
        {
            return;
        }

        var playerManager = PlayerManager.Instance;
        if (playerManager == null || (!playerManager.isClient && !playerManager.isServer))
        {
            return;
        }

        try
        {
            if (playerManager.isServer)
            {
                playerManager.SendRpcCommand(new RpcSkillCgPlayback(playback));
                return;
            }

            playerManager.SendRpcCommand(new RpcSkillCgPlaybackRequest(playback));
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("playback-relay-failed", "Skill CG playback relay failed once; later errors are suppressed. error=" + ex.Message);
            AuraCgLog.DebugLog("Skill CG playback relay exception: " + ex);
        }
    }

    public void ApplyServerPlaybackRequest(object? value, Action<IReadOnlyList<SkillCgRequest>> enqueuePlayback)
    {
        if (value is not SkillCgServerPlaybackEnvelope envelope)
        {
            return;
        }

        var playback = envelope.Playback ?? new SkillCgPlaybackSnapshot();
        var sender = envelope.Sender ?? AuraCgRpcSender.Unbound;
        var rejection = ValidateServerPlaybackRequest(playback, sender);
        if (!string.IsNullOrWhiteSpace(rejection))
        {
            AuraCgLog.WarnOnce("server-playback-rejected:" + rejection, "Skill CG server playback rejected: " + rejection);
            return;
        }

        playback.IssuerPlayerId = sender.PlayerId;
        AuraCgNetworkPolicy.NormalizePlaybackSnapshot(playback);
        ApplyPlaybackSnapshot(
            playback,
            "server",
            enqueuePlayback,
            relayAfterApply: true,
            queueIfUnavailable: true);
    }

    public void ApplyNetworkPlayback(object? value, Action<IReadOnlyList<SkillCgRequest>> enqueuePlayback)
    {
        if (value is SkillCgNetworkPlaybackEnvelope envelope)
        {
            ApplyPlaybackSnapshot(
                envelope.Playback,
                envelope.Source,
                enqueuePlayback,
                relayAfterApply: false,
                queueIfUnavailable: true);
        }
    }

    public void RetryPendingPlaybacks(Action<IReadOnlyList<SkillCgRequest>> enqueuePlayback)
    {
        if (pendingPlaybacks.Count == 0)
        {
            return;
        }

        var nowUtcTicks = DateTime.UtcNow.Ticks;
        foreach (var pending in pendingPlaybacks.Snapshot())
        {
            if (nowUtcTicks >= pending.ExpiresAtUtcTicks)
            {
                pendingPlaybacks.Remove(pending.Key);
                AuraCgLog.WarnOnce(
                    "network-playback-resolution-timeout:" + pending.Key,
                    "Skill CG network playback skipped after waiting for local registration. source="
                    + pending.Source + ", playId=" + pending.Playback.SkillCgPlayId + ".");
                continue;
            }

            if (ApplyPlaybackSnapshot(
                    pending.Playback,
                    pending.Source + ":retry",
                    enqueuePlayback,
                    pending.RelayAfterApply,
                    queueIfUnavailable: false))
            {
                pendingPlaybacks.Remove(pending.Key);
            }
        }
    }

    private bool ApplyPlaybackSnapshot(
        SkillCgPlaybackSnapshot? playback,
        string source,
        Action<IReadOnlyList<SkillCgRequest>> enqueuePlayback,
        bool relayAfterApply,
        bool queueIfUnavailable)
    {
        if (playback == null
            || string.IsNullOrWhiteSpace(playback.IssuerPlayerId)
            || string.IsNullOrWhiteSpace(playback.SkillCgPlayId)
            || playback.Events == null
            || playback.Events.Count == 0)
        {
            AuraCgLog.WarnOnce("network-playback-invalid:" + source, "Skill CG network playback skipped: invalid payload. source=" + source);
            return false;
        }

        if (!ValidateNetworkPlaybackBudget(playback))
        {
            AuraCgLog.WarnOnce("network-playback-over-budget:" + source, "Skill CG network playback skipped: payload exceeds the protocol budget.");
            return false;
        }

        if (IsMultiplayerSession()
            && !string.Equals(playback.FightToken, session.FightToken, StringComparison.Ordinal))
        {
            AuraCgLog.DebugLog("Skill CG network playback skipped: stale fight session. source=" + source);
            return false;
        }

        AuraCgNetworkPolicy.NormalizePlaybackSnapshot(playback);
        var requests = new List<SkillCgRequest>();
        foreach (var item in playback.Events)
        {
            var request = registeredRequestResolver(item, true);
            if (request == null)
            {
                if (queueIfUnavailable
                    && pendingPlaybacks.Enqueue(playback, source, relayAfterApply, DateTime.UtcNow.Ticks))
                {
                    AuraCgLog.DebugLog("Skill CG network playback pending local registration: source="
                                       + source + ", playId=" + playback.SkillCgPlayId + ".");
                }

                return false;
            }

            requests.Add(request);
        }

        if (!TryClaimPlayback(playback.IssuerPlayerId, playback.SkillCgPlayId, source))
        {
            return false;
        }

        enqueuePlayback(requests);
        if (relayAfterApply)
        {
            BroadcastAuthorizedPlayback(playback);
        }

        return true;
    }

    private static void BroadcastAuthorizedPlayback(SkillCgPlaybackSnapshot playback)
    {
        try
        {
            PlayerManager.Instance?.SendRpcCommand(new RpcSkillCgPlayback(playback));
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("server-playback-broadcast-failed", "Skill CG server broadcast failed once; later errors are suppressed. error=" + ex.Message);
            AuraCgLog.DebugLog("Skill CG server broadcast exception: " + ex);
        }
    }

    private string ValidateServerPlaybackRequest(SkillCgPlaybackSnapshot? playback, AuraCgRpcSender sender)
    {
        var identityRejection = AuraCgNetworkPolicy.ValidateServerPlaybackIdentity(
            playback,
            new AuraCgNetworkSenderSnapshot(
                sender.IsAvailable,
                sender.IsLobbyMember,
                sender.PlayerId),
            IsMultiplayerSession(),
            SenderOwnsStatus);
        if (!string.IsNullOrWhiteSpace(identityRejection))
        {
            return identityRejection;
        }

        if (playback == null)
        {
            return "missing payload";
        }

        if (string.IsNullOrWhiteSpace(playback.SkillCgPlayId))
        {
            return "missing play id";
        }

        if (playback.Events == null || playback.Events.Count == 0)
        {
            return "missing events";
        }

        if (!string.Equals(playback.FightToken, session.FightToken, StringComparison.Ordinal))
        {
            return "stale fight session";
        }

        if (!ValidateNetworkPlaybackBudget(playback))
        {
            return "payload budget exceeded";
        }

        if (playback.Events.Any(item => registeredRequestResolver(item, false) == null))
        {
            return "unregistered event identity";
        }

        return "";
    }

    private static bool ValidateNetworkPlaybackBudget(SkillCgPlaybackSnapshot playback)
    {
        return AuraCgNetworkPolicy.HasValidPlaybackShape(
                   playback,
                   MaximumEventsPerPlayback,
                   MaximumIdentifierLength)
               && AuraSharedPayloadBudget.FitsSoftLimit(playback, MaximumPayloadBytes, out _, out _);
    }

    private static bool SenderOwnsStatus(string playerId, string ownerStatusId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return false;
        }

        if (string.Equals(playerId, ownerStatusId, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var map = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            return map != null
                   && map.TryGetValue(playerId, out var statuses)
                   && statuses != null
                   && statuses.Contains(ownerStatusId);
        }
        catch
        {
            return false;
        }
    }

    private bool TryValidateLocalPlaybackOwner(SkillCgRequest request, out string issuerPlayerId, out string rejection)
    {
        issuerPlayerId = ResolveLocalPlayerId();
        rejection = "";

        var playerManager = PlayerManager.Instance;
        if (playerManager == null || (!playerManager.isClient && !playerManager.isServer))
        {
            issuerPlayerId = string.IsNullOrWhiteSpace(issuerPlayerId) ? "solo" : issuerPlayerId;
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.OwnerInstanceId))
        {
            rejection = "owner instance id is empty in multiplayer. card=" + request.CardId;
            return false;
        }

        if (string.IsNullOrWhiteSpace(issuerPlayerId))
        {
            rejection = "issuer player id is empty. owner=" + request.OwnerInstanceId + ", card=" + request.CardId;
            return false;
        }

        var localStatusId = ResolveLocalStatusId();
        if (string.IsNullOrWhiteSpace(localStatusId))
        {
            rejection = "local status id is empty. owner=" + request.OwnerInstanceId + ", card=" + request.CardId;
            return false;
        }

        if (!string.Equals(request.OwnerInstanceId, localStatusId, StringComparison.Ordinal))
        {
            rejection = "remote owner observed. owner=" + request.OwnerInstanceId + ", local=" + localStatusId + ", card=" + request.CardId;
            return false;
        }

        return true;
    }

    private SkillCgPlaybackSnapshot CreatePlaybackSnapshot(
        string issuerPlayerId,
        string playId,
        SkillCgRequest first,
        IReadOnlyList<SkillCgRequest> requests)
    {
        return new SkillCgPlaybackSnapshot
        {
            IssuerPlayerId = issuerPlayerId ?? "",
            SkillCgPlayId = playId ?? "",
            OwnerStatusId = first.OwnerInstanceId,
            CardId = first.CardId,
            ActionSequence = first.ActionSequence,
            FightToken = session.FightToken,
            Events = requests.Select(ToNetworkEvent).ToList()
        };
    }

    private static SkillCgNetworkEvent ToNetworkEvent(SkillCgRequest request)
    {
        request.Normalize();
        return new SkillCgNetworkEvent
        {
            ProviderId = request.ProviderId,
            OwnerModId = request.OwnerModId,
            CgId = RegisteredCgId(request),
            CardId = request.CardId,
            OwnerInstanceId = request.OwnerInstanceId,
            ActionSequence = request.ActionSequence,
            EventToken = request.EventToken,
            IssuerPlayerId = request.IssuerPlayerId,
            SkillCgPlayId = request.SkillCgPlayId
        };
    }

    private static string RegisteredCgId(SkillCgRequest request)
    {
        var prefix = (request.OwnerModId ?? "").Trim() + ".SkillCG.";
        return !string.IsNullOrWhiteSpace(request.ProviderId)
               && request.ProviderId.StartsWith(prefix, StringComparison.Ordinal)
            ? request.ProviderId.Substring(prefix.Length)
            : "";
    }

    private bool TryClaimPlayback(string issuerPlayerId, string playId, string source)
    {
        if (!session.TryClaimPlayback(issuerPlayerId, playId, out var key))
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                AuraCgLog.DebugLog("Duplicate Skill CG playback ignored from " + source + ": " + key);
            }
            return false;
        }

        return true;
    }

    private static bool HasBoundedIdentifier(string? value)
    {
        return AuraCgNetworkPolicy.HasBoundedIdentifier(value, MaximumIdentifierLength);
    }

    private static string CreateFightToken()
    {
        return "cg-" + Guid.NewGuid().ToString("N");
    }

    private static string ResolveLocalPlayerId()
    {
        try
        {
            return (PlayerManager.Instance?.PlayerId ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveLocalStatusId()
    {
        try
        {
            return (FightPlayer.Instance?.Status?.InstanceId ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private static bool IsMultiplayerSession()
    {
        var manager = PlayerManager.Instance;
        if (manager != null && (manager.isClient || manager.isServer))
        {
            return true;
        }

        try
        {
            return (GameServer.Instance?.LobbyInfo?.AddedPlayers?.Count ?? 0) > 1;
        }
        catch
        {
            return false;
        }
    }
}
