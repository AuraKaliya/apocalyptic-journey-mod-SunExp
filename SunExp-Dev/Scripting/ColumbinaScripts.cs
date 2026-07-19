using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class ColumbinaScripts
{
    public static void InitCareer(ScriptExecutor self)
    {
        try
        {
            PlayerApi.SetSkillTime(SunExpIds.ColumbinaEternalTideCardId, 0);
            PlayerApi.SetSkillTime(SunExpIds.ColumbinaHomesicknessCardId, 0);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Columbina InitCareer failed", ex);
        }
    }

    public static void Init(ScriptExecutor self, string id)
    {
        ExecutorApi.SetBaseScript(self, "CommonCardItem");
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            if (!ConstellationPoolCatalog.IsColumbina(PlayerApi.GetCurrentCareerId()))
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
            SunExpLog.Error("Columbina skill failed: " + id, ex);
        }
    }

    private static void UseEternalTide(ScriptExecutor self)
    {
        if (PlayerApi.GetSkillTime(SunExpIds.ColumbinaEternalTideCardId) > 0)
        {
            PlayerApi.ShowCaption("万古潮汐尚未冷却。");
            return;
        }

        AudioApi.PlayColumbinaEternalTide();
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.GravityRipple, "20");
        PlayerApi.SetSkillTime(SunExpIds.ColumbinaEternalTideCardId, 3);
    }

    private static void UseHomesickness(ScriptExecutor self)
    {
        if (PlayerApi.GetSkillTime(SunExpIds.ColumbinaHomesicknessCardId) > 0)
        {
            PlayerApi.ShowCaption("她的乡愁尚未冷却。");
            return;
        }

        AudioApi.PlayColumbinaHomesickness();
        var damage = Math.Max(1, StatusApi.MaxHp(self.Self) * 30 / 100);
        var targets = TargetApi.OpposingSideTargets(self, self.Self).ToArray();
        ElementalReactionService.HitAll(self, targets, ElementalType.Hydro, damage, "Columbina.Homesickness");

        FieldApi.ActivateField(self, SunExpFieldId.MoonDomain, 1, "Columbina.Homesickness");
        PlayerApi.SetSkillTime(SunExpIds.ColumbinaHomesicknessCardId, 7);
    }
}
