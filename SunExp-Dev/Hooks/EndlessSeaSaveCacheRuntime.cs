using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class EndlessSeaSaveCacheRuntime
{
    private static readonly HashSet<string> TemporarilyProtectedSaves = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "ModeChoiceUI.Init", _ => ClearNativeNormalCache("ModeChoiceUI.Init"));
        RegisterAfter(modConfig, "ModeChoiceUI.DataUpdate", _ => ClearNativeNormalCache("ModeChoiceUI.DataUpdate"));
        RegisterBefore(modConfig, "ModeChoiceUI.NormalMode", _ => ClearNativeNormalCache("ModeChoiceUI.NormalMode"));
        RegisterBefore(modConfig, "ModeChoiceUI.DeleteExistingSavesForMode", ProtectSeaSavesBeforeNativeDelete);
        RegisterAfter(modConfig, "ModeChoiceUI.DeleteExistingSavesForMode", RestoreSeaSavesAfterNativeDelete);
    }

    public static void ClearNativeNormalCache(string source)
    {
        ModeChoiceSaveCacheApi.ClearCachedSaveIf(
            SunExpIds.NativeNormalModeType,
            EndlessSeaRunStateStore.IsEndlessSeaSave,
            source);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "EndlessSeaSaveCache");
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.Before(config, target, action, "EndlessSeaSaveCache");
    }

    private static void ProtectSeaSavesBeforeNativeDelete(ModHookContext context)
    {
        try
        {
            if (!IsNativeNormalDelete(context))
            {
                return;
            }

            TemporarilyProtectedSaves.Clear();
            foreach (var saveInfo in Singleton<GameRuntimeData>.Instance?.Saves ?? new List<Data.Save.SaveInfo>())
            {
                if (!EndlessSeaRunStateStore.IsEndlessSeaSave(saveInfo)
                    || !string.Equals(saveInfo.modeType, SunExpIds.NativeNormalModeType, StringComparison.Ordinal))
                {
                    continue;
                }

                saveInfo.modeType = SunExpIds.EndlessSeaModeType;
                TemporarilyProtectedSaves.Add(saveInfo.Name);
            }

            if (TemporarilyProtectedSaves.Count > 0)
            {
                SunExpLog.Debug("[EndlessSeaSaveCache] protected "
                    + TemporarilyProtectedSaves.Count
                    + " Endless Sea saves from native Normal cleanup.");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessSeaSaveCache] protect before native delete failed: " + ex.Message);
        }
    }

    private static void RestoreSeaSavesAfterNativeDelete(ModHookContext context)
    {
        try
        {
            if (!IsNativeNormalDelete(context) || TemporarilyProtectedSaves.Count == 0)
            {
                return;
            }

            foreach (var saveInfo in Singleton<GameRuntimeData>.Instance?.Saves ?? new List<Data.Save.SaveInfo>())
            {
                if (saveInfo == null || !TemporarilyProtectedSaves.Contains(saveInfo.Name))
                {
                    continue;
                }

                saveInfo.modeType = SunExpIds.NativeNormalModeType;
            }

            SunExpLog.Debug("[EndlessSeaSaveCache] restored "
                + TemporarilyProtectedSaves.Count
                + " protected Endless Sea saves after native Normal cleanup.");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessSeaSaveCache] restore after native delete failed: " + ex.Message);
        }
        finally
        {
            TemporarilyProtectedSaves.Clear();
        }
    }

    private static bool IsNativeNormalDelete(ModHookContext context)
    {
        return context.Arguments != null
            && context.Arguments.Length > 0
            && string.Equals(Convert.ToString(context.Arguments[0]), SunExpIds.NativeNormalModeType, StringComparison.Ordinal);
    }
}
