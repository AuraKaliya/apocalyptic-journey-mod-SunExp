using System;
using AuraShared.Core;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpUiLifecycleRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "Fight_Win.Init", context => CloseAll("Fight_Win.Init"));
        RegisterBefore(modConfig, "Fight_Loss.Init", context => CloseAll("Fight_Loss.Init"));
        RegisterBefore(modConfig, "Fight_Escape.Init", context => CloseAll("Fight_Escape.Init"));
        RegisterBefore(modConfig, "Fight_Win.ResetStates", context => CloseAll("Fight_Win.ResetStates"));
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", context => CloseAll("Fight_Escape.ResetStates"));
        RegisterBefore(modConfig, "UIManager.CloseUI", CloseForUiManager);
        RegisterBefore(modConfig, "UIBase.Close", CloseForUiBase);
        RegisterBefore(modConfig, "GameEntryUI.Init", context => CloseAll("GameEntryUI.Init"));
        RegisterBefore(modConfig, "GameEntryUI.Start", context => CloseAll("GameEntryUI.Start"));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("SunExp UI lifecycle " + message));
    }

    private static void CloseForUiManager(ModHookContext context)
    {
        var uiName = context.Arguments != null && context.Arguments.Length > 0
            ? context.Arguments[0] as string ?? ""
            : "";
        if (ShouldCloseForUi(uiName))
        {
            CloseAll("UIManager.CloseUI:" + uiName);
        }
    }

    private static void CloseForUiBase(ModHookContext context)
    {
        var name = TargetName(context.Target);
        if (ShouldCloseForUi(name))
        {
            CloseAll("UIBase.Close:" + name);
        }
    }

    private static void CloseAll(string source)
    {
        SunExpTransientUiRegistry.CloseAll(source);
    }

    private static bool ShouldCloseForUi(string name)
    {
        return string.Equals(name, "FightUI", StringComparison.Ordinal)
               || string.Equals(name, "MapSelectUI", StringComparison.Ordinal)
               || string.Equals(name, "GameEntryUI", StringComparison.Ordinal)
               || string.Equals(name, "BattleRewardsUI", StringComparison.Ordinal);
    }

    private static string TargetName(object? target)
    {
        return target switch
        {
            UnityEngine.Component component => component.gameObject != null ? component.gameObject.name : target.GetType().Name,
            GameObject gameObject => gameObject.name,
            null => "",
            _ => target.GetType().Name
        };
    }
}
