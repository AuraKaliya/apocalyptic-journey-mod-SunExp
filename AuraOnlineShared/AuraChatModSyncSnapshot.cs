using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AuraOnline.Shared;

public static class AuraChatModSyncSnapshot
{
    private const int PlayerColumnWidth = 10;
    private const int ModColumnWidth = 10;
    private const char ColumnSeparator = '\t';

    public static string BuildStatus(IEnumerable<object?> players, string currentModId)
    {
        return FormatStatus(BuildState(players, currentModId, ""));
    }

    public static AuraChatModSyncState BuildState(IEnumerable<object?> players, string currentModId, string localPlayerId)
    {
        currentModId ??= "";
        var snapshots = players
            .Where(player => player != null)
            .Select(player => ReadPlayer(player!))
            .Where(player => !string.IsNullOrWhiteSpace(player.PlayerName) || !string.IsNullOrWhiteSpace(player.PlayerId))
            .ToList();

        return BuildStateFromSnapshots(snapshots, currentModId, localPlayerId);
    }

    public static AuraChatModSyncState BuildStateFromSnapshots(IEnumerable<AuraChatModPlayerSnapshot?> players, string currentModId, string localPlayerId)
    {
        currentModId ??= "";
        var snapshots = players
            .Where(player => player != null)
            .Select(player => player!)
            .Where(player => !string.IsNullOrWhiteSpace(player.PlayerName) || !string.IsNullOrWhiteSpace(player.PlayerId))
            .ToList();

        var state = new AuraChatModSyncState
        {
            CurrentModId = currentModId ?? "",
            LocalPlayerId = localPlayerId ?? "",
            HostPlayerId = snapshots.Count > 0 ? snapshots[0].PlayerId : "",
            Players = snapshots
        };

        var host = state.Players.FirstOrDefault(player => string.Equals(player.PlayerId, state.HostPlayerId, StringComparison.Ordinal));
        var local = state.Players.FirstOrDefault(player => string.Equals(player.PlayerId, state.LocalPlayerId, StringComparison.Ordinal));

        state.Rows = snapshots
            .SelectMany(player => player.Mods)
            .Where(mod => !string.IsNullOrWhiteSpace(mod.ModName))
            .GroupBy(mod => mod.MatchKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(mod => mod.Enabled))
            .Select(group =>
            {
                var first = group.FirstOrDefault(mod => mod.Enabled) ?? group.First();
                return new AuraChatModSyncRow
                {
                    ModKey = group.Key,
                    ModName = first.ModName,
                    HostMod = FindMod(host, group.Key),
                    LocalMod = FindMod(local, group.Key)
                };
            })
            .OrderBy(row => IsCurrentMod(row, state.CurrentModId) ? 0 : 1)
            .ThenBy(row => row.ModName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return state;
    }

    public static string FormatStatus(AuraChatModSyncState? state)
    {
        var snapshots = state?.Players ?? new List<AuraChatModPlayerSnapshot>();
        if (snapshots.Count == 0)
        {
            return "\u5f53\u524d\u65e0\u8054\u673a\u73a9\u5bb6\u4fe1\u606f\u3002";
        }

        var rows = state?.Rows ?? new List<AuraChatModSyncRow>();
        if (rows.Count == 0)
        {
            return "\u5f53\u524d\u6ca1\u6709\u5df2\u542f\u7528\u7684\u8054\u673aMOD\u3002";
        }

        var builder = new StringBuilder();
        builder.AppendLine("MOD\u540c\u6b65\u72b6\u6001");
        builder.Append(TruncateCell("MOD", ModColumnWidth));
        foreach (var player in snapshots)
        {
            builder.Append(ColumnSeparator);
            builder.Append(TruncateCell(DisplayName(player), PlayerColumnWidth));
        }

        builder.AppendLine();
        foreach (var row in rows)
        {
            builder.Append(TruncateCell(row.ModName, ModColumnWidth));
            foreach (var player in snapshots)
            {
                builder.Append(ColumnSeparator);
                builder.Append(TruncateCell(ModCell(player, row.ModKey), PlayerColumnWidth));
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
                    ModId = ReadString(modType, mod, "ModId"),
                    ModName = modName,
                    ModVersion = ReadString(modType, mod, "ModVersion"),
                    ModAuthor = ReadString(modType, mod, "ModAuthor"),
                    DirectoryName = ReadString(modType, mod, "DirectoryName"),
                    IsWorkshopMod = ReadBool(modType, mod, "IsWorkshopMod", false),
                    PublishedFileId = ReadPublishedFileId(modType, mod),
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

    private static string ModCell(AuraChatModPlayerSnapshot player, string modKey)
    {
        var mod = FindMod(player, modKey);
        if (mod == null)
        {
            return "-";
        }

        if (!mod.Enabled)
        {
            return "OFF";
        }

        return string.IsNullOrWhiteSpace(mod.ModVersion) ? "ON" : mod.ModVersion;
    }

    private static AuraChatModSnapshot? FindMod(AuraChatModPlayerSnapshot? player, string modKey)
    {
        return player?.Mods.FirstOrDefault(item => string.Equals(item.MatchKey, modKey, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCurrentMod(AuraChatModSyncRow row, string currentModId)
    {
        return string.Equals(row.ModName, currentModId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.ModKey, currentModId, StringComparison.OrdinalIgnoreCase);
    }

    private static string TruncateCell(string? value, int width)
    {
        var text = (value ?? "").Trim();
        var builder = new StringBuilder(text.Length);
        var used = 0;
        foreach (var ch in text)
        {
            var charWidth = DisplayWidth(ch);
            if (used + charWidth > width)
            {
                break;
            }

            builder.Append(ch);
            used += charWidth;
        }

        return builder.ToString();
    }

    private static int DisplayWidth(char ch)
    {
        return ch < 128 ? 1 : 2;
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

    private static ulong ReadPublishedFileId(Type type, object target)
    {
        var id = ReadULong(type, target, "WorkshopPublishedFileId");
        return id != 0UL ? id : ReadULong(type, target, "PublishedFileId");
    }

    private static ulong ReadULong(Type type, object target, string name)
    {
        var value = ReadMember(type, target, name);
        try
        {
            return value == null ? 0UL : Convert.ToUInt64(value);
        }
        catch
        {
            return 0UL;
        }
    }

    private static object? ReadMember(Type type, object target, string name)
    {
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }
}
