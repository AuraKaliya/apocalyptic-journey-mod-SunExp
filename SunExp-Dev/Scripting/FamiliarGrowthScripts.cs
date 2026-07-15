using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class FamiliarGrowthScripts
{
    public static void OpenPanel()
    {
        try
        {
            FamiliarGrowthApi.OpenPanel();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Familiar growth panel failed to open", ex);
        }
    }

    public static void GrantActiveExperience(int amount)
    {
        try
        {
            var result = FamiliarGrowthApi.GrantActiveExperience(Math.Max(0, amount));
            if (result == null)
            {
                PlayerApi.ShowCaption("使魔成长：当前未选择原生使魔。");
                return;
            }

            PlayerApi.ShowCaption(result.Value.LeveledUp
                ? "使魔成长：" + result.Value.Instance.Name + " Lv." + result.Value.Instance.Level
                : "使魔成长：经验+" + result.Value.GainedExperience);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Familiar active experience grant failed", ex);
        }
    }

    public static void GrantExperience(string partnerId, int amount)
    {
        try
        {
            FamiliarGrowthApi.GrantExperience(partnerId, Math.Max(0, amount));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Familiar experience grant failed: " + partnerId, ex);
        }
    }

    public static bool Rebirth(string partnerId)
    {
        try
        {
            return FamiliarGrowthApi.Rebirth(partnerId) != null;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Familiar rebirth failed: " + partnerId, ex);
            return false;
        }
    }
}
