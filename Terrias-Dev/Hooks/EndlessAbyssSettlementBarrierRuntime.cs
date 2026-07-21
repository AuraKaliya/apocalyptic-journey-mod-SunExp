using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class EndlessAbyssSettlementBarrierRuntime
{
    private const int HostWaitSeconds = 15;
    private const int ForcedCommitGraceSeconds = 2;
    private static readonly HashSet<string> ExpectedRemotePlayers = new(StringComparer.Ordinal);
    private static readonly HashSet<string> CommittedPlayers = new(StringComparer.Ordinal);
    private static readonly HashSet<string> AppliedEvents = new(StringComparer.Ordinal);

    private static string settlementToken = "";
    private static string pendingLocalCommitToken = "";
    private static bool hostReady;
    private static bool forceCommitSent;
    private static bool closingSent;
    private static bool hostCloseScheduled;
    private static long hostDeadlineUtcTicks;
    private static long forcedCommitDeadlineUtcTicks;
    private static GameExitUI? settlementUi;
    private static EndlessAbyssSettlementBarrierView? view;

    public static string SettlementToken => settlementToken;
    public static bool HostReady => hostReady;
    public static bool Closing => closingSent;
    public static int ExpectedRemoteCount => ExpectedRemotePlayers.Count;
    public static int CommittedRemoteCount => ExpectedRemotePlayers.Count(CommittedPlayers.Contains);
    public static long DeadlineUtcTicks => forceCommitSent ? forcedCommitDeadlineUtcTicks : hostDeadlineUtcTicks;

    public static void Prepare(EndlessAbyssEvacuationResolution resolution)
    {
        if (resolution?.IsValid != true)
        {
            return;
        }

        if (!string.Equals(settlementToken, resolution.Token, StringComparison.Ordinal))
        {
            Reset(resolution.Token);
        }

        if (!TerriasNetworkRuntime.IsClientOnly())
        {
            RefreshExpectedRemotePlayers();
        }
    }

    public static void Attach(GameExitUI ui, EndlessAbyssEvacuationResolution resolution)
    {
        if (ui == null || resolution?.IsValid != true)
        {
            return;
        }

        Prepare(resolution);
        settlementUi = ui;
        view = ui.GetComponent<EndlessAbyssSettlementBarrierView>()
               ?? ui.gameObject.AddComponent<EndlessAbyssSettlementBarrierView>();
        view.Bind(ui, resolution.Token);
    }

    public static void MarkHostReady()
    {
        if (TerriasNetworkRuntime.IsClientOnly() || string.IsNullOrWhiteSpace(settlementToken) || hostReady)
        {
            return;
        }

        if (!TerriasNetworkRuntime.IsMultiplayerSession() || !TerriasNetworkRuntime.HasRemotePlayers())
        {
            hostReady = true;
            BeginClosing("no-remote-players");
            view?.Refresh();
            return;
        }

        var deadline = DateTime.UtcNow.AddSeconds(HostWaitSeconds).Ticks;
        EndlessAbyssSettlementBarrierNetworkSync.BroadcastHostEvent(
            EndlessAbyssSettlementBarrierEventKinds.HostReady,
            settlementToken,
            deadline,
            "HostReady");
    }

    public static void CommitLocalPlayer(GameExitUI ui, string source)
    {
        if (ui == null
            || string.IsNullOrWhiteSpace(settlementToken)
            || !TerriasNetworkRuntime.IsClientOnly()
            || !string.IsNullOrWhiteSpace(pendingLocalCommitToken))
        {
            return;
        }

        pendingLocalCommitToken = settlementToken;
        MarkExpectedDisconnect();
        TerriasLog.Info("[EndlessAbyssSettlement] local commit entered: token="
                        + settlementToken
                        + ", source="
                        + source
                        + ".");
        ui.ReturnAsync();
    }

    public static void ObserveReturnStarting()
    {
        if (string.IsNullOrWhiteSpace(settlementToken))
        {
            return;
        }

        pendingLocalCommitToken = settlementToken;
    }

    public static void ObserveReturnCompleted()
    {
        var token = pendingLocalCommitToken;
        pendingLocalCommitToken = "";
        if (string.IsNullOrWhiteSpace(token) || !TerriasNetworkRuntime.IsClientOnly())
        {
            return;
        }

        if (!EndlessAbyssSettlementBarrierNetworkSync.SendPlayerCommitted(token))
        {
            TerriasLog.Warn("[EndlessAbyssSettlement] local commit ACK send failed: token=" + token + ".");
            return;
        }

        TerriasLog.Info("[EndlessAbyssSettlement] local commit ACK sent: token=" + token + ".");
    }

    public static void ApplyAuthoritativeEvent(RpcEndlessAbyssSettlementBarrier command, string source)
    {
        if (command == null
            || !command.Accepted
            || string.IsNullOrWhiteSpace(command.SettlementToken)
            || !string.Equals(command.SettlementToken, settlementToken, StringComparison.Ordinal))
        {
            return;
        }

        var eventKey = command.SettlementToken
                       + "|"
                       + command.EventKind
                       + "|"
                       + command.PlayerId
                       + "|"
                       + command.CommandToken;
        if (!AppliedEvents.Add(eventKey))
        {
            return;
        }

        switch (command.EventKind)
        {
            case EndlessAbyssSettlementBarrierEventKinds.PlayerCommitted:
                if (!string.IsNullOrWhiteSpace(command.PlayerId))
                {
                    CommittedPlayers.Add(command.PlayerId);
                }
                break;
            case EndlessAbyssSettlementBarrierEventKinds.HostReady:
                hostReady = true;
                hostDeadlineUtcTicks = command.DeadlineUtcTicks > 0
                    ? command.DeadlineUtcTicks
                    : DateTime.UtcNow.AddSeconds(HostWaitSeconds).Ticks;
                break;
            case EndlessAbyssSettlementBarrierEventKinds.ForceCommit:
                hostReady = true;
                forceCommitSent = true;
                forcedCommitDeadlineUtcTicks = command.DeadlineUtcTicks > 0
                    ? command.DeadlineUtcTicks
                    : DateTime.UtcNow.AddSeconds(ForcedCommitGraceSeconds).Ticks;
                MarkExpectedDisconnect();
                if (TerriasNetworkRuntime.IsClientOnly())
                {
                    view?.ForceCommit();
                }
                break;
            case EndlessAbyssSettlementBarrierEventKinds.Closing:
                hostReady = true;
                closingSent = true;
                MarkExpectedDisconnect();
                if (TerriasNetworkRuntime.IsClientOnly())
                {
                    view?.ForceCommit();
                }
                else
                {
                    ScheduleHostClose();
                }
                break;
        }

        TerriasLog.Info("[EndlessAbyssSettlement] barrier event applied: kind="
                        + command.EventKind
                        + ", player="
                        + command.PlayerId
                        + ", ready="
                        + CommittedRemoteCount
                        + "/"
                        + ExpectedRemoteCount
                        + ", source="
                        + source
                        + ".");
        view?.Refresh();
        if (!TerriasNetworkRuntime.IsClientOnly())
        {
            TerriasFrameDispatcher.RunOnceNextFrame(
                "EndlessAbyssSettlement.Evaluate." + command.CommandToken,
                EvaluateHostBarrier);
        }
    }

    public static void Tick()
    {
        if (TerriasNetworkRuntime.IsClientOnly() || !hostReady || closingSent)
        {
            return;
        }

        RefreshExpectedRemotePlayers();
        if (AllRemotePlayersCommitted())
        {
            BeginClosing("all-players-committed");
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        if (!forceCommitSent && hostDeadlineUtcTicks > 0 && now >= hostDeadlineUtcTicks)
        {
            forceCommitSent = true;
            forcedCommitDeadlineUtcTicks = DateTime.UtcNow.AddSeconds(ForcedCommitGraceSeconds).Ticks;
            EndlessAbyssSettlementBarrierNetworkSync.BroadcastHostEvent(
                EndlessAbyssSettlementBarrierEventKinds.ForceCommit,
                settlementToken,
                forcedCommitDeadlineUtcTicks,
                "ForceCommit");
            return;
        }

        if (forceCommitSent && forcedCommitDeadlineUtcTicks > 0 && now >= forcedCommitDeadlineUtcTicks)
        {
            BeginClosing("forced-commit-grace-expired");
        }
    }

    public static void Detach(EndlessAbyssSettlementBarrierView detached)
    {
        if (ReferenceEquals(view, detached))
        {
            view = null;
            settlementUi = null;
        }
    }

    public static void Clear(string source)
    {
        TerriasLog.Debug("[EndlessAbyssSettlement] barrier cleared from " + source + ".");
        Reset("");
    }

    private static void EvaluateHostBarrier()
    {
        Tick();
        view?.Refresh();
    }

    private static void BeginClosing(string source)
    {
        if (closingSent || string.IsNullOrWhiteSpace(settlementToken))
        {
            return;
        }

        closingSent = true;
        EndlessAbyssSettlementBarrierNetworkSync.BroadcastHostEvent(
            EndlessAbyssSettlementBarrierEventKinds.Closing,
            settlementToken,
            DateTime.UtcNow.AddMilliseconds(500).Ticks,
            "Closing:" + source);
        ScheduleHostClose();
    }

    private static void ScheduleHostClose()
    {
        if (hostCloseScheduled || TerriasNetworkRuntime.IsClientOnly())
        {
            return;
        }

        hostCloseScheduled = true;
        TerriasFrameDispatcher.RunOnceAfterFrames(
            "EndlessAbyssSettlement.HostClose." + settlementToken,
            8,
            () =>
            {
                var ui = settlementUi;
                if (ui == null)
                {
                    TerriasLog.Warn("[EndlessAbyssSettlement] host close skipped: settlement UI unavailable.");
                    hostCloseScheduled = false;
                    return;
                }

                pendingLocalCommitToken = settlementToken;
                TerriasLog.Info("[EndlessAbyssSettlement] host closing after client barrier: ready="
                                + CommittedRemoteCount
                                + "/"
                                + ExpectedRemoteCount
                                + ".");
                ui.ReturnAsync();
            });
    }

    private static bool AllRemotePlayersCommitted()
    {
        return ExpectedRemotePlayers.All(CommittedPlayers.Contains);
    }

    private static void RefreshExpectedRemotePlayers()
    {
        var local = TerriasNetworkRuntime.LocalPlayerId();
        var current = new HashSet<string>(
            TerriasNetworkRuntime.LobbyPlayerIds()
                .Where(id => !string.IsNullOrWhiteSpace(id)
                             && !string.Equals(id, local, StringComparison.Ordinal)),
            StringComparer.Ordinal);
        if (current.Count == 0 && ExpectedRemotePlayers.Count > 0)
        {
            return;
        }

        ExpectedRemotePlayers.Clear();
        foreach (var playerId in current)
        {
            ExpectedRemotePlayers.Add(playerId);
        }
    }

    private static void MarkExpectedDisconnect()
    {
        try
        {
            Singleton<TempDataManager>.Instance.GameOver = true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessAbyssSettlement] failed to mark expected disconnect: " + ex.Message);
        }
    }

    private static void Reset(string token)
    {
        settlementToken = token ?? "";
        pendingLocalCommitToken = "";
        hostReady = false;
        forceCommitSent = false;
        closingSent = false;
        hostCloseScheduled = false;
        hostDeadlineUtcTicks = 0L;
        forcedCommitDeadlineUtcTicks = 0L;
        settlementUi = null;
        view = null;
        ExpectedRemotePlayers.Clear();
        CommittedPlayers.Clear();
        AppliedEvents.Clear();
    }
}
