using System;
using Network.Command;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Network;

[Serializable]
public sealed class RpcEmberAdventureStateCommit : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public EmberAdventureStateSnapshot Snapshot { get; set; } = new();

    public bool Accepted { get; set; }

    public string RejectionReason { get; set; } = "";

    public RpcEmberAdventureStateCommit()
    {
    }

    public RpcEmberAdventureStateCommit(EmberAdventureStateSnapshot snapshot)
    {
        Snapshot = snapshot ?? new EmberAdventureStateSnapshot();
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
                SunExpLog.Warn("[EmberAdventureState] commit rejected: " + RejectionReason);
            }

            return;
        }

        EmberAdventureStateService.ApplySnapshot(Snapshot, "RpcEmberAdventureStateCommit");
    }

    internal static bool ApplyOnServer(EmberAdventureStateSnapshot? snapshot, SunExpRpcSender sender, bool remoteRpc)
    {
        return ApplyOnServer(snapshot, sender, remoteRpc, out _);
    }

    private static bool ApplyOnServer(
        EmberAdventureStateSnapshot? snapshot,
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

        EmberAdventureStateService.ApplySnapshot(snapshot, "server:" + (snapshot.Source ?? ""));
        return true;
    }
}
