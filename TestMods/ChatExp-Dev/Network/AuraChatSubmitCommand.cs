using System;
using System.Linq;
using AuraOnline.Shared;
using ChatExp.Dll.Infrastructure;
using Network.Command;

namespace ChatExp.Dll.Network;

[Serializable]
public sealed class AuraChatSubmitCommand : RpcCommandBase
{
    private static readonly AuraChatRateLimiter Limiter = new AuraChatRateLimiter();

    public AuraChatSubmitCommand()
    {
        RawText = string.Empty;
        SenderId = string.Empty;
        ClientMessageId = string.Empty;
        RejectionReason = string.Empty;
    }

    public AuraChatSubmitCommand(string rawText, string senderId, string clientMessageId)
    {
        RawText = rawText ?? string.Empty;
        SenderId = senderId ?? string.Empty;
        ClientMessageId = clientMessageId ?? string.Empty;
        RejectionReason = string.Empty;
    }

    public string RawText { get; set; }

    public string SenderId { get; set; }

    public string ClientMessageId { get; set; }

    public AuraChatMessage? ConfirmedMessage { get; set; }

    public string RejectionReason { get; set; }

    public override void CmdExecute()
    {
        ConfirmedMessage = null;
        RejectionReason = string.Empty;

        var safeText = AuraChatTextLimiter.LimitPlayerText(RawText);
        if (string.IsNullOrWhiteSpace(safeText))
        {
            Reject("empty message");
            return;
        }

        if (!Limiter.Allow(SenderId, DateTime.UtcNow))
        {
            Reject("rate limited");
            return;
        }

        var player = FindLobbyPlayer(SenderId);
        if (player == null)
        {
            Reject("sender not found");
            return;
        }

        ConfirmedMessage = AuraChatRuntime.ConfirmPlayerMessage(player.Id, player.Name, safeText);
    }

    public override void RpcExecute()
    {
        if (ConfirmedMessage != null)
        {
            AuraChatRuntime.Receive(ConfirmedMessage);
        }
    }

    private static LobbyInfo.PlayerInfo? FindLobbyPlayer(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId) || GameServer.Instance?.LobbyInfo?.AddedPlayers == null)
        {
            return null;
        }

        return GameServer.Instance.LobbyInfo.AddedPlayers.FirstOrDefault(player => player != null && player.Id == playerId);
    }

    private void Reject(string reason)
    {
        RejectionReason = reason;
        ChatExpLog.Warn("Chat message rejected: " + reason);
    }
}
