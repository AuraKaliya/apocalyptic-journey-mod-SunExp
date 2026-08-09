using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;

namespace Terrias.Dll.Hooks;

public static class SolarMemoryPlayerSetupState
{
    public static string GetValue(string key, string fallback = "", bool migrateLegacyWhenSolo = true)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var role = RoleTable.Instance;
        var map = role?.SpecialVarMap;
        if (map != null && map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (role == null)
        {
            return PlayerApi.GetGameVar(key, fallback);
        }

        if (migrateLegacyWhenSolo && !PlayerApi.IsMultiplayerSession())
        {
            var legacy = PlayerApi.GetGameVar(key, "");
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                SetValue(key, legacy);
                return legacy;
            }
        }

        return fallback;
    }

    public static void SetValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var role = RoleTable.Instance;
        if (role != null)
        {
            role.SpecialVarMap ??= new Dictionary<string, string>();
            role.SpecialVarMap[key] = value ?? "";
            return;
        }

        PlayerApi.SetGameVar(key, value ?? "");
    }

    public static void SetValue(RoleTable? role, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (role == null)
        {
            SetValue(key, value);
            return;
        }

        role.SpecialVarMap ??= new Dictionary<string, string>();
        role.SpecialVarMap[key] = value ?? "";
    }

    public static bool IsSet(string key)
    {
        return GetValue(key, "0") == "1";
    }

    public static void SetFlag(string key, bool value)
    {
        SetValue(key, value ? "1" : "0");
    }

    public static int GetInt(string key, int fallback = 0)
    {
        return DictionaryUtil.ParseInt(GetValue(key, fallback.ToString()), fallback);
    }

    public static void SetInt(string key, int value)
    {
        SetValue(key, Math.Max(0, value).ToString());
    }

    public static List<string> SelectedPacks()
    {
        var packs = SplitList(GetValue(TerriasIds.SolarMemorySelectedPacksKey, "", migrateLegacyWhenSolo: false));
        if (SunCardPackSelectionMigration.Apply(packs))
        {
            SetSelectedPacks(packs);
        }

        return packs;
    }

    public static void SetSelectedPacks(IEnumerable<string> packs)
    {
        var normalized = NormalizePacks(packs);
        SetValue(TerriasIds.SolarMemorySelectedPacksKey, JoinList(normalized));
    }

    public static void SetSelectedPacks(RoleTable? role, IEnumerable<string> packs)
    {
        var normalized = NormalizePacks(packs);
        SetValue(role, TerriasIds.SolarMemorySelectedPacksKey, JoinList(normalized));
    }

    public static List<string> SelectedBlessings()
    {
        return SplitList(GetValue(TerriasIds.SolarMemoryBlessSelectedIdsKey, ""));
    }

    public static void SetSelectedBlessings(IEnumerable<string> blessingIds)
    {
        var ids = blessingIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        SetValue(TerriasIds.SolarMemoryBlessSelectedIdsKey, JoinList(ids));
        SetInt(TerriasIds.SolarMemoryBlessPickCountKey, ids.Count);
    }

    public static string Snapshot()
    {
        return "scope="
            + ScopeLabel()
            + "; deck="
            + GetValue(TerriasIds.SolarMemoryDeckConfiguredKey, "0")
            + "; starter="
            + GetValue(TerriasIds.SolarMemoryStarterDeckAppliedKey, "0")
            + "; origin="
            + GetValue(TerriasIds.SolarMemoryOriginConfiguredKey, "0")
            + "; bless="
            + GetValue(TerriasIds.SolarMemoryBlessConfiguredKey, "0")
            + "; setup="
            + GetValue(TerriasIds.SolarMemorySetupFinishedKey, "0")
            + "; step="
            + GetValue(TerriasIds.SolarMemoryPrepStepKey, "");
    }

    private static string ScopeLabel()
    {
        var roleId = RoleTable.Instance?.Id;
        if (!string.IsNullOrWhiteSpace(roleId))
        {
            return roleId ?? "global";
        }

        return "global";
    }

    private static List<string> SplitList(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
    }

    private static List<string> NormalizePacks(IEnumerable<string> packs)
    {
        var normalized = (packs ?? Enumerable.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SunCardPackSelectionMigration.Apply(normalized);
        return normalized;
    }

    private static string JoinList(IEnumerable<string> values)
    {
        return string.Join("|", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
