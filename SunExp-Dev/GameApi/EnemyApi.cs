using System;
using SunExp.Dll.Infrastructure;
using Witch;

namespace SunExp.Dll.GameApi;

public static class EnemyApi
{
    public static bool CanAddDynamicEnemyAuthoritatively()
    {
        try
        {
            var playerManager = PlayerManager.Instance;
            return playerManager == null || playerManager.isServer;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsClientOnlyDynamicEnemyObserver()
    {
        try
        {
            var playerManager = PlayerManager.Instance;
            return playerManager != null && playerManager.isClient && !playerManager.isServer;
        }
        catch
        {
            return false;
        }
    }

    public static bool AddDynamicEnemyAuthoritative(string? enemyId, string source)
    {
        if (string.IsNullOrWhiteSpace(enemyId))
        {
            return false;
        }

        if (!CanAddDynamicEnemyAuthoritatively())
        {
            SunExpLog.Debug("[EnemyApi] skipped dynamic enemy add on non-authoritative client: "
                + enemyId
                + "; source="
                + source);
            return false;
        }

        try
        {
            var manager = EnemyManager.Instance;
            if (manager == null)
            {
                SunExpLog.Warn("[EnemyApi] dynamic enemy add skipped because EnemyManager is unavailable: "
                    + enemyId
                    + "; source="
                    + source);
                return false;
            }

            manager.AddEnemy(enemyId);
            SunExpLog.Info("[EnemyApi] authoritative dynamic enemy add: "
                + enemyId
                + "; source="
                + source);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EnemyApi] dynamic enemy add failed: "
                + enemyId
                + "; source="
                + source
                + "; error="
                + ex.Message);
            return false;
        }
    }
}
