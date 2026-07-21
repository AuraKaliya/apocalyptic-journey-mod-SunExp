using System;
using Data.Save;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaFloorPlanStore
{
    public static EndlessSeaFloorPlan? Load()
    {
        var json = GetSaveValue(TerriasIds.EndlessSeaFloorPlanKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var plan = JsonConvert.DeserializeObject<EndlessSeaFloorPlan>(json);
            plan?.Normalize();
            return plan != null && plan.IsValid ? plan : null;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessSeaMap] ignored invalid floor plan: " + ex.Message);
            return null;
        }
    }

    public static bool TryLoad(int floor, out EndlessSeaFloorPlan plan)
    {
        plan = Load()!;
        return plan != null && plan.Floor == Math.Max(1, floor) && plan.IsValid;
    }

    public static void Save(EndlessSeaFloorPlan plan)
    {
        plan.Normalize();
        SetSaveValue(TerriasIds.EndlessSeaFloorPlanKey, JsonConvert.SerializeObject(plan));
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
