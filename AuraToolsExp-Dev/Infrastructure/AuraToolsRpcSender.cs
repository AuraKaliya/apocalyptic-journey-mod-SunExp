namespace AuraToolsExp.Dll.Infrastructure;

public sealed class AuraToolsRpcSender
{
    public static readonly AuraToolsRpcSender Unbound = new("", "", false, false, "", false);

    public AuraToolsRpcSender(
        string playerId,
        string playerName,
        bool isLobbyMember,
        bool isLobbyHost,
        string sourceHook,
        bool isAvailable)
    {
        PlayerId = (playerId ?? "").Trim();
        PlayerName = (playerName ?? "").Trim();
        IsLobbyMember = isLobbyMember;
        IsLobbyHost = isLobbyHost;
        SourceHook = (sourceHook ?? "").Trim();
        IsAvailable = isAvailable && PlayerId.Length > 0;
    }

    public string PlayerId { get; }

    public string PlayerName { get; }

    public bool IsLobbyMember { get; }

    public bool IsLobbyHost { get; }

    public string SourceHook { get; }

    public bool IsAvailable { get; }
}

public interface IAuraToolsServerBoundRpcCommand
{
    void BindServerSender(AuraToolsRpcSender sender);
}
