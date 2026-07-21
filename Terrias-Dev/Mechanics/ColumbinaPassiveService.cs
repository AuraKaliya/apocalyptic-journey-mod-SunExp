using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public static class ColumbinaPassiveService
{
    public static bool IsActive(IStatusManager? status)
    {
        return status != null
            && !PolymorphStateStore.IsRoleSuppressedFor(status, "columbina")
            && ConstellationPoolCatalog.IsColumbina(StatusApi.RoleId(status));
    }
}
