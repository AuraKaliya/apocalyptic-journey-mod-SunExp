using System;

namespace AuraMode.Shared;

public static class AuraModeRunIdentity
{
    public const string NativeWorldSimulationModeId = "Witch:world-simulation";
    public const string NativeWorldSimulationModeType = "Normal";
    public const string RunIdentityKey = "AuraMode.RunIdentity";

    public static bool IsNativeWorldSimulation(
        string? nativeModeType,
        string? recordedRunIdentity,
        AuraActiveModeSnapshot? activeMode,
        string? currentSaveSlotId = null)
    {
        if (!string.Equals(
                (nativeModeType ?? "").Trim(),
                NativeWorldSimulationModeType,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                (recordedRunIdentity ?? "").Trim(),
                NativeWorldSimulationModeId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (activeMode == null
            || string.Equals(
                activeMode.ModeId,
                NativeWorldSimulationModeId,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var currentSave = (currentSaveSlotId ?? "").Trim();
        var activeSave = (activeMode.Run?.SaveSlotId ?? "").Trim();
        return currentSave.Length > 0
               && activeSave.Length > 0
               && !string.Equals(currentSave, activeSave, StringComparison.Ordinal);
    }
}
