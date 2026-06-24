using System;
using System.Collections.Generic;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class DamageMeterPersistence
{
    private const string SaveKey = "AuraTools.DamageMeter.History.v1";

    public static void Save(DamageHistoryStore history)
    {
        try
        {
            GameSaveManager.SetValue(SaveKey, AuraSharedJson.Serialize(history.CreateSnapshot()));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] history save failed: " + ex.Message);
        }
    }

    public static IReadOnlyList<DamageFightRecord> Load()
    {
        try
        {
            var json = GameSaveManager.GetValue<string>(SaveKey);
            return string.IsNullOrWhiteSpace(json)
                ? Array.Empty<DamageFightRecord>()
                : AuraSharedJson.Deserialize<List<DamageFightRecord>>(json)
                  ?? new List<DamageFightRecord>();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] history load failed: " + ex.Message);
            return Array.Empty<DamageFightRecord>();
        }
    }

    public static void Clear()
    {
        try
        {
            GameSaveManager.SetValue(SaveKey, "");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] history clear failed: " + ex.Message);
        }
    }
}
