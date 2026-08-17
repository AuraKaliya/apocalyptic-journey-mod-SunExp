using System;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Mirror;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayLocalHostRuntime
{
    private static LobbyManager? owner;
    private static MultiplexTransport? multiplexTransport;
    private static Transport[]? previousMultiplexTransports;
    private static Transport? previousActiveTransport;
    private static bool stopping;
    private static bool quitRequested;
    private static bool previousGameOver;
    private static bool gameOverCaptured;
    private static int hostGeneration;
    private static string baselineRuntimeIdentities = "unavailable";

    internal static bool OwnsHost => !ReferenceEquals(owner, null) || stopping;

    internal static bool IsStopped => MatchReplayExitPolicy.IsNetworkTeardownReady(
        CaptureTeardownState());

    internal static bool IsTransportQuiescent => MatchReplayExitPolicy.IsTransportQuiescent(
        CaptureTeardownState());

    internal static bool CanStart => MatchReplayExitPolicy.CanStartReplay(
                                         MatchReplaySessionState.IsExiting,
                                         OwnsHost,
                                         NetworkServer.active,
                                         NetworkClient.active)
                                     && LobbyManager.Instance != null
                                     && !NetworkClient.isConnected;

    internal static void Start()
    {
        if (!CanStart)
        {
            throw new InvalidOperationException(
                "A replay host can only be created from an idle main-menu network state.");
        }

        owner = LobbyManager.Instance;
        stopping = false;
        quitRequested = false;
        gameOverCaptured = false;
        hostGeneration++;
        baselineRuntimeIdentities = DescribeRuntimeIdentities();
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

        AuraToolsLog.Info("[MatchRecords] replay local host requested: generation="
                          + hostGeneration + ", transport=KCP, baseline="
                          + baselineRuntimeIdentities + ".");
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
            GameAppReady = GameApp.Instance != null,
            ChatUiReady = MatchReplayChatUiLeaseRuntime.IsNativeChatReady
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
        if (ReferenceEquals(owned, null) && !stopping)
        {
            return;
        }

        stopping = true;
        if (owned != null && (NetworkServer.active || NetworkClient.active))
        {
            var tempData = Singleton<TempDataManager>.Instance;
            if (!gameOverCaptured)
            {
                previousGameOver = tempData.GameOver;
                gameOverCaptured = true;
            }

            // Keep this flag until Mirror reports both sides inactive. Disconnect callbacks
            // may arrive after StopHost returns, and must not navigate the menu mid-handoff.
            tempData.GameOver = true;
            if (!quitRequested)
            {
                quitRequested = true;
                // Use the game's complete lobby-exit path so role caches, lobby mode,
                // player rows, and the local host are released as one native lifecycle.
                owned.QuitLobby();
            }
            else
            {
                // A bounded lifecycle retry only needs to prod Mirror; repeating the
                // full lobby exit would repeat Steam/UI side effects unnecessarily.
                owned.StopHost();
            }
        }

        AuraToolsLog.Debug("[MatchRecords] replay local host stop requested; awaiting end-of-frame network teardown.");
    }

    internal static void ForceStop()
    {
        stopping = true;
        try
        {
            owner?.StopHost();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay host force-stop degraded: " + ex.Message);
        }

        try
        {
            if (NetworkClient.active
                || NetworkClient.isConnected
                || NetworkClient.connection != null
                || NetworkClient.spawned.Count != 0)
            {
                NetworkClient.Shutdown();
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay client shutdown degraded: " + ex.Message);
        }

        try
        {
            if (NetworkServer.active
                || NetworkServer.connections.Count != 0
                || NetworkServer.spawned.Count != 0)
            {
                NetworkServer.Shutdown();
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay server shutdown degraded: " + ex.Message);
        }

        AuraToolsLog.Warn("[MatchRecords] replay local host force-stop issued: generation="
                          + hostGeneration + ", state=" + DescribeTeardownState() + ".");
    }

    internal static void CompleteStop(bool allowTransportOnly = false)
    {
        if (!OwnsHost && multiplexTransport == null && previousMultiplexTransports == null)
        {
            return;
        }

        if (!IsStopped && !(allowTransportOnly && IsTransportQuiescent))
        {
            throw new InvalidOperationException("Replay local host teardown is not complete.");
        }

        try
        {
            if (multiplexTransport != null && previousMultiplexTransports != null)
            {
                multiplexTransport.transports = previousMultiplexTransports;
            }

            Transport.active = previousActiveTransport;
        }
        finally
        {
            owner = null;
            stopping = false;
            quitRequested = false;
            if (gameOverCaptured)
            {
                Singleton<TempDataManager>.Instance.GameOver = previousGameOver;
            }

            previousGameOver = false;
            gameOverCaptured = false;
            multiplexTransport = null;
            previousMultiplexTransports = null;
            previousActiveTransport = null;
        }

        AuraToolsLog.Info("[MatchRecords] replay local host stopped: generation="
                          + hostGeneration + ", transportOnly=" + (!IsStopped)
                          + ", previousTransportRestored=True, baseline="
                          + baselineRuntimeIdentities + ", terminal="
                          + DescribeRuntimeIdentities() + ".");
    }

    internal static MatchReplayNetworkTeardownState CaptureTeardownState()
    {
        var manager = NetworkManager.singleton;
        return new MatchReplayNetworkTeardownState
        {
            ServerActive = NetworkServer.active,
            ClientActive = NetworkClient.active,
            ClientConnected = NetworkClient.isConnected,
            NetworkManagerOffline = manager == null || manager.mode == NetworkManagerMode.Offline,
            ServerConnectionCount = NetworkServer.connections?.Count ?? 0,
            ServerSpawnedCount = NetworkServer.spawned?.Count ?? 0,
            ClientSpawnedCount = NetworkClient.spawned?.Count ?? 0,
            ClientConnectionPresent = NetworkClient.connection != null,
            GameServerNetworkActive = IsNetworkActive(GameServer.Instance),
            PlayerNetworkActive = IsNetworkActive(PlayerManager.Instance),
            MapNetworkActive = IsNetworkActive(MapManager.Instance),
            FightNetworkActive = IsNetworkActive(FightManager.Instance)
        };
    }

    internal static string DescribeTeardownState()
    {
        var state = CaptureTeardownState();
        return "server=" + state.ServerActive
               + ",client=" + state.ClientActive
               + ",connected=" + state.ClientConnected
               + ",modeOffline=" + state.NetworkManagerOffline
               + ",serverConnections=" + state.ServerConnectionCount
               + ",serverSpawned=" + state.ServerSpawnedCount
               + ",clientSpawned=" + state.ClientSpawnedCount
               + ",clientConnection=" + state.ClientConnectionPresent
               + ",gameServerNetwork=" + state.GameServerNetworkActive
               + ",playerNetwork=" + state.PlayerNetworkActive
               + ",mapNetwork=" + state.MapNetworkActive
               + ",fightNetwork=" + state.FightNetworkActive
               + ",identities=" + DescribeRuntimeIdentities();
    }

    internal static string DescribeRuntimeIdentities()
    {
        return DescribeNetworkObject("gameServer", GameServer.Instance)
               + "|" + DescribeNetworkObject("player", PlayerManager.Instance)
               + "|" + DescribeNetworkObject("map", MapManager.Instance)
               + "|" + DescribeNetworkObject("fight", FightManager.Instance);
    }

    private static bool IsNetworkActive(NetworkBehaviour? target)
    {
        return target != null
               && (target.isServer || target.isClient || target.netId != 0);
    }

    private static string DescribeNetworkObject(string label, NetworkBehaviour? target)
    {
        if (target == null)
        {
            return label + "=none";
        }

        try
        {
            return label + "=#" + target.GetInstanceID()
                   + ":active=" + target.gameObject.activeInHierarchy
                   + ":server=" + target.isServer
                   + ":client=" + target.isClient
                   + ":netId=" + target.netId;
        }
        catch (Exception ex)
        {
            return label + "=unreadable:" + ex.GetType().Name;
        }
    }
}
