using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BattleBgmArbiter.Shared;
using SanGuoShaExp.Dll.Infrastructure;
using Witch.Mod;

namespace SanGuoShaExp.Dll.GameApi;

public static class BattleBgmProviderRuntime
{
    private const string ModId = "SanGuoShaExp";
    private const string ProviderId = "SanGuoShaExp.ShenZhugeLiangBattleBgm";
    private const string ShenZhugeLiangCareerId = "shen_zhugeliang";
    private const string ExpandedShenZhugeLiangCareerId = "SanGuoShaExp_shen_zhugeliang_shen_zhugeliang";
    private const string BattleBgmFileName = "\u5bf9\u5c40BGM.mp3";
    private static ModConfig? currentModConfig;

    public static void Initialize(ModConfig modConfig)
    {
        if (modConfig == null)
        {
            SanGuoShaExpLog.Warn("Battle BGM provider initialization skipped: mod config is null");
            return;
        }

        currentModConfig = modConfig;
        var audioPath = Path.Combine(modConfig.DirectoryName, BattleBgmFileName);
        SanGuoShaExpLog.Info("Battle BGM provider initializing: path=" + audioPath);

        BattleBgmArbiterRuntime.Initialize(modConfig, ModId);
        BattleBgmArbiterRuntime.RegisterProvider(
            modConfig,
            ModId,
            new FileBattleBgmProvider(
                providerId: ProviderId,
                ownerModId: ModId,
                audioPath: audioPath,
                priority: 200,
                hardClaim: true,
                silenceWhenLoading: true,
                fallbackToOriginalWhenFailed: true,
                adventureCondition: IsShenZhugeLiangAdventure,
                battleCondition: null,
                allowMidBattleSwitch: true));
    }

    public static void RequestBattleSwitch(string reason, bool force = false, bool allowSilenceWhenLoading = false, bool restartIfSameClip = true)
    {
        if (currentModConfig == null)
        {
            SanGuoShaExpLog.Warn("Battle BGM switch skipped: provider runtime is not initialized");
            return;
        }

        BattleBgmArbiterRuntime.Signal(
            currentModConfig,
            ModId,
            "BattleBgmSwitchRequested",
            new BattleBgmSwitchRequest
            {
                ProviderId = ProviderId,
                Reason = string.IsNullOrWhiteSpace(reason) ? "SanGuoShaExp.RequestBattleSwitch" : reason,
                Force = force,
                AllowSilenceWhenLoading = allowSilenceWhenLoading,
                RestartIfSameClip = restartIfSameClip
            });
    }

    private static bool IsShenZhugeLiangAdventure(object? context)
    {
        try
        {
            var careerId = ReadStringProperty(context, "CareerId");
            if (IsShenZhugeLiangCareerId(careerId))
            {
                SanGuoShaExpLog.Info("Battle BGM adventure condition matched by career: " + careerId);
                return true;
            }

            var packs = ReadStringSetProperty(context, "EnabledCardPackIds");
            return packs.Contains(ModId) || packs.Contains(ProviderId);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Battle BGM adventure condition failed: " + ex.Message);
            return false;
        }
    }

    private static bool IsShenZhugeLiangCareerId(string careerId)
    {
        if (string.IsNullOrWhiteSpace(careerId))
        {
            return false;
        }

        if (string.Equals(careerId, ShenZhugeLiangCareerId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(careerId, ExpandedShenZhugeLiangCareerId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return careerId.StartsWith(ModId + "_", StringComparison.OrdinalIgnoreCase)
            && careerId.EndsWith("_" + ShenZhugeLiangCareerId, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadStringProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return "";
        }

        return source.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(source) as string ?? "";
    }

    private static HashSet<string> ReadStringSetProperty(object? source, string propertyName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (source == null)
        {
            return result;
        }

        var value = source.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(source);
        if (value is IEnumerable items)
        {
            foreach (var item in items)
            {
                if (item is string text && !string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text);
                }
            }
        }

        return result;
    }
}
