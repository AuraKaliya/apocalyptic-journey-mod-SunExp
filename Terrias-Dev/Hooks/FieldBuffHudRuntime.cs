using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;
using Witch.Core;
using Witch.UI;

namespace Terrias.Dll.Hooks;

public static class FieldBuffHudRuntime
{
    private const int MaxHostRetryCount = 30;
    private static FieldBuffHudView? activeView;
    private static int hostRetryCount;

    public static void RequestRefresh(string source)
    {
        TerriasFrameScheduler.RunOnceNextFrame("FieldBuffHud.Refresh", Refresh);
    }

    public static void Refresh()
    {
        try
        {
            var snapshot = FieldApi.ActiveFieldSnapshot();
            if (!snapshot.IsActive)
            {
                if (TerriasNetworkRuntime.IsClientOnly())
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
            TerriasLog.Error("Field buff HUD refresh failed", ex);
        }
    }

    public static void Close(string source)
    {
        hostRetryCount = 0;
        if (activeView == null)
        {
            TerriasTransientUiRegistry.Unregister("FieldBuffHud");
            return;
        }

        activeView.Close(source);
        activeView = null;
        TerriasTransientUiRegistry.Unregister("FieldBuffHud");
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
        TerriasTransientUiRegistry.Register("FieldBuffHud", Close);
        return activeView;
    }

    private static void ScheduleHostRetry()
    {
        if (hostRetryCount >= MaxHostRetryCount)
        {
            TerriasLog.WarnOnce("FieldBuffHud.FightUiUnavailable",
                "Field buff HUD skipped after waiting for FightUI; a later field refresh can retry.");
            return;
        }

        hostRetryCount++;
        TerriasFrameScheduler.RunOnceAfterFrames("FieldBuffHud.WaitForFightUI", 2, Refresh);
    }
}
