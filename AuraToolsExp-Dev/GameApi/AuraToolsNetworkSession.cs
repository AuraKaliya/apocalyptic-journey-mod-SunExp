using System;
using System.Linq;

namespace AuraToolsExp.Dll.GameApi;

// A native local-server session also exists in single player. It does not
// imply that there is a remote recipient, and peer count does not grant authority.
internal static class AuraToolsNetworkSession
{
    internal static bool NetworkActive => PlayerManager.Instance != null;
    internal static bool IsAuthority => !NetworkActive || PlayerManager.Instance?.isServer == true;
    internal static string LocalPlayerId => PlayerManager.Instance?.PlayerId ?? "single-player";
    internal static string[] PlayerIds => (IsAuthority
            ? GameServer.Instance?.LobbyInfo?.AddedPlayers
            : PlayerManager.Instance?.LobbyInfos?.AddedPlayers)?
        .Where(player => player != null && !string.IsNullOrWhiteSpace(player.Id))
        .Select(player => player.Id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
    internal static bool HasRemotePeers => NetworkActive && !string.IsNullOrWhiteSpace(LocalPlayerId) && PlayerIds.Any(id => !string.Equals(id, LocalPlayerId, StringComparison.OrdinalIgnoreCase));
}
