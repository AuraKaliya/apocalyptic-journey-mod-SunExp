using System;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using Witch.Mod;

namespace AuraToolsExp.Dll.Config;

public static class AuraToolsConfigService
{
    private static readonly object Gate = new();
    private static JsonSerializerSettings SerializerSettings { get; } = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    public static AuraToolsRootConfig Root { get; private set; } = new();

    public static AuraToolsAudioSettings Audio { get; private set; } = new();

    public static AuraToolsMatchExperienceSettings MatchExperience { get; private set; } = new();

    public static AuraToolsSkillCgSettings SkillCg { get; private set; } = new();

    public static AuraToolsLoggingSettings Logging { get; private set; } = new();

    public static string ModDirectory => AuraToolsPaths.PackageDirectory;

    public static string DataRootDirectory => AuraToolsPaths.DataRootDirectory;

    public static string ConfigDirectory => AuraToolsPaths.ConfigDirectory;

    public static string ResourceDirectory => AuraToolsPaths.ResourceDirectory;

    public static string LogsDirectory => AuraToolsPaths.LogsDirectory;

    public static event Action? Changed;

    public static void Initialize(ModConfig config)
    {
        lock (Gate)
        {
            AuraToolsPaths.Initialize(config);
            MigrateLegacyFilesNoLock();
            ReloadNoLock();
            MigrateLoadedResourcePathsNoLock();
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
        Root = LoadOrDefault(AuraToolsIds.RootConfigFileName, new AuraToolsRootConfig());
        Root.Normalize();
        Audio = LoadOrDefault(Root.Audio.ConfigFile, new AuraToolsAudioSettings());
        MatchExperience = LoadOrDefault(Root.MatchExperience.ConfigFile, new AuraToolsMatchExperienceSettings());
        SkillCg = LoadOrDefault(Root.SkillCg.ConfigFile, new AuraToolsSkillCgSettings());
        Logging = LoadOrDefault(Root.Logging.ConfigFile, new AuraToolsLoggingSettings());

        Audio.Normalize();
        MatchExperience.Normalize();
        SkillCg.Normalize();
        Logging.Normalize();
    }

    private static void MigrateLegacyFilesNoLock()
    {
        CopyDirectoryNoOverwrite(AuraToolsPaths.LegacyConfigDirectory, ConfigDirectory, "*.json");
        CopyDirectoryNoOverwrite(AuraToolsPaths.LegacyResourceDirectory, ResourceDirectory, "*");
    }

    private static void MigrateLoadedResourcePathsNoLock()
    {
        Audio.BattleBgm.Common.RelativePath = MigrateConfiguredResourcePath(Audio.BattleBgm.Common.RelativePath);
        Audio.CardUse.Common.RelativePath = MigrateConfiguredResourcePath(Audio.CardUse.Common.RelativePath);

        foreach (var role in Audio.BattleBgm.Roles.Values.Concat(Audio.CardUse.Roles.Values))
        {
            if (role != null)
            {
                role.RelativePath = MigrateConfiguredResourcePath(role.RelativePath);
            }
        }

        foreach (var role in SkillCg.Roles.Values)
        {
            if (role?.Rules == null)
            {
                continue;
            }

            foreach (var rule in role.Rules)
            {
                if (rule != null)
                {
                    rule.Image = MigrateConfiguredResourcePath(rule.Image);
                }
            }
        }
    }

    private static string MigrateConfiguredResourcePath(string path)
    {
        var candidate = NormalizePathInput(path);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "";
        }

        if (StartsWithSegment(candidate, AuraToolsIds.ResourceDirectoryName))
        {
            return candidate;
        }

        var systemPath = candidate.Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(systemPath) && AuraToolsPaths.IsLegacyResourcePath(candidate))
        {
            var source = Path.Combine(ModDirectory, systemPath);
            var targetRelative = AuraToolsPaths.ConvertLegacyResourceRelativePath(candidate);
            return CopyResourceNoOverwrite(source, targetRelative);
        }

        string sourcePath;
        try
        {
            if (Path.IsPathRooted(systemPath))
            {
                sourcePath = Path.GetFullPath(systemPath);
            }
            else
            {
                var dataPath = Path.GetFullPath(Path.Combine(DataRootDirectory, systemPath));
                if (File.Exists(dataPath))
                {
                    return AuraToolsPaths.ToDataRelativePath(dataPath);
                }

                sourcePath = Path.GetFullPath(Path.Combine(ModDirectory, systemPath));
            }
        }
        catch
        {
            return candidate;
        }

        if (!File.Exists(sourcePath))
        {
            return candidate;
        }

        if (AuraToolsPaths.IsInsideDataRoot(sourcePath))
        {
            return AuraToolsPaths.ToDataRelativePath(sourcePath);
        }

        if (AuraToolsPaths.IsInsidePackageDirectory(sourcePath)
            && AuraToolsPaths.IsInsideDirectory(sourcePath, AuraToolsPaths.LegacyResourceDirectory))
        {
            var rest = MakeRelative(AuraToolsPaths.LegacyResourceDirectory, sourcePath);
            return CopyResourceNoOverwrite(sourcePath, AuraToolsIds.ResourceDirectoryName + "/" + rest.Replace('\\', '/'));
        }

        return CopyResourceNoOverwrite(
            sourcePath,
            AuraToolsIds.ResourceDirectoryName + "/Imported/" + Path.GetFileName(sourcePath));
    }

    private static void SaveAllNoLock()
    {
        SaveModule(Root, AuraToolsIds.RootConfigFileName);
        SaveModule(Audio, Root.Audio.ConfigFile);
        SaveModule(MatchExperience, Root.MatchExperience.ConfigFile);
        SaveModule(SkillCg, Root.SkillCg.ConfigFile);
        SaveModule(Logging, Root.Logging.ConfigFile);
    }

    private static T LoadOrDefault<T>(string fileName, T fallback)
    {
        try
        {
            var path = Path.Combine(ConfigDirectory, SafeConfigFileName(fileName));
            if (!File.Exists(path))
            {
                return fallback;
            }

            var text = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(text) ?? fallback;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Failed to load config " + fileName + ": " + ex.Message);
            return fallback;
        }
    }

    private static void SaveModule<T>(T value, string fileName)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var path = Path.Combine(ConfigDirectory, SafeConfigFileName(fileName));
                var tempPath = path + ".tmp";
                var text = JsonConvert.SerializeObject(value, SerializerSettings);
                File.WriteAllText(tempPath, text);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                AuraToolsLog.Error("Failed to save config " + fileName, ex);
            }
        }
    }

    private static void CopyDirectoryNoOverwrite(string sourceDirectory, string targetDirectory, string searchPattern)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory)
                || string.IsNullOrWhiteSpace(targetDirectory)
                || !Directory.Exists(sourceDirectory))
            {
                return;
            }

            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, searchPattern, SearchOption.AllDirectories))
            {
                var relative = MakeRelative(sourceDirectory, sourcePath);
                var targetPath = Path.Combine(targetDirectory, relative);
                if (File.Exists(targetPath))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? targetDirectory);
                File.Copy(sourcePath, targetPath, false);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Legacy data migration failed: " + ex.Message);
        }
    }

    private static string CopyResourceNoOverwrite(string sourcePath, string targetRelativePath)
    {
        var normalizedTarget = NormalizePathInput(targetRelativePath);
        var targetPath = Path.Combine(DataRootDirectory, normalizedTarget.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (File.Exists(sourcePath) && !AuraToolsPaths.IsSamePath(sourcePath, targetPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ResourceDirectory);
                if (!File.Exists(targetPath))
                {
                    File.Copy(sourcePath, targetPath, false);
                }
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Resource migration failed: " + sourcePath + " -> " + targetPath + ": " + ex.Message);
        }

        return AuraToolsPaths.ToDataRelativePath(targetPath);
    }

    private static string MakeRelative(string root, string path)
    {
        var rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
        var pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string value)
    {
        return value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || value.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? value
            : value + Path.DirectorySeparatorChar;
    }

    private static bool StartsWithSegment(string value, string segment)
    {
        return value.Equals(segment, StringComparison.OrdinalIgnoreCase)
               || value.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith(segment + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathInput(string value)
    {
        return (value ?? "").Trim().Trim('"').Replace('\\', '/');
    }

    private static string SafeConfigFileName(string fileName)
    {
        var candidate = NormalizePathInput(fileName);
        var safe = Path.GetFileName(candidate);
        return string.IsNullOrWhiteSpace(safe) ? AuraToolsIds.RootConfigFileName : safe;
    }
}
