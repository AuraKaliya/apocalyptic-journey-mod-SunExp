using System;
using System.Collections.Generic;
using System.IO;
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
        Skin.Normalize();
        Logging.Normalize();
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
