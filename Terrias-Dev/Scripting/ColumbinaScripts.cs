using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class ColumbinaScripts
{
    public static void InitCareer(ScriptExecutor self)
    {
        try
        {
            PlayerApi.SetSkillTime(TerriasIds.ColumbinaEternalTideCardId, 0);
            PlayerApi.SetSkillTime(TerriasIds.ColumbinaHomesicknessCardId, 0);
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Columbina InitCareer failed", ex);
        }
    }

    public static void Init(ScriptExecutor self, string id)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
        ScriptDelegateApi.BindParameterized(self, "InitScript", id, Init);
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            if (self == null
                || !PolymorphStateStore.IsEffectiveCombatRoleFor(self.Self, "columbina"))
            {
                PlayerApi.ShowCaption("当前化身无法使用哥伦比娅的技能。");
                return;
            }

            switch (id)
            {
                case "*columbina_eternal_tide":
                    UseEternalTide(self);
                    break;
                case "*columbina_homesickness":
                    UseHomesickness(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Columbina skill failed: " + id, ex);
        }
    }

    private static void UseEternalTide(ScriptExecutor self)
    {
        if (PlayerApi.GetSkillTime(TerriasIds.ColumbinaEternalTideCardId) > 0)
        {
            PlayerApi.ShowCaption("万古潮汐尚未冷却。");
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(TerriasIds.GravityRipple, "20");
        PlayerApi.SetSkillTime(TerriasIds.ColumbinaEternalTideCardId, 3);
    }

    private static void UseHomesickness(ScriptExecutor self)
    {
        if (PlayerApi.GetSkillTime(TerriasIds.ColumbinaHomesicknessCardId) > 0)
        {
            PlayerApi.ShowCaption("她的乡愁尚未冷却。");
            return;
        }

        var damage = Math.Max(1, StatusApi.MaxHp(self.Self) * 30 / 100);
        var targets = TargetApi.OpposingSideTargets(self, self.Self).ToArray();
        ElementalReactionService.HitAll(self, targets, ElementalType.Hydro, damage, "Columbina.Homesickness");

        FieldApi.ActivateField(self, TerriasFieldId.MoonDomain, 1, "Columbina.Homesickness");
        PlayerApi.SetSkillTime(TerriasIds.ColumbinaHomesicknessCardId, 7);
    }
}
