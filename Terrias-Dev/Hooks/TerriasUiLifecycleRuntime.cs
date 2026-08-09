using System;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class TerriasUiLifecycleRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        TerriasBattleLifecycleRouter.Register("TerriasUiLifecycle", new TerriasBattleLifecycleSubscription
        {
            FightEnding = context => CloseAll("FightEnding")
        });
        RegisterBefore(modConfig, "Fight_Win.Init", context => CloseAll("Fight_Win.Init"));
        RegisterBefore(modConfig, "Fight_Loss.Init", context => CloseAll("Fight_Loss.Init"));
        RegisterBefore(modConfig, "Fight_Escape.Init", context => CloseAll("Fight_Escape.Init"));
        RegisterBefore(modConfig, "UIManager.CloseUI", CloseForUiManager);
        RegisterBefore(modConfig, "UIBase.Close", CloseForUiBase);
        RegisterBefore(modConfig, "GameEntryUI.Init", context => ResetForGameEntry("GameEntryUI.Init"));
        RegisterBefore(modConfig, "GameEntryUI.Start", context => CloseAll("GameEntryUI.Start"));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        TerriasHookRegistry.Before(config, target, action, "TerriasUiLifecycle");
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
        TerriasTransientUiRegistry.CloseAll(source);
    }

    private static void ResetForGameEntry(string source)
    {
        CloseAll(source);
        FrameSpriteCache.Clear();
        TerriasUiSprites.Clear();
        TerriasResourceCache.ClearCategory("visual.effect-texture");
        TerriasResourceCache.ClearCategory("visual.card-skin");
        TerriasResourceCache.ClearCategory("visual.frame-animation");
        TerriasResourceCache.ClearCategory("ui.sprite-source");
        TerriasResourceCache.ClearCategory(TerriasIds.WitchArchiveResourceCategory);
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
