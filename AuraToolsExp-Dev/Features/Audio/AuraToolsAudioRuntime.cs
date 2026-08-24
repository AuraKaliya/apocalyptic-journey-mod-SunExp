using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraAudio.Shared;
using AuraShared.Core;
using AudioArbiter.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using AuraToolsExp.Dll.Features.SharedResources;
using BattleBgmArbiter.Shared;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.Audio;

public static class AuraToolsAudioRuntime
{
    private static readonly Dictionary<string, bool> PathExistsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> RegisteredBattleBgmSignatures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> RegisteredCardUseSignatures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, VoiceProviderRegistration> RegisteredVoiceProviders =
        new(StringComparer.OrdinalIgnoreCase);
    private static ModConfig? modConfig;
    private static bool initialized;

    public static void Initialize(ModConfig config)
    {
        if (initialized) return;
        modConfig = config;
        AuraSharedRuntime.Initialize(config, AuraToolsIds.ModId);
        AudioArbiterRuntime.Initialize(config, AuraToolsIds.ModId);
        BattleBgmArbiterRuntime.Initialize(config, AuraToolsIds.ModId);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.BattleBgm,
            RegisterProviders);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.CardUseAudio,
            RegisterProviders);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.Voice,
            RegisterProviders);
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
            RegisterVoiceProviders(modConfig);
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
        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changes = 0;
        var feature = AuraToolsConfigService.Audio.BattleBgm;
        if (feature.Enabled)
        {
            if (feature.Mode == AudioModes.Common)
            {
                var common = feature.Common;
                var path = AuraToolsConfiguredResourceResolver.ResolveAudioPath(common.RelativePath);
                changes += RegisterBattleBgmProvider(
                    config,
                    desired,
                    ProviderIds.CommonBattleBgm,
                    Signature(path, common.Priority, common.HardClaim, common.SilenceWhenLoading,
                        common.FallbackToOriginalWhenFailed),
                    () => new FileBattleBgmProvider(
                        ProviderIds.CommonBattleBgm,
                        AuraToolsIds.ModId,
                        path,
                        common.Priority,
                        common.HardClaim,
                        common.SilenceWhenLoading,
                        common.FallbackToOriginalWhenFailed,
                        IsCommonBattleBgmEnabled,
                        IsCommonBattleBgmEnabled,
                        false));
            }
            else
            {
                foreach (var role in CurrentBattleBgmRoles().Where(pair => pair.Value.Enabled))
                {
                    var roleId = role.Key;
                    var settings = role.Value;
                    var providerId = ProviderIds.RoleBattleBgm(roleId);
                    var path = AuraToolsConfiguredResourceResolver.ResolveAudioPath(settings.RelativePath);
                    changes += RegisterBattleBgmProvider(
                        config,
                        desired,
                        providerId,
                        Signature(path, settings.Priority, settings.HardClaim),
                        () => new FileBattleBgmProvider(
                            providerId,
                            AuraToolsIds.ModId,
                            path,
                            settings.Priority,
                            settings.HardClaim,
                            false,
                            true,
                            context => IsRoleBattleBgmEnabled(context, roleId),
                            context => IsRoleBattleBgmEnabled(context, roleId),
                            false));
                }
            }
        }

        changes += RemoveStaleBattleBgmProviders(config, desired);
        if (changes > 0)
        {
            AuraToolsLog.Info("Audio/BGM providers synchronized. mode=" + DescribeBattleBgmMode()
                              + ", active=" + desired.Count + ", changes=" + changes + ".");
        }
    }

    private static void RegisterCardUseProviders(ModConfig config)
    {
        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changes = 0;
        var feature = AuraToolsConfigService.Audio.CardUse;
        if (feature.Enabled)
        {
            if (feature.Mode == AudioModes.Common)
            {
                var common = feature.Common;
                var path = AuraToolsConfiguredResourceResolver.ResolveAudioPath(common.RelativePath);
                changes += RegisterCardUseProvider(
                    config,
                    desired,
                    ProviderIds.CommonCardUse,
                    Signature(path, common.Priority, common.HardClaim, common.GainDb),
                    () => new FileSoundProvider(
                        ProviderIds.CommonCardUse,
                        AuraToolsIds.ModId,
                        path,
                        common.Priority,
                        SoundBuses.Effect,
                        SoundPolicies.Replace,
                        common.HardClaim,
                        IsCommonCardUseEnabled,
                        0.02f,
                        false,
                        common.GainDb,
                        kind: SoundEventKinds.CardUse));
            }
            else
            {
                foreach (var role in CurrentCardUseRoles().Where(pair => pair.Value.Enabled))
                {
                    var roleId = role.Key;
                    var settings = role.Value;
                    var providerId = ProviderIds.RoleCardUse(roleId);
                    var path = AuraToolsConfiguredResourceResolver.ResolveAudioPath(settings.RelativePath);
                    changes += RegisterCardUseProvider(
                        config,
                        desired,
                        providerId,
                        Signature(path, settings.Priority, settings.HardClaim, settings.GainDb),
                        () => new FileSoundProvider(
                            providerId,
                            AuraToolsIds.ModId,
                            path,
                            settings.Priority,
                            SoundBuses.Effect,
                            SoundPolicies.Replace,
                            settings.HardClaim,
                            context => IsRoleCardUseEnabled(context, roleId),
                            0.02f,
                            false,
                            settings.GainDb,
                            kind: SoundEventKinds.CardUse));
                }
            }
        }

        changes += RemoveStaleCardUseProviders(config, desired);
        if (changes > 0)
        {
            AuraToolsLog.Info("Audio/CardUse providers synchronized. mode=" + DescribeCardUseMode()
                              + ", presentationRelay=client-request-host-authorized"
                              + ", active=" + desired.Count + ", changes=" + changes + ".");
        }
    }

    private static int RegisterBattleBgmProvider(
        ModConfig config,
        ISet<string> desired,
        string providerId,
        string signature,
        Func<object> factory)
    {
        desired.Add(providerId);
        if (RegisteredBattleBgmSignatures.TryGetValue(providerId, out var current)
            && string.Equals(current, signature, StringComparison.Ordinal))
        {
            return 0;
        }

        BattleBgmArbiterRuntime.RegisterProvider(config, AuraToolsIds.ModId, factory());
        RegisteredBattleBgmSignatures[providerId] = signature;
        return 1;
    }

    private static int RegisterCardUseProvider(
        ModConfig config,
        ISet<string> desired,
        string providerId,
        string signature,
        Func<object> factory)
    {
        desired.Add(providerId);
        if (RegisteredCardUseSignatures.TryGetValue(providerId, out var current)
            && string.Equals(current, signature, StringComparison.Ordinal))
        {
            return 0;
        }

        AudioArbiterRuntime.RegisterSoundProvider(config, AuraToolsIds.ModId, factory());
        RegisteredCardUseSignatures[providerId] = signature;
        return 1;
    }

    private static void RegisterVoiceProviders(ModConfig config)
    {
        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var migratedBindings = false;
        if (AuraToolsConfigService.Audio.Voice.Enabled)
        {
            foreach (var contribution in AuraAudioRegistryRuntime.GetSnapshot().Contributions)
            {
                if (!AuraToolsSharedResourceDiscoveryRuntime.IsSourceActive(
                        contribution.SourceModProjectId))
                {
                    continue;
                }
                var manifest = contribution.Manifest ?? new AudioRegistryManifest();
                var defaults = manifest.defaults ?? new AudioRegistryDefaults();
                foreach (var provider in manifest.providers ?? Array.Empty<AudioProviderManifest>())
                {
                    if (provider == null || string.IsNullOrWhiteSpace(provider.providerId)) continue;
                    var owner = string.IsNullOrWhiteSpace(provider.ownerModId)
                        ? contribution.OwnerModId
                        : provider.ownerModId.Trim();
                    var providerId = provider.providerId.Trim();
                    var qualifiedId = owner + ":" + providerId;
                    var settings = EnsureVoiceBinding(qualifiedId, provider);
                    migratedBindings |= MigrateSkillVoiceBinding(provider, settings);
                    if (!settings.Enabled) continue;
                    var signal = string.IsNullOrWhiteSpace(settings.Signal) ? provider.kind : settings.Signal;
                    var stage = string.IsNullOrWhiteSpace(settings.Stage)
                        ? provider.match?.stages?.FirstOrDefault() ?? ""
                        : settings.Stage;
                    var resourceOverridden = !string.IsNullOrWhiteSpace(settings.ResourcePath);
                    var audioPath = ResolveVoicePath(owner, config, resourceOverridden ? settings.ResourcePath : provider.path);
                    var variants = resourceOverridden
                        ? Array.Empty<string>()
                        : (provider.variantPaths ?? Array.Empty<string>())
                            .Select(value => ResolveVoicePath(owner, config, value))
                            .Where(value => value.Length > 0)
                            .ToArray();
                    var gain = settings.GainDb ?? provider.gainDb ?? defaults.gainDb ?? 0f;
                    var cooldown = settings.CooldownSeconds ?? provider.cooldownSeconds ?? defaults.cooldownSeconds ?? 0f;
                    var threshold = settings.HpRatioThreshold ?? provider.match?.hpRatioCrossDown;
                    var signature = Signature(audioPath, string.Join(";", variants), signal, stage, settings.ActionId,
                        settings.SkillSlot,
                        gain, cooldown, threshold, settings.Enabled);
                    desired.Add(qualifiedId);
                    if (RegisteredVoiceProviders.TryGetValue(qualifiedId, out var current)
                        && string.Equals(current.Signature, signature, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    AudioArbiterRuntime.RegisterSoundProvider(config, AuraToolsIds.ModId, new FileSoundProvider(
                        providerId,
                        owner,
                        audioPath,
                        variants,
                        provider.priority,
                        string.IsNullOrWhiteSpace(provider.bus) ? defaults.bus ?? SoundBuses.Vocal : provider.bus,
                        string.IsNullOrWhiteSpace(provider.policy) ? defaults.policy ?? SoundPolicies.Additive : provider.policy,
                        provider.hardClaim ?? defaults.hardClaim ?? false,
                        request => VoiceMatches(request, provider, settings, signal, stage, threshold),
                        cooldown,
                        provider.sync ?? defaults.sync ?? true,
                        gain,
                        provider.volumeMultiplier ?? defaults.volumeMultiplier ?? 1f,
                        signal,
                        threshold,
                        provider.suppressOriginal?.vocalStates,
                        provider.suppressOriginal?.narrationIds));
                    RegisteredVoiceProviders[qualifiedId] = new VoiceProviderRegistration
                    {
                        OwnerModId = owner,
                        ProviderId = providerId,
                        Signature = signature
                    };
                }
            }
        }

        foreach (var qualifiedId in RegisteredVoiceProviders.Keys.Where(id => !desired.Contains(id)).ToList())
        {
            var registered = RegisteredVoiceProviders[qualifiedId];
            AudioArbiterRuntime.UnregisterSoundProvider(config, registered.OwnerModId, registered.ProviderId);
            RegisteredVoiceProviders.Remove(qualifiedId);
        }

        if (migratedBindings)
        {
            AuraToolsConfigService.PersistVoiceMigration();
        }
    }

    private static AuraToolsVoiceBindingSettings EnsureVoiceBinding(
        string qualifiedProviderId,
        AudioProviderManifest provider)
    {
        var bindings = AuraToolsConfigService.Audio.Voice.Bindings;
        if (!bindings.TryGetValue(qualifiedProviderId, out var settings) || settings == null)
        {
            settings = new AuraToolsVoiceBindingSettings
            {
                ProviderId = qualifiedProviderId,
                Signal = provider.kind,
                Stage = provider.match?.stages?.FirstOrDefault() ?? "",
                ActionId = FirstActionId(provider),
                SkillSlot = provider.match?.skillSlot,
                HpRatioThreshold = provider.match?.hpRatioCrossDown
            };
            settings.Normalize(qualifiedProviderId);
            bindings[qualifiedProviderId] = settings;
        }
        return settings;
    }

    private static bool MigrateSkillVoiceBinding(
        AudioProviderManifest provider,
        AuraToolsVoiceBindingSettings settings)
    {
        if (!string.Equals(provider.kind, SoundEventKinds.SkillVoice, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var configuredSkills = ResolveProviderSkills(provider)
            .Select(skill => new AuraToolsVoiceSkillDescriptor
            {
                Id = skill.Id,
                Slot = skill.Slot
            });
        return AuraToolsVoiceSkillBindingMigration.Migrate(
            settings,
            provider.kind,
            provider.match?.stages?.FirstOrDefault() ?? AudioSignalStages.Committed,
            provider.match?.skillSlot,
            configuredSkills);
    }

    private static IReadOnlyList<RoleSkillInfo> ResolveProviderSkills(AudioProviderManifest provider)
    {
        foreach (var roleId in (provider.match?.roleIds ?? Array.Empty<string>())
                     .Concat(provider.match?.careerIds ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var skills = RoleCatalog.GetRoleSkills(roleId);
            if (skills.Count > 0)
            {
                return skills;
            }
        }

        return Array.Empty<RoleSkillInfo>();
    }

    private static string FirstActionId(AudioProviderManifest provider)
    {
        return provider.match?.cardIds?.FirstOrDefault()
               ?? provider.match?.battleResults?.FirstOrDefault()
               ?? provider.vocalState
               ?? "";
    }

    private static string ResolveVoicePath(string ownerModId, ModConfig config, string value)
    {
        var text = (value ?? "").Trim();
        const string shared = "Shared:";
        if (text.StartsWith(shared, StringComparison.OrdinalIgnoreCase))
        {
            return AuraSharedResourceProtocol.ResolvePath(ownerModId, text.Substring(shared.Length));
        }
        return Path.IsPathRooted(text) ? text : Path.Combine(config.DirectoryName, text.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool VoiceMatches(
        object? request,
        AudioProviderManifest provider,
        AuraToolsVoiceBindingSettings settings,
        string signal,
        string stage,
        float? threshold)
    {
        if (!string.Equals(AudioArbiterRuntime.ReadString(request, "Kind"), signal, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(stage)
            && !string.Equals(AudioArbiterRuntime.ReadString(request, "Stage"), stage, StringComparison.OrdinalIgnoreCase)) return false;
        var match = provider.match ?? new AudioProviderMatch();
        if (!MatchesAny(match.careerIds, AudioArbiterRuntime.ReadString(request, "CareerId"))) return false;
        if (!MatchesAny(match.roleIds, AudioArbiterRuntime.ReadString(request, "RoleId"))) return false;
        var isSkillVoice = string.Equals(signal, SoundEventKinds.SkillVoice, StringComparison.OrdinalIgnoreCase);
        if (isSkillVoice
            && (!settings.SkillSlot.HasValue
                || settings.SkillSlot.Value <= 0
                || AudioArbiterRuntime.ReadInt(request, "SkillSlot", 0) != settings.SkillSlot.Value))
        {
            return false;
        }
        var actionId = settings.ActionId;
        if (!isSkillVoice && !string.IsNullOrWhiteSpace(actionId))
        {
            var actual = string.Equals(signal, SoundEventKinds.BattleCompleted, StringComparison.OrdinalIgnoreCase)
                ? AudioArbiterRuntime.ReadString(request, "BattleResult")
                : string.Equals(signal, SoundEventKinds.VocalState, StringComparison.OrdinalIgnoreCase)
                    ? AudioArbiterRuntime.ReadString(request, "VocalState")
                    : AudioArbiterRuntime.ReadString(request, "CardId");
            if (!MatchesId(actionId, actual)) return false;
        }
        if (match.localOwnerOnly == true
            && !AudioArbiterRuntime.ReadBool(request, "IsRemote", false)
            && !AudioArbiterRuntime.ReadBool(request, "IsLocalOwner", false)) return false;
        if (threshold.HasValue
            && !(AudioArbiterRuntime.ReadFloat(request, "PreviousHpRatio", 0f) > threshold.Value
                 && AudioArbiterRuntime.ReadFloat(request, "HpRatio", 0f) <= threshold.Value)) return false;
        return true;
    }

    private static bool MatchesAny(IEnumerable<string>? expected, string actual)
    {
        var values = (expected ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return values.Length == 0 || values.Any(value => MatchesId(value, actual));
    }

    private static bool MatchesId(string expected, string actual)
    {
        var left = (expected ?? "").Trim().TrimStart('*');
        var right = (actual ?? "").Trim();
        return left.Length > 0 && (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
                                  || right.EndsWith("_" + left, StringComparison.OrdinalIgnoreCase)
                                  || right.EndsWith("_*" + left, StringComparison.OrdinalIgnoreCase));
    }

    private static int RemoveStaleBattleBgmProviders(ModConfig config, ISet<string> desired)
    {
        var stale = RegisteredBattleBgmSignatures.Keys
            .Where(providerId => !desired.Contains(providerId))
            .ToList();
        foreach (var providerId in stale)
        {
            BattleBgmArbiterRuntime.UnregisterProvider(config, AuraToolsIds.ModId, providerId);
            RegisteredBattleBgmSignatures.Remove(providerId);
        }

        return stale.Count;
    }

    private static int RemoveStaleCardUseProviders(ModConfig config, ISet<string> desired)
    {
        var stale = RegisteredCardUseSignatures.Keys
            .Where(providerId => !desired.Contains(providerId))
            .ToList();
        foreach (var providerId in stale)
        {
            AudioArbiterRuntime.UnregisterSoundProvider(config, AuraToolsIds.ModId, providerId);
            RegisteredCardUseSignatures.Remove(providerId);
        }

        return stale.Count;
    }

    private static string Signature(params object?[] values)
    {
        return string.Join("|", values.Select(value => value?.ToString() ?? ""));
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

        PathExistsCache[key] = File.Exists(AuraToolsConfiguredResourceResolver.ResolveAudioPath(key));
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

        exists = File.Exists(AuraToolsConfiguredResourceResolver.ResolveAudioPath(key));
        PathExistsCache[key] = exists;
        return exists;
    }

    private static bool IsCommonBattleBgmEnabled(object? context)
    {
        var settings = AuraToolsConfigService.Audio.BattleBgm;
        return settings.Enabled
               && settings.Mode == AudioModes.Common
               && CachedPathExists(settings.Common.RelativePath);
    }

    private static bool IsRoleBattleBgmEnabled(object? context, string roleId)
    {
        var settings = AuraToolsConfigService.Audio.BattleBgm;
        if (!settings.Enabled
            || settings.Mode != AudioModes.Advanced
            || !settings.Roles.TryGetValue(roleId, out var role)
            || role == null
            || !role.Enabled
            || string.IsNullOrWhiteSpace(role.RelativePath)
            || !CachedPathExists(role.RelativePath))
        {
            return false;
        }

        return MatchesCareer(context, roleId, settings.Roles.Keys);
    }

    private static bool IsCommonCardUseEnabled(object? context)
    {
        var settings = AuraToolsConfigService.Audio.CardUse;
        return settings.Enabled
               && settings.Mode == AudioModes.Common
               && IsCardUse(context)
               && CachedPathExists(settings.Common.RelativePath);
    }

    private static bool IsRoleCardUseEnabled(object? context, string roleId)
    {
        var settings = AuraToolsConfigService.Audio.CardUse;
        if (!settings.Enabled
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

        return MatchesCareer(context, roleId, settings.Roles.Keys);
    }

    private static bool IsCardUse(object? context)
    {
        return string.Equals(AudioArbiterRuntime.ReadString(context, "Kind"), SoundEventKinds.CardUse, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCareer(object? context, string roleId, IEnumerable<string> configuredRoleIds)
    {
        var careerId = ReadCareerId(context);
        if (string.IsNullOrWhiteSpace(careerId))
        {
            return false;
        }

        var configured = configuredRoleIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var resolution = AuraSharedContentId.Resolve(
            careerId,
            configured,
            knownPrefixes: new[] { AuraSharedIdentity.OfficialCareerPrefix });
        var resolvedRoleId = resolution.Success ? resolution.ResolvedId : "";
        if (resolvedRoleId.Length == 0)
        {
            var reverseMatches = configured
                .Where(configuredRoleId => AuraSharedContentId.Resolve(
                    configuredRoleId,
                    new[] { careerId },
                    knownPrefixes: new[] { AuraSharedIdentity.OfficialCareerPrefix }).Success)
                .ToList();
            resolvedRoleId = reverseMatches.Count == 1 ? reverseMatches[0] : "";
        }

        return resolvedRoleId.Length > 0
               && string.Equals(
                   AuraSharedIdentity.NormalizeRoleId(resolvedRoleId),
                   AuraSharedIdentity.NormalizeRoleId(roleId),
                   StringComparison.OrdinalIgnoreCase);
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

    private sealed class VoiceProviderRegistration
    {
        public string OwnerModId { get; set; } = "";
        public string ProviderId { get; set; } = "";
        public string Signature { get; set; } = "";
    }
}
