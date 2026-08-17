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
        long initialStateRevision = 1L)
    {
        StatusId = statusId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = ownerPlayerId ?? "";
        RoleId = roleId ?? "";
        Projection = projection;
        SlotIndex = slotIndex;
        Replication = new ProjectionReplicationClock(generation, initialStateRevision);
        RemoteTurnGate = new ProjectionRemoteTurnGate();
    }

    public string StatusId { get; }

    public string OwnerStatusId { get; }

    public string OwnerPlayerId { get; }

    public string RoleId { get; }

    public ProjectionOtherObj Projection { get; }

    public int SlotIndex { get; }

    public ProjectionReplicationClock Replication { get; }

    public ProjectionRemoteTurnGate RemoteTurnGate { get; }

    public bool IsSuspended { get; private set; }

    public void SetSuspended(bool suspended)
    {
        IsSuspended = suspended;
    }
}
