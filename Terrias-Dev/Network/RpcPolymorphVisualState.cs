using System;
using Network.Command;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcPolymorphVisualState : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public PolymorphVisualSnapshot Snapshot { get; set; } = new();

    public bool Accepted { get; set; }

    public string RejectionReason { get; set; } = "";

    public RpcPolymorphVisualState()
    {
    }

    public RpcPolymorphVisualState(PolymorphVisualSnapshot snapshot)
    {
        Snapshot = snapshot ?? new PolymorphVisualSnapshot();
        Accepted = true;
    }

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = ValidateSender(out var rejection);
        RejectionReason = rejection;
    }

    public override void RpcExecute()
    {
        if (!Accepted)
        {
            if (!string.IsNullOrWhiteSpace(RejectionReason))
            {
                TerriasLog.Warn("[PolymorphSync] visual state rejected: " + RejectionReason);
            }

            return;
        }

        PolymorphNetworkSync.ApplyVisualSnapshot(Snapshot, "RpcPolymorphVisualState");
    }

    private bool ValidateSender(out string rejection)
    {
        rejection = "";
        if (!serverSender.IsAvailable)
        {
            rejection = "missing sender";
            return !TerriasNetworkRuntime.IsMultiplayerSession();
        }

        if (!serverSender.IsLobbyMember)
        {
            rejection = "sender outside lobby: " + serverSender.PlayerId;
            return false;
        }

        var ownerStatusId = Snapshot?.OwnerStatusId ?? "";
        if (!string.IsNullOrWhiteSpace(ownerStatusId)
            && !string.Equals(ownerStatusId, serverSender.PlayerId, StringComparison.Ordinal))
        {
            if (!TerriasStatusOwnershipPolicy.SenderOwnsStatus(
                    serverSender.PlayerId,
                    ownerStatusId,
                    out var ownershipDetail))
            {
                rejection = "owner mismatch: owner="
                    + ownerStatusId
                    + ", sender="
                    + serverSender.PlayerId
                    + ", authority="
                    + ownershipDetail;
                return false;
            }
        }

        Accepted = true;
        return true;
    }
}
