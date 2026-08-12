using System;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Mirror;
using UnityEngine;
using Witch.UI;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayLocalHostRuntime
{
    private static LobbyManager? owner;
    private static MultiplexTransport? multiplexTransport;
    private static Transport[]? previousMultiplexTransports;
    private static Transport? previousActiveTransport;

    internal static bool OwnsHost => !ReferenceEquals(owner, null);

    internal static bool CanStart => !NetworkServer.active
                                     && !NetworkClient.active
                                     && LobbyManager.Instance != null;

    internal static void Start()
    {
        if (!CanStart)
        {
            throw new InvalidOperationException(
                "A replay host can only be created from an idle main-menu network state.");
        }

        owner = LobbyManager.Instance;
        previousActiveTransport = Transport.active;
        multiplexTransport = GameObject.Find("Network Manager")?.GetComponent<MultiplexTransport>();
        previousMultiplexTransports = multiplexTransport?.transports?.ToArray();
        try
        {
            owner.StartLocalHost();
        }
        catch
        {
            Stop();
            throw;
        }

        AuraToolsLog.Info("[MatchRecords] replay local host requested: transport=KCP.");
    }

    internal static MatchReplayRuntimeReadiness CaptureReadiness()
    {
        var gameServer = GameServer.Instance;
        var player = PlayerManager.Instance;
        var map = MapManager.Instance;
        var fight = FightManager.Instance;
        var mapInstanceReady = map != null;
        var mapNetworkReady = map != null
                              && map.isServer
                              && map.isClient
                              && map.netId != 0;
        var mapContextReady = mapNetworkReady
                              && MatchReplayEnvironmentScope.TryInstallMapContext(map);
        return new MatchReplayRuntimeReadiness
        {
            ServerActive = NetworkServer.active,
            ClientActive = NetworkClient.active,
            ClientConnected = NetworkClient.isConnected,
            ServerConnectionReady = NetworkServer.connections.Values.Any(
                connection => connection?.identity != null),
            GameServerReady = gameServer != null
                              && gameServer.isServer
                              && gameServer.isClient
                              && gameServer.netId != 0
                              && gameServer.LobbyInfo != null,
            PlayerReady = player != null
                          && player.isServer
                          && player.isClient
                          && player.isLocalPlayer
                          && player.netId != 0
                          && !string.IsNullOrWhiteSpace(player.PlayerId),
            MapInstanceReady = mapInstanceReady,
            MapNetworkReady = mapNetworkReady,
            MapContextReady = mapContextReady
                              && MatchReplayEnvironmentScope.IsMapContextInstalled(map),
            DiceReady = mapContextReady && map?.ModeMapManager?.NowDice != null,
            RandomPoolReady = MatchReplayEnvironmentScope.IsRandomPoolReady,
            FightReady = fight != null
                         && fight.isServer
                         && fight.isClient
                         && fight.netId != 0
                         && fight.fightType == FightType.None,
            RoleTableReady = RoleTable.Instance != null,
            UiReady = UIManager.Instance != null,
            GameAppReady = GameApp.Instance != null
        };
    }

    internal static void BindReplayIdentity(string replayPlayerId)
    {
        var normalized = (replayPlayerId ?? "").Trim();
        var player = PlayerManager.Instance;
        if (string.IsNullOrWhiteSpace(normalized) || player?.playerInfo == null)
        {
            throw new InvalidOperationException(
                "The replay player identity is missing and cannot be bound to the local host.");
        }

        player.playerInfo.Id = normalized;
        var lobbyPlayer = GameServer.Instance?.LobbyInfo?.AddedPlayers?
            .FirstOrDefault(item => item?.Connection?.identity == player.netIdentity);
        if (lobbyPlayer != null)
        {
            lobbyPlayer.Id = normalized;
        }

        AuraToolsLog.Debug("[MatchRecords] replay local host identity rebound to recorded player.");
    }

    internal static void Stop()
    {
        var owned = owner;
        if (ReferenceEquals(owned, null))
        {
            return;
        }

        owner = null;
        try
        {
            if (owned != null && (NetworkServer.active || NetworkClient.active))
            {
                var tempData = Singleton<TempDataManager>.Instance;
                var previousGameOver = tempData.GameOver;
                try
                {
                    // LobbyManager normally returns to the menu on disconnect. The replay host
                    // was created at the menu, so suppress that destructive navigation callback.
                    tempData.GameOver = true;
                    owned.StopHost();
                }
                finally
                {
                    tempData.GameOver = previousGameOver;
                }
            }
        }
        finally
        {
            if (multiplexTransport != null && previousMultiplexTransports != null)
            {
                multiplexTransport.transports = previousMultiplexTransports;
            }

            Transport.active = previousActiveTransport;
            multiplexTransport = null;
            previousMultiplexTransports = null;
            previousActiveTransport = null;
        }

        AuraToolsLog.Info("[MatchRecords] replay local host stopped; previous transport restored.");
    }
}
