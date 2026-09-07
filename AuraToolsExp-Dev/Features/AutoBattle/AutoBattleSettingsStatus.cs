using UnityEngine;

namespace AuraToolsExp.Dll.Features.AutoBattle;

// Every UI poll in a frame observes the same snapshots. This cache is used
// only by presentation; commands and runtime authority still read live state.
internal static class AutoBattleSettingsStatus
{
    private static int frame = -1;
    private static AutoBattleSimulationStatus simulation = null!;
    private static AutoBattleGameValidationStatus validation = null!;
    private static bool training;
    private static void Refresh()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        simulation = AuraToolsAutoBattleSimulationRuntime.GetStatus();
        validation = AuraToolsAutoBattleGameValidationRuntime.GetStatus();
        training = AuraToolsAutoBattleModelRuntime.AnyTrainingBusy();
    }
    internal static AutoBattleSimulationStatus Simulation { get { Refresh(); return simulation; } }
    internal static AutoBattleGameValidationStatus Validation { get { Refresh(); return validation; } }
    internal static bool Training { get { Refresh(); return training; } }
}
