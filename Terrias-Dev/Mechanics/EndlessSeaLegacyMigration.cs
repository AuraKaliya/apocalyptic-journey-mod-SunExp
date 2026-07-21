using System;
using System.Collections.Generic;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class EndlessSeaLegacyMigration
{
    private const string LegacyNamePrefix = "SunExpTongtianTower";
    private const string LegacyModeType = "SunExpTongtianTower";

    private static readonly IReadOnlyList<KeyPair> SaveKeyPairs = new[]
    {
        Pair("SunExp_TongtianTowerMode", SunExpIds.EndlessSeaModeKey),
        Pair("SunExp_TongtianTowerFloor", SunExpIds.EndlessSeaFloorKey),
        Pair("SunExp_TongtianTowerGeneratedFloor", SunExpIds.EndlessSeaGeneratedFloorKey),
        Pair("SunExp_TongtianTowerSeed", SunExpIds.EndlessSeaSeedKey),
        Pair("SunExp_TongtianTowerFloorPlan", SunExpIds.EndlessSeaFloorPlanKey),
        Pair("SunExp_TongtianTowerIntroSeen", SunExpIds.EndlessSeaIntroSeenKey),
        Pair("SunExp_TongtianTowerStarterDeckApplied", SunExpIds.EndlessSeaStarterDeckAppliedKey),
        Pair("SunExp_TongtianTowerStarterDeckMode", SunExpIds.EndlessSeaStarterDeckModeKey),
        Pair("SunExp_TongtianTowerRunId", SunExpIds.EndlessSeaRunIdKey),
        Pair("SunExp_TongtianTowerRunVersion", SunExpIds.EndlessSeaRunVersionKey),
        Pair("SunExp_TongtianTowerRunPhase", SunExpIds.EndlessSeaRunPhaseKey),
        Pair("SunExp_TongtianTowerRunEnded", SunExpIds.EndlessSeaRunEndedKey),
        Pair("SunExp_TongtianTowerRunUpdatedAt", SunExpIds.EndlessSeaRunUpdatedAtKey)
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
            SunExpLog.Debug("[EndlessSeaLegacy] current save migration skipped from " + source + ": " + ex.Message);
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
            saveInfo.modeType = SunExpIds.NativeNormalModeType;
            changed = true;
        }

        if (changed)
        {
            SunExpLog.Info("[EndlessSeaLegacy] migrated legacy save keys from " + source + "; save=" + saveInfo.Name + ".");
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
            && saveInfo.GameVars.TryGetValue("SunExp_TongtianTowerMode", out var mode)
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
