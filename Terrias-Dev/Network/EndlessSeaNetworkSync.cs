using System;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;
using Data.Save;
using Network.Command;
using Newtonsoft.Json;
using SunExp.Dll.Hooks;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.UI.Window;

namespace SunExp.Dll.Network;

[Serializable]
public sealed class EndlessSeaStateSnapshot
{
    public const int CurrentProtocolVersion = 3;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
    public string HostSession { get; set; } = "";
    public int Generation { get; set; }
    public string Mode { get; set; } = "";
    public int Floor { get; set; }
    public int GeneratedFloor { get; set; }
    public string RunId { get; set; } = "";
    public string RunPhase { get; set; } = "";
    public string RunEnded { get; set; } = "";
    public string StarterDeckApplied { get; set; } = "";
    public int GazeLevel { get; set; }
    public string PendingShockJson { get; set; } = "";
    public string EvacuationToken { get; set; } = "";
    public string EvacuationReason { get; set; } = "";
    public int EvacuationFloor { get; set; }
    public int EvacuationDepth { get; set; }
    public string EvacuationAt { get; set; } = "";
    public string FloorPlanHash { get; set; } = "";
    public string FloorPlanJson { get; set; } = "";

    public static EndlessSeaStateSnapshot Capture(string source)
    {
        return EndlessSeaNetworkSync.CaptureAuthoritative(includePlan: true);
    }

    public static EndlessSeaStateSnapshot Capture(string hostSession, int generation, bool includePlan)
    {
        var floorPlan = includePlan ? ReadString(SunExpIds.EndlessSeaFloorPlanKey) : "";
        var canonicalPlan = string.IsNullOrWhiteSpace(floorPlan)
            ? ReadString(SunExpIds.EndlessSeaFloorPlanKey)
            : floorPlan;
        return new EndlessSeaStateSnapshot
        {
            HostSession = hostSession ?? "",
            Generation = Math.Max(0, generation),
            Mode = ReadString(SunExpIds.EndlessSeaModeKey),
            Floor = Math.Max(1, ReadInt(SunExpIds.EndlessSeaFloorKey)),
            GeneratedFloor = Math.Max(0, ReadInt(SunExpIds.EndlessSeaGeneratedFloorKey)),
            RunId = ReadString(SunExpIds.EndlessSeaRunIdKey),
            RunPhase = ReadString(SunExpIds.EndlessSeaRunPhaseKey),
            RunEnded = ReadString(SunExpIds.EndlessSeaRunEndedKey),
            StarterDeckApplied = ReadString(SunExpIds.EndlessSeaStarterDeckAppliedKey),
            GazeLevel = Math.Max(0, ReadInt(SunExpIds.EndlessAbyssGazeLevelKey)),
            PendingShockJson = ReadString(SunExpIds.EndlessAbyssPendingShockKey),
            EvacuationToken = ReadString(SunExpIds.EndlessAbyssEvacuationTokenKey),
            EvacuationReason = ReadString(SunExpIds.EndlessAbyssEvacuationReasonKey),
            EvacuationFloor = Math.Max(0, ReadInt(SunExpIds.EndlessAbyssEvacuationFloorKey)),
            EvacuationDepth = Math.Max(0, ReadInt(SunExpIds.EndlessAbyssEvacuationDepthKey)),
            EvacuationAt = ReadString(SunExpIds.EndlessAbyssEvacuationAtKey),
            FloorPlanHash = Hash(canonicalPlan),
            FloorPlanJson = floorPlan
        };
    }

    internal static EndlessSeaFloorPlan? ParsePlan(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var plan = JsonConvert.DeserializeObject<EndlessSeaFloorPlan>(json);
            plan?.Normalize();
            return plan != null && plan.IsValid ? plan : null;
        }
        catch
        {
            return null;
        }
    }

    public void Apply(string source)
    {
        EndlessSeaNetworkSync.AcceptRemoteSnapshot(this, source ?? "EndlessSeaStateSnapshot.Apply");
    }

    internal static string Hash(string value)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2"));
        }

        return builder.ToString();
    }

    private static string ReadString(string key)
    {
        try
        {
            return GameSaveManager.GetValue<string>(key) ?? "";
        }
        catch
        {
            return GameSaveManager.GetNowSave()?.GameVars?.TryGetValue(key, out var value) == true ? value ?? "" : "";
        }
    }

    private static int ReadInt(string key)
    {
        return DictionaryUtil.ParseInt(ReadString(key));
    }
}

[Serializable]
public sealed class RpcEndlessSeaStateSnapshot : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

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
            RejectionReason = "host snapshot publisher required";
            Accepted = false;
            return;
        }

        Snapshot = EndlessSeaNetworkSync.CaptureAuthoritative(includePlan: true);
        if (!AuraSharedPayloadBudget.FitsSoftLimit(Snapshot, AuraSharedPayloadBudget.DefaultSoftLimitBytes, out _, out var payloadError))
        {
            RejectionReason = "snapshot payload budget exceeded: " + payloadError;
            Accepted = false;
            return;
        }

        Accepted = Snapshot.Mode == "1";
    }

    public override void RpcExecute()
    {
        if (Accepted)
        {
            EndlessSeaNetworkSync.AcceptRemoteSnapshot(Snapshot, "RpcEndlessSeaStateSnapshot");
        }
    }
}

[Serializable]
public sealed class RpcEndlessSeaStateSnapshotRequest : RpcCommandBase, ISunExpServerBoundRpcCommand
{
    private SunExpRpcSender serverSender = SunExpRpcSender.Unbound;

    public int ProtocolVersion { get; set; } = EndlessSeaStateSnapshot.CurrentProtocolVersion;
    public int Token { get; set; }
    public string KnownRunId { get; set; } = "";
    public int KnownGeneration { get; set; }
    public string KnownFloorPlanHash { get; set; } = "";
    public EndlessSeaStateSnapshot? Snapshot { get; set; }
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = "";

    public void BindServerSender(SunExpRpcSender sender)
    {
        serverSender = sender ?? SunExpRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        if (ProtocolVersion != EndlessSeaStateSnapshot.CurrentProtocolVersion
            || !serverSender.IsAvailable
            || !serverSender.IsLobbyMember)
        {
            RejectionReason = "invalid repair request sender or protocol";
            return;
        }

        if (!EndlessSeaNetworkSync.TryAcceptRepairRequest(serverSender.PlayerId, Token))
        {
            RejectionReason = "repair request throttled or duplicated";
            return;
        }

        var current = EndlessSeaNetworkSync.CaptureAuthoritative(includePlan: false);
        if (current.Mode != "1")
        {
            RejectionReason = "endless sea inactive";
            return;
        }

        var includePlan = !string.Equals(KnownRunId, current.RunId, StringComparison.Ordinal)
                          || KnownGeneration != current.Generation
                          || !string.Equals(KnownFloorPlanHash, current.FloorPlanHash, StringComparison.Ordinal);
        Snapshot = EndlessSeaNetworkSync.CaptureAuthoritative(includePlan);
        if (!AuraSharedPayloadBudget.FitsSoftLimit(Snapshot, AuraSharedPayloadBudget.DefaultSoftLimitBytes, out _, out var payloadError))
        {
            RejectionReason = "snapshot payload budget exceeded: " + payloadError;
            Accepted = false;
            return;
        }

        Accepted = true;
    }

    public override void RpcExecute()
    {
        if (Accepted && Snapshot != null)
        {
            EndlessSeaNetworkSync.AcceptRemoteSnapshot(Snapshot, "RpcEndlessSeaStateSnapshotRequest");
        }
    }
}

public static class EndlessSeaNetworkSync
{
    private const string DomainId = "EndlessSeaState";
    private const double SnapshotRequestThrottleSeconds = 1.5d;
    private static readonly AuraAuthoritativeSyncDomain SyncDomain =
        AuraAuthoritativeSyncRuntime.RegisterDomain(new AuraAuthoritativeSyncDomainOptions
        {
            OwnerModId = SunExpIds.ModId,
            DomainId = DomainId,
            SnapshotRequestThrottleSeconds = SnapshotRequestThrottleSeconds,
            MaxResolvedTokens = 512
        });
    private static readonly string HostSession = Guid.NewGuid().ToString("N");
    private static int hostGeneration;
    private static string remoteSession = "";
    private static int remoteGeneration = -1;
    private static EndlessSeaStateSnapshot? pendingProjection;
    private static EndlessSeaFloorPlan? cachedRemotePlan;
    private static string cachedRemotePlanHash = "";

    public static void BroadcastSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsMultiplayerSession()
            || SunExpNetworkRuntime.IsClientOnly())
        {
            return;
        }

        hostGeneration++;
        var command = new RpcEndlessSeaStateSnapshot();
        command.BindServerSender(SunExpRpcAuthorityRuntime.CreateLocalServerSender(source));
        SunExpNetworkRuntime.Send(command, source ?? "EndlessSeaNetworkSync.BroadcastSnapshot");
    }

    public static void RequestSnapshot(string source)
    {
        if (!SunExpNetworkRuntime.HasRemotePlayers()
            || !SunExpNetworkRuntime.IsClientOnly()
            || !SyncDomain.TryBeginSnapshotRequest())
        {
            return;
        }

        var snapshot = pendingProjection;
        SunExpNetworkRuntime.Send(new RpcEndlessSeaStateSnapshotRequest
        {
            Token = SyncDomain.NextToken(),
            KnownRunId = snapshot?.RunId ?? "",
            KnownGeneration = Math.Max(0, snapshot?.Generation ?? remoteGeneration),
            KnownFloorPlanHash = cachedRemotePlanHash
        }, source ?? "EndlessSeaNetworkSync.RequestSnapshot");
    }

    internal static bool TryAcceptRepairRequest(string senderId, int token)
    {
        return SyncDomain.TryClaimToken(senderId, token);
    }

    internal static EndlessSeaStateSnapshot CaptureAuthoritative(bool includePlan)
    {
        return EndlessSeaStateSnapshot.Capture(HostSession, Math.Max(1, hostGeneration), includePlan);
    }

    internal static EndlessSeaStateSnapshot CaptureNextAuthoritative(bool includePlan)
    {
        hostGeneration++;
        return CaptureAuthoritative(includePlan);
    }

    internal static void AcceptRemoteSnapshot(EndlessSeaStateSnapshot? snapshot, string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != EndlessSeaStateSnapshot.CurrentProtocolVersion
            || snapshot.Mode != "1"
            || string.IsNullOrWhiteSpace(snapshot.HostSession))
        {
            return;
        }

        if (string.Equals(remoteSession, snapshot.HostSession, StringComparison.Ordinal)
            && snapshot.Generation < remoteGeneration)
        {
            return;
        }

        if (!string.Equals(remoteSession, snapshot.HostSession, StringComparison.Ordinal))
        {
            remoteSession = snapshot.HostSession;
            remoteGeneration = -1;
            cachedRemotePlan = null;
            cachedRemotePlanHash = "";
        }

        remoteGeneration = Math.Max(remoteGeneration, snapshot.Generation);
        Set(SunExpIds.EndlessSeaModeKey, "1");
        Set(SunExpIds.EndlessSeaFloorKey, Math.Max(1, snapshot.Floor).ToString());
        Set(SunExpIds.EndlessSeaGeneratedFloorKey, Math.Max(0, snapshot.GeneratedFloor).ToString());
        Set(SunExpIds.EndlessSeaRunIdKey, snapshot.RunId ?? "");
        Set(SunExpIds.EndlessSeaRunPhaseKey, snapshot.RunPhase ?? "");
        Set(SunExpIds.EndlessSeaRunEndedKey, snapshot.RunEnded ?? "0");
        Set(SunExpIds.EndlessSeaStarterDeckAppliedKey, snapshot.StarterDeckApplied ?? "");
        Set(SunExpIds.EndlessAbyssGazeLevelKey, Math.Max(0, snapshot.GazeLevel).ToString());
        Set(SunExpIds.EndlessAbyssPendingShockKey, snapshot.PendingShockJson ?? "");
        Set(SunExpIds.EndlessAbyssEvacuationTokenKey, snapshot.EvacuationToken ?? "");
        Set(SunExpIds.EndlessAbyssEvacuationReasonKey, snapshot.EvacuationReason ?? "");
        Set(SunExpIds.EndlessAbyssEvacuationFloorKey, Math.Max(0, snapshot.EvacuationFloor).ToString());
        Set(SunExpIds.EndlessAbyssEvacuationDepthKey, Math.Max(0, snapshot.EvacuationDepth).ToString());
        Set(SunExpIds.EndlessAbyssEvacuationAtKey, snapshot.EvacuationAt ?? "");

        if (!string.IsNullOrWhiteSpace(snapshot.FloorPlanJson)
            && string.Equals(EndlessSeaStateSnapshot.Hash(snapshot.FloorPlanJson), snapshot.FloorPlanHash, StringComparison.Ordinal)
            && EndlessSeaStateSnapshot.ParsePlan(snapshot.FloorPlanJson) is { } plan
            && plan.Floor == snapshot.Floor)
        {
            cachedRemotePlan = plan;
            cachedRemotePlanHash = snapshot.FloorPlanHash;
        }

        pendingProjection = snapshot;
        if (string.Equals(snapshot.RunPhase, EndlessSeaRunPhase.Evacuating, StringComparison.Ordinal))
        {
            EndlessAbyssEvacuationRuntime.ReceiveAuthoritative(
                new EndlessAbyssEvacuationResolution
                {
                    RunId = snapshot.RunId ?? "",
                    Token = snapshot.EvacuationToken ?? "",
                    Reason = snapshot.EvacuationReason ?? "",
                    Floor = snapshot.EvacuationFloor,
                    SettlementDepth = snapshot.EvacuationDepth,
                    EvacuatedAt = snapshot.EvacuationAt ?? ""
                },
                source + ":snapshot");
        }
        SunExpLog.Debug("[EndlessSeaSync] accepted host snapshot; floor=" + snapshot.Floor
            + "; generation=" + snapshot.Generation
            + "; source=" + source + ".");
    }

    public static bool TryGetCachedPlan(int floor, out EndlessSeaFloorPlan plan)
    {
        plan = cachedRemotePlan!;
        return cachedRemotePlan != null && cachedRemotePlan.Floor == Math.Max(1, floor) && cachedRemotePlan.IsValid;
    }

    public static void ApplyPendingProjection(MapSelectUI? mapSelect, NormalMapManager? manager, string source)
    {
        var snapshot = pendingProjection;
        if (snapshot == null || mapSelect == null || manager == null || snapshot.Floor != ReadCurrentFloor())
        {
            return;
        }

        EndlessSeaMapViewPresenter.SetLayerTitle(mapSelect, snapshot.Floor);
        EndlessSeaMapViewPresenter.ApplySlots(mapSelect, manager, snapshot.Floor, applyAllSlots: false, sync: false, source);
        pendingProjection = null;
    }

    private static int ReadCurrentFloor()
    {
        try
        {
            return Math.Max(1, GameSaveManager.GetValue<int>(SunExpIds.EndlessSeaFloorKey));
        }
        catch
        {
            return 1;
        }
    }

    private static void Set(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value ?? "");
        }
        catch
        {
            GameSaveManager.GetNowSave()?.SetValue(key, value ?? "");
        }
    }
}
