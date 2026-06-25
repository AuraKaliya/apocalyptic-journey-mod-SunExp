using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class DuskPartnerScripts
{
    public static void ApplyTrait(ScriptExecutor self)
    {
        try
        {
            var token = ExecutorApi.RegisterHook(self, "SunExpDuskAfterheatHook", "SunExpDuskAfterheatToken");
            if (token == null)
            {
                return;
            }

            void RegisterEnemyBurnTriggers()
            {
                if (!ExecutorApi.IsHookTokenActive(self, "SunExpDuskAfterheatToken", token))
                {
                    return;
                }

                foreach (var target in ExecutorApi.EnemyTargets(self))
                {
                    var targetId = target.InstanceId;
                    if (string.IsNullOrWhiteSpace(targetId))
                    {
                        continue;
                    }

                    var listenerKey = "SunExpDuskBurnListener_" + targetId + "_" + token;
                    if (ExecutorApi.GetVar(self, listenerKey, "0") == "1")
                    {
                        continue;
                    }

                    ExecutorApi.SetVar(self, listenerKey, "1");
                    EventCenter.Instance.AddEventListener("StartRound" + targetId, new Action(() => GrantEmberFromBurn(self, target, token)), self, EventDispose.OnFightEnd);
                }
            }

            ExecutorApi.TryAddTokenedEvent(self, "FightStart", "SunExpDuskAfterheatToken", token, new Action(RegisterEnemyBurnTriggers), "dusk_afterheat");
            ExecutorApi.TryAddTokenedEvent(self, "Action", "SunExpDuskAfterheatToken", token, new Action(RegisterEnemyBurnTriggers), "dusk_afterheat");
            ExecutorApi.TryAddTokenedEvent(self, "StartRound", "SunExpDuskAfterheatToken", token, new Action(RegisterEnemyBurnTriggers), "dusk_afterheat");
            RegisterEnemyBurnTriggers();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dusk trait apply failed", ex);
        }
    }

    public static void ClearTrait(ScriptExecutor self)
    {
        try
        {
            ExecutorApi.ClearHook(self, "SunExpDuskAfterheatHook", "SunExpDuskAfterheatToken");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dusk trait clear failed", ex);
        }
    }

    private static void GrantEmberFromBurn(ScriptExecutor self, IStatusManager target, string token)
    {
        if (!ExecutorApi.IsHookTokenActive(self, "SunExpDuskAfterheatToken", token))
        {
            return;
        }

        var gain = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn) / 2;
        if (gain <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(SunExpIds.Ember, gain.ToString());
        BuffApi.SyncEmberDamageBonus(self, self.Self);
    }
}
