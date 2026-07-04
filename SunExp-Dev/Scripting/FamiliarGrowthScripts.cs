using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

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

    public static void GrantSelectedExperience(int amount)
    {
        try
        {
            var result = FamiliarGrowthApi.GrantSelectedExperience(Math.Max(0, amount));
            if (result == null)
            {
                PlayerApi.ShowCaption("\u4f7f\u9b54\u6210\u957f\uff1a\u672a\u9009\u62e9\u968f\u884c\u4e2a\u4f53\u3002");
                return;
            }

            PlayerApi.ShowCaption(result.Value.LeveledUp
                ? "\u4f7f\u9b54\u6210\u957f\uff1a" + result.Value.Instance.Name + " Lv." + result.Value.Instance.Level
                : "\u4f7f\u9b54\u6210\u957f\uff1a\u7ecf\u9a8c+" + result.Value.GainedExperience);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Familiar selected experience grant failed", ex);
        }
    }

    public static void GrantExperience(string instanceId, int amount)
    {
        try
        {
            FamiliarGrowthApi.GrantExperience(instanceId, Math.Max(0, amount));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Familiar experience grant failed: " + instanceId, ex);
        }
    }

    public static void CreateInstance(string speciesId)
    {
        try
        {
            var instance = FamiliarGrowthApi.Create(speciesId);
            if (instance != null)
            {
                PlayerApi.ShowCaption("\u4f7f\u9b54\u6210\u957f\uff1a\u767b\u8bb0 " + instance.Name);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Familiar instance creation failed: " + speciesId, ex);
        }
    }

    public static bool SelectedCanManifest()
    {
        try
        {
            return FamiliarGrowthApi.SelectedCanManifest();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Familiar manifest capability check failed", ex);
            return false;
        }
    }
}
