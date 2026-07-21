using System;
using AuraShared.Core;
using Network.Command;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Network;

[Serializable]
public sealed class RpcEndlessAbyssEvacuation : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public string RequestedToken { get; set; } = "";
    public int CommandToken { get; set; }
    public EndlessAbyssEvacuationResolution Resolution { get; set; } = new();
    public EndlessSeaStateSnapshot Snapshot { get; set; } = new();
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (!serverSender.IsAvailable || !serverSender.IsLobbyMember || !serverSender.IsLobbyHost)
        {
            RejectionReason = "host evacuation publisher required";
            return;
        }

        if (!EndlessAbyssEvacuationNetworkSync.TryAcceptCommand(serverSender.PlayerId, CommandToken))
        {
            RejectionReason = "evacuation command duplicated";
            return;
        }

        if (!EndlessAbyssEvacuationService.TryCaptureStored(RequestedToken, out var authoritative))
        {
            RejectionReason = "authoritative evacuation state missing";
            return;
        }

        Resolution = authoritative;
        Snapshot = EndlessSeaNetworkSync.CaptureNextAuthoritative(includePlan: false);
        if (!AuraSharedPayloadBudget.FitsSoftLimit(
                this,
                AuraSharedPayloadBudget.DefaultSoftLimitBytes,
                out _,
                out var payloadError))
        {
            RejectionReason = "evacuation payload budget exceeded: " + payloadError;
            return;
        }

        Accepted = true;
    }

    public override void RpcExecute()
    {
        if (!Accepted || !Resolution.IsValid)
        {
            return;
        }

        Snapshot?.Apply("RpcEndlessAbyssEvacuation");
        EndlessAbyssEvacuationRuntime.ReceiveAuthoritative(
            Resolution,
            "RpcEndlessAbyssEvacuation");
    }
}

public static class EndlessAbyssEvacuationNetworkSync
{
    private static readonly AuraAuthoritativeSyncDomain SyncDomain =
        AuraAuthoritativeSyncRuntime.RegisterDomain(new AuraAuthoritativeSyncDomainOptions
        {
            OwnerModId = SunExpIds.ModId,
            DomainId = "EndlessAbyssEvacuation",
            MaxResolvedTokens = 128
        });

    internal static bool TryAcceptCommand(string senderId, int commandToken)
    {
        return commandToken > 0 && SyncDomain.TryClaimToken(senderId, commandToken);
    }

    public static void Broadcast(EndlessAbyssEvacuationResolution resolution, string source)
    {
        if (resolution?.IsValid != true
            || !SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsMultiplayerSession()
            || SunExpNetworkRuntime.IsClientOnly())
        {
            return;
        }

        var command = new RpcEndlessAbyssEvacuation
        {
            RequestedToken = resolution.Token,
            CommandToken = SyncDomain.NextToken()
        };
        command.BindServerSender(SunExpRpcAuthorityRuntime.CreateLocalServerSender(source));
        SunExpNetworkRuntime.Send(command, source ?? "EndlessAbyssEvacuationNetworkSync.Broadcast");
    }
}
