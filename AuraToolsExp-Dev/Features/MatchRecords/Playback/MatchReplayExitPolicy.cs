namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal enum MatchReplayExitPhases
{
    Idle,
    ClosingTransientUi,
    StoppingNetwork,
    ReturningToMenu,
    VerifyingMenu,
    RebuildingMenuCaches,
    MenuCacheFailed,
    Ready
}

internal sealed class MatchReplayNetworkTeardownState
{
    internal bool ServerActive { get; set; }

    internal bool ClientActive { get; set; }

    internal bool ClientConnected { get; set; }

    internal bool NetworkManagerOffline { get; set; }

    internal int ServerConnectionCount { get; set; }

    internal int ServerSpawnedCount { get; set; }

    internal int ClientSpawnedCount { get; set; }

    internal bool ClientConnectionPresent { get; set; }

    internal bool GameServerNetworkActive { get; set; }

    internal bool PlayerNetworkActive { get; set; }

    internal bool MapNetworkActive { get; set; }

    internal bool FightNetworkActive { get; set; }
}

internal sealed class MatchReplayMenuRestorationState
{
    internal bool NativeReturnRequested { get; set; }

    internal bool ExpectedHouseActive { get; set; }

    internal bool HouseActive { get; set; }

    internal bool ReplayBackgroundAlive { get; set; }

    internal int ResidualReplayUiCount { get; set; }

    internal int SettingUiCount { get; set; }

    internal bool ChatUiClosing { get; set; }

    internal bool InputInfrastructureReady { get; set; }
}

internal sealed class MatchReplayMenuCacheState
{
    internal int SettingUiCount { get; set; }

    internal bool Registered { get; set; }

    internal bool RegisteredMatchesOnlyInstance { get; set; }

    internal bool ActiveSelf { get; set; }

    internal bool BlocksRaycasts { get; set; }

    internal bool ParentIsMainCanvas { get; set; }

    internal bool InputInfrastructureReady { get; set; }
}

internal static class MatchReplayExitPolicy
{
    internal static bool IsNetworkTeardownReady(MatchReplayNetworkTeardownState? state)
    {
        return state != null
               && IsTransportQuiescent(state)
               && state.NetworkManagerOffline
               && !state.GameServerNetworkActive
               && !state.PlayerNetworkActive
               && !state.MapNetworkActive
               && !state.FightNetworkActive;
    }

    internal static bool IsTransportQuiescent(MatchReplayNetworkTeardownState? state)
    {
        return state != null
               && !state.ServerActive
               && !state.ClientActive
               && !state.ClientConnected
               && state.ServerConnectionCount == 0
               && state.ServerSpawnedCount == 0
               && state.ClientSpawnedCount == 0
               && !state.ClientConnectionPresent;
    }

    internal static bool IsMenuRestorationReady(MatchReplayMenuRestorationState? state)
    {
        return state != null
               && state.NativeReturnRequested
               && state.HouseActive == state.ExpectedHouseActive
               && !state.ReplayBackgroundAlive
               && state.ResidualReplayUiCount == 0
               && state.SettingUiCount == 0
               && !state.ChatUiClosing
               && state.InputInfrastructureReady;
    }

    internal static bool IsMenuCacheReady(MatchReplayMenuCacheState? state)
    {
        return state != null
               && state.SettingUiCount == 1
               && state.Registered
               && state.RegisteredMatchesOnlyInstance
               && !state.ActiveSelf
               && !state.BlocksRaycasts
               && state.ParentIsMainCanvas
               && state.InputInfrastructureReady;
    }

    internal static bool CanStartReplay(
        bool exitInProgress,
        bool ownsReplayHost,
        bool serverActive,
        bool clientActive)
    {
        return !exitInProgress
               && !ownsReplayHost
               && !serverActive
               && !clientActive;
    }

    internal static bool CanWaitForUiBeforeNetworkStop(bool replayNativePresentationOwned)
    {
        // FightUI and the other native presentation roots are still owned by
        // Mirror/game shutdown at this point. Only transient tool/origin roots
        // may be awaited or force-cleaned before the local host is offline.
        return !replayNativePresentationOwned;
    }
}
