using System;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Hooks;

public static class EndlessAbyssMilestonePromptService
{
    private static int lastPromptedFloor;

    public static void Schedule(string source)
    {
        if (!TongtianTowerModeRuntime.IsTongtianTowerRun())
        {
            return;
        }

        SunExpFrameDispatcher.RunOnceNextFrame(
            "EndlessAbyssMilestonePrompt." + (source ?? "unknown"),
            () => TryOpen(source + ":next-frame"));
    }

    public static bool TryOpen(string source)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun()
                || EndlessAbyssMilestoneRewardPanel.IsOpen
                || EndlessAbyssShockPanel.IsOpen
                || EndlessAbyssShockService.PendingRequest() != null)
            {
                return false;
            }

            var floor = TongtianTowerModeRuntime.CurrentFloor();
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
            SunExpLog.Warn("[EndlessAbyssMilestone] prompt failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static void Reset(string source)
    {
        lastPromptedFloor = 0;
        SunExpLog.Debug("[EndlessAbyssMilestone] prompt state reset from " + source + ".");
    }
}
