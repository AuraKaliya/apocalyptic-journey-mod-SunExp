using System;
using StarExp.Dll.GameApi;
using StarExp.Dll.Infrastructure;
using StarExp.Dll.Mechanics;

namespace StarExp.Dll.Scripting;

public static class StarMiracleScripts
{
    public static void InitCareer(ScriptExecutor self)
    {
        try
        {
            StarMiracleService.RegisterCareer(self);
        }
        catch (Exception ex)
        {
            StarExpLog.Error("StarMiracle InitCareer failed", ex);
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
            StarExpLog.Error("StarMiracle Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "*star_morning_guidance":
                    UseMorningGuidance(self);
                    break;
                case "*star_borrowed_miracle":
                    UseBorrowedMiracle(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            StarExpLog.Error("StarMiracle Use failed: " + id, ex);
        }
    }

    private static void UseMorningGuidance(ScriptExecutor self)
    {
        if (PlayerApi.GetSkillTime(StarExpIds.MorningStarSkillCardId) > 0)
        {
            PlayerApi.ShowCaption("晨星指引尚未冷却。");
            return;
        }

        StarMiracleService.TriggerNaturalMorningStar(self);
        PlayerApi.SetSkillTime(StarExpIds.MorningStarSkillCardId, 5);
    }

    private static void UseBorrowedMiracle(ScriptExecutor self)
    {
        if (PlayerApi.GetSkillTime(StarExpIds.BorrowedMiracleSkillCardId) > 0)
        {
            PlayerApi.ShowCaption("借来的奇迹尚未冷却。");
            return;
        }

        StarMiracleService.TriggerBorrowedMiracle(self);
        PlayerApi.SetSkillTime(StarExpIds.BorrowedMiracleSkillCardId, 4);
    }
}
