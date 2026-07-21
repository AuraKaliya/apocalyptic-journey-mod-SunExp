using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Scripting;

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
            TerriasLog.Error("Solar memory node init failed", ex);
        }
    }

    public static void ContinueSolarMemory()
    {
        try
        {
            SolarMemoryFlowApi.ContinueAfterPreparation();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory continue failed", ex);
        }
    }

    public static void CompleteSolarMemoryPostPreparationDialogue()
    {
        try
        {
            SolarMemoryFlowApi.CompletePostPreparationDialogue();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory post-preparation dialogue completion failed", ex);
        }
    }

    public static void CompleteSolarMemorySecondSunEndingDialogue()
    {
        try
        {
            SolarMemoryFlowApi.CompleteSecondSunEndingDialogue();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory second-sun ending dialogue completion failed", ex);
        }
    }

    public static void CompleteSolarMemorySaintWunaPreludeDialogue()
    {
        try
        {
            SolarMemoryFlowApi.CompleteSaintWunaPreludeDialogue();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory saint-wuna prelude dialogue completion failed", ex);
        }
    }

    public static void CompleteSolarMemorySaintWunaEndingDialogue()
    {
        try
        {
            SolarMemoryFlowApi.CompleteSaintWunaEndingDialogue();
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Solar memory saint-wuna ending dialogue completion failed", ex);
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
            TerriasLog.Error("Solar memory origin option failed", ex);
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
            TerriasLog.Error("Solar memory blessing option failed", ex);
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
            TerriasLog.Error("Solar memory deck option failed", ex);
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
