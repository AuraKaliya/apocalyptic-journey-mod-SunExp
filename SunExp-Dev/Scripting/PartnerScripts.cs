using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class PartnerScripts
{
    public static void Fight(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "dusk":
                    RegisterDuskAfterheatRecovery(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Partner Fight failed: " + id, ex);
        }
    }

    private static void RegisterDuskAfterheatRecovery(ScriptExecutor self)
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

                var listenerKey = DuskListenerKey(targetId);
                if (ExecutorApi.GetVar(self, listenerKey, "0") == "1")
                {
                    continue;
                }

                ExecutorApi.SetVar(self, listenerKey, "1");
                EventCenter.Instance.AddEventListener("StartRound" + targetId, new Action(() => GrantEmberFromBurnTrigger(self, target, token)), self, EventDispose.OnFightEnd);
            }
        }

        self.AddEvent("FightStart", new Action(RegisterEnemyBurnTriggers));
        self.AddEvent("Action", new Action(RegisterEnemyBurnTriggers));
        self.AddEvent("StartRound", new Action(RegisterEnemyBurnTriggers));
        RegisterEnemyBurnTriggers();
    }

    private static void GrantEmberFromBurnTrigger(ScriptExecutor self, IStatusManager target, string token)
    {
        if (!ExecutorApi.IsHookTokenActive(self, "SunExpDuskAfterheatToken", token))
        {
            return;
        }

        var burn = ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn);
        var gain = burn / 2;
        if (gain <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(SunExpIds.Ember, gain.ToString());
        BuffApi.SyncEmberDamageBonus(self, self.Self);
    }

    private static string DuskListenerKey(string targetId)
    {
        return "SunExpDuskBurnListener_" + targetId;
    }
}
