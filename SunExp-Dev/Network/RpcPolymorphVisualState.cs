using System;
using System.Linq;
using Network.Command;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;

namespace SunExp.Dll.Network;

[Serializable]
public sealed class RpcPolymorphVisualState : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

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

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
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
                SunExpLog.Warn("[PolymorphSync] visual state rejected: " + RejectionReason);
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
            return !SunExpNetworkRuntime.IsMultiplayerSession();
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
            var mapped = false;
            try
            {
                var values = Singleton<TempDataManager>.Instance?.RoleStatusMap;
                mapped = values != null
                    && values.TryGetValue(serverSender.PlayerId, out var statusIds)
                    && statusIds != null
                    && statusIds.Contains(ownerStatusId);
            }
            catch
            {
                mapped = false;
            }

            if (!mapped)
            {
                rejection = "owner mismatch: owner=" + ownerStatusId + ", sender=" + serverSender.PlayerId;
                return false;
            }
        }

        Accepted = true;
        return true;
    }
}
