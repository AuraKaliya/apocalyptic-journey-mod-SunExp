using System;
using System.Collections.Generic;
using Data.Save;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class EndlessSeaLegacyMigration
{
    private const string LegacyNamePrefix = "TerriasTongtianTower";
    private const string LegacyModeType = "TerriasTongtianTower";

    private static readonly IReadOnlyList<KeyPair> SaveKeyPairs = new[]
    {
        Pair("Terrias_TongtianTowerMode", TerriasIds.EndlessSeaModeKey),
        Pair("Terrias_TongtianTowerFloor", TerriasIds.EndlessSeaFloorKey),
        Pair("Terrias_TongtianTowerGeneratedFloor", TerriasIds.EndlessSeaGeneratedFloorKey),
        Pair("Terrias_TongtianTowerSeed", TerriasIds.EndlessSeaSeedKey),
        Pair("Terrias_TongtianTowerFloorPlan", TerriasIds.EndlessSeaFloorPlanKey),
        Pair("Terrias_TongtianTowerIntroSeen", TerriasIds.EndlessSeaIntroSeenKey),
        Pair("Terrias_TongtianTowerStarterDeckApplied", TerriasIds.EndlessSeaStarterDeckAppliedKey),
        Pair("Terrias_TongtianTowerStarterDeckMode", TerriasIds.EndlessSeaStarterDeckModeKey),
        Pair("Terrias_TongtianTowerRunId", TerriasIds.EndlessSeaRunIdKey),
        Pair("Terrias_TongtianTowerRunVersion", TerriasIds.EndlessSeaRunVersionKey),
        Pair("Terrias_TongtianTowerRunPhase", TerriasIds.EndlessSeaRunPhaseKey),
        Pair("Terrias_TongtianTowerRunEnded", TerriasIds.EndlessSeaRunEndedKey),
        Pair("Terrias_TongtianTowerRunUpdatedAt", TerriasIds.EndlessSeaRunUpdatedAtKey)
    };

    public static bool MigrateCurrentSave(string source)
    {
        try
        {
            var saveInfo = GameSaveManager.GetNowSave();
            var changed = MigrateSaveInfo(saveInfo, source);
            if (changed)
            {
                foreach (var pair in SaveKeyPairs)
                {
                    if (saveInfo?.GameVars == null
                        || !saveInfo.GameVars.TryGetValue(pair.NewKey, out var value)
                        || string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    try
                    {
                        GameSaveManager.SetValue(pair.NewKey, value);
                    }
                    catch
                    {
                        // SaveInfo mutation above is enough for paths that do not expose SetValue.
                    }
                }
            }

            return changed;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[EndlessSeaLegacy] current save migration skipped from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static bool MigrateSaveInfo(SaveInfo? saveInfo, string source)
    {
        if (saveInfo?.GameVars == null || !IsLegacySave(saveInfo))
        {
            return false;
        }

        var changed = false;
        foreach (var pair in SaveKeyPairs)
        {
            if (!saveInfo.GameVars.TryGetValue(pair.OldKey, out var value)
                || string.IsNullOrWhiteSpace(value)
                || saveInfo.GameVars.ContainsKey(pair.NewKey))
            {
                continue;
            }

            saveInfo.GameVars[pair.NewKey] = value;
            changed = true;
        }

        if (string.Equals(saveInfo.modeType, LegacyModeType, StringComparison.Ordinal))
        {
            saveInfo.modeType = TerriasIds.NativeNormalModeType;
            changed = true;
        }

        if (changed)
        {
            TerriasLog.Info("[EndlessSeaLegacy] migrated legacy save keys from " + source + "; save=" + saveInfo.Name + ".");
        }

        return changed;
    }

    public static bool IsLegacySave(SaveInfo? saveInfo)
    {
        if (saveInfo == null)
        {
            return false;
        }

        if (saveInfo.GameVars != null
            && saveInfo.GameVars.TryGetValue("Terrias_TongtianTowerMode", out var mode)
            && mode == "1")
        {
            return true;
        }

        return string.Equals(saveInfo.modeType, LegacyModeType, StringComparison.Ordinal)
            || (saveInfo.Name != null && saveInfo.Name.StartsWith(LegacyNamePrefix, StringComparison.Ordinal));
    }

    private static KeyPair Pair(string oldKey, string newKey)
    {
        return new KeyPair(oldKey, newKey);
    }

    private readonly struct KeyPair
    {
        public KeyPair(string oldKey, string newKey)
        {
            OldKey = oldKey;
            NewKey = newKey;
        }

        public string OldKey { get; }

        public string NewKey { get; }
    }
}
