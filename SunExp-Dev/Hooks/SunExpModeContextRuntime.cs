using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpModeContextRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        SunExpHookRegistry.After(modConfig, "GameEntryUI.Init", _ => Reconcile("GameEntryUI.Init"), "ModeContext");
        SunExpHookRegistry.After(modConfig, "GameEntryUI.ShowCareer", _ => Reconcile("GameEntryUI.ShowCareer"), "ModeContext");
        SunExpHookRegistry.Before(modConfig, "GameEntryUI.StartGame", _ => Reconcile("GameEntryUI.StartGame"), "ModeContext");
        SunExpHookRegistry.After(modConfig, "NormalMapManager.InitRoleTable", _ => Reconcile("NormalMapManager.InitRoleTable"), "ModeContext");
    }

    private static void Reconcile(string source)
    {
        try
        {
            SunExpModeApi.ReconcileSelectedSave(source);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[ModeContext] reconciliation failed: " + ex.Message);
        }
    }
}
