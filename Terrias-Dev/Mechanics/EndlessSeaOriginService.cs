using System;
using Data.Save;
using Terrias.Dll.Infrastructure;
using Witch;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaOriginService
{
    public const int OriginCap = 50;

    public static void EnsureOriginCaps(string source)
    {
        try
        {
            var role = RoleTable.Instance;
            if (role == null)
            {
                return;
            }

            var changed = false;
            if (role.MainVarUpperBound < OriginCap)
            {
                role.MainVarUpperBound = OriginCap;
                changed = true;
            }

            if (role.SecondaryVarUpperBound < OriginCap)
            {
                role.SecondaryVarUpperBound = OriginCap;
                changed = true;
            }

            if (role.OtherVarUpperBound < OriginCap)
            {
                role.OtherVarUpperBound = OriginCap;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            GameSaveManager.UpdateRoles(role);
            TerriasLog.Info("[EndlessSeaOrigin] raised origin caps to 50 from " + source + ".");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessSeaOrigin] origin cap update failed from " + source + ": " + ex.Message);
        }
    }
}
