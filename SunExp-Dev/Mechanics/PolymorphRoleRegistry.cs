using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class PolymorphRoleRegistry
{
    public static IReadOnlyList<PolymorphRoleSpec> AllRoles()
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            return SunExpConfigIndex.Rows(DataType.Career)
                .Select(ToSpec)
                .Where(spec => !string.IsNullOrWhiteSpace(spec.Id))
                .OrderBy(spec => spec.DisplayName, StringComparer.Ordinal)
                .ThenBy(spec => spec.Id, StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("PolymorphRoleRegistry.AllRoles", start);
        }
    }

    public static PolymorphRoleSpec? Find(string roleId)
    {
        var id = (roleId ?? "").Trim();
        if (id.Length == 0)
        {
            return null;
        }

        return AllRoles().FirstOrDefault(role => string.Equals(role.Id, id, StringComparison.Ordinal));
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
        var roleData = SunExpConfigIndex.Row(DataType.RoleData, id);
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
                SunExpIds.PolymorphPlaceholderCardIconPath),
            FirstNonEmpty(
                DictionaryUtil.Get(data, "Avatar"),
                DictionaryUtil.Get(roleData, "Avatar"),
                DictionaryUtil.Get(data, "ChoiceIcon"),
                SunExpIds.PolymorphPlaceholderCardIconPath),
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
            return new Dictionary<string, string>(new DataConfig(id, DataType.Career).data);
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
}
