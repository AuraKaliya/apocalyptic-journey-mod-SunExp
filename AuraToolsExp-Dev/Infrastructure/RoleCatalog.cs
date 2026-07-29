using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AuraGameData.Shared;
using AuraShared.Core;
using AuraGameData.Shared.GameApi;
using AuraRole.Shared;
using Witch.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public sealed class RoleInfo
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string PackBelong { get; set; } = "";

    public string Icon { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public List<string> Aliases { get; set; } = new();

    public List<RoleSkillInfo> Skills { get; set; } = new();

    public Dictionary<string, int> InitialStatuses { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RoleSkillInfo
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Slot { get; set; }

    public int CooldownTurns { get; set; } = 1;
}

public static class RoleCatalog
{
    private static readonly object Gate = new();
    private static List<RoleInfo> cached = new();
    private static float lastScanRealtime;
    private static long cachedCatalogEpoch = -1;
    private static long cachedRoleRevision = -1;

    public static IReadOnlyList<RoleInfo> GetRoles(bool forceRefresh = false)
    {
        var snapshot = AuraRoleRegistryRuntime.GetEffectiveSnapshot();
        lock (Gate)
        {
            if (!snapshot.NativeReady)
            {
                return cached.ToList();
            }

            if (!forceRefresh
                && cached.Count > 0
                && cachedCatalogEpoch == snapshot.CatalogEpoch
                && cachedRoleRevision == snapshot.RegistryRevision
                && UnityEngine.Time.realtimeSinceStartup - lastScanRealtime < 10f)
            {
                return cached.ToList();
            }

            cached = ScanRoles(snapshot);
            cachedCatalogEpoch = snapshot.CatalogEpoch;
            cachedRoleRevision = snapshot.RegistryRevision;
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

    public static bool MatchesRole(string activeRoleId, string candidateRoleId, IEnumerable<string>? candidateAliases = null)
    {
        var active = GetRoles().FirstOrDefault(role => role.Aliases
            .Concat(new[] { role.Id })
            .Any(alias => string.Equals(NormalizeRoleId(alias), NormalizeRoleId(activeRoleId), StringComparison.OrdinalIgnoreCase)));
        if (active == null) return false;
        var candidateIds = (candidateAliases ?? Array.Empty<string>()).Concat(new[] { candidateRoleId });
        return active.Aliases.Concat(new[] { active.Id })
            .Select(NormalizeRoleId)
            .Intersect(candidateIds.Select(NormalizeRoleId), StringComparer.OrdinalIgnoreCase)
            .Any();
    }

    private static List<RoleInfo> ScanRoles(AuraEffectiveRoleSnapshot effective)
    {
        var result = new List<RoleInfo>();

        try
        {
            var nativeRoles = AuraGameDataHostApi.Query(DataType.Career, includeAllCandidates: true).Items
                .Where(item => item.Enabled
                    && !item.Retired
                    && string.Equals(item.SourceKind, AuraGameDataSourceKinds.Native, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var entry in effective.Entries)
            {
                var normalizedId = NormalizeRoleId(entry.RoleId);
                var native = nativeRoles.FirstOrDefault(item =>
                    string.Equals(NormalizeRoleId(item.Id), normalizedId, StringComparison.OrdinalIgnoreCase)
                    || item.Aliases.Any(alias => string.Equals(
                        NormalizeRoleId(alias),
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase)));
                if (native == null)
                {
                    continue;
                }

                var row = native.Fields;
                var localizedName = ResolveDisplayName(normalizedId, row);

                result.Add(new RoleInfo
                {
                    Id = normalizedId,
                    DisplayName = string.IsNullOrWhiteSpace(localizedName) || localizedName == normalizedId
                        ? (string.IsNullOrWhiteSpace(entry.DisplayName) ? normalizedId : entry.DisplayName)
                        : localizedName,
                    PackBelong = string.IsNullOrWhiteSpace(entry.PackBelong)
                        ? (row.TryGetValue("PackBelong", out var pack) ? pack : "")
                        : entry.PackBelong,
                    Icon = string.IsNullOrWhiteSpace(entry.Icon)
                        ? (row.TryGetValue("Icon", out var icon) ? icon : "")
                        : entry.Icon,
                    OwnerModId = string.IsNullOrWhiteSpace(entry.OwnerModId) ? native.OwnerModId : entry.OwnerModId,
                    Aliases = entry.Aliases
                        .Concat(native.Aliases)
                        .Concat(new[] { normalizedId })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    Skills = ResolveSkills(row),
                    InitialStatuses = ResolveInitialStatuses(row)
                });
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Role scan failed: " + ex.Message);
        }

        return result
            .OrderBy(role => role.PackBelong)
            .ThenBy(role => role.DisplayName)
            .ThenBy(role => role.Id)
            .ToList();
    }

    private static string ResolveDisplayName(string id, IReadOnlyDictionary<string, string> row)
    {
        try
        {
            // Role scans can run while registered CG defaults are being imported,
            // before DataConfig's id cache is ready. The table row is already the
            // authoritative source here, so localize it directly without forcing
            // an early DataConfig lookup that logs a false missing-key error.
            var localized = (row as IDictionary<string, string>)?.Localize("Name") ?? "";
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

    private static List<RoleSkillInfo> ResolveSkills(IReadOnlyDictionary<string, string> row)
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
                    Slot = slot,
                    CooldownTurns = ResolveActiveSkillCooldown(row, slot)
                });
            }
        }

        return skills;
    }

    private static Dictionary<string, int> ResolveInitialStatuses(
        IReadOnlyDictionary<string, string> row)
    {
        var result = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        var script = row.TryGetValue("SkillScript", out var value)
            ? value ?? ""
            : "";
        var eventIndex = script.IndexOf(
            "AddEvent",
            StringComparison.OrdinalIgnoreCase);
        if (eventIndex >= 0)
        {
            script = script.Substring(0, eventIndex);
        }
        foreach (Match match in Regex.Matches(
                     script,
                     @"AddBuff\s*\(\s*(?:""|\bDataId\.)?([A-Za-z0-9_]+)""?\s*,\s*""?(\d+)""?\s*\)",
                     RegexOptions.IgnoreCase))
        {
            var statusId = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(statusId)
                || !int.TryParse(match.Groups[2].Value, out var stacks)
                || stacks <= 0)
            {
                continue;
            }
            result[statusId] = result.TryGetValue(statusId, out var current)
                ? current + stacks
                : stacks;
        }
        return result;
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
            var data = AuraGameDataHostApi.CopyRow(DataType.Card, cardId);
            if (data == null)
            {
                return cardId;
            }
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

    private static string ResolveActiveSkillDisplayName(IReadOnlyDictionary<string, string> row, int slot, string cardId)
    {
        var actionName = ExtractTaggedName(ResolveLocalizedCareerField(row, "Action" + slot));
        return string.IsNullOrWhiteSpace(actionName) ? ResolveCardDisplayName(cardId) : actionName;
    }

    private static int ResolveActiveSkillCooldown(
        IReadOnlyDictionary<string, string> row,
        int slot)
    {
        var action = ResolveLocalizedCareerField(row, "Action" + slot);
        var match = Regex.Match(
            action,
            @"(?:<cd>\s*)?CD\s*[:：]\s*(\d+)",
            RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value)
            ? Math.Max(1, value)
            : 1;
    }

    private static string ResolveLocalizedCareerField(IReadOnlyDictionary<string, string> data, string key)
    {
        try
        {
            var localized = (data as IDictionary<string, string>)?.Localize(key) ?? "";
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
