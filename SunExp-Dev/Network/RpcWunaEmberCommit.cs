using System;
using Network.Command;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Network;

[Serializable]
public sealed class RpcWunaEmberCommit : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public WunaEmberSnapshot Snapshot { get; set; } = new();

    public bool Accepted { get; set; }

    public string RejectionReason { get; set; } = "";

    public RpcWunaEmberCommit()
    {
    }

    public RpcWunaEmberCommit(WunaEmberSnapshot snapshot)
    {
        Snapshot = snapshot ?? new WunaEmberSnapshot();
    }

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = ApplyOnServer(Snapshot, serverSender, remoteRpc: true, out var rejection);
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (!Accepted)
        {
            if (!string.IsNullOrWhiteSpace(RejectionReason))
            {
                SunExpLog.Warn("[WunaEmberSync] commit rejected: " + RejectionReason);
            }

            return;
        }

        WunaEmberSyncService.ApplySnapshot(Snapshot, "RpcWunaEmberCommit");
    }

    internal static bool ApplyOnServer(WunaEmberSnapshot? snapshot, SunExpRpcSender sender, bool remoteRpc)
    {
        return ApplyOnServer(snapshot, sender, remoteRpc, out _);
    }

    private static bool ApplyOnServer(
        WunaEmberSnapshot? snapshot,
        SunExpRpcSender sender,
        bool remoteRpc,
        out string rejection)
    {
        rejection = "";
        if (snapshot == null)
        {
            rejection = "empty snapshot";
            return false;
        }

        if ((remoteRpc || SunExpNetworkRuntime.IsMultiplayerSession()) && !sender.IsAvailable)
        {
            rejection = "missing sender";
            return false;
        }

        if (sender.IsAvailable && !sender.IsLobbyMember)
        {
            rejection = "sender outside lobby: " + sender.PlayerId;
            return false;
        }

        if (sender.IsAvailable)
        {
            if (string.IsNullOrWhiteSpace(snapshot.OwnerPlayerId))
            {
                snapshot.OwnerPlayerId = sender.PlayerId;
            }
            else if (!string.Equals(snapshot.OwnerPlayerId, sender.PlayerId, StringComparison.Ordinal))
            {
                rejection = "owner mismatch: owner=" + snapshot.OwnerPlayerId + ", sender=" + sender.PlayerId;
                return false;
            }
        }

        WunaEmberSyncService.ApplySnapshot(snapshot, "server:" + (snapshot.Source ?? ""));
        return true;
    }
}
