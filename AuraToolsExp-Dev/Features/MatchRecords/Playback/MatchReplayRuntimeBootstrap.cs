using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayRuntimeBootstrapPhases
{
    internal const string Idle = "Idle";
    internal const string WaitingForRuntime = "WaitingForRuntime";
    internal const string Ready = "Ready";
    internal const string Failed = "Failed";
}

internal sealed class MatchReplayRuntimeReadiness
{
    internal bool ServerActive { get; set; }

    internal bool ClientActive { get; set; }

    internal bool ClientConnected { get; set; }

    internal bool ServerConnectionReady { get; set; }

    internal bool GameServerReady { get; set; }

    internal bool PlayerReady { get; set; }

    internal bool MapInstanceReady { get; set; }

    internal bool MapNetworkReady { get; set; }

    internal bool MapContextReady { get; set; }

    internal bool DiceReady { get; set; }

    internal bool RandomPoolReady { get; set; }

    internal bool FightReady { get; set; }

    internal bool RoleTableReady { get; set; }

    internal bool UiReady { get; set; }

    internal bool GameAppReady { get; set; }

    internal bool ChatUiReady { get; set; }

    internal bool IsReady => ServerActive
                             && ClientActive
                             && ClientConnected
                             && ServerConnectionReady
                             && GameServerReady
                             && PlayerReady
                             && MapInstanceReady
                             && MapNetworkReady
                             && MapContextReady
                             && DiceReady
                             && RandomPoolReady
                             && FightReady
                             && RoleTableReady
                             && UiReady
                             && GameAppReady
                             && ChatUiReady;

    internal string DescribeMissing()
    {
        var missing = new List<string>();
        if (!ServerActive) missing.Add("server");
        if (!ClientActive) missing.Add("client");
        if (!ClientConnected) missing.Add("client-connection");
        if (!ServerConnectionReady) missing.Add("server-connection");
        if (!GameServerReady) missing.Add("game-server");
        if (!PlayerReady) missing.Add("player");
        if (!MapInstanceReady)
        {
            missing.Add("map-instance");
        }
        else if (!MapNetworkReady)
        {
            missing.Add("map-network");
        }
        else
        {
            if (!MapContextReady) missing.Add("mode-context");
            if (!DiceReady) missing.Add("dice-state");
            if (!RandomPoolReady) missing.Add("random-pool");
        }
        if (!FightReady) missing.Add("fight-idle");
        if (!RoleTableReady) missing.Add("role-table");
        if (!UiReady) missing.Add("ui");
        if (!GameAppReady) missing.Add("game-app");
        if (!ChatUiReady) missing.Add("chat-ui");
        return missing.Count == 0 ? "none" : string.Join(",", missing);
    }

    internal string DescribeState()
    {
        return "server=" + ServerActive
               + ", client=" + ClientActive
               + ", connected=" + ClientConnected
               + ", serverConnection=" + ServerConnectionReady
               + ", gameServer=" + GameServerReady
               + ", player=" + PlayerReady
               + ", mapInstance=" + MapInstanceReady
               + ", mapNetwork=" + MapNetworkReady
               + ", modeContext=" + MapContextReady
               + ", dice=" + DiceReady
               + ", randomPool=" + RandomPoolReady
               + ", fight=" + FightReady
               + ", roleTable=" + RoleTableReady
               + ", ui=" + UiReady
               + ", gameApp=" + GameAppReady
               + ", chatUi=" + ChatUiReady;
    }
}

internal sealed class MatchReplayRuntimeBootstrap
{
    internal const int TimeoutMilliseconds = 15000;

    private int elapsedMilliseconds;
    private bool observedClientRuntime;

    internal string Phase { get; private set; } = MatchReplayRuntimeBootstrapPhases.Idle;

    internal string FailureCode { get; private set; } = "";

    internal string FailureMessage { get; private set; } = "";

    internal string MissingRuntime { get; private set; } = "none";

    internal bool Begin(
        bool serverActive,
        bool clientActive,
        bool lobbyManagerAvailable,
        out string message)
    {
        Reset();
        if (serverActive || clientActive)
        {
            return Fail(
                "network-session-active",
                "当前冒险或联机运行时仍在使用中，请先返回主菜单再开始回放。",
                out message);
        }

        if (!lobbyManagerAvailable)
        {
            return Fail(
                "lobby-manager-missing",
                "本地网络管理器尚未就绪，无法创建专用回放环境。",
                out message);
        }

        Phase = MatchReplayRuntimeBootstrapPhases.WaitingForRuntime;
        message = "正在创建专用本地回放环境……";
        return true;
    }

    internal void Advance(int elapsedDeltaMilliseconds, MatchReplayRuntimeReadiness readiness)
    {
        if (Phase != MatchReplayRuntimeBootstrapPhases.WaitingForRuntime)
        {
            return;
        }

        MissingRuntime = readiness.DescribeMissing();
        var clientWasObserved = observedClientRuntime;
        observedClientRuntime |= readiness.ClientActive || readiness.ClientConnected;
        if (clientWasObserved && !readiness.ClientActive && !readiness.ClientConnected)
        {
            Phase = MatchReplayRuntimeBootstrapPhases.Failed;
            FailureCode = "replay-host-disconnected";
            FailureMessage = "专用回放客户端在视图初始化完成前已断开（缺少："
                             + MissingRuntime + "）。";
            return;
        }

        if (readiness.IsReady)
        {
            Phase = MatchReplayRuntimeBootstrapPhases.Ready;
            MissingRuntime = "none";
            return;
        }

        elapsedMilliseconds += Math.Max(0, elapsedDeltaMilliseconds);
        if (elapsedMilliseconds < TimeoutMilliseconds)
        {
            return;
        }

        Phase = MatchReplayRuntimeBootstrapPhases.Failed;
        FailureCode = "runtime-timeout";
        FailureMessage = "专用回放环境启动超时（缺少：" + MissingRuntime + "）。";
    }

    internal void MarkHostStartFailed(string detail)
    {
        Phase = MatchReplayRuntimeBootstrapPhases.Failed;
        FailureCode = "host-start-failed";
        FailureMessage = "专用本地回放环境启动失败：" + detail;
    }

    internal void Reset()
    {
        elapsedMilliseconds = 0;
        observedClientRuntime = false;
        Phase = MatchReplayRuntimeBootstrapPhases.Idle;
        FailureCode = "";
        FailureMessage = "";
        MissingRuntime = "none";
    }

    private bool Fail(string code, string detail, out string message)
    {
        Phase = MatchReplayRuntimeBootstrapPhases.Failed;
        FailureCode = code;
        FailureMessage = detail;
        message = detail;
        return false;
    }
}
