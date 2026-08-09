using System;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SandroneCatMaxHpService
{
    private static readonly SandroneCatBattleState BattleState = new();

    public static bool ApplyBattleStart(IStatusManager? status)
    {
        if (status == null)
        {
            return false;
        }

        var sessionId = AuraBattleLifecycleRouter.EnsureBattleSession();
        var ownerId = OwnerId(status);
        if (!BattleState.TryMarkStarted(sessionId, ownerId))
        {
            return false;
        }

        TerriasLog.Info("[SandroneCat] combat registered: session=" + sessionId + ".");
        return true;
    }

    public static bool ApplyBattleEnd(IStatusManager? status)
    {
        if (status == null)
        {
            return false;
        }

        var sessionId = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        var ownerId = OwnerId(status);
        if (!BattleState.TryMarkEnded(sessionId, ownerId))
        {
            return false;
        }

        var oldMaxHp = Math.Max(1, status.MaxHp);
        var nextMaxHp = SandroneCatMaxHpFormula.MaxHpAfterEnd(oldMaxHp);
        if (PlayerMaxHpApi.TrySetNativeMaxHp(
                status,
                nextMaxHp,
                persistRole: true,
                "FightEnding"))
        {
            TerriasLog.Info("[SandroneCat] combat-end growth applied: session="
                            + sessionId
                            + ", gain="
                            + (nextMaxHp - oldMaxHp)
                            + ".");
            return true;
        }

        BattleState.ReleaseEnd(sessionId, ownerId);
        return false;
    }

    private static string OwnerId(IStatusManager status)
    {
        return string.IsNullOrWhiteSpace(status.InstanceId)
            ? status.GetHashCode().ToString()
            : status.InstanceId;
    }
}
