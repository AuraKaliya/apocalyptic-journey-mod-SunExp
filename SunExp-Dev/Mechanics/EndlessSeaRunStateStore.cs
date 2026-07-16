using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class EndlessSeaRunPhase
{
    public const string Intro = "Intro";
    public const string MapPlanning = "MapPlanning";
    public const string InBattle = "InBattle";
    public const string Reward = "Reward";
    public const string BetweenFloors = "BetweenFloors";
    public const string Evacuating = "Evacuating";
    public const string Ended = "Ended";
}

public static class EndlessSeaRunStateStore
{
    private const string Version = "1";

    public static void InitializeNewRun(SaveInfo saveInfo, string seed)
    {
        if (saveInfo?.GameVars == null)
        {
            return;
        }

        saveInfo.modeType = SunExpIds.NativeNormalModeType;
        Set(saveInfo, SunExpIds.EndlessSeaModeKey, "1");
        Set(saveInfo, SunExpIds.EndlessSeaFloorKey, "1");
        Set(saveInfo, SunExpIds.EndlessSeaGeneratedFloorKey, "0");
        Set(saveInfo, SunExpIds.EndlessSeaSeedKey, seed ?? "");
        Set(saveInfo, SunExpIds.EndlessSeaFloorPlanKey, "");
        Set(saveInfo, SunExpIds.EndlessSeaIntroSeenKey, "0");
        Set(saveInfo, SunExpIds.EndlessSeaStarterDeckAppliedKey, "0");
        Set(saveInfo, SunExpIds.EndlessSeaStarterDeckModeKey, "");
        Set(saveInfo, SunExpIds.EndlessSeaRunIdKey, Guid.NewGuid().ToString("N"));
        Set(saveInfo, SunExpIds.EndlessSeaRunVersionKey, Version);
        Set(saveInfo, SunExpIds.EndlessSeaRunPhaseKey, EndlessSeaRunPhase.Intro);
        Set(saveInfo, SunExpIds.EndlessSeaRunEndedKey, "0");
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationTokenKey, "");
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationReasonKey, "");
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationFloorKey, "0");
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationDepthKey, "0");
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationAtKey, "");
        EndlessAbyssGazeService.Initialize(saveInfo);
        EndlessAbyssRunLedger.Initialize(saveInfo);
        Touch(saveInfo);
    }

    public static bool RepairCurrentRun(string source)
    {
        var saveInfo = GameSaveManager.GetNowSave();
        EndlessSeaLegacyMigration.MigrateSaveInfo(saveInfo, source);
        if (!IsEndlessSeaSave(saveInfo))
        {
            return false;
        }

        return RepairSave(saveInfo, source);
    }

    public static bool RepairSave(SaveInfo? saveInfo, string source)
    {
        EndlessSeaLegacyMigration.MigrateSaveInfo(saveInfo, source);
        if (!IsEndlessSeaSave(saveInfo) || saveInfo?.GameVars == null)
        {
            return false;
        }

        var changed = false;
        if (!string.Equals(saveInfo.modeType, SunExpIds.NativeNormalModeType, StringComparison.Ordinal))
        {
            var legacyModeType = saveInfo.modeType ?? "";
            saveInfo.modeType = SunExpIds.NativeNormalModeType;
            changed = true;
            SunExpLog.Info("[EndlessSeaRunState] migrated save mode from "
                + legacyModeType
                + " to "
                + SunExpIds.NativeNormalModeType
                + " from "
                + source
                + "; save="
                + saveInfo.Name
                + ".");
        }

        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaModeKey, "1");
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaFloorKey, "1");
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaGeneratedFloorKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaSeedKey, saveInfo.Seed ?? "");
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaFloorPlanKey, "");
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaIntroSeenKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaStarterDeckAppliedKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaStarterDeckModeKey, "");
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaRunIdKey, Guid.NewGuid().ToString("N"));
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaRunVersionKey, Version);
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaRunPhaseKey, DefaultPhase(saveInfo));
        changed |= Ensure(saveInfo, SunExpIds.EndlessSeaRunEndedKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssEvacuationTokenKey, "");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssEvacuationReasonKey, "");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssEvacuationFloorKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssEvacuationDepthKey, "0");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssEvacuationAtKey, "");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssGazeLevelKey, EndlessAbyssGazeService.InitialLevel.ToString(CultureInfo.InvariantCulture));
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssLedgerKey, "{\"Entries\":[]}");
        changed |= Ensure(saveInfo, SunExpIds.EndlessAbyssPendingShockKey, "");

        if (changed)
        {
            Touch(saveInfo);
            SunExpLog.Info("[EndlessSeaRunState] repaired save from "
                + source
                + "; name="
                + saveInfo.Name
                + "; floor="
                + Value(saveInfo, SunExpIds.EndlessSeaFloorKey));
        }

        return changed;
    }

    public static void MarkPhase(string phase, string source)
    {
        var saveInfo = GameSaveManager.GetNowSave();
        if (!IsEndlessSeaSave(saveInfo) || saveInfo?.GameVars == null || string.IsNullOrWhiteSpace(phase))
        {
            return;
        }

        if (Set(saveInfo, SunExpIds.EndlessSeaRunPhaseKey, phase))
        {
            Touch(saveInfo);
            SunExpLog.Debug("[EndlessSeaRunState] phase=" + phase + " from " + source);
        }
    }

    public static string CurrentPhase()
    {
        var saveInfo = GameSaveManager.GetNowSave();
        return IsEndlessSeaSave(saveInfo) && saveInfo?.GameVars != null
            ? Value(saveInfo, SunExpIds.EndlessSeaRunPhaseKey)
            : "";
    }

    public static bool IsEvacuating()
    {
        return string.Equals(CurrentPhase(), EndlessSeaRunPhase.Evacuating, StringComparison.Ordinal);
    }

    public static bool BeginEvacuation(
        string token,
        int floor,
        int depth,
        string evacuatedAt,
        string source)
    {
        var saveInfo = GameSaveManager.GetNowSave();
        if (!IsEndlessSeaSave(saveInfo)
            || saveInfo?.GameVars == null
            || string.IsNullOrWhiteSpace(token)
            || Value(saveInfo, SunExpIds.EndlessSeaRunEndedKey) == "1")
        {
            return false;
        }

        var phase = Value(saveInfo, SunExpIds.EndlessSeaRunPhaseKey);
        if (string.Equals(phase, EndlessSeaRunPhase.Evacuating, StringComparison.Ordinal))
        {
            return string.Equals(
                Value(saveInfo, SunExpIds.EndlessAbyssEvacuationTokenKey),
                token,
                StringComparison.Ordinal);
        }

        if (!string.Equals(phase, EndlessSeaRunPhase.MapPlanning, StringComparison.Ordinal))
        {
            return false;
        }

        Set(saveInfo, SunExpIds.EndlessSeaRunPhaseKey, EndlessSeaRunPhase.Evacuating);
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationTokenKey, token.Trim());
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationReasonKey, "Evacuation");
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationFloorKey, Math.Max(1, floor).ToString(CultureInfo.InvariantCulture));
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationDepthKey, Math.Max(0, depth).ToString(CultureInfo.InvariantCulture));
        Set(saveInfo, SunExpIds.EndlessAbyssEvacuationAtKey, evacuatedAt ?? "");
        Touch(saveInfo);
        SunExpLog.Info("[EndlessAbyssEvacuation] prepared from "
            + source
            + "; floor="
            + Math.Max(1, floor)
            + "; depth="
            + Math.Max(0, depth)
            + "; token="
            + token
            + ".");
        return true;
    }

    public static void MarkEnded(string source)
    {
        var saveInfo = GameSaveManager.GetNowSave();
        if (!IsEndlessSeaSave(saveInfo) || saveInfo?.GameVars == null)
        {
            return;
        }

        Set(saveInfo, SunExpIds.EndlessSeaRunPhaseKey, EndlessSeaRunPhase.Ended);
        Set(saveInfo, SunExpIds.EndlessSeaRunEndedKey, "1");
        Touch(saveInfo);
        SunExpLog.Info("[EndlessSeaRunState] marked ended from " + source + ".");
    }

    public static SaveInfo? FindLatestUnfinishedRun()
    {
        try
        {
            return CandidateSaves()
                .Where(save => IsEndlessSeaSave(save) && !IsEnded(save))
                .Select(save =>
                {
                    RepairSave(save, "EndlessSeaRunStateStore.FindLatestUnfinishedRun");
                    return save;
                })
                .OrderByDescending(SortStamp)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessSeaRunState] find latest run failed: " + ex.Message);
            return null;
        }
    }

    public static bool IsEndlessSeaSave(SaveInfo? saveInfo)
    {
        if (saveInfo == null)
        {
            return false;
        }

        if (EndlessSeaLegacyMigration.IsLegacySave(saveInfo))
        {
            EndlessSeaLegacyMigration.MigrateSaveInfo(saveInfo, "EndlessSeaRunStateStore.IsEndlessSeaSave");
            return true;
        }

        if (saveInfo.GameVars != null
            && saveInfo.GameVars.TryGetValue(SunExpIds.EndlessSeaModeKey, out var value)
            && value == "1")
        {
            return true;
        }

        return string.Equals(saveInfo.modeType, SunExpIds.EndlessSeaModeType, StringComparison.Ordinal)
            && saveInfo.Name != null
            && saveInfo.Name.StartsWith("SunExpEndlessSea", StringComparison.Ordinal);
    }

    public static int DeleteUnfinishedRuns(string source)
    {
        var deleted = 0;
        foreach (var save in CandidateSaves().Where(save => IsEndlessSeaSave(save) && !IsEnded(save)).ToList())
        {
            try
            {
                save.Delete();
                RemoveRuntimeSave(save);
                deleted++;
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("[EndlessSeaRunState] delete unfinished run failed from "
                    + source
                    + "; save="
                    + save.Name
                    + "; error="
                    + ex.Message);
            }
        }

        if (deleted > 0)
        {
            SunExpLog.Info("[EndlessSeaRunState] deleted unfinished runs from "
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
            SunExpLog.Debug("[EndlessSeaRunState] LoadAll skipped: " + ex.Message);
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
        return Value(saveInfo, SunExpIds.EndlessSeaRunEndedKey) == "1"
            || Value(saveInfo, SunExpIds.EndlessSeaRunPhaseKey) == EndlessSeaRunPhase.Ended;
    }

    private static long SortStamp(SaveInfo saveInfo)
    {
        var updated = Value(saveInfo, SunExpIds.EndlessSeaRunUpdatedAtKey);
        if (DateTime.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.Ticks;
        }

        return saveInfo.startTime.Ticks != 0 ? saveInfo.startTime.Ticks : saveInfo.endTime.Ticks;
    }

    private static string DefaultPhase(SaveInfo saveInfo)
    {
        return Value(saveInfo, SunExpIds.EndlessSeaStarterDeckAppliedKey) == "1"
            ? EndlessSeaRunPhase.MapPlanning
            : EndlessSeaRunPhase.Intro;
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

        saveInfo.GameVars[SunExpIds.EndlessSeaRunUpdatedAtKey] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }
}
