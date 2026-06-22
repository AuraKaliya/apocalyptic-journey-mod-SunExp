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
        ContentKind = string.Empty;
        ContentId = string.Empty;
        CatalogHash = string.Empty;
        SenderId = string.Empty;
        RejectionReason = string.Empty;
    }

    public AuraChatSubmitCommand(string contentKind, string contentId, string catalogHash, string senderId)
    {
        ContentKind = contentKind ?? string.Empty;
        ContentId = contentId ?? string.Empty;
        CatalogHash = catalogHash ?? string.Empty;
        SenderId = senderId ?? string.Empty;
        RejectionReason = string.Empty;
    }

    public string ContentKind { get; set; }

    public string ContentId { get; set; }

    public string CatalogHash { get; set; }

    public string SenderId { get; set; }

    public AuraChatMessage? ConfirmedMessage { get; set; }

    public string RejectionReason { get; set; }

    public override void CmdExecute()
    {
        ConfirmedMessage = null;
        RejectionReason = string.Empty;

        if (!AuraChatCatalogStore.TryResolveContent(ContentKind, ContentId, CatalogHash, out _, out var reason))
        {
            Reject(reason);
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

        ConfirmedMessage = AuraChatRuntime.ConfirmCatalogMessage(player.Id, player.Name, ContentKind, ContentId);
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
