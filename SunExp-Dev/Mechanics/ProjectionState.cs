namespace SunExp.Dll.Mechanics;

public sealed class ProjectionState
{
    public ProjectionState(string statusId, string ownerStatusId, string roleId, string displayName, ProjectionOtherObj projection)
    {
        StatusId = statusId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        RoleId = roleId ?? "";
        DisplayName = displayName ?? "";
        Projection = projection;
    }

    public string StatusId { get; }

    public string OwnerStatusId { get; }

    public string RoleId { get; }

    public string DisplayName { get; }

    public ProjectionOtherObj Projection { get; }
}
