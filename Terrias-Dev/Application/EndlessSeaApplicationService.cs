using System;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;
using Newtonsoft.Json;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Application;

public static class EndlessSeaApplicationService
{
    private const string DomainId = "EndlessSeaState";
    private const double SnapshotRequestThrottleSeconds = 1.5d;
    private static readonly AuraAuthoritativeSyncDomain SyncDomain =
        AuraAuthoritativeSyncRuntime.RegisterDomain(new AuraAuthoritativeSyncDomainOptions
        {
            OwnerModId = TerriasIds.ModId,
            DomainId = DomainId,
            SnapshotRequestThrottleSeconds = SnapshotRequestThrottleSeconds,
            MaxResolvedTokens = 512
        });
    private static readonly string HostSession = Guid.NewGuid().ToString("N");
    private static IEndlessSeaNetworkPort? networkPort;
    private static int hostGeneration;
    private static readonly EndlessSeaReplicationClock RemoteClock = new();
    private static EndlessSeaStateSnapshot? lastRemoteSnapshot;
    private static bool initialized;

    public static event Action<EndlessSeaStateCommitted>? StateCommitted;
    public static event Action<string>? ShockResolutionCommitted;

    public static void ConfigureNetwork(IEndlessSeaNetworkPort port)
    {
        networkPort = port ?? throw new ArgumentNullException(nameof(port));
        if (!initialized)
        {
            initialized = true;
            EndlessAbyssShockService.ResolutionCommitted += OnShockResolutionCommitted;
        }
    }

    public static void BroadcastSnapshot(string source)
    {
        var port = RequireNetworkPort();
        if (!port.HasRemotePlayers || !port.IsMultiplayerSession || port.IsClientOnly)
        {
            return;
        }
        port.SendSnapshotPublisher(source ?? "EndlessSeaApplicationService.BroadcastSnapshot");
    }

    public static void RequestSnapshot(string source)
    {
        var port = RequireNetworkPort();
        if (!port.HasRemotePlayers || !port.IsClientOnly || !SyncDomain.TryBeginSnapshotRequest())
        {
            return;
        }
        var known = lastRemoteSnapshot;
        port.SendSnapshotRequest(new EndlessSeaSnapshotRequest
        {
            Token = SyncDomain.NextToken(),
            KnownRunId = known?.RunId ?? "",
            KnownGeneration = Math.Max(0, known?.Generation ?? RemoteClock.Generation),
            KnownFloorPlanHash = known?.FloorPlanHash ?? ""
        }, source ?? "EndlessSeaApplicationService.RequestSnapshot");
    }

    public static bool TryCreateHostSnapshot(
        TerriasCommandActor actor,
        bool includePlan,
        out EndlessSeaStateSnapshot snapshot,
        out string rejectionReason)
    {
        snapshot = new EndlessSeaStateSnapshot();
        rejectionReason = "";
        if (!actor.IsAvailable || !actor.IsLobbyMember || !actor.IsLobbyHost)
        {
            rejectionReason = "host snapshot publisher required";
            return false;
        }
        snapshot = CaptureAuthoritative(includePlan, advanceGeneration: true);
        if (snapshot.Mode != "1")
        {
            rejectionReason = "endless sea inactive";
            return false;
        }
        return true;
    }

    public static bool TryCreateRepairSnapshot(
        TerriasCommandActor actor,
        EndlessSeaSnapshotRequest request,
        out EndlessSeaStateSnapshot snapshot,
        out string rejectionReason)
    {
        snapshot = new EndlessSeaStateSnapshot();
        rejectionReason = "";
        if (request == null
            || request.ProtocolVersion != EndlessSeaStateSnapshot.CurrentProtocolVersion
            || !actor.IsAvailable
            || !actor.IsLobbyMember)
        {
            rejectionReason = "invalid repair request sender or protocol";
            return false;
        }
        if (!SyncDomain.TryClaimToken(actor.PlayerId, request.Token))
        {
            rejectionReason = "repair request throttled or duplicated";
            return false;
        }

        var current = CaptureAuthoritative(includePlan: false, advanceGeneration: false);
        if (current.Mode != "1")
        {
            rejectionReason = "endless sea inactive";
            return false;
        }
        var includePlan = !string.Equals(request.KnownRunId, current.RunId, StringComparison.Ordinal)
                          || request.KnownGeneration != current.Generation
                          || !string.Equals(request.KnownFloorPlanHash, current.FloorPlanHash, StringComparison.Ordinal);
        snapshot = CaptureAuthoritative(includePlan, advanceGeneration: false);
        return true;
    }

    public static EndlessSeaStateSnapshot CaptureAuthoritative(bool includePlan, bool advanceGeneration = false)
    {
        if (advanceGeneration) hostGeneration++;
        var data = EndlessSeaSaveApi.Capture(includePlan);
        var canonicalPlan = string.IsNullOrWhiteSpace(data.FloorPlanJson)
            ? EndlessSeaSaveApi.Capture(includePlan: true).FloorPlanJson
            : data.FloorPlanJson;
        return new EndlessSeaStateSnapshot
        {
            HostSession = HostSession,
            Generation = Math.Max(1, hostGeneration),
            Mode = data.Mode,
            Floor = Math.Max(1, data.Floor),
            GeneratedFloor = Math.Max(0, data.GeneratedFloor),
            RunId = data.RunId,
            RunPhase = data.RunPhase,
            RunEnded = data.RunEnded,
            StarterDeckApplied = data.StarterDeckApplied,
            GazeLevel = Math.Max(0, data.GazeLevel),
            PendingShockJson = data.PendingShockJson,
            EvacuationToken = data.EvacuationToken,
            EvacuationReason = data.EvacuationReason,
            EvacuationFloor = Math.Max(0, data.EvacuationFloor),
            EvacuationDepth = Math.Max(0, data.EvacuationDepth),
            EvacuationAt = data.EvacuationAt,
            FloorPlanHash = Hash(canonicalPlan),
            FloorPlanJson = data.FloorPlanJson
        };
    }

    public static bool AcceptRemoteSnapshot(EndlessSeaStateSnapshot? snapshot, string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != EndlessSeaStateSnapshot.CurrentProtocolVersion
            || snapshot.Mode != "1"
            || string.IsNullOrWhiteSpace(snapshot.HostSession))
        {
            return false;
        }
        if (!RemoteClock.CanAccept(snapshot.HostSession, snapshot.Generation))
        {
            return false;
        }
        if (!string.Equals(RemoteClock.Session, snapshot.HostSession, StringComparison.Ordinal))
        {
            lastRemoteSnapshot = null;
        }

        EndlessSeaFloorPlan? plan = null;
        if (!string.IsNullOrWhiteSpace(snapshot.FloorPlanJson))
        {
            if (!string.Equals(Hash(snapshot.FloorPlanJson), snapshot.FloorPlanHash, StringComparison.Ordinal)
                || (plan = ParsePlan(snapshot.FloorPlanJson)) == null
                || plan.Floor != snapshot.Floor)
            {
                TerriasLog.Warn("[EndlessSeaApplication] rejected invalid floor plan from " + source + ".");
                return false;
            }
        }

        EndlessSeaSaveApi.Apply(new EndlessSeaSaveData
        {
            Mode = "1",
            Floor = snapshot.Floor,
            GeneratedFloor = snapshot.GeneratedFloor,
            RunId = snapshot.RunId,
            RunPhase = snapshot.RunPhase,
            RunEnded = snapshot.RunEnded ?? "0",
            StarterDeckApplied = snapshot.StarterDeckApplied,
            GazeLevel = snapshot.GazeLevel,
            PendingShockJson = snapshot.PendingShockJson,
            EvacuationToken = snapshot.EvacuationToken,
            EvacuationReason = snapshot.EvacuationReason,
            EvacuationFloor = snapshot.EvacuationFloor,
            EvacuationDepth = snapshot.EvacuationDepth,
            EvacuationAt = snapshot.EvacuationAt,
            FloorPlanJson = snapshot.FloorPlanJson
        });

        RemoteClock.Commit(snapshot.HostSession, snapshot.Generation);
        lastRemoteSnapshot = snapshot;
        PublishStateCommitted(new EndlessSeaStateCommitted(snapshot, plan, source));
        TerriasLog.Debug("[EndlessSeaApplication] committed host snapshot; floor="
                         + snapshot.Floor + "; generation=" + snapshot.Generation + "; source=" + source + ".");
        return true;
    }

    public static void ApplyShockResolution(
        EndlessSeaShockMessage? message,
        EndlessSeaStateSnapshot? snapshot,
        string source)
    {
        if (message?.Request == null) return;
        var result = EndlessAbyssShockService.ApplyNetworkResolution(
            new EndlessAbyssShockResolution
            {
                Request = new EndlessAbyssShockRequest
                {
                    Key = message.Request.Key,
                    Trigger = message.Request.Trigger,
                    Floor = message.Request.Floor,
                    NativeLevel = message.Request.NativeLevel,
                    NodeId = message.Request.NodeId,
                    NodeKind = message.Request.NodeKind,
                    GazeLevelAtEnqueue = message.Request.GazeLevelAtEnqueue,
                    Source = message.Request.Source
                },
                Options = message.Options,
                Source = message.Source,
                Token = message.Token
            },
            source);
        if (result.Success && AcceptRemoteSnapshot(snapshot, source))
        {
            PublishShockResolutionCommitted(source);
        }
    }

    private static void OnShockResolutionCommitted(
        EndlessAbyssShockResolution resolution,
        string source)
    {
        var port = RequireNetworkPort();
        if (!port.IsMultiplayerSession || port.IsClientOnly) return;
        var request = resolution.Request ?? new EndlessAbyssShockRequest();
        var message = new EndlessSeaShockMessage
        {
            Request = new EndlessSeaShockRequestMessage
            {
                Key = request.Key,
                Trigger = request.Trigger,
                Floor = request.Floor,
                NativeLevel = request.NativeLevel,
                NodeId = request.NodeId,
                NodeKind = request.NodeKind,
                GazeLevelAtEnqueue = request.GazeLevelAtEnqueue,
                Source = request.Source
            },
            Options = resolution.Options,
            Source = resolution.Source,
            Token = resolution.Token
        };
        port.SendShockResolution(
            message,
            CaptureAuthoritative(includePlan: true, advanceGeneration: true),
            source);
    }

    private static IEndlessSeaNetworkPort RequireNetworkPort()
    {
        return networkPort
               ?? throw new InvalidOperationException("EndlessSea Application network port was not configured by Entry.");
    }

    private static void PublishStateCommitted(EndlessSeaStateCommitted committed)
    {
        var handlers = StateCommitted;
        if (handlers == null) return;
        foreach (Action<EndlessSeaStateCommitted> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(committed);
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[EndlessSeaApplication] committed-state subscriber failed: " + ex.Message);
            }
        }
    }

    private static void PublishShockResolutionCommitted(string source)
    {
        var handlers = ShockResolutionCommitted;
        if (handlers == null) return;
        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(source);
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[EndlessSeaApplication] shock subscriber failed: " + ex.Message);
            }
        }
    }

    private static EndlessSeaFloorPlan? ParsePlan(string json)
    {
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

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash) builder.Append(item.ToString("x2"));
        return builder.ToString();
    }
}
