namespace AuraShared.Core;

public sealed class AuraRpcSender
{
    public static readonly AuraRpcSender Unbound = new("", "", false, false, "", false);

    public AuraRpcSender(
        string playerId,
        string playerName,
        bool isLobbyMember,
        bool isLobbyHost,
        string sourceHook,
        bool isAvailable)
        : this(playerId, playerName, isLobbyMember, isLobbyHost, sourceHook, isAvailable, -1)
    {
    }

    public AuraRpcSender(string playerId, string playerName, bool isLobbyMember, bool isLobbyHost, string sourceHook, bool isAvailable, int connectionId)
    {
        ConnectionId = connectionId;
        PlayerId = (playerId ?? "").Trim();
        PlayerName = (playerName ?? "").Trim();
        IsLobbyMember = isLobbyMember;
        IsLobbyHost = isLobbyHost;
        SourceHook = (sourceHook ?? "").Trim();
        IsAvailable = isAvailable && PlayerId.Length > 0;
    }

    public string PlayerId { get; }
    public int ConnectionId { get; }

    public string PlayerName { get; }

    public bool IsLobbyMember { get; }

    public bool IsLobbyHost { get; }

    public string SourceHook { get; }

    public bool IsAvailable { get; }
}
