using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class TerriasModeContextRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        TerriasHookRegistry.After(modConfig, "GameEntryUI.Init", _ => Reconcile("GameEntryUI.Init"), "ModeContext");
        TerriasHookRegistry.After(modConfig, "GameEntryUI.ShowCareer", _ => Reconcile("GameEntryUI.ShowCareer"), "ModeContext");
        TerriasHookRegistry.Before(modConfig, "GameEntryUI.StartGame", _ => Reconcile("GameEntryUI.StartGame"), "ModeContext");
        TerriasHookRegistry.After(modConfig, "NormalMapManager.InitRoleTable", _ => Reconcile("NormalMapManager.InitRoleTable"), "ModeContext");
    }

    private static void Reconcile(string source)
    {
        try
        {
            TerriasModeApi.ReconcileSelectedSave(source);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ModeContext] reconciliation failed: " + ex.Message);
        }
    }
}
