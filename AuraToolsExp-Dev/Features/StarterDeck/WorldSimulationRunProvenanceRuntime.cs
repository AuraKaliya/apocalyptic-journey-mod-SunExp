using System;
using AuraMode.Shared;
using AuraToolsExp.Dll.Infrastructure;
using Data.Save;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal static class WorldSimulationRunProvenanceRuntime
{
    private static bool nativeNormalChoicePending;

    internal static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "ModeChoiceUI.NormalMode", _ => BeginNativeNormalChoice());
        RegisterAfter(modConfig, "ModeChoiceUI.CreateNewSave", CaptureNativeNormalSave);
        RegisterAfter(modConfig, "ModeChoiceUI.ReturnGame", CaptureNativeNormalSave);
        RegisterBefore(modConfig, "ModeChoiceUI.SlotMode", _ => CancelNativeNormalChoice());
        RegisterBefore(modConfig, "ModeChoiceUI.SublimationMode", _ => CancelNativeNormalChoice());
        RegisterBefore(modConfig, "ModeChoiceUI.TeachMode", _ => CancelNativeNormalChoice());
    }

    private static void BeginNativeNormalChoice()
    {
        nativeNormalChoicePending = true;
        if (ModeChoiceUI.beforeSave != null
            && ModeChoiceUI.beforeSave.TryGetValue(AuraModeRunIdentity.NativeWorldSimulationModeType, out var cached))
        {
            Mark(cached, "ModeChoiceUI.NormalMode.cached");
        }
    }

    private static void CaptureNativeNormalSave(ModHookContext context)
    {
        var modeType = context.Arguments != null && context.Arguments.Length > 0
            ? context.Arguments[0] as string ?? AuraModeRunIdentity.NativeWorldSimulationModeType
            : AuraModeRunIdentity.NativeWorldSimulationModeType;
        if (!string.Equals(
                modeType,
                AuraModeRunIdentity.NativeWorldSimulationModeType,
                StringComparison.OrdinalIgnoreCase))
        {
            CancelNativeNormalChoice();
            return;
        }

        if (!nativeNormalChoicePending)
        {
            return;
        }

        Mark(GameEntryUI.selectedSave ?? GameSaveManager.GetNowSave(), context.Target?.GetType().Name ?? "ModeChoiceUI");
        nativeNormalChoicePending = false;
    }

    private static void Mark(SaveInfo? saveInfo, string source)
    {
        if (saveInfo == null
            || !string.Equals(
                saveInfo.modeType,
                AuraModeRunIdentity.NativeWorldSimulationModeType,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        saveInfo.GameVars ??= new System.Collections.Generic.Dictionary<string, string>();
        saveInfo.GameVars[AuraModeRunIdentity.RunIdentityKey] = AuraModeRunIdentity.NativeWorldSimulationModeId;
        AuraToolsLog.Debug("[CustomStart] recorded native world-simulation provenance; save="
                           + (saveInfo.Name ?? "")
                           + ", source="
                           + source
                           + ".");
    }

    private static void CancelNativeNormalChoice()
    {
        nativeNormalChoicePending = false;
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action) =>
        AuraToolsHookRegistry.Before(config, target, action, "WorldSimulationRunProvenance");

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action) =>
        AuraToolsHookRegistry.After(config, target, action, "WorldSimulationRunProvenance");
}
