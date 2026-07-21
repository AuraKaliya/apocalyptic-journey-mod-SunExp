using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Data.Save;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

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

        saveInfo.modeType = TerriasIds.NativeNormalModeType;
        Set(saveInfo, TerriasIds.EndlessSeaModeKey, "1");
        Set(saveInfo, TerriasIds.EndlessSeaFloorKey, "1");
        Set(saveInfo, TerriasIds.EndlessSeaGeneratedFloorKey, "0");
        Set(saveInfo, TerriasIds.EndlessSeaSeedKey, seed ?? "");
        Set(saveInfo, TerriasIds.EndlessSeaFloorPlanKey, "");
        Set(saveInfo, TerriasIds.EndlessSeaIntroSeenKey, "0");
        Set(saveInfo, TerriasIds.EndlessSeaStarterDeckAppliedKey, "0");
        Set(saveInfo, TerriasIds.EndlessSeaStarterDeckModeKey, "");
        Set(saveInfo, TerriasIds.EndlessSeaRunIdKey, Guid.NewGuid().ToString("N"));
        Set(saveInfo, TerriasIds.EndlessSeaRunVersionKey, Version);
        Set(saveInfo, TerriasIds.EndlessSeaRunPhaseKey, EndlessSeaRunPhase.Intro);
        Set(saveInfo, TerriasIds.EndlessSeaRunEndedKey, "0");
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationTokenKey, "");
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationReasonKey, "");
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationFloorKey, "0");
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationDepthKey, "0");
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationAtKey, "");
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
        if (!string.Equals(saveInfo.modeType, TerriasIds.NativeNormalModeType, StringComparison.Ordinal))
        {
            var legacyModeType = saveInfo.modeType ?? "";
            saveInfo.modeType = TerriasIds.NativeNormalModeType;
            changed = true;
            TerriasLog.Info("[EndlessSeaRunState] migrated save mode from "
                + legacyModeType
                + " to "
                + TerriasIds.NativeNormalModeType
                + " from "
                + source
                + "; save="
                + saveInfo.Name
                + ".");
        }

        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaModeKey, "1");
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaFloorKey, "1");
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaGeneratedFloorKey, "0");
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaSeedKey, saveInfo.Seed ?? "");
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaFloorPlanKey, "");
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaIntroSeenKey, "0");
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaStarterDeckAppliedKey, "0");
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaStarterDeckModeKey, "");
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaRunIdKey, Guid.NewGuid().ToString("N"));
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaRunVersionKey, Version);
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaRunPhaseKey, DefaultPhase(saveInfo));
        changed |= Ensure(saveInfo, TerriasIds.EndlessSeaRunEndedKey, "0");
        changed |= Ensure(saveInfo, TerriasIds.EndlessAbyssEvacuationTokenKey, "");
        changed |= Ensure(saveInfo, TerriasIds.EndlessAbyssEvacuationReasonKey, "");
        changed |= Ensure(saveInfo, TerriasIds.EndlessAbyssEvacuationFloorKey, "0");
        changed |= Ensure(saveInfo, TerriasIds.EndlessAbyssEvacuationDepthKey, "0");
        changed |= Ensure(saveInfo, TerriasIds.EndlessAbyssEvacuationAtKey, "");
        changed |= Ensure(saveInfo, TerriasIds.EndlessAbyssGazeLevelKey, EndlessAbyssGazeService.InitialLevel.ToString(CultureInfo.InvariantCulture));
        changed |= Ensure(saveInfo, TerriasIds.EndlessAbyssLedgerKey, "{\"Entries\":[]}");
        changed |= Ensure(saveInfo, TerriasIds.EndlessAbyssPendingShockKey, "");

        if (changed)
        {
            Touch(saveInfo);
            TerriasLog.Info("[EndlessSeaRunState] repaired save from "
                + source
                + "; name="
                + saveInfo.Name
                + "; floor="
                + Value(saveInfo, TerriasIds.EndlessSeaFloorKey));
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

        if (Set(saveInfo, TerriasIds.EndlessSeaRunPhaseKey, phase))
        {
            Touch(saveInfo);
            TerriasLog.Debug("[EndlessSeaRunState] phase=" + phase + " from " + source);
        }
    }

    public static string CurrentPhase()
    {
        var saveInfo = GameSaveManager.GetNowSave();
        return IsEndlessSeaSave(saveInfo) && saveInfo?.GameVars != null
            ? Value(saveInfo, TerriasIds.EndlessSeaRunPhaseKey)
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
            || Value(saveInfo, TerriasIds.EndlessSeaRunEndedKey) == "1")
        {
            return false;
        }

        var phase = Value(saveInfo, TerriasIds.EndlessSeaRunPhaseKey);
        if (string.Equals(phase, EndlessSeaRunPhase.Evacuating, StringComparison.Ordinal))
        {
            return string.Equals(
                Value(saveInfo, TerriasIds.EndlessAbyssEvacuationTokenKey),
                token,
                StringComparison.Ordinal);
        }

        if (!string.Equals(phase, EndlessSeaRunPhase.MapPlanning, StringComparison.Ordinal))
        {
            return false;
        }

        Set(saveInfo, TerriasIds.EndlessSeaRunPhaseKey, EndlessSeaRunPhase.Evacuating);
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationTokenKey, token.Trim());
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationReasonKey, "Evacuation");
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationFloorKey, Math.Max(1, floor).ToString(CultureInfo.InvariantCulture));
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationDepthKey, Math.Max(0, depth).ToString(CultureInfo.InvariantCulture));
        Set(saveInfo, TerriasIds.EndlessAbyssEvacuationAtKey, evacuatedAt ?? "");
        Touch(saveInfo);
        TerriasLog.Info("[EndlessAbyssEvacuation] prepared from "
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

        Set(saveInfo, TerriasIds.EndlessSeaRunPhaseKey, EndlessSeaRunPhase.Ended);
        Set(saveInfo, TerriasIds.EndlessSeaRunEndedKey, "1");
        Touch(saveInfo);
        TerriasLog.Info("[EndlessSeaRunState] marked ended from " + source + ".");
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
            TerriasLog.Warn("[EndlessSeaRunState] find latest run failed: " + ex.Message);
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
            && saveInfo.GameVars.TryGetValue(TerriasIds.EndlessSeaModeKey, out var value)
            && value == "1")
        {
            return true;
        }

        return string.Equals(saveInfo.modeType, TerriasIds.EndlessSeaModeType, StringComparison.Ordinal)
            && saveInfo.Name != null
            && saveInfo.Name.StartsWith("TerriasEndlessSea", StringComparison.Ordinal);
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
                TerriasLog.Warn("[EndlessSeaRunState] delete unfinished run failed from "
                    + source
                    + "; save="
                    + save.Name
                    + "; error="
                    + ex.Message);
            }
        }

        if (deleted > 0)
        {
            TerriasLog.Info("[EndlessSeaRunState] deleted unfinished runs from "
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
            TerriasLog.Debug("[EndlessSeaRunState] LoadAll skipped: " + ex.Message);
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
        return Value(saveInfo, TerriasIds.EndlessSeaRunEndedKey) == "1"
            || Value(saveInfo, TerriasIds.EndlessSeaRunPhaseKey) == EndlessSeaRunPhase.Ended;
    }

    private static long SortStamp(SaveInfo saveInfo)
    {
        var updated = Value(saveInfo, TerriasIds.EndlessSeaRunUpdatedAtKey);
        if (DateTime.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.Ticks;
        }

        return saveInfo.startTime.Ticks != 0 ? saveInfo.startTime.Ticks : saveInfo.endTime.Ticks;
    }

    private static string DefaultPhase(SaveInfo saveInfo)
    {
        return Value(saveInfo, TerriasIds.EndlessSeaStarterDeckAppliedKey) == "1"
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

        saveInfo.GameVars[TerriasIds.EndlessSeaRunUpdatedAtKey] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }
}
