using System;
using GoldExp.Dll.GameApi;
using GoldExp.Dll.Infrastructure;
using GoldExp.Dll.Mechanics;

namespace GoldExp.Dll.Scripting;

public static class GoldWitchScripts
{
    public static void InitCareer(ScriptExecutor self)
    {
        try
        {
            GoldDreamService.RegisterCareer(self);
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("GoldWitch InitCareer failed", ex);
        }
    }

    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, "CommonCardItem");
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("GoldWitch Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "*goldwitch_midas_contract":
                    UseMidasContract(self);
                    break;
                case "*goldwitch_final_audit":
                    UseFinalAudit(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            GoldExpLog.Error("GoldWitch Use failed: " + id, ex);
        }
    }

    private static void UseMidasContract(ScriptExecutor self)
    {
        if (!TryUseCooldown(GoldExpIds.MidasContractCardId, 3, "点金梦契尚未冷却。"))
        {
            return;
        }

        if (GoldDreamService.PayGold(self, 25))
        {
            GoldDreamService.AddFalseGold(self, 5);
            self.SetStatus("Self");
            self.DrawCount("1");
        }
        else
        {
            GoldDreamService.AddFalseGold(self, 4);
            GoldDreamService.AddDebt(self, 3);
        }
    }

    private static void UseFinalAudit(ScriptExecutor self)
    {
        if (!TryUseCooldown(GoldExpIds.FinalAuditCardId, 4, "黄金清算尚未冷却。"))
        {
            return;
        }

        var falseGold = GoldDreamService.ConsumeAllFalseGold(self);
        ExecutorApi.DealDamageAllEnemies(self, 10 + falseGold * 3);

        var debt = GoldDreamService.Debt(self);
        if (debt > 0)
        {
            GoldDreamService.SettleDebt(self, (debt + 1) / 2, removeSettledStacks: true);
        }
    }

    private static bool TryUseCooldown(string key, int cooldown, string message)
    {
        if (PlayerApi.GetSkillTime(key) > 0)
        {
            PlayerApi.ShowCaption(message);
            return false;
        }

        PlayerApi.SetSkillTime(key, cooldown);
        return true;
    }
}
