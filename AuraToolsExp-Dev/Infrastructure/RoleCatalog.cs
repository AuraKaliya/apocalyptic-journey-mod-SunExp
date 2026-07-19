using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AuraShared.Core;
using AuraRole.Shared;
using Witch.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public sealed class RoleInfo
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string PackBelong { get; set; } = "";

    public string Icon { get; set; } = "";

    public List<RoleSkillInfo> Skills { get; set; } = new();
}

public sealed class RoleSkillInfo
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Slot { get; set; }
}

public static class RoleCatalog
{
    private static readonly object Gate = new();
    private static List<RoleInfo> cached = new();
    private static float lastScanRealtime;

    public static IReadOnlyList<RoleInfo> GetRoles(bool forceRefresh = false)
    {
        lock (Gate)
        {
            if (!forceRefresh && cached.Count > 0 && UnityEngine.Time.realtimeSinceStartup - lastScanRealtime < 10f)
            {
                return cached.ToList();
            }

            cached = ScanRoles();
            lastScanRealtime = UnityEngine.Time.realtimeSinceStartup;
            return cached.ToList();
        }
    }

    public static string GetDisplayName(string roleId)
    {
        var normalized = NormalizeRoleId(roleId);
        return GetRoles()
            .FirstOrDefault(role => string.Equals(role.Id, normalized, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? normalized;
    }

    public static IReadOnlyList<RoleSkillInfo> GetRoleSkills(string roleId, bool forceRefresh = false)
    {
        var normalized = NormalizeRoleId(roleId);
        return GetRoles(forceRefresh)
            .FirstOrDefault(role => string.Equals(role.Id, normalized, StringComparison.OrdinalIgnoreCase))
            ?.Skills
            .ToList() ?? new List<RoleSkillInfo>();
    }

    public static string NormalizeRoleId(string? roleId)
    {
        return AuraSharedIdentity.NormalizeRoleId(roleId);
    }

    private static List<RoleInfo> ScanRoles()
    {
        var result = new List<RoleInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var lines = Singleton<GameConfigManager>.Instance
                ?.GetTable(DataType.Career)
                ?.Getlines();
            if (lines == null)
            {
                return result;
            }

            foreach (var row in lines)
            {
                if (!row.TryGetValue("Id", out var id))
                {
                    continue;
                }

                var normalizedId = NormalizeRoleId(id);
                if (string.IsNullOrWhiteSpace(normalizedId) || !seen.Add(normalizedId))
                {
                    continue;
                }

                result.Add(new RoleInfo
                {
                    Id = normalizedId,
                    DisplayName = ResolveDisplayName(normalizedId, row),
                    PackBelong = row.TryGetValue("PackBelong", out var pack) ? pack : "",
                    Icon = row.TryGetValue("Icon", out var icon) ? icon : "",
                    Skills = ResolveSkills(row)
                });
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Role scan failed: " + ex.Message);
        }

        var roles = result
            .OrderBy(role => role.PackBelong)
            .ThenBy(role => role.DisplayName)
            .ThenBy(role => role.Id)
            .ToList();
        AuraRoleRegistryRuntime.PublishRuntimeRoles(
            AuraToolsIds.ModId,
            "game-career-scan",
            roles.Select(role => new AuraRoleRegistryEntry
            {
                RoleId = role.Id,
                DisplayName = role.DisplayName,
                PackBelong = role.PackBelong,
                Icon = role.Icon,
                Priority = 0,
                Tags = new List<string> { "game-career-table" }
            }));
        return roles;
    }

    private static string ResolveDisplayName(string id, Dictionary<string, string> row)
    {
        try
        {
            // Role scans can run while registered CG defaults are being imported,
            // before DataConfig's id cache is ready. The table row is already the
            // authoritative source here, so localize it directly without forcing
            // an early DataConfig lookup that logs a false missing-key error.
            var localized = row.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, "Name", StringComparison.OrdinalIgnoreCase))
            {
                return localized;
            }
        }
        catch
        {
            // Fall through to raw table values.
        }

        return row.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : id;
    }

    private static List<RoleSkillInfo> ResolveSkills(Dictionary<string, string> row)
    {
        var skills = new List<RoleSkillInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in row
            .Where(pair => IsSkillSlotKey(pair.Key))
            .OrderBy(pair => SkillSlotIndex(pair.Key)))
        {
            var slot = SkillSlotIndex(pair.Key);
            foreach (var id in SplitSkillIds(pair.Value))
            {
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    continue;
                }

                skills.Add(new RoleSkillInfo
                {
                    Id = id,
                    DisplayName = ResolveActiveSkillDisplayName(row, slot, id),
                    Slot = slot
                });
            }
        }

        return skills;
    }

    private static bool IsSkillSlotKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("Skill", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = key.Substring("Skill".Length);
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    private static int SkillSlotIndex(string key)
    {
        return int.TryParse(key.Substring("Skill".Length), out var index) ? index : 999;
    }

    private static IEnumerable<string> SplitSkillIds(string value)
    {
        return (value ?? "")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id));
    }

    private static string ResolveCardDisplayName(string cardId)
    {
        try
        {
            var data = new DataConfig(cardId, DataType.Card).data;
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, "Name", StringComparison.OrdinalIgnoreCase))
            {
                return localized;
            }

            if (data.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch
        {
            // Some legacy skill ids only resolve at runtime; keep the raw id visible.
        }

        return cardId;
    }

    private static string ResolveActiveSkillDisplayName(IDictionary<string, string> row, int slot, string cardId)
    {
        var actionName = ExtractTaggedName(ResolveLocalizedCareerField(row, "Action" + slot));
        return string.IsNullOrWhiteSpace(actionName) ? ResolveCardDisplayName(cardId) : actionName;
    }

    private static string ResolveLocalizedCareerField(IDictionary<string, string> data, string key)
    {
        try
        {
            var localized = data.Localize(key);
            if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, key, StringComparison.OrdinalIgnoreCase))
            {
                return localized;
            }
        }
        catch
        {
        }

        return data.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static string ExtractTaggedName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var match = Regex.Match(value, "<name>(.*?)</name>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return value.Trim();
    }
}
