using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class SolarMemoryFlowApi
{
    public static bool IsPreparationComplete()
    {
        return SolarMemoryPreparationRuntime.IsComplete();
    }

    public static void StartOrResumePreparation()
    {
        SolarMemoryPreparationRuntime.StartOrResume();
    }

    public static void EnsureOriginPoints(int defaultValue)
    {
        if (SolarMemoryPlayerSetupState.GetValue(SunExpIds.SolarMemoryOriginPointsKey, "") == "")
        {
            SolarMemoryPlayerSetupState.SetInt(SunExpIds.SolarMemoryOriginPointsKey, defaultValue);
        }
    }

    public static void MarkPrepared()
    {
        SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryPreparedKey, true);
    }

    public static void OpenOriginWindow()
    {
        SolarMemoryModeRuntime.OpenOriginWindow();
    }

    public static void OpenBlessingWindow()
    {
        SolarMemoryModeRuntime.OpenBlessingWindow();
    }

    public static void OpenDeckWindow()
    {
        SolarMemoryModeRuntime.OpenDeckWindow();
    }

    public static void ShowSettlement()
    {
        SolarMemoryModeRuntime.ShowSolarMemorySettlement();
    }
}
