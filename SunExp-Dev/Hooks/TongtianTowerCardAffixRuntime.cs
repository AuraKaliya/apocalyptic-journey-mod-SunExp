using System;
using System.Reflection;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerCardAffixRuntime
{
    private const string BurnoutTag = "Burnout";
    private static readonly FieldInfo? CardChoiceItemDataConfigField = typeof(CardChoiceItem).GetField(
        "dataConfig",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "CardChoiceItem.Initialize", ApplyToChoiceItem);
        RegisterBefore(modConfig, "CardChoiceUI.Select", ApplyToSelectedCard);
        SunExpLog.Info("Tongtian Tower card affix runtime initialized");
    }

    private static void ApplyToChoiceItem(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun()
                || context.Target is not CardChoiceItem item
                || CardChoiceItemDataConfigField?.GetValue(item) is not DataConfig config)
            {
                return;
            }

            if (!ApplyBurnout(config, "CardChoiceItem.Initialize"))
            {
                return;
            }

            RefreshChoiceItem(item, config);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] choice item hook failed", ex);
        }
    }

    private static void ApplyToSelectedCard(ModHookContext context)
    {
        try
        {
            if (!TongtianTowerModeRuntime.IsTongtianTowerRun())
            {
                return;
            }

            var args = context.Arguments;
            if (args == null || args.Length < 2 || args[1] is not IDataConfig config)
            {
                return;
            }

            ApplyBurnout(config, "CardChoiceUI.Select");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[TongtianTowerCardAffix] selected card hook failed", ex);
        }
    }

    private static bool ApplyBurnout(IDataConfig config, string source)
    {
        var changed = CardMutationService.AddNativeTags(config, BurnoutTag);
        if (changed)
        {
            SunExpLog.Debug("[TongtianTowerCardAffix] applied Burnout from " + source);
        }

        return changed;
    }

    private static void RefreshChoiceItem(CardChoiceItem item, DataConfig config)
    {
        ICard.SetCardMsg(item.transform, config, null);
        item.DataUpdate();
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian Tower card affix " + message));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Tongtian Tower card affix " + message));
    }
}
