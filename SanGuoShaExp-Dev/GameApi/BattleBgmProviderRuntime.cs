using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using BattleBgmArbiter.Shared;
using SanGuoShaExp.Dll.Infrastructure;
using Witch.Mod;

namespace SanGuoShaExp.Dll.GameApi;

public static class BattleBgmProviderRuntime
{
    private const string ModId = "SanGuoShaExp";
    private const string ManifestPath = "audio.registry.json";
    private static ModConfig? currentModConfig;
    private static string primaryProviderId = "";

    public static void Initialize(ModConfig modConfig)
    {
        if (modConfig == null)
        {
            SanGuoShaExpLog.Warn("Battle BGM provider initialization skipped: mod config is null");
            return;
        }

        currentModConfig = modConfig;
        BattleBgmArbiterRuntime.Initialize(modConfig, ModId);
        if (!RegisterManifest(modConfig, ModId, ManifestPath))
        {
            SanGuoShaExpLog.Warn("Battle BGM manifest registration failed: " + ManifestPath);
        }
    }

    public static void RequestBattleSwitch(string reason, bool force = false, bool allowSilenceWhenLoading = false, bool restartIfSameClip = true)
    {
        if (currentModConfig == null)
        {
            SanGuoShaExpLog.Warn("Battle BGM switch skipped: provider runtime is not initialized");
            return;
        }

        if (string.IsNullOrWhiteSpace(primaryProviderId))
        {
            SanGuoShaExpLog.Warn("Battle BGM switch skipped: no provider registered from manifest");
            return;
        }

        BattleBgmArbiterRuntime.Signal(
            currentModConfig,
            ModId,
            "BattleBgmSwitchRequested",
            new BattleBgmSwitchRequest
            {
                ProviderId = primaryProviderId,
                Reason = string.IsNullOrWhiteSpace(reason) ? "SanGuoShaExp.RequestBattleSwitch" : reason,
                Force = force,
                AllowSilenceWhenLoading = allowSilenceWhenLoading,
                RestartIfSameClip = restartIfSameClip
            });
    }

    private static bool RegisterManifest(ModConfig modConfig, string ownerModId, string manifestRelativePath)
    {
        try
        {
            var manifestPath = Path.Combine(modConfig.DirectoryName, manifestRelativePath);
            if (!File.Exists(manifestPath))
            {
                SanGuoShaExpLog.Warn("Battle BGM manifest missing: " + manifestPath);
                return false;
            }

            var manifest = DeserializeManifest(File.ReadAllText(manifestPath));
            if (manifest == null)
            {
                SanGuoShaExpLog.Warn("Battle BGM manifest invalid: " + manifestPath);
                return false;
            }

            var manifestOwner = string.IsNullOrWhiteSpace(manifest.ownerModId) ? ownerModId : manifest.ownerModId.Trim();
            var defaults = manifest.battleBgmDefaults ?? new BattleBgmDefaultsManifest();
            var providers = manifest.battleBgmProviders ?? Array.Empty<BattleBgmProviderManifest>();
            var registered = 0;

            foreach (var provider in providers)
            {
                if (provider == null)
                {
                    continue;
                }

                var providerId = provider.providerId?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    SanGuoShaExpLog.Warn("Battle BGM provider skipped: providerId is empty");
                    continue;
                }

                var audioPath = ResolveManifestPath(modConfig.DirectoryName, provider.path);
                BattleBgmArbiterRuntime.RegisterProvider(
                    modConfig,
                    manifestOwner,
                    new FileBattleBgmProvider(
                        providerId: providerId,
                        ownerModId: string.IsNullOrWhiteSpace(provider.ownerModId) ? manifestOwner : provider.ownerModId.Trim(),
                        audioPath: audioPath,
                        priority: provider.priority ?? defaults.priority ?? 0,
                        hardClaim: provider.hardClaim ?? defaults.hardClaim ?? false,
                        silenceWhenLoading: provider.silenceWhenLoading ?? defaults.silenceWhenLoading ?? false,
                        fallbackToOriginalWhenFailed: provider.fallbackToOriginalWhenFailed ?? defaults.fallbackToOriginalWhenFailed ?? true,
                        adventureCondition: BuildAdventureCondition(provider.match),
                        battleCondition: BuildBattleCondition(provider.match),
                        allowMidBattleSwitch: provider.allowMidBattleSwitch ?? defaults.allowMidBattleSwitch ?? false));

                if (string.IsNullOrWhiteSpace(primaryProviderId) || provider.isDefault == true)
                {
                    primaryProviderId = providerId;
                }

                registered++;
                SanGuoShaExpLog.Info("Battle BGM provider registered from manifest: " + providerId + ", path=" + audioPath);
            }

            SanGuoShaExpLog.Info("Battle BGM manifest registered: providers=" + registered + ", path=" + manifestPath);
            return registered > 0;
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Battle BGM manifest registration failed: " + ex);
            return false;
        }
    }

    private static BattleAudioRegistryManifest? DeserializeManifest(string json)
    {
        try
        {
            var jsonConvert = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json")
                ?? Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var method = jsonConvert?.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
            if (method != null)
            {
                return method.Invoke(null, new object[] { json, typeof(BattleAudioRegistryManifest) }) as BattleAudioRegistryManifest;
            }
        }
        catch
        {
        }

        try
        {
            var jsonUtility = Type.GetType("UnityEngine.JsonUtility, UnityEngine.JSONSerializeModule")
                ?? Assembly.Load("UnityEngine.JSONSerializeModule").GetType("UnityEngine.JsonUtility");
            var method = jsonUtility?.GetMethod("FromJson", new[] { typeof(string), typeof(Type) });
            if (method != null)
            {
                return method.Invoke(null, new object[] { json, typeof(BattleAudioRegistryManifest) }) as BattleAudioRegistryManifest;
            }
        }
        catch
        {
        }

        return null;
    }

    private static string ResolveManifestPath(string modRoot, string relativeOrAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
        {
            return "";
        }

        const string sharedPrefix = "Shared:";
        if (relativeOrAbsolutePath.StartsWith(sharedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuraSharedPaths.ResolveSharedPath(relativeOrAbsolutePath.Substring(sharedPrefix.Length));
        }

        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.Combine(modRoot, relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static Func<object?, bool> BuildAdventureCondition(BattleBgmMatchManifest? match)
    {
        var careerIds = ToSet(match?.careerIds);
        var enabledCardPackIds = ToSet(match?.enabledCardPackIds);
        var modeTypes = ToSet(match?.modeTypes);

        if (careerIds.Count == 0 && enabledCardPackIds.Count == 0 && modeTypes.Count == 0)
        {
            return _ => true;
        }

        return context =>
        {
            try
            {
                var careerId = ReadStringProperty(context, "CareerId");
                if (careerIds.Count > 0 && !MatchesAnyId(careerIds, careerId))
                {
                    return false;
                }

                var packs = ReadStringSetProperty(context, "EnabledCardPackIds");
                if (enabledCardPackIds.Count > 0 && !enabledCardPackIds.Any(packs.Contains))
                {
                    return false;
                }

                var modeType = ReadStringProperty(context, "ModeType");
                if (modeTypes.Count > 0 && !modeTypes.Contains(modeType))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                SanGuoShaExpLog.Warn("Battle BGM adventure condition failed: " + ex.Message);
                return false;
            }
        };
    }

    private static Func<object?, bool> BuildBattleCondition(BattleBgmMatchManifest? match)
    {
        var levelIds = ToSet(match?.levelIds);
        var enemyIds = ToSet(match?.enemyIds);
        var levelBgmNames = ToSet(match?.levelBgmNames);
        var bossOnly = match?.bossOnly;
        var highTideOnly = match?.highTideOnly;

        if (levelIds.Count == 0 && enemyIds.Count == 0 && levelBgmNames.Count == 0
            && !bossOnly.HasValue && !highTideOnly.HasValue)
        {
            return _ => true;
        }

        return context =>
        {
            try
            {
                if (levelIds.Count > 0 && !MatchesAnyId(levelIds, ReadStringProperty(context, "LevelId")))
                {
                    return false;
                }

                var enemies = ReadStringSetProperty(context, "EnemyIds");
                if (enemyIds.Count > 0 && !enemyIds.Any(enemies.Contains))
                {
                    return false;
                }

                if (levelBgmNames.Count > 0 && !levelBgmNames.Contains(ReadStringProperty(context, "LevelBgmName")))
                {
                    return false;
                }

                if (bossOnly.HasValue && ReadBoolProperty(context, "IsBoss") != bossOnly.Value)
                {
                    return false;
                }

                if (highTideOnly.HasValue && ReadBoolProperty(context, "IsHighTide") != highTideOnly.Value)
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                SanGuoShaExpLog.Warn("Battle BGM battle condition failed: " + ex.Message);
                return false;
            }
        };
    }

    private static HashSet<string> ToSet(string[]? values)
    {
        return new HashSet<string>(
            values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesAnyId(HashSet<string> accepted, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (accepted.Contains(value))
        {
            return true;
        }

        return accepted.Any(id =>
            value.StartsWith(id + "_", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("_" + id, StringComparison.OrdinalIgnoreCase));
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

    private static bool ReadBoolProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return false;
        }

        var value = source.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(source);
        return value is bool typed && typed;
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

[Serializable]
public sealed class BattleAudioRegistryManifest
{
    public int schemaVersion = 1;
    public string ownerModId = "";
    public BattleBgmDefaultsManifest? battleBgmDefaults;
    public BattleBgmProviderManifest[]? battleBgmProviders;
}

[Serializable]
public sealed class BattleBgmDefaultsManifest
{
    public int? priority;
    public bool? hardClaim;
    public bool? silenceWhenLoading;
    public bool? fallbackToOriginalWhenFailed;
    public bool? allowMidBattleSwitch;
}

[Serializable]
public sealed class BattleBgmProviderManifest
{
    public string providerId = "";
    public string ownerModId = "";
    public string path = "";
    public int? priority;
    public bool? hardClaim;
    public bool? silenceWhenLoading;
    public bool? fallbackToOriginalWhenFailed;
    public bool? allowMidBattleSwitch;
    public bool? isDefault;
    public BattleBgmMatchManifest? match;
}

[Serializable]
public sealed class BattleBgmMatchManifest
{
    public string[]? careerIds;
    public string[]? enabledCardPackIds;
    public string[]? modeTypes;
    public string[]? levelIds;
    public string[]? enemyIds;
    public string[]? levelBgmNames;
    public bool? bossOnly;
    public bool? highTideOnly;
}
