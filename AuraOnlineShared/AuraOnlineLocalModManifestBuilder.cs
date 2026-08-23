using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Newtonsoft.Json.Linq;
using Witch.Core;

namespace AuraOnline.Shared;

public static class AuraOnlineLocalModManifestBuilder
{
    public static AuraChatModPlayerSnapshot CreateLocalPlayerSnapshot(string playerId, string playerName)
    {
        var snapshot = new AuraChatModPlayerSnapshot
        {
            PlayerId = (playerId ?? "").Trim(),
            PlayerName = (playerName ?? "").Trim()
        };

        var configs = Singleton<GameConfigManager>.Instance?.modConfigs as IEnumerable;
        if (configs == null)
        {
            return snapshot;
        }

        foreach (var config in configs)
        {
            if (config == null)
            {
                continue;
            }

            var mod = ReadMod(config);
            if (string.IsNullOrWhiteSpace(mod.ModName) && string.IsNullOrWhiteSpace(mod.ModId))
            {
                continue;
            }

            snapshot.Mods.Add(mod);
        }

        snapshot.Mods = snapshot.Mods
            .GroupBy(mod => mod.MatchKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(mod => mod.ModName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return snapshot;
    }

    private static AuraChatModSnapshot ReadMod(object config)
    {
        var directory = ReadString(config, "DirectoryName", "Directory", "Path", "ModPath");
        var json = ReadModConfigJson(directory);
        var discovery = AuraSharedDiscoveryLoader.Load(directory);
        var publishedFileId = ReadULong(config, "WorkshopPublishedFileId", "WorkshopPublishedFileID", "PublishedFileId", "PublishedFileID");
        if (publishedFileId == 0UL)
        {
            publishedFileId = ReadJsonULong(json, "WorkshopPublishedFileId", "WorkshopPublishedFileID", "PublishedFileId", "PublishedFileID");
        }

        if (publishedFileId == 0UL)
        {
            publishedFileId = discovery.Source != null
                              && ulong.TryParse(discovery.Source.ModProjectId, out var discoveredId)
                ? discoveredId
                : ReadModProjectIdFile(directory);
        }

        if (publishedFileId == 0UL)
        {
            publishedFileId = ReadWorkshopIdFile(directory);
        }

        var modId = FirstNonEmpty(
            ReadString(config, "ModId", "ModID", "Id", "ID"),
            ReadJsonString(json, "ModId", "ModID", "Id", "ID"));
        var modName = FirstNonEmpty(
            ReadString(config, "ModName", "Name"),
            ReadJsonString(json, "ModName", "Name"),
            modId,
            Path.GetFileName(NormalizePath(directory)));
        return new AuraChatModSnapshot
        {
            ModId = modId,
            ModName = modName,
            ModVersion = FirstNonEmpty(ReadString(config, "ModVersion", "Version"), ReadJsonString(json, "ModVersion", "Version")),
            ModAuthor = FirstNonEmpty(ReadString(config, "ModAuthor", "Author"), ReadJsonString(json, "ModAuthor", "Author")),
            DirectoryName = directory,
            IsWorkshopMod = ReadBool(config, "IsWorkshopMod", false) || publishedFileId != 0UL,
            PublishedFileId = publishedFileId,
            SharedResourceFingerprint = discovery.Source?.Fingerprint ?? "",
            Enabled = ReadBool(config, "Enabled", ReadJsonBool(json, "Enabled", true))
        };
    }

    private static ulong ReadModProjectIdFile(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return 0UL;
            var files = Directory.GetFiles(directory, "*.modproj", SearchOption.TopDirectoryOnly);
            if (files.Length != 1) return 0UL;
            var text = File.ReadAllText(files[0]).Trim();
            return ulong.TryParse(text, out var id) ? id : 0UL;
        }
        catch
        {
            return 0UL;
        }
    }

    private static JObject? ReadModConfigJson(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            var path = Path.Combine(directory, "ModConfig.json");
            return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static ulong ReadWorkshopIdFile(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return 0UL;
            }

            var path = Path.Combine(directory, ".workshop-id");
            if (!File.Exists(path))
            {
                return 0UL;
            }

            var text = File.ReadAllText(path).Trim();
            return ulong.TryParse(text, out var id) ? id : 0UL;
        }
        catch
        {
            return 0UL;
        }
    }

    private static string ReadString(object target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(target, name);
            if (value == null)
            {
                continue;
            }

            var text = Convert.ToString(value)?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return "";
    }

    private static ulong ReadULong(object target, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(target, name);
            try
            {
                var id = value == null ? 0UL : Convert.ToUInt64(value);
                if (id != 0UL)
                {
                    return id;
                }
            }
            catch
            {
            }
        }

        return 0UL;
    }

    private static bool ReadBool(object target, string name, bool fallback)
    {
        var value = ReadMember(target, name);
        if (value is bool boolean)
        {
            return boolean;
        }

        if (bool.TryParse(Convert.ToString(value), out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static object? ReadMember(object target, string name)
    {
        try
        {
            var type = target.GetType();
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
                   ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadJsonString(JObject? json, params string[] names)
    {
        foreach (var name in names)
        {
            var text = json?.GetValue(name, StringComparison.OrdinalIgnoreCase)?.ToString().Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return "";
    }

    private static ulong ReadJsonULong(JObject? json, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var token = json?.GetValue(name, StringComparison.OrdinalIgnoreCase);
                var value = token == null ? 0UL : token.Value<ulong>();
                if (value != 0UL)
                {
                    return value;
                }
            }
            catch
            {
            }
        }

        return 0UL;
    }

    private static bool ReadJsonBool(JObject? json, string name, bool fallback)
    {
        try
        {
            var token = json?.GetValue(name, StringComparison.OrdinalIgnoreCase);
            return token == null ? fallback : token.Value<bool>();
        }
        catch
        {
            return fallback;
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "" : path.Replace('\\', '/').TrimEnd('/');
    }
}
