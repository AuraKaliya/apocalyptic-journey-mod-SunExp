using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraAudio.Shared;
using AudioArbiter.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using BattleBgmArbiter.Shared;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Audio;

public static class AuraToolsAudioRuntime
{
    public const string AudioSystemVersion = "2.0.0";
    private static readonly Dictionary<string, bool> PathExistsCache = new(StringComparer.OrdinalIgnoreCase);
    private static ModConfig? modConfig;
    private static bool initialized;

    public static void Initialize(ModConfig config)
    {
        modConfig = config;
        var audio = AuraAudioRuntime.Initialize(
            config,
            AuraToolsIds.ModId,
            installPackage: false);
        if (!audio.Success)
        {
            AuraToolsLog.Warn("Audio shared runtime initialization reported issues: " + audio.ErrorMessage);
        }
        BattleBgmArbiterRuntime.Initialize(config, AuraToolsIds.ModId);
        AuraToolsConfigService.Changed += RegisterProviders;
        initialized = true;
        RegisterProviders();
    }

    public static void RegisterProviders()
    {
        if (!initialized || modConfig == null)
        {
            return;
        }

        try
        {
            RefreshPathExistsCache();
            RegisterBattleBgmProviders(modConfig);
            RegisterCardUseProviders(modConfig);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("Audio provider registration failed", ex);
        }
    }

    public static string DescribeBattleBgmMode()
    {
        var mode = AuraToolsConfigService.Audio.BattleBgm.Mode == AudioModes.Advanced ? "高级" : "通用";
        return AuraToolsConfigService.Audio.BattleBgm.Enabled ? mode : "关闭";
    }

    public static string DescribeCardUseMode()
    {
        var mode = AuraToolsConfigService.Audio.CardUse.Mode == AudioModes.Advanced ? "高级" : "通用";
        return AuraToolsConfigService.Audio.CardUse.Enabled ? mode : "关闭";
    }

    private static void RegisterBattleBgmProviders(ModConfig config)
    {
        var commonPath = AuraToolsConfigService.ResolveConfiguredPath(AuraToolsConfigService.Audio.BattleBgm.Common.RelativePath);
        BattleBgmArbiterRuntime.RegisterProvider(
            config,
            AuraToolsIds.ModId,
            new FileBattleBgmProvider(
                providerId: ProviderIds.CommonBattleBgm,
                ownerModId: AuraToolsIds.ModId,
                audioPath: commonPath,
                priority: AuraToolsConfigService.Audio.BattleBgm.Common.Priority,
                hardClaim: AuraToolsConfigService.Audio.BattleBgm.Common.HardClaim,
                silenceWhenLoading: AuraToolsConfigService.Audio.BattleBgm.Common.SilenceWhenLoading,
                fallbackToOriginalWhenFailed: AuraToolsConfigService.Audio.BattleBgm.Common.FallbackToOriginalWhenFailed,
                adventureCondition: IsCommonBattleBgmEnabled,
                battleCondition: IsCommonBattleBgmEnabled,
                allowMidBattleSwitch: false));

        foreach (var role in CurrentBattleBgmRoles())
        {
            var roleId = role.Key;
            var settings = role.Value;
            var providerId = ProviderIds.RoleBattleBgm(roleId);
            BattleBgmArbiterRuntime.RegisterProvider(
                config,
                AuraToolsIds.ModId,
                new FileBattleBgmProvider(
                    providerId: providerId,
                    ownerModId: AuraToolsIds.ModId,
                    audioPath: AuraToolsConfigService.ResolveConfiguredPath(settings.RelativePath),
                    priority: settings.Priority,
                    hardClaim: settings.HardClaim,
                    silenceWhenLoading: false,
                    fallbackToOriginalWhenFailed: true,
                    adventureCondition: context => IsRoleBattleBgmEnabled(context, roleId),
                    battleCondition: context => IsRoleBattleBgmEnabled(context, roleId),
                    allowMidBattleSwitch: false));
        }

        AuraToolsLog.Info("Audio/BGM providers registered. mode=" + DescribeBattleBgmMode()
                          + ", roles=" + CurrentBattleBgmRoles().Count);
    }

    private static void RegisterCardUseProviders(ModConfig config)
    {
        var common = AuraToolsConfigService.Audio.CardUse.Common;
        AudioArbiterRuntime.RegisterSoundProvider(
            config,
            AuraToolsIds.ModId,
            new FileSoundProvider(
                providerId: ProviderIds.CommonCardUse,
                ownerModId: AuraToolsIds.ModId,
                audioPath: AuraToolsConfigService.ResolveConfiguredPath(common.RelativePath),
                priority: common.Priority,
                bus: SoundBuses.Effect,
                policy: SoundPolicies.Replace,
                hardClaim: common.HardClaim,
                condition: IsCommonCardUseEnabled,
                cooldownSeconds: 0.02f,
                sync: true,
                gainDb: common.GainDb,
                kind: SoundEventKinds.CardUse));

        foreach (var role in CurrentCardUseRoles())
        {
            var roleId = role.Key;
            var settings = role.Value;
            AudioArbiterRuntime.RegisterSoundProvider(
                config,
                AuraToolsIds.ModId,
                new FileSoundProvider(
                    providerId: ProviderIds.RoleCardUse(roleId),
                    ownerModId: AuraToolsIds.ModId,
                    audioPath: AuraToolsConfigService.ResolveConfiguredPath(settings.RelativePath),
                    priority: settings.Priority,
                    bus: SoundBuses.Effect,
                    policy: SoundPolicies.Replace,
                    hardClaim: settings.HardClaim,
                    condition: context => IsRoleCardUseEnabled(context, roleId),
                    cooldownSeconds: 0.02f,
                    sync: true,
                    gainDb: settings.GainDb,
                    kind: SoundEventKinds.CardUse));
        }

        AuraToolsLog.Info("Audio/CardUse providers registered. mode=" + DescribeCardUseMode()
                          + ", roles=" + CurrentCardUseRoles().Count);
    }

    private static Dictionary<string, AudioRoleSettings> CurrentBattleBgmRoles()
    {
        return AuraToolsConfigService.Audio.BattleBgm.Roles
            .Where(pair => pair.Value != null && !string.IsNullOrWhiteSpace(pair.Value.RelativePath))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, AudioRoleSettings> CurrentCardUseRoles()
    {
        return AuraToolsConfigService.Audio.CardUse.Roles
            .Where(pair => pair.Value != null && !string.IsNullOrWhiteSpace(pair.Value.RelativePath))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void RefreshPathExistsCache()
    {
        PathExistsCache.Clear();
        RememberPath(AuraToolsConfigService.Audio.BattleBgm.Common.RelativePath);
        RememberPath(AuraToolsConfigService.Audio.CardUse.Common.RelativePath);
        foreach (var role in AuraToolsConfigService.Audio.BattleBgm.Roles.Values)
        {
            RememberPath(role?.RelativePath);
        }

        foreach (var role in AuraToolsConfigService.Audio.CardUse.Roles.Values)
        {
            RememberPath(role?.RelativePath);
        }
    }

    private static void RememberPath(string? relativeOrAbsolute)
    {
        var key = (relativeOrAbsolute ?? "").Trim();
        if (key.Length == 0)
        {
            return;
        }

        PathExistsCache[key] = File.Exists(AuraToolsConfigService.ResolveConfiguredPath(key));
    }

    private static bool CachedPathExists(string relativeOrAbsolute)
    {
        var key = (relativeOrAbsolute ?? "").Trim();
        if (key.Length == 0)
        {
            return false;
        }

        if (PathExistsCache.TryGetValue(key, out var exists))
        {
            return exists;
        }

        exists = File.Exists(AuraToolsConfigService.ResolveConfiguredPath(key));
        PathExistsCache[key] = exists;
        return exists;
    }

    private static bool IsCommonBattleBgmEnabled(object? context)
    {
        var settings = AuraToolsConfigService.Audio.BattleBgm;
        return AuraToolsConfigService.Root.Audio.Enabled
               && settings.Enabled
               && settings.Mode == AudioModes.Common
               && CachedPathExists(settings.Common.RelativePath);
    }

    private static bool IsRoleBattleBgmEnabled(object? context, string roleId)
    {
        var settings = AuraToolsConfigService.Audio.BattleBgm;
        if (!AuraToolsConfigService.Root.Audio.Enabled
            || !settings.Enabled
            || settings.Mode != AudioModes.Advanced
            || !settings.Roles.TryGetValue(roleId, out var role)
            || role == null
            || !role.Enabled
            || string.IsNullOrWhiteSpace(role.RelativePath)
            || !CachedPathExists(role.RelativePath))
        {
            return false;
        }

        return MatchesCareer(context, roleId);
    }

    private static bool IsCommonCardUseEnabled(object? context)
    {
        var settings = AuraToolsConfigService.Audio.CardUse;
        return AuraToolsConfigService.Root.Audio.Enabled
               && settings.Enabled
               && settings.Mode == AudioModes.Common
               && IsCardUse(context)
               && CachedPathExists(settings.Common.RelativePath);
    }

    private static bool IsRoleCardUseEnabled(object? context, string roleId)
    {
        var settings = AuraToolsConfigService.Audio.CardUse;
        if (!AuraToolsConfigService.Root.Audio.Enabled
            || !settings.Enabled
            || settings.Mode != AudioModes.Advanced
            || !IsCardUse(context)
            || !settings.Roles.TryGetValue(roleId, out var role)
            || role == null
            || !role.Enabled
            || string.IsNullOrWhiteSpace(role.RelativePath)
            || !CachedPathExists(role.RelativePath))
        {
            return false;
        }

        return MatchesCareer(context, roleId);
    }

    private static bool IsCardUse(object? context)
    {
        return string.Equals(AudioArbiterRuntime.ReadString(context, "Kind"), SoundEventKinds.CardUse, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCareer(object? context, string roleId)
    {
        var careerId = ReadCareerId(context);
        return !string.IsNullOrWhiteSpace(careerId)
               && string.Equals(careerId, roleId, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadCareerId(object? context)
    {
        if (context == null)
        {
            return "";
        }

        var direct = ReflectionUtil.ReadString(context, "CareerId", "RoleId");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var adventure = ReflectionUtil.GetMemberValue(context, "Adventure");
        return ReflectionUtil.ReadString(adventure, "CareerId", "RoleId");
    }

    private static class ProviderIds
    {
        public const string CommonBattleBgm = AuraToolsIds.ModId + ".Audio.BattleBgm.Common";
        public const string CommonCardUse = AuraToolsIds.ModId + ".Audio.CardUse.Common";

        public static string RoleBattleBgm(string roleId)
        {
            return AuraToolsIds.ModId + ".Audio.BattleBgm.Role." + Sanitize(roleId);
        }

        public static string RoleCardUse(string roleId)
        {
            return AuraToolsIds.ModId + ".Audio.CardUse.Role." + Sanitize(roleId);
        }

        private static string Sanitize(string value)
        {
            return string.Join("_", (value ?? "").Split(Path.GetInvalidFileNameChars()));
        }
    }
}
