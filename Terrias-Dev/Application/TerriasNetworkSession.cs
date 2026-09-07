using System.Collections.Generic;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Application;

// Network adapters consume the same host session facts as gameplay callers.
public static class TerriasNetworkSession
{
    public static bool NetworkActive() => TerriasNetworkQueries.NetworkActive();
    public static bool IsClientOnly() => TerriasNetworkQueries.IsClientOnly();
    public static bool IsServer() => TerriasNetworkQueries.IsServer();
    public static bool HasRemotePlayers() => TerriasNetworkQueries.HasRemotePlayers();
    public static string LocalPlayerId() => TerriasNetworkQueries.LocalPlayerId();
    public static bool IsLocalPlayer(string id) => TerriasNetworkQueries.IsLocalPlayer(id);
    public static IReadOnlyList<string> LobbyPlayerIds() => TerriasNetworkQueries.LobbyPlayerIds();
}
