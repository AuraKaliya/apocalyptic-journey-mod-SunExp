using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal readonly struct AuraCgNetworkSenderSnapshot
{
    public AuraCgNetworkSenderSnapshot(
        bool isAvailable,
        bool isLobbyMember,
        string playerId)
    {
        IsAvailable = isAvailable;
        IsLobbyMember = isLobbyMember;
        PlayerId = (playerId ?? "").Trim();
    }

    public bool IsAvailable { get; }

    public bool IsLobbyMember { get; }

    public string PlayerId { get; }
}

internal static class AuraCgNetworkPolicy
{
    public static bool HasBoundedIdentifier(string? value, int maximumLength)
    {
        var text = (value ?? "").Trim();
        return text.Length > 0 && text.Length <= maximumLength;
    }

    public static bool HasValidEventIdentity(SkillCgNetworkEvent? item, int maximumIdentifierLength)
    {
        return item != null
               && HasBoundedIdentifier(item.OwnerModId, maximumIdentifierLength)
               && HasBoundedIdentifier(item.CgId, maximumIdentifierLength)
               && HasBoundedIdentifier(item.ProviderId, maximumIdentifierLength)
               && HasBoundedIdentifier(item.CardId, maximumIdentifierLength)
               && (string.Equals(item.TriggerKind, "skill", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(item.TriggerKind, "card", StringComparison.OrdinalIgnoreCase))
               && HasBoundedIdentifier(item.OwnerInstanceId, maximumIdentifierLength);
    }

    public static bool HasValidPlaybackShape(
        SkillCgPlaybackSnapshot? playback,
        int maximumEvents,
        int maximumIdentifierLength)
    {
        return playback != null
               && playback.Events != null
               && playback.Events.Count > 0
               && playback.Events.Count <= maximumEvents
               && HasBoundedIdentifier(playback.IssuerPlayerId, maximumIdentifierLength)
               && HasBoundedIdentifier(playback.SkillCgPlayId, maximumIdentifierLength)
               && HasBoundedIdentifier(playback.OwnerStatusId, maximumIdentifierLength)
               && HasBoundedIdentifier(playback.CardId, maximumIdentifierLength)
               && HasBoundedIdentifier(playback.FightToken, maximumIdentifierLength);
    }

    public static void NormalizePlaybackSnapshot(SkillCgPlaybackSnapshot playback)
    {
        playback.IssuerPlayerId = (playback.IssuerPlayerId ?? "").Trim();
        playback.SkillCgPlayId = (playback.SkillCgPlayId ?? "").Trim();
        playback.OwnerStatusId = (playback.OwnerStatusId ?? "").Trim();
        playback.CardId = (playback.CardId ?? "").Trim();
        playback.FightToken = (playback.FightToken ?? "").Trim();

        foreach (var item in playback.Events ?? new List<SkillCgNetworkEvent>())
        {
            item.IssuerPlayerId = playback.IssuerPlayerId;
            item.SkillCgPlayId = playback.SkillCgPlayId;
            item.OwnerInstanceId = playback.OwnerStatusId;
            item.CardId = string.IsNullOrWhiteSpace(item.CardId) ? playback.CardId : item.CardId;
            item.TriggerKind = (item.TriggerKind ?? "").Trim().ToLowerInvariant();
            item.EventToken = playback.SkillCgPlayId;
            item.ActionSequence = playback.ActionSequence;
        }
    }

    public static string PlaybackKey(string issuerPlayerId, string playId)
    {
        issuerPlayerId = (issuerPlayerId ?? "").Trim();
        playId = (playId ?? "").Trim();
        return string.IsNullOrWhiteSpace(issuerPlayerId) || string.IsNullOrWhiteSpace(playId)
            ? ""
            : issuerPlayerId + "|" + playId;
    }

    public static string ValidateServerPlaybackIdentity(
        SkillCgPlaybackSnapshot? playback,
        AuraCgNetworkSenderSnapshot sender,
        bool isMultiplayer,
        Func<string, string, bool> senderOwnsStatus)
    {
        if (playback == null)
        {
            return "missing payload";
        }

        if (!sender.IsAvailable)
        {
            return isMultiplayer ? "missing sender" : "";
        }

        if (!sender.IsLobbyMember)
        {
            return "sender outside lobby: " + sender.PlayerId;
        }

        if (!string.IsNullOrWhiteSpace(playback.IssuerPlayerId)
            && !string.Equals(
                playback.IssuerPlayerId,
                sender.PlayerId,
                StringComparison.Ordinal))
        {
            return "issuer mismatch: issuer=" + playback.IssuerPlayerId
                   + ", sender=" + sender.PlayerId;
        }

        if (string.IsNullOrWhiteSpace(playback.OwnerStatusId))
        {
            return "missing owner status";
        }

        if (!senderOwnsStatus(sender.PlayerId, playback.OwnerStatusId))
        {
            return "owner mismatch: owner=" + playback.OwnerStatusId
                   + ", sender=" + sender.PlayerId;
        }

        return "";
    }
}
