using System;
using System.Globalization;
using Data.Save;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

[Serializable]
public sealed class EndlessAbyssEvacuationResolution
{
    public const int CurrentProtocolVersion = 1;

    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;
    public string RunId { get; set; } = "";
    public string Token { get; set; } = "";
    public string Reason { get; set; } = "Evacuation";
    public int Floor { get; set; }
    public int SettlementDepth { get; set; }
    public string EvacuatedAt { get; set; } = "";

    public bool IsValid => ProtocolVersion == CurrentProtocolVersion
                           && !string.IsNullOrWhiteSpace(RunId)
                           && !string.IsNullOrWhiteSpace(Token)
                           && string.Equals(Reason, "Evacuation", StringComparison.Ordinal)
                           && Floor >= 1
                           && SettlementDepth >= 0;
}

public static class EndlessAbyssEvacuationService
{
    public static int CalculateSettlementDepth(int floor, int level)
    {
        return EndlessAbyssEvacuationDepth.Calculate(floor, level);
    }

    public static bool TryBegin(string source, out EndlessAbyssEvacuationResolution resolution, out string rejection)
    {
        resolution = new EndlessAbyssEvacuationResolution();
        rejection = "";
        var saveInfo = GameSaveManager.GetNowSave();
        if (!EndlessSeaRunStateStore.IsEndlessSeaSave(saveInfo) || saveInfo?.GameVars == null)
        {
            rejection = "mode-inactive";
            return false;
        }

        if (!string.Equals(EndlessSeaRunStateStore.CurrentPhase(), EndlessSeaRunPhase.MapPlanning, StringComparison.Ordinal))
        {
            rejection = "invalid-phase";
            return false;
        }

        var runId = ReadString(SunExpIds.EndlessSeaRunIdKey);
        if (string.IsNullOrWhiteSpace(runId))
        {
            rejection = "run-id-missing";
            return false;
        }

        var floor = Math.Max(1, ReadInt(SunExpIds.EndlessSeaFloorKey));
        var level = Math.Max(0, MapManager.Instance?.Level ?? 0);
        var depth = CalculateSettlementDepth(floor, level);
        var token = runId + ":evacuation:" + Guid.NewGuid().ToString("N");
        var evacuatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (!EndlessSeaRunStateStore.BeginEvacuation(token, floor, depth, evacuatedAt, source))
        {
            rejection = "state-commit-failed";
            return false;
        }

        PersistCurrentSave(source);
        resolution = CaptureStored();
        if (!resolution.IsValid)
        {
            rejection = "stored-resolution-invalid";
            return false;
        }

        return true;
    }

    public static EndlessAbyssEvacuationResolution CaptureStored()
    {
        return new EndlessAbyssEvacuationResolution
        {
            RunId = ReadString(SunExpIds.EndlessSeaRunIdKey),
            Token = ReadString(SunExpIds.EndlessAbyssEvacuationTokenKey),
            Reason = ReadString(SunExpIds.EndlessAbyssEvacuationReasonKey),
            Floor = Math.Max(1, ReadInt(SunExpIds.EndlessAbyssEvacuationFloorKey)),
            SettlementDepth = Math.Max(0, ReadInt(SunExpIds.EndlessAbyssEvacuationDepthKey)),
            EvacuatedAt = ReadString(SunExpIds.EndlessAbyssEvacuationAtKey)
        };
    }

    public static bool TryCaptureStored(string expectedToken, out EndlessAbyssEvacuationResolution resolution)
    {
        resolution = CaptureStored();
        return EndlessSeaRunStateStore.IsEvacuating()
               && resolution.IsValid
               && string.Equals(resolution.Token, expectedToken ?? "", StringComparison.Ordinal);
    }

    public static bool MatchesCurrentRun(EndlessAbyssEvacuationResolution? resolution)
    {
        return resolution?.IsValid == true
               && string.Equals(
                   resolution.RunId,
                   ReadString(SunExpIds.EndlessSeaRunIdKey),
                   StringComparison.Ordinal);
    }

    public static void PersistCurrentSave(string source)
    {
        try
        {
            if (PlayerManager.Instance == null || !PlayerManager.Instance.isClientOnly)
            {
                GameSaveManager.Save();
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssEvacuation] save failed from " + source + ": " + ex.Message);
        }
    }

    private static string ReadString(string key)
    {
        try
        {
            return GameSaveManager.GetValue<string>(key) ?? "";
        }
        catch
        {
            var save = GameSaveManager.GetNowSave();
            return save?.GameVars != null && save.GameVars.TryGetValue(key, out var value) ? value ?? "" : "";
        }
    }

    private static int ReadInt(string key)
    {
        return DictionaryUtil.ParseInt(ReadString(key));
    }
}
