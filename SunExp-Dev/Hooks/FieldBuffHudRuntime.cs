using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Network;
using Witch.Core;
using Witch.UI;

namespace SunExp.Dll.Hooks;

public static class FieldBuffHudRuntime
{
    private const int MaxHostRetryCount = 30;
    private static FieldBuffHudView? activeView;
    private static int hostRetryCount;

    public static void RequestRefresh(string source)
    {
        SunExpFrameScheduler.RunOnceNextFrame("FieldBuffHud.Refresh", Refresh);
    }

    public static void Refresh()
    {
        try
        {
            var snapshot = FieldApi.ActiveFieldSnapshot();
            if (!snapshot.IsActive)
            {
                if (SunExpNetworkRuntime.IsClientOnly())
                {
                    FieldNetworkSync.RequestSnapshot("FieldBuffHud.Empty");
                }

                Close("FieldBuffHud.Empty");
                return;
            }

            var view = EnsureView();
            if (view == null)
            {
                ScheduleHostRetry();
                return;
            }

            hostRetryCount = 0;
            view.ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Field buff HUD refresh failed", ex);
        }
    }

    public static void Close(string source)
    {
        hostRetryCount = 0;
        if (activeView == null)
        {
            SunExpTransientUiRegistry.Unregister("FieldBuffHud");
            return;
        }

        activeView.Close(source);
        activeView = null;
        SunExpTransientUiRegistry.Unregister("FieldBuffHud");
    }

    private static FieldBuffHudView? EnsureView()
    {
        if (activeView != null)
        {
            return activeView;
        }

        if (!BattleHudHost.TryGet(out var parent))
        {
            return null;
        }

        activeView = FieldBuffHudView.Create(parent);
        SunExpTransientUiRegistry.Register("FieldBuffHud", Close);
        return activeView;
    }

    private static void ScheduleHostRetry()
    {
        if (hostRetryCount >= MaxHostRetryCount)
        {
            SunExpLog.WarnOnce("FieldBuffHud.FightUiUnavailable",
                "Field buff HUD skipped after waiting for FightUI; a later field refresh can retry.");
            return;
        }

        hostRetryCount++;
        SunExpFrameScheduler.RunOnceAfterFrames("FieldBuffHud.WaitForFightUI", 2, Refresh);
    }
}
