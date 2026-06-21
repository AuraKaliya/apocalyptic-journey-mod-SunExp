using System;
using AuraShared.Core;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraSkin.Shared.Hooks;

public static class SkinRuntimeHooks
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "GameEntryUI.Init", SkinUiRuntime.OnGameEntryRefresh);
        RegisterAfter(modConfig, "GameEntryUI.DataUpdate", SkinUiRuntime.OnGameEntryRefresh);
        RegisterAfter(modConfig, "GameEntryUI.ShowCareer", SkinUiRuntime.OnCareerListReady);
        RegisterAfter(modConfig, "GameEntryUI.ShowDetail", SkinUiRuntime.OnCareerChanged);
        RegisterAfter(modConfig, "GameEntryUI.ApplyCareerDetail", SkinUiRuntime.OnCareerDetailApplied);
        RegisterAfter(modConfig, "ShowCareer.Init", SkinUiRuntime.OnCareerChoiceItemInitialized);
        RegisterBefore(modConfig, "AnimatorRole.Init", EnsureAnimatorSkin);
        RegisterAfter(modConfig, "TopBarUI.ChangeCareer", SkinUiRuntime.OnTopBarAvatarChanged);
        RegisterAfter(modConfig, "TopBarUI.ChangeCareerAvator", SkinUiRuntime.OnTopBarAvatarChanged);
        RegisterAfter(modConfig, "TopStatusItem.Init", SkinUiRuntime.OnTopStatusChanged);
        RegisterAfter(modConfig, "TopStatusItem.CareerInit", SkinUiRuntime.OnTopStatusCareerChanged);
        RegisterAfter(modConfig, "StatusUI.ShowMsg", SkinUiRuntime.OnStatusShown);
        SkinLog.Info("Skin runtime hooks registered");
    }

    private static void EnsureAnimatorSkin(ModHookContext context)
    {
        try
        {
            if (context.Arguments != null && context.Arguments.Length > 0)
            {
                SkinRuntime.EnsureAnimation(context.Arguments[0] as DataConfig);
            }
        }
        catch (Exception ex)
        {
            SkinLog.Error("AnimatorRole.Init skin preparation failed", ex);
        }
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, warn: SkinLog.Warn);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, warn: SkinLog.Warn);
    }
}
