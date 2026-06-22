using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AuraOnline.Shared;

public static class AuraChatModSyncSnapshot
{
    public static string BuildStatus(IEnumerable<object?> players, string currentModId)
    {
        var snapshots = players
            .Where(player => player != null)
            .Select(player => ReadPlayer(player!))
            .Where(player => !string.IsNullOrWhiteSpace(player.PlayerName) || !string.IsNullOrWhiteSpace(player.PlayerId))
            .ToList();

        if (snapshots.Count == 0)
        {
            return "当前无联机玩家信息。";
        }

        var allMods = snapshots
            .SelectMany(player => player.Mods)
            .Where(mod => !string.IsNullOrWhiteSpace(mod.ModName))
            .Select(mod => mod.ModName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => string.Equals(name, currentModId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(currentModId)
            && !allMods.Any(name => string.Equals(name, currentModId, StringComparison.OrdinalIgnoreCase)))
        {
            allMods.Insert(0, currentModId);
        }

        var builder = new StringBuilder();
        builder.AppendLine("当前MOD同步状态");
        foreach (var modName in allMods)
        {
            var enabledPlayers = snapshots
                .Where(player => player.Mods.Any(mod => string.Equals(mod.ModName, modName, StringComparison.OrdinalIgnoreCase) && mod.Enabled))
                .Select(DisplayName)
                .ToList();
            var missingPlayers = snapshots
                .Where(player => !player.Mods.Any(mod => string.Equals(mod.ModName, modName, StringComparison.OrdinalIgnoreCase) && mod.Enabled))
                .Select(DisplayName)
                .ToList();

            builder.Append(modName);
            builder.Append(": ");
            builder.Append(missingPlayers.Count == 0 ? "一致" : "不一致");
            if (missingPlayers.Count > 0)
            {
                builder.Append(" 缺少=");
                builder.Append(string.Join(",", missingPlayers));
            }
            else if (enabledPlayers.Count > 0)
            {
                builder.Append(" 玩家=");
                builder.Append(string.Join(",", enabledPlayers));
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static AuraChatModPlayerSnapshot ReadPlayer(object player)
    {
        var type = player.GetType();
        var snapshot = new AuraChatModPlayerSnapshot
        {
            PlayerId = ReadString(type, player, "Id"),
            PlayerName = ReadString(type, player, "Name")
        };

        if (ReadMember(type, player, "Mods") is System.Collections.IEnumerable mods)
        {
            foreach (var mod in mods)
            {
                if (mod == null)
                {
                    continue;
                }

                var modType = mod.GetType();
                var modName = ReadString(modType, mod, "ModName");
                if (string.IsNullOrWhiteSpace(modName))
                {
                    continue;
                }

                snapshot.Mods.Add(new AuraChatModSnapshot
                {
                    ModName = modName,
                    ModVersion = ReadString(modType, mod, "ModVersion"),
                    Enabled = ReadBool(modType, mod, "Enabled", true)
                });
            }
        }

        return snapshot;
    }

    private static string DisplayName(AuraChatModPlayerSnapshot player)
    {
        return string.IsNullOrWhiteSpace(player.PlayerName) ? player.PlayerId : player.PlayerName;
    }

    private static string ReadString(Type type, object target, string name)
    {
        return Convert.ToString(ReadMember(type, target, name)) ?? "";
    }

    private static bool ReadBool(Type type, object target, string name, bool fallback)
    {
        var value = ReadMember(type, target, name);
        return value is bool boolean ? boolean : fallback;
    }

    private static object? ReadMember(Type type, object target, string name)
    {
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }
}
