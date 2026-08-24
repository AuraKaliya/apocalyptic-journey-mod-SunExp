using System;

namespace Terrias.Dll.Mechanics;

public sealed class ProjectionState
{
    public ProjectionState(
        string statusId,
        string ownerStatusId,
        string roleId,
        ProjectionOtherObj projection,
        int slotIndex,
        string ownerPlayerId = "",
        string generation = "",
        long initialStateRevision = 1L,
        int summonRoundSequence = 0,
        string summonTurnToken = "",
        long summonTurnOrder = 0)
    {
        StatusId = statusId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = ownerPlayerId ?? "";
        RoleId = roleId ?? "";
        Projection = projection;
        SlotIndex = slotIndex;
        Replication = new ProjectionReplicationClock(generation, initialStateRevision);
        RemoteTurnGate = new ProjectionRemoteTurnGate();
        SummonRoundSequence = Math.Max(0, summonRoundSequence);
        SummonTurnToken = summonTurnToken ?? "";
        SummonTurnOrder = Math.Max(0L, summonTurnOrder);
    }

    public string StatusId { get; }

    public string OwnerStatusId { get; }

    public string OwnerPlayerId { get; }

    public string RoleId { get; }

    public ProjectionOtherObj Projection { get; }

    public int SlotIndex { get; }

    public ProjectionReplicationClock Replication { get; }

    public ProjectionRemoteTurnGate RemoteTurnGate { get; }

    public int SummonRoundSequence { get; }

    public string SummonTurnToken { get; }

    public long SummonTurnOrder { get; }

    public bool IsSuspended { get; private set; }

    public void SetSuspended(bool suspended)
    {
        IsSuspended = suspended;
    }
}
