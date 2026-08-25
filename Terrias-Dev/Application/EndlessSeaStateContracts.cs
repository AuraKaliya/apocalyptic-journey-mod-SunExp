using System;
using System.Collections.Generic;

namespace Terrias.Dll.Application;

[Serializable]
public sealed class EndlessSeaStateSnapshot
{
    public const int CurrentProtocolVersion = 4;

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
}

public sealed class EndlessSeaSnapshotRequest
{
    public int ProtocolVersion { get; set; } = EndlessSeaStateSnapshot.CurrentProtocolVersion;
    public int Token { get; set; }
    public string KnownRunId { get; set; } = "";
    public int KnownGeneration { get; set; }
    public string KnownFloorPlanHash { get; set; } = "";
}

public sealed class EndlessSeaStateCommitted
{
    public EndlessSeaStateCommitted(
        EndlessSeaStateSnapshot snapshot,
        Terrias.Dll.Mechanics.EndlessSeaFloorPlan? floorPlan,
        string source)
    {
        Snapshot = snapshot;
        FloorPlan = floorPlan;
        Source = source ?? "";
    }

    public EndlessSeaStateSnapshot Snapshot { get; }
    public Terrias.Dll.Mechanics.EndlessSeaFloorPlan? FloorPlan { get; }
    public string Source { get; }
}

[Serializable]
public sealed class EndlessSeaShockRequestMessage
{
    public string Key { get; set; } = "";
    public string Trigger { get; set; } = "";
    public int Floor { get; set; }
    public int NativeLevel { get; set; }
    public string NodeId { get; set; } = "";
    public string NodeKind { get; set; } = "";
    public int GazeLevelAtEnqueue { get; set; }
    public string Source { get; set; } = "";
}

[Serializable]
public sealed class EndlessSeaShockMessage
{
    public EndlessSeaShockRequestMessage Request { get; set; } = new();
    public List<string> Options { get; set; } = new();
    public string Source { get; set; } = "";
    public string Token { get; set; } = "";
}

public interface IEndlessSeaNetworkPort
{
    bool HasRemotePlayers { get; }
    bool IsMultiplayerSession { get; }
    bool IsClientOnly { get; }
    bool SendSnapshotPublisher(string source);
    bool SendSnapshotRequest(EndlessSeaSnapshotRequest request, string source);
    bool SendShockResolution(
        EndlessSeaShockMessage resolution,
        EndlessSeaStateSnapshot snapshot,
        string source);
}
