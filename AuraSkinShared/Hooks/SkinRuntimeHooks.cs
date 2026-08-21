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
                var instanceId = context.Arguments.Length > 1 ? context.Arguments[1] as string ?? "" : "";
                SkinRuntime.EnsureAnimation(context.Arguments[0] as DataConfig, instanceId);
            }
        }
        catch (Exception ex)
        {
            SkinLog.Error("AnimatorRole.Init skin preparation failed", ex);
        }
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBeforeRouted(
            config,
            target,
            Request(target, action),
            warn: SkinLog.Warn);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfterRouted(
            config,
            target,
            Request(target, action),
            warn: SkinLog.Warn);
    }

    private static AuraRoutedHookRequest Request(
        string target,
        Action<ModHookContext> action)
    {
        return new AuraRoutedHookRequest
        {
            OwnerModId = "AuraSkinShared",
            HandlerId = target + ":" + action.Method.Name,
            Handler = action,
            SafeInvoke = true
        };
    }
}
