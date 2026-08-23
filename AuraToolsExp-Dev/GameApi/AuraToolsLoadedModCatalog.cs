using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.GameApi;

internal sealed class AuraToolsLoadedModSnapshot
{
    internal bool LoadedStateAvailable { get; set; }

    internal IReadOnlyList<ModConfig> Mods { get; set; } = Array.Empty<ModConfig>();

    internal string Diagnostic { get; set; } = "";
}

internal static class AuraToolsLoadedModCatalog
{
    private static readonly string[] LoadedDirectoryFieldNames =
    {
        "loadedModDirectories",
        "LoadedModDirectories"
    };

    internal static AuraToolsLoadedModSnapshot Capture()
    {
        try
        {
            var manager = Singleton<GameConfigManager>.Instance;
            if (manager == null)
            {
                return Unavailable("GameConfigManager is unavailable.");
            }

            var directories = ReadLoadedDirectories(manager);
            if (directories == null)
            {
                return Unavailable("The current game build does not expose the loaded Mod directory set.");
            }

            var mods = (manager.modConfigs ?? new List<ModConfig>())
                .Where(mod => mod != null
                              && mod.Enabled
                              && !string.IsNullOrWhiteSpace(mod.DirectoryName)
                              && directories.Contains(Normalize(mod.DirectoryName)))
                .OrderBy(mod => Normalize(mod.DirectoryName), StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new AuraToolsLoadedModSnapshot
            {
                LoadedStateAvailable = true,
                Mods = mods
            };
        }
        catch (Exception ex)
        {
            return Unavailable(ex.Message);
        }
    }

    private static HashSet<string>? ReadLoadedDirectories(GameConfigManager manager)
    {
        foreach (var fieldName in LoadedDirectoryFieldNames)
        {
            var field = typeof(GameConfigManager).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(manager) is not IEnumerable values)
            {
                continue;
            }
            return new HashSet<string>(
                values.Cast<object>()
                    .Select(value => Normalize(Convert.ToString(value) ?? ""))
                    .Where(value => value.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }
        return null;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path ?? "")
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return (path ?? "").Trim();
        }
    }

    private static AuraToolsLoadedModSnapshot Unavailable(string diagnostic)
    {
        return new AuraToolsLoadedModSnapshot
        {
            LoadedStateAvailable = false,
            Diagnostic = diagnostic ?? ""
        };
    }
}
