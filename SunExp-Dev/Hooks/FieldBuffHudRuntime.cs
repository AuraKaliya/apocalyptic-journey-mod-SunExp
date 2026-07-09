using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.UI;

namespace SunExp.Dll.Hooks;

public static class FieldBuffHudRuntime
{
    private static FieldBuffHudView? activeView;

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
                Close("FieldBuffHud.Empty");
                return;
            }

            var view = EnsureView();
            if (view == null)
            {
                return;
            }

            view.ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Field buff HUD refresh failed", ex);
        }
    }

    public static void Close(string source)
    {
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
            activeView.transform.SetAsLastSibling();
            return activeView;
        }

        var parent = UIManager.Instance?.canvasTf ?? UIManager.Instance?.upperCanvasTf;
        if (parent == null)
        {
            SunExpLog.Warn("Field buff HUD skipped: UI canvas unavailable.");
            return null;
        }

        activeView = FieldBuffHudView.Create(parent);
        SunExpTransientUiRegistry.Register("FieldBuffHud", Close);
        return activeView;
    }
}
