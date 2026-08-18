using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using Newtonsoft.Json;
using Witch.Mod;

namespace AuraToolsExp.Dll.Config;

public static class AuraToolsConfigService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, long> Revisions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReadOnlyConfigFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly AuraToolModuleConfigStore ModuleStore = new();

    public static AuraToolsRootConfig Root { get; private set; } = new();

    public static AuraToolsAudioSettings Audio { get; private set; } = new();

    public static AuraToolsMatchExperienceSettings MatchExperience { get; private set; } = new();

    public static AuraToolsPixelEmojiSettings PixelEmoji { get; private set; } = new();

    public static AuraToolsSkillCgSettings SkillCg { get; private set; } = new();

    public static AuraToolsSkinSettings Skin { get; private set; } = new();

    public static AuraToolsLoggingSettings Logging { get; private set; } = new();

    public static string ModDirectory => AuraToolsPaths.PackageDirectory;

    public static string DataRootDirectory => AuraToolsPaths.DataRootDirectory;

    public static string ConfigDirectory => AuraToolsPaths.ConfigDirectory;

    public static string ModuleConfigDirectory =>
        AuraSharedPaths.OwnerSystemConfigDirectory(
            AuraToolsIds.ModId,
            AuraToolModuleConfigStore.ConfigSystem);

    public static string ResourceDirectory => AuraToolsPaths.ResourceDirectory;

    public static string AudioDirectory => AuraToolsPaths.AudioDirectory;

    public static string CgDirectory => AuraToolsPaths.CgDirectory;

    public static string SkinDirectory => AuraToolsPaths.SkinDirectory;

    public static string LogsDirectory => AuraToolsPaths.LogsDirectory;

    public static event Action? Changed;

    public static event Action? AudioChanged;

    public static event Action? MatchExperienceChanged;

    public static event Action? LoggingChanged;

    public static bool IsModuleConfigReadOnly(string moduleId)
    {
        lock (Gate)
        {
            return ModuleStore.IsReadOnly(moduleId);
        }
    }

    public static IDisposable SubscribeModule(string moduleId, Action changed)
    {
        return AuraToolConfigChangeBus.Subscribe(
            moduleId,
            _ => changed());
    }

    public static void Initialize(ModConfig config)
    {
        lock (Gate)
        {
            AuraToolsPaths.Initialize(config);
            ReloadNoLock();
            SaveAllNoLock();
            AuraToolsLog.Info("[Config] package=" + ModDirectory + ", data=" + DataRootDirectory);
        }
    }

    public static void Reload()
    {
        lock (Gate)
        {
            ReloadNoLock();
        }
        NotifyAllModules();
    }

    public static void SaveAll()
    {
        lock (Gate)
        {
            SaveAllNoLock();
        }
        NotifyAllModules();
    }

    public static void SaveAudio()
    {
        SaveBattleBgm();
        SaveCardUseAudio();
        AudioChanged?.Invoke();
    }

    public static void SaveMatchExperience()
    {
        SaveStarterDeck();
        SaveCardRefresh();
        SaveFeast();
        SaveSafeBox();
        SaveModSync();
        SaveDamageStatistics();
        SaveBattleReplay();
        SaveAutoBattle();
        MatchExperienceChanged?.Invoke();
    }

    public static void SavePixelEmoji()
    {
        PixelEmoji.Normalize();
        SaveModuleSetting(
            AuraToolModuleIds.PixelEmoji,
            PixelEmoji,
            () => SaveModule(PixelEmoji, Root.PixelEmoji.ConfigFile));
    }

    public static void SaveSkillCg()
    {
        SkillCg.Normalize();
        SaveModuleSetting(
            AuraToolModuleIds.SkillCg,
            SkillCg,
            () => SaveModule(SkillCg, Root.SkillCg.ConfigFile));
    }

    public static void SaveCardUseCg()
    {
        SkillCg.CardUseCg.Normalize();
        SaveModuleSetting(
            AuraToolModuleIds.CardUseCg,
            SkillCg.CardUseCg,
            () => SaveModule(SkillCg, Root.SkillCg.ConfigFile));
    }

    public static void SaveSkin()
    {
        Skin.Normalize();
        SaveModuleSetting(
            AuraToolModuleIds.Skin,
            Skin,
            () => SaveModule(Skin, Root.Skin.ConfigFile));
    }

    public static void SaveLogging()
    {
        Logging.Normalize();
        SaveModuleSetting(
            AuraToolModuleIds.FileLogging,
            Logging,
            () => SaveModule(Logging, Root.Logging.ConfigFile));
        LoggingChanged?.Invoke();
    }

    public static void SaveBattleBgm()
    {
        Audio.Normalize();
        SaveModuleSetting(
            AuraToolModuleIds.BattleBgm,
            Audio.BattleBgm,
            () => SaveModule(Audio, Root.Audio.ConfigFile));
    }

    public static void SaveCardUseAudio()
    {
        Audio.Normalize();
        SaveModuleSetting(
            AuraToolModuleIds.CardUseAudio,
            Audio.CardUse,
            () => SaveModule(Audio, Root.Audio.ConfigFile));
    }

    public static void SaveAudioFeature(bool battleBgm)
    {
        if (battleBgm)
        {
            SaveBattleBgm();
        }
        else
        {
            SaveCardUseAudio();
        }
    }

    public static void SaveStarterDeck()
    {
        _ = TrySaveStarterDeck();
    }

    public static bool TrySaveStarterDeck()
    {
        MatchExperience.StarterDeck.Normalize();
        long revision;
        lock (Gate)
        {
            if (!SaveModuleSettingNoNotify(
                    AuraToolModuleIds.StarterDeck,
                    MatchExperience.StarterDeck,
                    out revision))
            {
                return false;
            }

            SaveModule(MatchExperience, Root.MatchExperience.ConfigFile);
        }

        AuraToolConfigChangeBus.Publish(AuraToolModuleIds.StarterDeck, revision);
        return true;
    }

    public static void SaveCardRefresh()
    {
        SaveMatchExperienceModule(
            AuraToolModuleIds.CardRefresh,
            MatchExperience.CardRefresh);
    }

    public static void SaveFeast()
    {
        MatchExperience.Feast.Normalize();
        SaveMatchExperienceModule(
            AuraToolModuleIds.Feast,
            MatchExperience.Feast);
    }

    public static void SaveSafeBox()
    {
        SaveMatchExperienceModule(
            AuraToolModuleIds.SafeBox,
            MatchExperience.SafeBox);
    }

    public static void SaveModSync()
    {
        SaveMatchExperienceModule(
            AuraToolModuleIds.ModSync,
            MatchExperience.ModSync);
    }

    public static void SaveDamageStatistics()
    {
        MatchExperience.DamageMeter.Normalize();
        MatchExperience.MatchRecords.Enabled =
            MatchExperience.DamageMeter.Enabled
            || MatchExperience.MatchRecords.Replay.Enabled;
        SaveMatchExperienceModule(
            AuraToolModuleIds.DamageStatistics,
            MatchExperience.DamageMeter);
    }

    public static void SaveBattleReplay()
    {
        MatchExperience.MatchRecords.Replay.Normalize();
        MatchExperience.MatchRecords.Enabled =
            MatchExperience.DamageMeter.Enabled
            || MatchExperience.MatchRecords.Replay.Enabled;
        SaveMatchExperienceModule(
            AuraToolModuleIds.BattleReplay,
            MatchExperience.MatchRecords.Replay);
    }

    public static void SaveAutoBattle()
    {
        MatchExperience.AutoBattle.Normalize();
        SaveMatchExperienceModule(
            AuraToolModuleIds.AutoBattle,
            MatchExperience.AutoBattle);
    }

    private static void NotifyAllModules()
    {
        foreach (var moduleId in AuraToolModuleIds.Persisted)
        {
            AuraToolConfigChangeBus.Publish(moduleId, 0);
        }
        AudioChanged?.Invoke();
        MatchExperienceChanged?.Invoke();
        LoggingChanged?.Invoke();
        Changed?.Invoke();
    }

    public static string ResolveConfiguredPath(string relativeOrAbsolute)
    {
        return AuraToolsPaths.ResolveConfiguredPath(relativeOrAbsolute);
    }

    public static string ResolveModPath(string relativeOrAbsolute)
    {
        return ResolveConfiguredPath(relativeOrAbsolute);
    }

    public static string ToDataRelativePath(string absoluteOrRelative)
    {
        return AuraToolsPaths.ToDataRelativePath(absoluteOrRelative);
    }

    public static string ToModRelativePath(string absoluteOrRelative)
    {
        return ToDataRelativePath(absoluteOrRelative);
    }

    private static void ReloadNoLock()
    {
        Revisions.Clear();
        ReadOnlyConfigFiles.Clear();
        ModuleStore.Reset();
        Root = LoadOrDefault(AuraToolsIds.RootConfigFileName, new AuraToolsRootConfig());
        Root.Normalize();
        Root.Audio.Enabled = true;
        Root.MatchExperience.Enabled = true;
        Root.PixelEmoji.Enabled = true;
        Root.SkillCg.Enabled = true;
        Root.Skin.Enabled = true;
        Root.Logging.Enabled = true;
        Audio = LoadOrDefault(Root.Audio.ConfigFile, new AuraToolsAudioSettings());
        MatchExperience = LoadOrDefault(Root.MatchExperience.ConfigFile, new AuraToolsMatchExperienceSettings());
        PixelEmoji = LoadOrDefault(Root.PixelEmoji.ConfigFile, new AuraToolsPixelEmojiSettings());
        SkillCg = LoadOrDefault(Root.SkillCg.ConfigFile, new AuraToolsSkillCgSettings());
        Skin = LoadOrDefault(Root.Skin.ConfigFile, new AuraToolsSkinSettings());
        Logging = LoadOrDefault(Root.Logging.ConfigFile, new AuraToolsLoggingSettings());

        Audio.Normalize();
        MatchExperience.Normalize();
        PixelEmoji.Normalize();
        SkillCg.Normalize();
        Skin.Normalize();
        Logging.Normalize();

        var migrated = 0;
        Audio.BattleBgm = LoadModuleSetting(
            AuraToolModuleIds.BattleBgm,
            Audio.BattleBgm,
            ref migrated);
        Audio.CardUse = LoadModuleSetting(
            AuraToolModuleIds.CardUseAudio,
            Audio.CardUse,
            ref migrated);
        MatchExperience.StarterDeck = LoadModuleSetting(
            AuraToolModuleIds.StarterDeck,
            MatchExperience.StarterDeck,
            ref migrated);
        MatchExperience.CardRefresh = LoadModuleSetting(
            AuraToolModuleIds.CardRefresh,
            MatchExperience.CardRefresh,
            ref migrated);
        MatchExperience.Feast = LoadModuleSetting(
            AuraToolModuleIds.Feast,
            MatchExperience.Feast,
            ref migrated);
        MatchExperience.SafeBox = LoadModuleSetting(
            AuraToolModuleIds.SafeBox,
            MatchExperience.SafeBox,
            ref migrated);
        MatchExperience.ModSync = LoadModuleSetting(
            AuraToolModuleIds.ModSync,
            MatchExperience.ModSync,
            ref migrated);

        var legacyStatistics = MatchExperience.MatchRecords.Statistics;
        legacyStatistics.Enabled = MatchExperience.MatchRecords.Enabled
                                   && legacyStatistics.Enabled;
        var legacyReplay = MatchExperience.MatchRecords.Replay;
        legacyReplay.Enabled = MatchExperience.MatchRecords.Enabled
                               && legacyReplay.Enabled;
        MatchExperience.MatchRecords.Statistics = LoadModuleSetting(
            AuraToolModuleIds.DamageStatistics,
            legacyStatistics,
            ref migrated);
        MatchExperience.MatchRecords.Replay = LoadModuleSetting(
            AuraToolModuleIds.BattleReplay,
            legacyReplay,
            ref migrated);
        MatchExperience.MatchRecords.Enabled =
            MatchExperience.MatchRecords.Statistics.Enabled
            || MatchExperience.MatchRecords.Replay.Enabled;
        MatchExperience.AutoBattle = LoadModuleSetting(
            AuraToolModuleIds.AutoBattle,
            MatchExperience.AutoBattle,
            ref migrated);
        PixelEmoji = LoadModuleSetting(
            AuraToolModuleIds.PixelEmoji,
            PixelEmoji,
            ref migrated);
        SkillCg = LoadModuleSetting(
            AuraToolModuleIds.SkillCg,
            SkillCg,
            ref migrated);
        SkillCg.CardUseCg = LoadModuleSetting(
            AuraToolModuleIds.CardUseCg,
            SkillCg.CardUseCg,
            ref migrated);
        Skin = LoadModuleSetting(
            AuraToolModuleIds.Skin,
            Skin,
            ref migrated);
        Logging = LoadModuleSetting(
            AuraToolModuleIds.FileLogging,
            Logging,
            ref migrated);

        Audio.Normalize();
        MatchExperience.Normalize();
        PixelEmoji.Normalize();
        SkillCg.Normalize();
        ImportRegisteredSkillCgDefaultsNoLock();
        SkillCg.Normalize();
        Skin.Normalize();
        Logging.Normalize();
        if (migrated > 0)
        {
            AuraToolsLog.Info(
                "[Config] migrated legacy aggregate settings into module files: "
                + migrated);
        }
    }

    public static bool ImportRegisteredSkillCgDefaults()
    {
        bool changed;
        var revision = 0L;
        lock (Gate)
        {
            changed = ImportRegisteredSkillCgDefaultsNoLock();
            if (changed)
            {
                SkillCg.Normalize();
                SaveModuleSettingNoNotify(
                    AuraToolModuleIds.SkillCg,
                    SkillCg,
                    out revision);
                SaveModule(SkillCg, Root.SkillCg.ConfigFile);
            }
        }

        if (changed)
        {
            AuraToolConfigChangeBus.Publish(
                AuraToolModuleIds.SkillCg,
                revision);
        }

        return changed;
    }

    private static void SaveAllNoLock()
    {
        SaveModule(Root, AuraToolsIds.RootConfigFileName);
        SaveModule(Audio, Root.Audio.ConfigFile);
        SaveModule(MatchExperience, Root.MatchExperience.ConfigFile);
        SaveModule(PixelEmoji, Root.PixelEmoji.ConfigFile);
        SaveModule(SkillCg, Root.SkillCg.ConfigFile);
        SaveModule(Skin, Root.Skin.ConfigFile);
        SaveModule(Logging, Root.Logging.ConfigFile);
        SaveAllModuleSettingsNoLock();
    }

    private static T LoadModuleSetting<T>(
        string moduleId,
        T fallback,
        ref int migratedCount)
    {
        var value = ModuleStore.Load(moduleId, fallback, out var migrated);
        if (migrated)
        {
            migratedCount++;
        }
        return value;
    }

    private static void SaveAllModuleSettingsNoLock()
    {
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.BattleBgm,
            Audio.BattleBgm,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.CardUseAudio,
            Audio.CardUse,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.StarterDeck,
            MatchExperience.StarterDeck,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.CardRefresh,
            MatchExperience.CardRefresh,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.Feast,
            MatchExperience.Feast,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.SafeBox,
            MatchExperience.SafeBox,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.ModSync,
            MatchExperience.ModSync,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.DamageStatistics,
            MatchExperience.DamageMeter,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.BattleReplay,
            MatchExperience.MatchRecords.Replay,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.AutoBattle,
            MatchExperience.AutoBattle,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.PixelEmoji,
            PixelEmoji,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.SkillCg,
            SkillCg,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.CardUseCg,
            SkillCg.CardUseCg,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.Skin,
            Skin,
            out _);
        SaveModuleSettingNoNotify(
            AuraToolModuleIds.FileLogging,
            Logging,
            out _);
    }

    private static void SaveMatchExperienceModule<T>(string moduleId, T settings)
    {
        SaveModuleSetting(
            moduleId,
            settings,
            () => SaveModule(MatchExperience, Root.MatchExperience.ConfigFile));
    }

    private static void SaveModuleSetting<T>(
        string moduleId,
        T settings,
        Action saveLegacy)
    {
        long revision;
        lock (Gate)
        {
            if (!SaveModuleSettingNoNotify(moduleId, settings, out revision))
            {
                return;
            }
            saveLegacy();
        }
        AuraToolConfigChangeBus.Publish(moduleId, revision);
    }

    private static bool SaveModuleSettingNoNotify<T>(
        string moduleId,
        T settings,
        out long revision)
    {
        return ModuleStore.Save(moduleId, settings, out revision);
    }

    private static T LoadOrDefault<T>(string fileName, T fallback)
    {
        var safeName = SafeConfigFileName(fileName);
        var bundled = LoadBundledOrDefault(safeName, fallback);
        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            AuraToolsPaths.ConfigSystem,
            safeName,
            bundled);
        Revisions[safeName] = snapshot.Revision;
        if (snapshot.Found
            && AuraToolsConfigSchemaPolicy.IsNewer(
                snapshot.SchemaVersion,
                snapshot.Value,
                fallback))
        {
            ReadOnlyConfigFiles.Add(safeName);
            AuraToolsLog.Warn(
                "Config uses a newer schema and was opened read-only: "
                + safeName
                + "; envelope=" + snapshot.SchemaVersion
                + "; value="
                + AuraToolsConfigSchemaPolicy.ReadValueVersion(snapshot.Value)
                + "; supported="
                + AuraToolsConfigSchemaPolicy.ReadValueVersion(fallback));
            return bundled;
        }
        return snapshot.Value;
    }

    private static T LoadBundledOrDefault<T>(string fileName, T fallback)
    {
        try
        {
            var path = Path.Combine(AuraToolsPaths.BundledConfigDirectory, fileName);
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? fallback
                : fallback;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Failed to load bundled config " + fileName + ": " + ex.Message);
            return fallback;
        }
    }

    private static bool ImportRegisteredSkillCgDefaultsNoLock()
    {
        var changed = 0;
        foreach (var entry in AuraCgRegistryRuntime.GetRegisteredEntries())
        {
            if (!string.Equals(entry.Kind, SkillCgArbiterRuntime.SkillCgKind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            changed += ImportRegisteredSkillCgEntryNoLock(entry);
        }

        return changed > 0;
    }

    private static int ImportRegisteredSkillCgEntryNoLock(AuraCgRegistryEntry entry)
    {
        var roleId = ResolveRegisteredRoleId(entry);
        var image = ResolveRegisteredImage(entry);
        if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(image))
        {
            return 0;
        }

        if (!SkillCg.Roles.TryGetValue(roleId, out var role) || role == null)
        {
            role = new SkillCgRoleSettings
            {
                Enabled = true,
                RoleId = roleId,
                DisplayName = ResolveRegisteredRoleDisplayName(roleId)
            };
            SkillCg.Roles[roleId] = role;
        }
        else if (string.IsNullOrWhiteSpace(role.DisplayName)
                 || string.Equals(role.DisplayName, entry.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            role.DisplayName = ResolveRegisteredRoleDisplayName(roleId);
        }

        role.Rules ??= new List<SkillCgRuleSettings>();
        var cardIds = ResolveRegisteredCardIds(entry).ToList();
        var added = 0;
        for (var i = 0; i < cardIds.Count; i++)
        {
            var cardId = cardIds[i];
            var providerId = RegisteredProviderId(entry, i);
            var existing = role.Rules.FirstOrDefault(rule => IsSameRegisteredRule(rule, providerId, cardId, image));
            if (existing != null)
            {
                if (ApplyRegisteredRuleDefaults(existing, entry, providerId, cardId, image))
                {
                    added++;
                }

                continue;
            }

            role.Rules.Add(CreateRegisteredRule(entry, providerId, cardId, image));
            added++;
        }

        return added;
    }

    private static SkillCgRuleSettings CreateRegisteredRule(AuraCgRegistryEntry entry, string providerId, string cardId, string image)
    {
        return new SkillCgRuleSettings
        {
            Enabled = true,
            ProviderId = providerId,
            DisplayName = entry.DisplayName,
            SourceOwnerModId = entry.OwnerModId,
            SourceCgId = entry.CgId,
            CardId = cardId,
            Action = "*",
            Image = image,
            Priority = entry.Priority,
            Presentation = CreatePresentationSettings(entry)
        };
    }

    private static bool ApplyRegisteredRuleDefaults(SkillCgRuleSettings rule, AuraCgRegistryEntry entry, string providerId, string cardId, string image)
    {
        var changed = false;
        if (!string.Equals(rule.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            rule.ProviderId = providerId;
            changed = true;
        }

        if (!string.Equals(rule.DisplayName, entry.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            rule.DisplayName = entry.DisplayName;
            changed = true;
        }

        if (!string.Equals(rule.SourceOwnerModId, entry.OwnerModId, StringComparison.OrdinalIgnoreCase))
        {
            rule.SourceOwnerModId = entry.OwnerModId;
            changed = true;
        }

        if (!string.Equals(rule.SourceCgId, entry.CgId, StringComparison.OrdinalIgnoreCase))
        {
            rule.SourceCgId = entry.CgId;
            changed = true;
        }

        if (!string.Equals(rule.CardId, cardId, StringComparison.OrdinalIgnoreCase))
        {
            rule.CardId = cardId;
            changed = true;
        }

        if (!string.Equals(AuraSharedPaths.NormalizeRelativePath(rule.Image), image, StringComparison.OrdinalIgnoreCase))
        {
            rule.Image = image;
            changed = true;
        }

        if (rule.Priority != entry.Priority)
        {
            rule.Priority = entry.Priority;
            changed = true;
        }

        var presentation = CreatePresentationSettings(entry);
        if (!SamePresentation(rule.Presentation, presentation))
        {
            rule.Presentation = presentation;
            changed = true;
        }

        return changed;
    }

    private static SkillCgPresentationSettings CreatePresentationSettings(AuraCgRegistryEntry entry)
    {
        return new SkillCgPresentationSettings
        {
            Mode = entry.DefaultPresentation.Mode,
            Fit = entry.DefaultPresentation.Fit,
            FadeIn = entry.DefaultPresentation.FadeIn,
            Hold = entry.DefaultPresentation.Hold,
            FadeOut = entry.DefaultPresentation.FadeOut,
            FocusX = entry.DefaultPresentation.FocusX,
            FocusY = entry.DefaultPresentation.FocusY,
            SafeScale = entry.DefaultPresentation.SafeScale
        };
    }

    private static bool SamePresentation(SkillCgPresentationSettings? left, SkillCgPresentationSettings right)
    {
        left ??= SkillCgPresentationSettings.CreateInherited();
        return string.Equals(left.Mode, right.Mode, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Fit, right.Fit, StringComparison.OrdinalIgnoreCase)
               && Math.Abs(left.FadeIn - right.FadeIn) < 0.001f
               && Math.Abs(left.Hold - right.Hold) < 0.001f
               && Math.Abs(left.FadeOut - right.FadeOut) < 0.001f
               && Math.Abs(left.FocusX - right.FocusX) < 0.001f
               && Math.Abs(left.FocusY - right.FocusY) < 0.001f
               && Math.Abs(left.SafeScale - right.SafeScale) < 0.001f;
    }

    private static string ResolveRegisteredRoleId(AuraCgRegistryEntry entry)
    {
        foreach (var value in entry.TargetRoleIds ?? new List<string>())
        {
            var roleId = RoleCatalog.NormalizeRoleId(value);
            if (!string.IsNullOrWhiteSpace(roleId))
            {
                return roleId;
            }
        }

        return "";
    }

    private static string ResolveRegisteredRoleDisplayName(string roleId)
    {
        var displayName = RoleCatalog.GetDisplayName(roleId);
        return string.IsNullOrWhiteSpace(displayName)
               || string.Equals(displayName, roleId, StringComparison.OrdinalIgnoreCase)
            ? ""
            : displayName;
    }

    private static string ResolveRegisteredImage(AuraCgRegistryEntry entry)
    {
        var image = entry.Media.Resource;
        if (string.IsNullOrWhiteSpace(image))
        {
            image = entry.Media.FallbackImage;
        }

        return AuraSharedPaths.NormalizeRelativePath(image);
    }

    private static IEnumerable<string> ResolveRegisteredCardIds(AuraCgRegistryEntry entry)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in entry.CardIds ?? new List<string>())
        {
            var cardId = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(cardId))
            {
                continue;
            }

            if (cardId.Contains("*") && !string.Equals(cardId, "*", StringComparison.Ordinal))
            {
                continue;
            }

            if (seen.Add(cardId))
            {
                yield return cardId;
            }
        }

        if (seen.Count == 0)
        {
            yield return "*";
        }
    }

    private static bool IsSameRegisteredRule(SkillCgRuleSettings rule, string providerId, string cardId, string image)
    {
        return string.Equals(rule.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
               || (string.Equals(rule.CardId, cardId, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(AuraSharedPaths.NormalizeRelativePath(rule.Image), image, StringComparison.OrdinalIgnoreCase));
    }

    private static string RegisteredProviderId(AuraCgRegistryEntry entry, int index)
    {
        return entry.OwnerModId + ".SkillCG." + entry.CgId;
    }

    private static string SafeProviderSegment(string value)
    {
        var text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "unknown";
        }

        var chars = text
            .Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '.')
            .ToArray();
        var result = new string(chars).Trim('.');
        while (result.Contains(".."))
        {
            result = result.Replace("..", ".");
        }

        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private static void SaveModule<T>(T value, string fileName)
    {
        lock (Gate)
        {
            var safeName = SafeConfigFileName(fileName);
            if (ReadOnlyConfigFiles.Contains(safeName))
            {
                AuraToolsLog.Warn(
                    "Refusing to overwrite newer read-only config: "
                    + safeName);
                return;
            }
            var expectedRevision = Revisions.TryGetValue(safeName, out var revision) ? revision : 0;
            var result = AuraSharedConfigStore.WriteOwner(
                AuraToolsIds.ModId,
                AuraToolsPaths.ConfigSystem,
                safeName,
                value,
                expectedRevision,
                schemaVersion:
                    AuraToolsConfigSchemaPolicy.CurrentEnvelopeVersion);
            if (!result.Success)
            {
                AuraToolsLog.Warn("Failed to save config " + safeName + ": " + result.Message);
                return;
            }

            Revisions[safeName] = result.Revision;
        }
    }

    private static string SafeConfigFileName(string fileName)
    {
        var safe = Path.GetFileName((fileName ?? "").Trim());
        return string.IsNullOrWhiteSpace(safe) ? AuraToolsIds.RootConfigFileName : safe;
    }

}
