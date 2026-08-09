using System;
using Data.Save;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class PlayerMaxHpApi
{
    public static bool TrySetNativeMaxHp(
        IStatusManager? status,
        int nextMaxHp,
        bool persistRole,
        string source)
    {
        if (status == null
            || !string.Equals(status.fatherObject?.GetType().Name, "FightPlayer", StringComparison.Ordinal))
        {
            return false;
        }

        var oldMaxHp = Math.Max(1, status.MaxHp);
        var next = Math.Max(1, nextMaxHp);
        try
        {
            // The native setter also restores current HP by a positive delta and
            // synchronizes RoleTable.MaxSan. Do not add a second heal here.
            status.MaxHp = next;
            status.UpdateStatus(true);
            if (persistRole && RoleTable.Instance != null)
            {
                GameSaveManager.UpdateRoles(RoleTable.Instance);
            }

            TerriasLog.Info("[PlayerMaxHp] native max HP change from "
                            + source
                            + ": "
                            + oldMaxHp
                            + "->"
                            + next
                            + ", owner="
                            + (status.InstanceId ?? "")
                            + ".");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[PlayerMaxHp] native max HP change failed from "
                            + source
                            + ": "
                            + ex.Message);
            return false;
        }
    }
}
