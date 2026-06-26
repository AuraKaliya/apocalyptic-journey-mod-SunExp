using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class EventScripts
{
    public static void InitSolarMemoryNode(ScriptExecutor self)
    {
        try
        {
            SolarMemoryFlowApi.EnsureOriginPoints(50);

            SetEventChoices(self, "1", "1", "", "");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory node init failed", ex);
        }
    }

    public static void ContinueSolarMemory()
    {
        try
        {
            if (!SolarMemoryFlowApi.IsPreparationComplete())
            {
                SunExpLog.Info("[SolarMemoryEvent] continue requested before preparation complete; resuming preparation.");
                SolarMemoryFlowApi.StartOrResumePreparation();
                return;
            }

            SunExpLog.Info("[SolarMemoryEvent] continue accepted; prepared=1.");
            SolarMemoryFlowApi.MarkPrepared();
            PlayerApi.EndEvent();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory continue failed", ex);
        }
    }

    public static void OpenSolarMemoryOrigin()
    {
        try
        {
            SolarMemoryFlowApi.OpenOriginWindow();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory origin option failed", ex);
        }
    }

    public static void OpenSolarMemoryBless()
    {
        try
        {
            SolarMemoryFlowApi.OpenBlessingWindow();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory blessing option failed", ex);
        }
    }

    public static void OpenSolarMemoryDeck()
    {
        try
        {
            SolarMemoryFlowApi.OpenDeckWindow();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory deck option failed", ex);
        }
    }

    private static bool SetEventChoices(ScriptExecutor self, string choice1, string choice2, string choice3, string choice4)
    {
        if (self?.Vars == null)
        {
            return false;
        }

        ExecutorApi.SetVar(self, "Choice1", string.IsNullOrWhiteSpace(choice1) ? "0" : choice1);
        ExecutorApi.SetVar(self, "Choice2", string.IsNullOrWhiteSpace(choice2) ? "0" : choice2);
        ExecutorApi.SetVar(self, "Choice3", string.IsNullOrWhiteSpace(choice3) ? "0" : choice3);
        ExecutorApi.SetVar(self, "Choice4", string.IsNullOrWhiteSpace(choice4) ? "0" : choice4);
        return true;
    }
}
