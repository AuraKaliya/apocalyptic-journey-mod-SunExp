using System;
using System.Collections.Generic;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class DamageMeterPersistence
{
    private const string LegacyHistorySaveKey = "AuraTools.DamageMeter.History.v1";
    private const string AdventureIdSaveKey = "AuraTools.DamageMeter.AdventureId.v2";

    public static void SaveAdventureId(string adventureId)
    {
        try
        {
            GameSaveManager.SetValue(AdventureIdSaveKey, adventureId ?? "");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] adventure id save failed: " + ex.Message);
        }
    }

    public static string LoadAdventureId()
    {
        try
        {
            return GameSaveManager.GetValue<string>(AdventureIdSaveKey) ?? "";
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] adventure id load failed: " + ex.Message);
            return "";
        }
    }

    public static IReadOnlyList<DamageFightRecord> LoadLegacyHistory()
    {
        try
        {
            var json = GameSaveManager.GetValue<string>(LegacyHistorySaveKey);
            return string.IsNullOrWhiteSpace(json)
                ? Array.Empty<DamageFightRecord>()
                : AuraSharedJson.Deserialize<List<DamageFightRecord>>(json)
                  ?? new List<DamageFightRecord>();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] legacy history load failed: " + ex.Message);
            return Array.Empty<DamageFightRecord>();
        }
    }

    public static void ClearLegacyHistory()
    {
        try
        {
            GameSaveManager.SetValue(LegacyHistorySaveKey, "");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] legacy history clear failed: " + ex.Message);
        }
    }
}
