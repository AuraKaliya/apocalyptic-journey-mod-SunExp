using System;
using System.Collections.Generic;
using AuraShared.Core;
using Network.Command;
using Terrias.Dll.Application;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcEndlessSeaStateSnapshot : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public EndlessSeaStateSnapshot Snapshot { get; set; } = new();
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = EndlessSeaApplicationService.TryCreateHostSnapshot(
            EndlessSeaNetworkAdapter.ToActor(serverSender),
            includePlan: true,
            out var snapshot,
            out var rejection);
        Snapshot = snapshot;
        RejectionReason = rejection;
        if (Accepted
            && !AuraSharedPayloadBudget.FitsSoftLimit(
                Snapshot,
                AuraSharedPayloadBudget.DefaultSoftLimitBytes,
                out _,
                out var payloadError))
        {
            Accepted = false;
            RejectionReason = "snapshot payload budget exceeded: " + payloadError;
        }
    }

    public override void RpcExecute()
    {
        if (Accepted)
        {
            EndlessSeaApplicationService.AcceptRemoteSnapshot(Snapshot, "RpcEndlessSeaStateSnapshot");
        }
    }
}

[Serializable]
public sealed class RpcEndlessSeaStateSnapshotRequest : RpcCommandBase, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender serverSender = TerriasRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = EndlessSeaStateSnapshot.CurrentProtocolVersion;
    public int Token { get; set; }
    public string KnownRunId { get; set; } = "";
    public int KnownGeneration { get; set; }
    public string KnownFloorPlanHash { get; set; } = "";
    public EndlessSeaStateSnapshot? Snapshot { get; set; }
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(TerriasRpcSender sender)
    {
        serverSender = sender ?? TerriasRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = EndlessSeaApplicationService.TryCreateRepairSnapshot(
            EndlessSeaNetworkAdapter.ToActor(serverSender),
            new EndlessSeaSnapshotRequest
            {
                ProtocolVersion = ProtocolVersion,
                Token = Token,
                KnownRunId = KnownRunId,
                KnownGeneration = KnownGeneration,
                KnownFloorPlanHash = KnownFloorPlanHash
            },
            out var snapshot,
            out var rejection);
        Snapshot = snapshot;
        RejectionReason = rejection;
        if (Accepted
            && !AuraSharedPayloadBudget.FitsSoftLimit(
                Snapshot,
                AuraSharedPayloadBudget.DefaultSoftLimitBytes,
                out _,
                out var payloadError))
        {
            Accepted = false;
            RejectionReason = "snapshot payload budget exceeded: " + payloadError;
        }
    }

    public override void RpcExecute()
    {
        if (Accepted && Snapshot != null)
        {
            EndlessSeaApplicationService.AcceptRemoteSnapshot(Snapshot, "RpcEndlessSeaStateSnapshotRequest");
        }
    }
}

public sealed class EndlessSeaNetworkAdapter : IEndlessSeaNetworkPort
{
    public static readonly EndlessSeaNetworkAdapter Instance = new();
    private const int MaximumSendAttempts = 8;
    private const int RetryFrames = 30;
    private static readonly object OutboxSync = new();
    private static readonly Dictionary<string, PendingSend> Pending = new(StringComparer.Ordinal);

    private EndlessSeaNetworkAdapter()
    {
    }

    public bool HasRemotePlayers => TerriasNetworkRuntime.HasRemotePlayers();
    public bool IsMultiplayerSession => TerriasNetworkRuntime.IsMultiplayerSession();
    public bool IsClientOnly => TerriasNetworkRuntime.IsClientOnly();

    public static void Initialize()
    {
        EndlessSeaApplicationService.ConfigureNetwork(Instance);
    }

    public bool SendSnapshotPublisher(string source)
    {
        var command = new RpcEndlessSeaStateSnapshot();
        command.BindServerSender(TerriasRpcAuthorityRuntime.CreateLocalServerSender(source));
        return TrySendOrQueue("snapshot-publisher", command, source);
    }

    public bool SendSnapshotRequest(EndlessSeaSnapshotRequest request, string source)
    {
        return TrySendOrQueue("snapshot-request:" + request.Token, new RpcEndlessSeaStateSnapshotRequest
        {
            ProtocolVersion = request.ProtocolVersion,
            Token = request.Token,
            KnownRunId = request.KnownRunId,
            KnownGeneration = request.KnownGeneration,
            KnownFloorPlanHash = request.KnownFloorPlanHash
        }, source);
    }

    public bool SendShockResolution(
        EndlessSeaShockMessage resolution,
        EndlessSeaStateSnapshot snapshot,
        string source)
    {
        return TrySendOrQueue(
            "shock-resolution:" + (resolution.Token ?? ""),
            new RpcEndlessAbyssShockResolution(resolution, snapshot, source),
            source);
    }

    private static bool TrySendOrQueue(string key, RpcCommandBase command, string source)
    {
        var status = TerriasNetworkRuntime.TrySend(command, source);
        if (status == TerriasNetworkSendStatus.Sent)
        {
            lock (OutboxSync) Pending.Remove(key);
            return true;
        }

        lock (OutboxSync)
        {
            Pending[key] = new PendingSend(command, source, 1);
        }
        ScheduleDrain(key);
        return true;
    }

    private static void ScheduleDrain(string key)
    {
        TerriasFrameDispatcher.RunOnceAfterFrames(
            "EndlessSea.NetworkOutbox." + key,
            RetryFrames,
            () => Drain(key));
    }

    private static void Drain(string key)
    {
        PendingSend pending;
        lock (OutboxSync)
        {
            if (!Pending.TryGetValue(key, out pending)) return;
        }

        var status = TerriasNetworkRuntime.TrySend(
            pending.Command,
            pending.Source + ".OutboxRetry" + pending.Attempts);
        if (status == TerriasNetworkSendStatus.Sent)
        {
            lock (OutboxSync) Pending.Remove(key);
            return;
        }
        if (pending.Attempts >= MaximumSendAttempts)
        {
            lock (OutboxSync) Pending.Remove(key);
            TerriasLog.Warn("[EndlessSeaNetwork] pending send reached terminal attempt limit: key="
                            + key + ", source=" + pending.Source + ".");
            return;
        }

        lock (OutboxSync)
        {
            Pending[key] = pending.NextAttempt();
        }
        ScheduleDrain(key);
    }

    internal static TerriasCommandActor ToActor(TerriasRpcSender sender)
    {
        return new TerriasCommandActor(
            sender?.PlayerId ?? "",
            sender?.IsAvailable == true,
            sender?.IsLobbyMember == true,
            sender?.IsLobbyHost == true,
            sender?.SourceHook ?? "");
    }

    private readonly struct PendingSend
    {
        public PendingSend(RpcCommandBase command, string source, int attempts)
        {
            Command = command;
            Source = source ?? "";
            Attempts = Math.Max(1, attempts);
        }

        public RpcCommandBase Command { get; }
        public string Source { get; }
        public int Attempts { get; }

        public PendingSend NextAttempt() => new(Command, Source, Attempts + 1);
    }
}
