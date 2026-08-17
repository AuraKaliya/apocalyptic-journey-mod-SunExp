using System;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class GoldDreamHudRuntime
{
    private static GoldDreamHudView? activeView;
    private static GoldDreamSnapshot pendingSnapshot = GoldDreamSnapshot.Empty;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        GoldDreamEconomyService.Changed += OnChanged;
        RegisterAfter(modConfig, "TopBarUI.Awake", EnsureFromHook);
        RegisterAfter(modConfig, "TopBarUI.Start", EnsureFromHook);
        RegisterAfter(modConfig, "TopBarUI.ShowLeftUp", EnsureFromHook);
    }

    private static void RegisterAfter(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.After(modConfig, target, action, "GoldDreamHud");
    }

    private static void EnsureFromHook(ModHookContext context)
    {
        if (context.Target is TopBarUI topBar)
        {
            EnsureView(topBar)?.ApplySnapshot(pendingSnapshot);
        }
    }

    private static void OnChanged(GoldDreamSnapshot snapshot)
    {
        pendingSnapshot = snapshot ?? GoldDreamSnapshot.Empty;
        TerriasFrameScheduler.RunOnceNextFrame("GoldDreamHud.Refresh", Refresh);
    }

    private static void Refresh()
    {
        try
        {
            var topBar = UIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            EnsureView(topBar)?.ApplySnapshot(pendingSnapshot);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Gold Dream HUD refresh skipped: " + ex.Message);
        }
    }

    private static GoldDreamHudView? EnsureView(TopBarUI? topBar)
    {
        if (topBar == null || topBar.gameObject == null)
        {
            return null;
        }

        if (activeView != null && activeView.gameObject == topBar.gameObject)
        {
            return activeView;
        }

        activeView = topBar.GetComponent<GoldDreamHudView>() ?? topBar.gameObject.AddComponent<GoldDreamHudView>();
        activeView.Bind(topBar);
        return activeView;
    }
}
