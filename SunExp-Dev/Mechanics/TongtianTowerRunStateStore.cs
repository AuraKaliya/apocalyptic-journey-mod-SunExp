using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class TongtianTowerRunPhase
{
    public const string Intro = "Intro";
    public const string MapPlanning = "MapPlanning";
    public const string InBattle = "InBattle";
    public const string Reward = "Reward";
    public const string BetweenFloors = "BetweenFloors";
    public const string Ended = "Ended";
}

public static class TongtianTowerRunStateStore
{
    private const string Version = "1";

    public static void InitializeNewRun(SaveInfo saveInfo, string seed)
    {
        if (saveInfo?.GameVars == null)
        {
            return;
        }

        saveInfo.modeType = SunExpIds.NativeNormalModeType;
        Set(saveInfo, SunExpIds.TongtianTowerModeKey, "1");
        Set(saveInfo, SunExpIds.TongtianTowerFloorKey, "1");
        Set(saveInfo, SunExpIds.TongtianTowerGeneratedFloorKey, "0");
        Set(saveInfo, SunExpIds.TongtianTowerSeedKey, seed ?? "");
        Set(saveInfo, SunExpIds.TongtianTowerFloorPlanKey, "");
        Set(saveInfo, SunExpIds.TongtianTowerIntroSeenKey, "0");
        Set(saveInfo, SunExpIds.TongtianTowerStarterDeckAppliedKey, "0");
        Set(saveInfo, SunExpIds.TongtianTowerStarterDeckModeKey, "");
        Set(saveInfo, SunExpIds.TongtianTowerRunIdKey, Guid.NewGuid().ToString("N"));
        Set(saveInfo, SunExpIds.TongtianTowerRunVersionKey, Version);
        Set(saveInfo, SunExpIds.TongtianTowerRunPhaseKey, TongtianTowerRunPhase.Intro);
        Set(saveInfo, SunExpIds.TongtianTowerRunEndedKey, "0");
        EndlessAbyssGazeService.Initialize(saveInfo);
        EndlessAbyssRunLedger.Initialize(saveInfo);
        Touch(saveInfo);
    }

    public static bool RepairCurrentRun(string source)
    {
        var saveInfo = GameSaveManager.GetNowSave();
        if (!IsTongtianSave(saveInfo))
        {
            return false;
        }

        return RepairSave(saveInfo, source);
    }

    public static bool RepairSave(SaveInfo? saveInfo, string source)
    {
        if (!IsTongtianSave(saveInfo) || saveInfo?.GameVars == null)
        {
            return false;
        }

        var changed = false;
        if (!string.Equals(saveInfo.modeType, SunExpIds.NativeNormalModeType, StringComparison.Ordinal))
        {
            var legacyModeType = saveInfo.modeType ?? "";
            saveInfo.modeType = SunExpIds.NativeNormalModeType;
            changed = true;
            SunExpLog.Info("[TongtianTowerRunState] migrated save mode from "
                + legacyModeType
                + " to "
                + SunExpIds.NativeNormalModeType
                + " from "
                + source
                + "; save="
                + saveInfo.Name
                + ".");
        }

        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerModeKey, "1");
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerFloorKey, "1");
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerGeneratedFloorKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerSeedKey, saveInfo.Seed ?? "");
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerFloorPlanKey, "");
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerIntroSeenKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerStarterDeckAppliedKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerStarterDeckModeKey, "");
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerRunIdKey, Guid.NewGuid().ToString("N"));
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerRunVersionKey, Version);
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerRunPhaseKey, DefaultPhase(saveInfo));
        changed |= Ensure(saveInfo, SunExpIds.TongtianTowerRunEndedKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssGazeLevelKey, EndlessAbyssGazeService.InitialLevel.ToString(CultureInfo.InvariantCulture));
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssLedgerKey, "{\"Entries\":[]}");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssPendingShockKey, "");

        if (changed)
        {
            Touch(saveInfo);
            SunExpLog.Info("[TongtianTowerRunState] repaired save from "
                + source
                + "; name="
                + saveInfo.Name
                + "; floor="
                + Value(saveInfo, SunExpIds.TongtianTowerFloorKey));
        }

        return changed;
    }

    public static void MarkPhase(string phase, string source)
    {
        var saveInfo = GameSaveManager.GetNowSave();
        if (!IsTongtianSave(saveInfo) || saveInfo?.GameVars == null || string.IsNullOrWhiteSpace(phase))
        {
            return;
        }

        if (Set(saveInfo, SunExpIds.TongtianTowerRunPhaseKey, phase))
        {
            Touch(saveInfo);
            SunExpLog.Debug("[TongtianTowerRunState] phase=" + phase + " from " + source);
        }
    }

    public static void MarkEnded(string source)
    {
        var saveInfo = GameSaveManager.GetNowSave();
        if (!IsTongtianSave(saveInfo) || saveInfo?.GameVars == null)
        {
            return;
        }

        Set(saveInfo, SunExpIds.TongtianTowerRunPhaseKey, TongtianTowerRunPhase.Ended);
        Set(saveInfo, SunExpIds.TongtianTowerRunEndedKey, "1");
        Touch(saveInfo);
        SunExpLog.Info("[TongtianTowerRunState] marked ended from " + source + ".");
    }

    public static SaveInfo? FindLatestUnfinishedRun()
    {
        try
        {
            return CandidateSaves()
                .Where(save => IsTongtianSave(save) && !IsEnded(save))
                .Select(save =>
                {
                    RepairSave(save, "TongtianTowerRunStateStore.FindLatestUnfinishedRun");
                    return save;
                })
                .OrderByDescending(SortStamp)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerRunState] find latest run failed: " + ex.Message);
            return null;
        }
    }

    public static bool IsTongtianSave(SaveInfo? saveInfo)
    {
        if (saveInfo == null)
        {
            return false;
        }

        if (saveInfo.GameVars != null
            && saveInfo.GameVars.TryGetValue(SunExpIds.TongtianTowerModeKey, out var value)
            && value == "1")
        {
            return true;
        }

        return string.Equals(saveInfo.modeType, SunExpIds.TongtianTowerModeType, StringComparison.Ordinal)
            && saveInfo.Name != null
            && saveInfo.Name.StartsWith("SunExpTongtianTower", StringComparison.Ordinal);
    }

    public static int DeleteUnfinishedRuns(string source)
    {
        var deleted = 0;
        foreach (var save in CandidateSaves().Where(save => IsTongtianSave(save) && !IsEnded(save)).ToList())
        {
            try
            {
                save.Delete();
                RemoveRuntimeSave(save);
                deleted++;
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("[TongtianTowerRunState] delete unfinished run failed from "
                    + source
                    + "; save="
                    + save.Name
                    + "; error="
                    + ex.Message);
            }
        }

        if (deleted > 0)
        {
            SunExpLog.Info("[TongtianTowerRunState] deleted unfinished runs from "
                + source
                + ": "
                + deleted
                + ".");
        }

        return deleted;
    }

    private static IEnumerable<SaveInfo> CandidateSaves()
    {
        var byName = new Dictionary<string, SaveInfo>(StringComparer.Ordinal);
        void AddRange(IEnumerable<SaveInfo>? saves)
        {
            foreach (var save in saves ?? Enumerable.Empty<SaveInfo>())
            {
                if (save == null || string.IsNullOrWhiteSpace(save.Name))
                {
                    continue;
                }

                byName[save.Name] = save;
            }
        }

        try
        {
            AddRange(Singleton<GameRuntimeData>.Instance?.Saves);
        }
        catch
        {
            // Runtime list may not be hydrated on some entry paths.
        }

        try
        {
            AddRange(GameSaveManager.LoadAll());
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[TongtianTowerRunState] LoadAll skipped: " + ex.Message);
        }

        return byName.Values;
    }

    private static void RemoveRuntimeSave(SaveInfo saveInfo)
    {
        try
        {
            var saves = Singleton<GameRuntimeData>.Instance?.Saves;
            if (saves == null)
            {
                return;
            }

            saves.RemoveAll(save => string.Equals(save?.Name, saveInfo.Name, StringComparison.Ordinal));
        }
        catch
        {
            // Runtime save list may be unavailable on some menu paths.
        }
    }

    private static bool IsEnded(SaveInfo saveInfo)
    {
        return Value(saveInfo, SunExpIds.TongtianTowerRunEndedKey) == "1"
            || Value(saveInfo, SunExpIds.TongtianTowerRunPhaseKey) == TongtianTowerRunPhase.Ended;
    }

    private static long SortStamp(SaveInfo saveInfo)
    {
        var updated = Value(saveInfo, SunExpIds.TongtianTowerRunUpdatedAtKey);
        if (DateTime.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.Ticks;
        }

        return saveInfo.startTime.Ticks != 0 ? saveInfo.startTime.Ticks : saveInfo.endTime.Ticks;
    }

    private static string DefaultPhase(SaveInfo saveInfo)
    {
        return Value(saveInfo, SunExpIds.TongtianTowerStarterDeckAppliedKey) == "1"
            ? TongtianTowerRunPhase.MapPlanning
            : TongtianTowerRunPhase.Intro;
    }

    private static bool Ensure(SaveInfo saveInfo, string key, string value)
    {
        if (saveInfo.GameVars.ContainsKey(key))
        {
            return false;
        }

        saveInfo.GameVars[key] = value;
        return true;
    }

    private static bool Set(SaveInfo saveInfo, string key, string value)
    {
        if (saveInfo.GameVars.TryGetValue(key, out var existing) && existing == value)
        {
            return false;
        }

        saveInfo.GameVars[key] = value;
        return true;
    }

    private static string Value(SaveInfo saveInfo, string key)
    {
        return saveInfo.GameVars != null && saveInfo.GameVars.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static void Touch(SaveInfo saveInfo)
    {
        if (saveInfo.GameVars == null)
        {
            return;
        }

        saveInfo.GameVars[SunExpIds.TongtianTowerRunUpdatedAtKey] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }
}
