using System;
using Data.Save;
using Newtonsoft.Json;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerFloorPlanStore
{
    public static TongtianTowerFloorPlan? Load()
    {
        var json = GetSaveValue(SunExpIds.TongtianTowerFloorPlanKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var plan = JsonConvert.DeserializeObject<TongtianTowerFloorPlan>(json);
            plan?.Normalize();
            return plan != null && plan.IsValid ? plan : null;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerMap] ignored invalid floor plan: " + ex.Message);
            return null;
        }
    }

    public static bool TryLoad(int floor, out TongtianTowerFloorPlan plan)
    {
        plan = Load()!;
        return plan != null && plan.Floor == Math.Max(1, floor) && plan.IsValid;
    }

    public static void Save(TongtianTowerFloorPlan plan)
    {
        plan.Normalize();
        SetSaveValue(SunExpIds.TongtianTowerFloorPlanKey, JsonConvert.SerializeObject(plan));
    }

    private static string GetSaveValue(string key)
    {
        try
        {
            return GameSaveManager.GetValue<string>(key) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void SetSaveValue(string key, string value)
    {
        try
        {
            GameSaveManager.GetNowSave()?.SetValue(key, value);
        }
        catch
        {
            GameSaveManager.SetValue(key, value);
        }
    }
}
