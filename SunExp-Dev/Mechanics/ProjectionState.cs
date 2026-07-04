namespace SunExp.Dll.Mechanics;

public sealed class ProjectionState
{
    public ProjectionState(string statusId, string ownerStatusId, string roleId, string displayName, ProjectionOtherObj projection, int slotIndex)
    {
        StatusId = statusId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        RoleId = roleId ?? "";
        DisplayName = displayName ?? "";
        Projection = projection;
        SlotIndex = slotIndex;
    }

    public string StatusId { get; }

    public string OwnerStatusId { get; }

    public string RoleId { get; }

    public string DisplayName { get; }

    public ProjectionOtherObj Projection { get; }

    public int SlotIndex { get; }
}
