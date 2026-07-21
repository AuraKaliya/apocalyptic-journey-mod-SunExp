using System;
using AuraShared.Core;
using Network.Command;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Network;

public static class EndlessAbyssSettlementBarrierEventKinds
{
    public const string PlayerCommitted = "PlayerCommitted";
    public const string HostReady = "HostReady";
    public const string ForceCommit = "ForceCommit";
    public const string Closing = "Closing";
}

[Serializable]
public sealed class RpcEndlessAbyssSettlementBarrier : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    public const int CurrentProtocolVersion = 1;

    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
    public string SettlementToken { get; set; } = "";
    public string EventKind { get; set; } = "";
    public int CommandToken { get; set; }
    public string PlayerId { get; set; } = "";
    public long DeadlineUtcTicks { get; set; }
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = EndlessAbyssSettlementBarrierNetworkSync.TryResolve(
            this,
            serverSender,
            out var playerId,
            out var rejection);
        PlayerId = playerId;
        RejectionReason = rejection;
        if (Accepted)
        {
            EndlessAbyssSettlementBarrierRuntime.ApplyAuthoritativeEvent(this, "server");
        }
    }

    public override void RpcExecute()
    {
        if (Accepted)
        {
            EndlessAbyssSettlementBarrierRuntime.ApplyAuthoritativeEvent(this, "rpc");
        }
    }
}

public static class EndlessAbyssSettlementBarrierNetworkSync
{
    private static readonly AuraAuthoritativeSyncDomain SyncDomain =
        AuraAuthoritativeSyncRuntime.RegisterDomain(new AuraAuthoritativeSyncDomainOptions
        {
            OwnerModId = TerriasIds.ModId,
            DomainId = "EndlessAbyssSettlementBarrier",
            MaxResolvedTokens = 256
        });

    public static bool SendPlayerCommitted(string settlementToken)
    {
        return Send(EndlessAbyssSettlementBarrierEventKinds.PlayerCommitted, settlementToken, 0L, "PlayerCommitted");
    }

    public static bool BroadcastHostEvent(string eventKind, string settlementToken, long deadlineUtcTicks, string source)
    {
        if (TerriasNetworkRuntime.IsClientOnly())
        {
            return false;
        }

        return Send(eventKind, settlementToken, deadlineUtcTicks, source);
    }

    internal static bool TryResolve(
        RpcEndlessAbyssSettlementBarrier command,
        TerriasRpcSender sender,
        out string playerId,
        out string rejection)
    {
        playerId = "";
        rejection = "";
        if (command == null || command.ProtocolVersion != RpcEndlessAbyssSettlementBarrier.CurrentProtocolVersion)
        {
            rejection = "protocol mismatch";
            return false;
        }

        if (!sender.IsAvailable || !sender.IsLobbyMember)
        {
            rejection = "lobby member sender required";
            return false;
        }

        if (!EndlessAbyssEvacuationService.TryCaptureStored(command.SettlementToken, out _))
        {
            rejection = "settlement token is not authoritative";
            return false;
        }

        if (command.CommandToken <= 0 || !SyncDomain.TryClaimToken(sender.PlayerId, command.CommandToken))
        {
            rejection = "duplicate settlement barrier command";
            return false;
        }

        var kind = command.EventKind ?? "";
        if (string.Equals(kind, EndlessAbyssSettlementBarrierEventKinds.PlayerCommitted, StringComparison.Ordinal))
        {
            playerId = sender.PlayerId;
            command.DeadlineUtcTicks = 0L;
            return true;
        }

        if (!sender.IsLobbyHost)
        {
            rejection = "host settlement barrier event required";
            return false;
        }

        if (!string.Equals(kind, EndlessAbyssSettlementBarrierEventKinds.HostReady, StringComparison.Ordinal)
            && !string.Equals(kind, EndlessAbyssSettlementBarrierEventKinds.ForceCommit, StringComparison.Ordinal)
            && !string.Equals(kind, EndlessAbyssSettlementBarrierEventKinds.Closing, StringComparison.Ordinal))
        {
            rejection = "unsupported settlement barrier event";
            return false;
        }

        playerId = sender.PlayerId;
        command.DeadlineUtcTicks = Math.Max(0L, command.DeadlineUtcTicks);
        return true;
    }

    private static bool Send(string eventKind, string settlementToken, long deadlineUtcTicks, string source)
    {
        if (string.IsNullOrWhiteSpace(settlementToken))
        {
            return false;
        }

        var command = new RpcEndlessAbyssSettlementBarrier
        {
            SettlementToken = settlementToken.Trim(),
            EventKind = eventKind ?? "",
            CommandToken = SyncDomain.NextToken(),
            DeadlineUtcTicks = Math.Max(0L, deadlineUtcTicks)
        };
        if (!TerriasNetworkRuntime.IsClientOnly())
        {
            command.BindServerSender(TerriasRpcAuthorityRuntime.CreateLocalServerSender(source));
        }

        return TerriasNetworkRuntime.Send(command, "EndlessAbyssSettlementBarrier." + source);
    }
}
