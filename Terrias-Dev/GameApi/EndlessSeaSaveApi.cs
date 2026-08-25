using System;
using Data.Save;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public sealed class EndlessSeaSaveData
{
    public string Mode { get; set; } = "";
    public int Floor { get; set; }
    public int GeneratedFloor { get; set; }
    public string RunId { get; set; } = "";
    public string RunPhase { get; set; } = "";
    public string RunEnded { get; set; } = "";
    public string StarterDeckApplied { get; set; } = "";
    public int GazeLevel { get; set; }
    public string PendingShockJson { get; set; } = "";
    public string EvacuationToken { get; set; } = "";
    public string EvacuationReason { get; set; } = "";
    public int EvacuationFloor { get; set; }
    public int EvacuationDepth { get; set; }
    public string EvacuationAt { get; set; } = "";
    public string FloorPlanJson { get; set; } = "";
}

public static class EndlessSeaSaveApi
{
    public static EndlessSeaSaveData Capture(bool includePlan)
    {
        return new EndlessSeaSaveData
        {
            Mode = ReadString(TerriasIds.EndlessSeaModeKey),
            Floor = Math.Max(1, ReadInt(TerriasIds.EndlessSeaFloorKey)),
            GeneratedFloor = Math.Max(0, ReadInt(TerriasIds.EndlessSeaGeneratedFloorKey)),
            RunId = ReadString(TerriasIds.EndlessSeaRunIdKey),
            RunPhase = ReadString(TerriasIds.EndlessSeaRunPhaseKey),
            RunEnded = ReadString(TerriasIds.EndlessSeaRunEndedKey),
            StarterDeckApplied = ReadString(TerriasIds.EndlessSeaStarterDeckAppliedKey),
            GazeLevel = Math.Max(0, ReadInt(TerriasIds.EndlessAbyssGazeLevelKey)),
            PendingShockJson = ReadString(TerriasIds.EndlessAbyssPendingShockKey),
            EvacuationToken = ReadString(TerriasIds.EndlessAbyssEvacuationTokenKey),
            EvacuationReason = ReadString(TerriasIds.EndlessAbyssEvacuationReasonKey),
            EvacuationFloor = Math.Max(0, ReadInt(TerriasIds.EndlessAbyssEvacuationFloorKey)),
            EvacuationDepth = Math.Max(0, ReadInt(TerriasIds.EndlessAbyssEvacuationDepthKey)),
            EvacuationAt = ReadString(TerriasIds.EndlessAbyssEvacuationAtKey),
            FloorPlanJson = includePlan ? ReadString(TerriasIds.EndlessSeaFloorPlanKey) : ""
        };
    }

    public static void Apply(EndlessSeaSaveData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        Set(TerriasIds.EndlessSeaModeKey, data.Mode);
        Set(TerriasIds.EndlessSeaFloorKey, Math.Max(1, data.Floor).ToString());
        Set(TerriasIds.EndlessSeaGeneratedFloorKey, Math.Max(0, data.GeneratedFloor).ToString());
        Set(TerriasIds.EndlessSeaRunIdKey, data.RunId);
        Set(TerriasIds.EndlessSeaRunPhaseKey, data.RunPhase);
        Set(TerriasIds.EndlessSeaRunEndedKey, data.RunEnded);
        Set(TerriasIds.EndlessSeaStarterDeckAppliedKey, data.StarterDeckApplied);
        Set(TerriasIds.EndlessAbyssGazeLevelKey, Math.Max(0, data.GazeLevel).ToString());
        Set(TerriasIds.EndlessAbyssPendingShockKey, data.PendingShockJson);
        Set(TerriasIds.EndlessAbyssEvacuationTokenKey, data.EvacuationToken);
        Set(TerriasIds.EndlessAbyssEvacuationReasonKey, data.EvacuationReason);
        Set(TerriasIds.EndlessAbyssEvacuationFloorKey, Math.Max(0, data.EvacuationFloor).ToString());
        Set(TerriasIds.EndlessAbyssEvacuationDepthKey, Math.Max(0, data.EvacuationDepth).ToString());
        Set(TerriasIds.EndlessAbyssEvacuationAtKey, data.EvacuationAt);
        if (!string.IsNullOrWhiteSpace(data.FloorPlanJson))
        {
            Set(TerriasIds.EndlessSeaFloorPlanKey, data.FloorPlanJson);
        }
    }

    public static int CurrentFloor() => Math.Max(1, ReadInt(TerriasIds.EndlessSeaFloorKey));

    private static string ReadString(string key)
    {
        try
        {
            return GameSaveManager.GetValue<string>(key) ?? "";
        }
        catch
        {
            return GameSaveManager.GetNowSave()?.GameVars?.TryGetValue(key, out var value) == true
                ? value ?? ""
                : "";
        }
    }

    private static int ReadInt(string key) => DictionaryUtil.ParseInt(ReadString(key));

    private static void Set(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value ?? "");
        }
        catch
        {
            GameSaveManager.GetNowSave()?.SetValue(key, value ?? "");
        }
    }
}
