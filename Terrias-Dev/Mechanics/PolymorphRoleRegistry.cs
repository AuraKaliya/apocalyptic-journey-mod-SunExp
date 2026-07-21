using System;
using System.Collections.Generic;
using System.Linq;
using AuraRole.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class PolymorphRoleRegistry
{
    public static IReadOnlyList<PolymorphRoleSpec> AllRoles()
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            var effective = AuraRoleRegistryRuntime.GetEffectiveSnapshot();
            if (!effective.NativeReady)
            {
                TerriasLog.Debug("[Polymorph] effective role catalog is waiting for native career capture.");
                return Array.Empty<PolymorphRoleSpec>();
            }

            return effective.Entries
                .Select(entry => TerriasConfigIndex.Row(DataType.Career, entry.RoleId))
                .Where(row => row != null)
                .Select(row => ToSpec(row!))
                .Where(spec => !string.IsNullOrWhiteSpace(spec.Id) && !spec.IsLocked)
                .OrderBy(spec => spec.DisplayName, StringComparer.Ordinal)
                .ThenBy(spec => spec.Id, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("PolymorphRoleRegistry.AllRoles", start);
        }
    }

    public static PolymorphRoleSpec? Find(string roleId)
    {
        var id = NormalizeRoleId(roleId);
        if (id.Length == 0)
        {
            return null;
        }

        return AllRoles().FirstOrDefault(role =>
        {
            var roleIdValue = NormalizeRoleId(role.Id);
            return string.Equals(roleIdValue, id, StringComparison.OrdinalIgnoreCase)
                || roleIdValue.EndsWith("_" + id, StringComparison.OrdinalIgnoreCase)
                || id.EndsWith("_" + roleIdValue, StringComparison.OrdinalIgnoreCase);
        });
    }

    public static PolymorphRoleSpec? CurrentRole()
    {
        try
        {
            var data = RoleTable.Instance?.Career?.data;
            if (data != null)
            {
                return ToSpec(new Dictionary<string, string>(data));
            }
        }
        catch
        {
            // Fall through to current career id lookup.
        }

        return Find(PlayerApi.GetCurrentCareerId());
    }

    public static IEnumerable<string> CardFacePaths(int limit)
    {
        return AllRoles()
            .Select(role => role.CardFacePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(0, limit));
    }

    private static PolymorphRoleSpec ToSpec(Dictionary<string, string> row)
    {
        var id = DictionaryUtil.Get(row, "Id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return EmptySpec();
        }

        var data = MergedCareerData(id, row);
        var roleData = TerriasConfigIndex.Row(DataType.RoleData, id);
        var crop = PolymorphRoleCropRegistry.CropFor(id);
        return new PolymorphRoleSpec(
            id,
            DisplayName(data, id),
            FirstNonEmpty(
                DictionaryUtil.Get(data, "CareerImage"),
                DictionaryUtil.Get(data, "Character"),
                DictionaryUtil.Get(roleData, "CharacterImage"),
                DictionaryUtil.Get(data, "Avatar"),
                DictionaryUtil.Get(data, "ChoiceIcon"),
                TerriasIds.PolymorphPlaceholderCardIconPath),
            FirstNonEmpty(
                DictionaryUtil.Get(data, "Avatar"),
                DictionaryUtil.Get(roleData, "Avatar"),
                DictionaryUtil.Get(data, "ChoiceIcon"),
                TerriasIds.PolymorphPlaceholderCardIconPath),
            DictionaryUtil.Get(data, "Skill1"),
            DictionaryUtil.Get(data, "Skill2"),
            IsLocked(id),
            crop.OffsetX,
            crop.OffsetY,
            crop.Size);
    }

    private static Dictionary<string, string> MergedCareerData(string id, Dictionary<string, string> fallback)
    {
        try
        {
            return TerriasConfigIndex.Row(DataType.Career, id) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string DisplayName(Dictionary<string, string> data, string fallback)
    {
        try
        {
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }
        }
        catch
        {
            // Fall through to raw name fields.
        }

        return FirstNonEmpty(DictionaryUtil.Get(data, "Name"), fallback);
    }

    private static bool IsLocked(string id)
    {
        try
        {
            return Singleton<GameRuntimeData>.Instance?.IsLocked(id) == true;
        }
        catch
        {
            return false;
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static PolymorphRoleSpec EmptySpec()
    {
        return new PolymorphRoleSpec("", "", "", "", "", "", false, 0, 0, PolymorphRoleCropRegistry.DefaultCropSize);
    }

    private static string NormalizeRoleId(string roleId)
    {
        var value = (roleId ?? "").Trim().TrimStart('*');
        const string terriasPrefix = "Terrias_";
        if (value.StartsWith(terriasPrefix, StringComparison.Ordinal))
        {
            var parts = value.Split('_');
            return parts.Length > 0 ? parts[parts.Length - 1] : value;
        }

        return value;
    }
}
