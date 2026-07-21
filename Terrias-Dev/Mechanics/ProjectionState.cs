namespace Terrias.Dll.Mechanics;

public sealed class ProjectionState
{
    public ProjectionState(string statusId, string ownerStatusId, string roleId, string displayName, ProjectionOtherObj projection, int slotIndex, string ownerPlayerId = "")
    {
        StatusId = statusId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = ownerPlayerId ?? "";
        RoleId = roleId ?? "";
        DisplayName = displayName ?? "";
        Projection = projection;
        SlotIndex = slotIndex;
    }

    public string StatusId { get; }

    public string OwnerStatusId { get; }

    public string OwnerPlayerId { get; }

    public string RoleId { get; }

    public string DisplayName { get; }

    public ProjectionOtherObj Projection { get; }

    public int SlotIndex { get; }

    public bool IsSuspended { get; private set; }

    public void SetSuspended(bool suspended)
    {
        IsSuspended = suspended;
    }
}
