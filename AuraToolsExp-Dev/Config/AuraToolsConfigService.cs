using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using Witch.Mod;

namespace AuraToolsExp.Dll.Config;

public static class AuraToolsConfigService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, long> Revisions = new(StringComparer.OrdinalIgnoreCase);

    public static AuraToolsRootConfig Root { get; private set; } = new();

    public static AuraToolsAudioSettings Audio { get; private set; } = new();

    public static AuraToolsMatchExperienceSettings MatchExperience { get; private set; } = new();

    public static AuraToolsSkillCgSettings SkillCg { get; private set; } = new();

    public static AuraToolsSkinSettings Skin { get; private set; } = new();

    public static AuraToolsLoggingSettings Logging { get; private set; } = new();

    public static string ModDirectory => AuraToolsPaths.PackageDirectory;

    public static string DataRootDirectory => AuraToolsPaths.DataRootDirectory;

    public static string ConfigDirectory => AuraToolsPaths.ConfigDirectory;

    public static string ResourceDirectory => AuraToolsPaths.ResourceDirectory;

    public static string AudioDirectory => AuraToolsPaths.AudioDirectory;

    public static string CgDirectory => AuraToolsPaths.CgDirectory;

    public static string SkinsDirectory => AuraToolsPaths.SkinsDirectory;

    public static string LogsDirectory => AuraToolsPaths.LogsDirectory;

    public static event Action? Changed;

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
        Changed?.Invoke();
    }

    public static void SaveAll()
    {
        lock (Gate)
        {
            SaveAllNoLock();
        }
        Changed?.Invoke();
    }

    public static void SaveAudio()
    {
        SaveModule(Audio, Root.Audio.ConfigFile);
        Changed?.Invoke();
    }

    public static void SaveMatchExperience()
    {
        SaveModule(MatchExperience, Root.MatchExperience.ConfigFile);
        Changed?.Invoke();
    }

    public static void SaveSkillCg()
    {
        SaveModule(SkillCg, Root.SkillCg.ConfigFile);
        Changed?.Invoke();
    }

    public static void SaveSkin()
    {
        SaveModule(Skin, Root.Skin.ConfigFile);
        Changed?.Invoke();
    }

    public static void SaveLogging()
    {
        SaveModule(Logging, Root.Logging.ConfigFile);
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
        Root = LoadOrDefault(AuraToolsIds.RootConfigFileName, new AuraToolsRootConfig());
        Root.Normalize();
        Audio = LoadOrDefault(Root.Audio.ConfigFile, new AuraToolsAudioSettings());
        MatchExperience = LoadOrDefault(Root.MatchExperience.ConfigFile, new AuraToolsMatchExperienceSettings());
        SkillCg = LoadOrDefault(Root.SkillCg.ConfigFile, new AuraToolsSkillCgSettings());
        Skin = LoadOrDefault(Root.Skin.ConfigFile, new AuraToolsSkinSettings());
        Logging = LoadOrDefault(Root.Logging.ConfigFile, new AuraToolsLoggingSettings());

        Audio.Normalize();
        MatchExperience.Normalize();
        SkillCg.Normalize();
        ImportRegisteredSkillCgDefaultsNoLock();
        SkillCg.Normalize();
        Skin.Normalize();
        Logging.Normalize();
    }

    public static bool ImportRegisteredSkillCgDefaults()
    {
        bool changed;
        lock (Gate)
        {
            changed = ImportRegisteredSkillCgDefaultsNoLock();
            if (changed)
            {
                SkillCg.Normalize();
                SaveModule(SkillCg, Root.SkillCg.ConfigFile);
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }

        return changed;
    }

    private static void SaveAllNoLock()
    {
        SaveModule(Root, AuraToolsIds.RootConfigFileName);
        SaveModule(Audio, Root.Audio.ConfigFile);
        SaveModule(MatchExperience, Root.MatchExperience.ConfigFile);
        SaveModule(SkillCg, Root.SkillCg.ConfigFile);
        SaveModule(Skin, Root.Skin.ConfigFile);
        SaveModule(Logging, Root.Logging.ConfigFile);
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
            var expectedRevision = Revisions.TryGetValue(safeName, out var revision) ? revision : 0;
            var result = AuraSharedConfigStore.WriteOwner(
                AuraToolsIds.ModId,
                AuraToolsPaths.ConfigSystem,
                safeName,
                value,
                expectedRevision,
                schemaVersion: 1);
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
