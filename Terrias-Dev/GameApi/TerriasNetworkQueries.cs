using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.GameApi;

// Host transport existence, authority and recipients are independent facts.
public static class TerriasNetworkQueries
{
    public static bool NetworkActive() => PlayerManager.Instance != null;
    public static bool IsClientOnly() => PlayerManager.Instance != null && PlayerManager.Instance.isClient && !PlayerManager.Instance.isServer;
    public static bool IsServer() => PlayerManager.Instance?.isServer == true;
    public static string LocalPlayerId() => (PlayerManager.Instance?.PlayerId ?? "").Trim();
    public static bool IsLocalPlayer(string playerId) => LocalPlayerId().Length > 0 && string.Equals(LocalPlayerId(), (playerId ?? "").Trim(), StringComparison.Ordinal);
    public static IReadOnlyList<string> LobbyPlayerIds() => (IsServer()
            ? GameServer.Instance?.LobbyInfo?.AddedPlayers
            : PlayerManager.Instance?.LobbyInfos?.AddedPlayers)?
        .Where(player => player != null && !string.IsNullOrWhiteSpace(player.Id))
        .Select(player => player.Id.Trim()).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
    public static bool HasRemotePlayers() => NetworkActive() && LocalPlayerId().Length > 0 && LobbyPlayerIds().Any(id => !string.Equals(id, LocalPlayerId(), StringComparison.Ordinal));
}
