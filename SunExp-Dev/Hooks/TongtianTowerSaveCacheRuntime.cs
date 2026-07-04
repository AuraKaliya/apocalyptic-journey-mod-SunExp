using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class TongtianTowerSaveCacheRuntime
{
    private static readonly HashSet<string> TemporarilyProtectedSaves = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "ModeChoiceUI.Init", _ => ClearNativeNormalCache("ModeChoiceUI.Init"));
        RegisterAfter(modConfig, "ModeChoiceUI.DataUpdate", _ => ClearNativeNormalCache("ModeChoiceUI.DataUpdate"));
        RegisterBefore(modConfig, "ModeChoiceUI.NormalMode", _ => ClearNativeNormalCache("ModeChoiceUI.NormalMode"));
        RegisterBefore(modConfig, "ModeChoiceUI.DeleteExistingSavesForMode", ProtectTowerSavesBeforeNativeDelete);
        RegisterAfter(modConfig, "ModeChoiceUI.DeleteExistingSavesForMode", RestoreTowerSavesAfterNativeDelete);
    }

    public static void ClearNativeNormalCache(string source)
    {
        ModeChoiceSaveCacheApi.ClearCachedSaveIf(
            SunExpIds.NativeNormalModeType,
            TongtianTowerRunStateStore.IsTongtianSave,
            source);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("tongtian tower save cache " + message));
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("tongtian tower save cache " + message));
    }

    private static void ProtectTowerSavesBeforeNativeDelete(ModHookContext context)
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
                if (!TongtianTowerRunStateStore.IsTongtianSave(saveInfo)
                    || !string.Equals(saveInfo.modeType, SunExpIds.NativeNormalModeType, StringComparison.Ordinal))
                {
                    continue;
                }

                saveInfo.modeType = SunExpIds.TongtianTowerModeType;
                TemporarilyProtectedSaves.Add(saveInfo.Name);
            }

            if (TemporarilyProtectedSaves.Count > 0)
            {
                SunExpLog.Debug("[TongtianTowerSaveCache] protected "
                    + TemporarilyProtectedSaves.Count
                    + " tower saves from native Normal cleanup.");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerSaveCache] protect before native delete failed: " + ex.Message);
        }
    }

    private static void RestoreTowerSavesAfterNativeDelete(ModHookContext context)
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

            SunExpLog.Debug("[TongtianTowerSaveCache] restored "
                + TemporarilyProtectedSaves.Count
                + " protected tower saves after native Normal cleanup.");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[TongtianTowerSaveCache] restore after native delete failed: " + ex.Message);
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
