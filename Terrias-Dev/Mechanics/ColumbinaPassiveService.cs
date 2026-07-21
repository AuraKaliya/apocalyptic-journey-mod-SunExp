using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

public static class ColumbinaPassiveService
{
    public static bool IsActive(IStatusManager? status)
    {
        return status != null
            && ConstellationPoolCatalog.IsColumbina(
                PolymorphStateStore.EffectiveCombatRoleIdFor(status));
    }
}
