using System;

namespace SunExp.Dll.Mechanics;

public static class CompanionPositionOwnershipService
{
    public static bool HasForOwner(string ownerPlayerId, string ownerStatusId = "")
    {
        return ProjectionStateStore.HasForOwner(ownerPlayerId, ownerStatusId)
            || SpiritStateStore.HasForOwner(ownerPlayerId, ownerStatusId);
    }

    public static bool IsCompanion(IStatusManager? status)
    {
        return status != null && (ProjectionStateStore.IsProjection(status) || SpiritStateStore.IsSpirit(status));
    }
}
