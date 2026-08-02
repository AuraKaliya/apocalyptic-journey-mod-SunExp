using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal sealed class AutoBattleRewardCardPackInfo
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool Required { get; set; }
}

internal static class AuraToolsAutoBattleGameParameterRuntime
{
    private static readonly object CatalogGate = new();
    private static List<AutoBattleRewardCardPackInfo> cachedPacks = new();
    private static float lastPackScanRealtime;

    public static void ResolvePresetReferences(AutoBattleSettings settings)
    {
        settings.Normalize();
        foreach (var preset in settings.GameParameters.Presets)
        {
            var role = RoleCatalog.GetRoles().FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    preset.RoleId,
                    StringComparison.OrdinalIgnoreCase));
            preset.ResolvedRoleSkillIds = (role?.Skills
                                           ?? new List<RoleSkillInfo>())
                .Select(item => item.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            preset.ResolvedRoleSkillCooldownTurns = (role?.Skills
                                                      ?? new List<RoleSkillInfo>())
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => Math.Max(
                        1,
                        group.First().CooldownTurns),
                    StringComparer.OrdinalIgnoreCase);
            preset.ResolvedRoleInitialStatuses =
                new Dictionary<string, int>(
                    role?.InitialStatuses
                    ?? new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            preset.ResolvedFamiliarBlessingIds = PartnerCatalog
                .GetBlessingIds(preset.PartnerId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            preset.Normalize();
        }
    }

    public static AutoBattleGameParameterPreset Apply(
        AutoBattleSettings settings,
        CombatCampaignDefinition campaign)
    {
        settings.Normalize();
        var preset = settings.GameParameters.ActivePreset;
        preset.Normalize();

        CombatGameSubjectPresetRuntime.Apply(
            ToSharedPreset(preset),
            campaign);
        AuraToolsRoleCampaignStrategy.Apply(campaign);
        return preset;
    }

    public static IReadOnlyList<AutoBattleRewardCardPackInfo> GetRewardCardPacks(
        bool forceRefresh = false)
    {
        lock (CatalogGate)
        {
            if (!forceRefresh
                && cachedPacks.Count > 0
                && UnityEngine.Time.realtimeSinceStartup - lastPackScanRealtime < 10f)
            {
                return cachedPacks.Select(ClonePack).ToList();
            }

            cachedPacks = ScanRewardCardPacks();
            lastPackScanRealtime = UnityEngine.Time.realtimeSinceStartup;
            return cachedPacks.Select(ClonePack).ToList();
        }
    }

    public static string ComputeHash(
        AutoBattleGameParameterPreset preset,
        IEnumerable<string> startingDeck)
    {
        return CombatGameSubjectPresetRuntime.ComputeHash(
            ToSharedPreset(preset),
            startingDeck);
    }

    private static CombatGameSubjectPreset ToSharedPreset(
        AutoBattleGameParameterPreset preset)
    {
        return new CombatGameSubjectPreset
        {
            Id = preset.Id,
            DisplayName = preset.DisplayName,
            RoleId = preset.RoleId,
            PartnerId = preset.PartnerId,
            EnabledRewardCardPackIds =
                preset.EnabledRewardCardPackIds.ToList(),
            PreferredDeckSizeMinimum =
                preset.PreferredDeckSizeMinimum,
            PreferredDeckSizeMaximum =
                preset.PreferredDeckSizeMaximum,
            ResolvedRoleSkillIds =
                preset.ResolvedRoleSkillIds.ToList(),
            ResolvedRoleInitialStatuses =
                new Dictionary<string, int>(
                    preset.ResolvedRoleInitialStatuses,
                    StringComparer.OrdinalIgnoreCase),
            ResolvedRoleSkillCooldownTurns =
                new Dictionary<string, int>(
                    preset.ResolvedRoleSkillCooldownTurns,
                    StringComparer.OrdinalIgnoreCase),
            ResolvedFamiliarBlessingIds =
                preset.ResolvedFamiliarBlessingIds.ToList()
        }.Normalize();
    }

    private static List<AutoBattleRewardCardPackInfo> ScanRewardCardPacks()
    {
        var result = new List<AutoBattleRewardCardPackInfo>();
        try
        {
            var query = AuraGameDataHostApi.Query(
                DataType.CardPack,
                includeAllCandidates: true);
            foreach (var item in query.Items.Where(item =>
                         item.Enabled
                         && !item.Retired
                         && !string.Equals(
                             item.Id,
                             "cardpack_13",
                             StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new AutoBattleRewardCardPackInfo
                {
                    Id = item.Id,
                    DisplayName = ResolveDisplayName(item.Id, item.Fields),
                    Required = IsRequiredPack(item.Id)
                });
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Reward card-pack scan failed: " + ex.Message);
        }

        foreach (var required in new[] { "cardpack_1", "cardpack_2" })
        {
            if (result.All(item => !string.Equals(
                    item.Id,
                    required,
                    StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new AutoBattleRewardCardPackInfo
                {
                    Id = required,
                    DisplayName = required,
                    Required = true
                });
            }
        }

        return result
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => CardPackOrder(item.Id))
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveDisplayName(
        string id,
        IReadOnlyDictionary<string, string> row)
    {
        try
        {
            var localized = (row as IDictionary<string, string>)?.Localize("Name") ?? "";
            if (!string.IsNullOrWhiteSpace(localized)
                && !string.Equals(
                    localized,
                    "Name",
                    StringComparison.OrdinalIgnoreCase))
            {
                return localized;
            }
        }
        catch
        {
        }

        return row.TryGetValue("Name", out var name)
               && !string.IsNullOrWhiteSpace(name)
            ? name
            : id;
    }

    private static bool IsRequiredPack(string id)
    {
        return string.Equals(id, "cardpack_1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   id,
                   "cardpack_2",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static int CardPackOrder(string id)
    {
        const string prefix = "cardpack_";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(id.Substring(prefix.Length), out var value)
            ? value
            : int.MaxValue;
    }

    private static AutoBattleRewardCardPackInfo ClonePack(
        AutoBattleRewardCardPackInfo source)
    {
        return new AutoBattleRewardCardPackInfo
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            Required = source.Required
        };
    }
}
