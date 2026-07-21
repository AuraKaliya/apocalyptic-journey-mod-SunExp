using System;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Hooks;

public static class EndlessAbyssMilestonePromptService
{
    private static int lastPromptedFloor;

    public static void Schedule(string source)
    {
        if (!EndlessSeaModeRuntime.IsEndlessSeaRun())
        {
            return;
        }

        TerriasFrameDispatcher.RunOnceNextFrame(
            "EndlessAbyssMilestonePrompt." + (source ?? "unknown"),
            () => TryOpen(source + ":next-frame"));
    }

    public static bool TryOpen(string source)
    {
        try
        {
            if (!EndlessSeaModeRuntime.IsEndlessSeaRun()
                || EndlessAbyssMilestoneRewardPanel.IsOpen
                || EndlessAbyssShockPanel.IsOpen
                || EndlessAbyssShockService.PendingRequest() != null)
            {
                return false;
            }

            var floor = EndlessSeaModeRuntime.CurrentFloor();
            if (!EndlessAbyssMilestoneRewardService.CanClaim(floor))
            {
                if (lastPromptedFloor == floor)
                {
                    lastPromptedFloor = 0;
                }

                return false;
            }

            if (lastPromptedFloor == floor)
            {
                return false;
            }

            if (!EndlessAbyssMilestoneRewardPanel.TryOpenForCurrentFloor(source))
            {
                return false;
            }

            lastPromptedFloor = floor;
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[EndlessAbyssMilestone] prompt failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static void Reset(string source)
    {
        lastPromptedFloor = 0;
        TerriasLog.Debug("[EndlessAbyssMilestone] prompt state reset from " + source + ".");
    }
}
