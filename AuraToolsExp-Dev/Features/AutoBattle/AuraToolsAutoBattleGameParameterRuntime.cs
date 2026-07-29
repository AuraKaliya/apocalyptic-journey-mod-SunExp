using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

        campaign.Player ??= new CombatPlayerSetup();
        campaign.Player.RoleId = preset.RoleId;
        campaign.Player.PartnerId = preset.PartnerId;
        campaign.Player.SkillCardIds = preset.ResolvedRoleSkillIds.ToList();
        campaign.Player.SkillCooldownTurns =
            new Dictionary<string, int>(
                preset.ResolvedRoleSkillCooldownTurns,
                StringComparer.OrdinalIgnoreCase);
        campaign.Player.InitialStatuses =
            preset.ResolvedRoleInitialStatuses
                .Select(item => new CombatInitialStatus
                {
                    StatusId = item.Key,
                    Stacks = item.Value
                })
                .ToList();
        campaign.Player.FamiliarBlessingIds =
            preset.ResolvedFamiliarBlessingIds.ToList();
        campaign.Player.GameParameterPresetId = preset.Id;
        campaign.EnabledRewardCardPackIds =
            preset.EnabledRewardCardPackIds.ToList();
        campaign.TargetDeckSizeMinimum = preset.PreferredDeckSizeMinimum;
        campaign.TargetDeckSizeMaximum = preset.PreferredDeckSizeMaximum;
        campaign.DeckSizeAlertThreshold = Math.Max(
            preset.PreferredDeckSizeMaximum + 5,
            preset.PreferredDeckSizeMaximum);
        campaign.Player.GameParameterHash = ComputeHash(preset, campaign.Player.Deck);
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
        var canonical = string.Join(
            "\n",
            new[]
            {
                "preset=" + preset.Id,
                "role=" + preset.RoleId,
                "partner=" + preset.PartnerId,
                "skills=" + JoinSorted(preset.ResolvedRoleSkillIds),
                "skillCooldowns="
                + string.Join(
                    ",",
                    preset.ResolvedRoleSkillCooldownTurns
                        .OrderBy(
                            item => item.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Key + ":" + item.Value)),
                "roleStatuses="
                + string.Join(
                    ",",
                    preset.ResolvedRoleInitialStatuses
                        .OrderBy(
                            item => item.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Key + ":" + item.Value)),
                "familiarBlessings="
                + JoinSorted(preset.ResolvedFamiliarBlessingIds),
                "rewardPacks=" + JoinSorted(preset.EnabledRewardCardPackIds),
                "deckMin=" + preset.PreferredDeckSizeMinimum.ToString(
                    CultureInfo.InvariantCulture),
                "deckMax=" + preset.PreferredDeckSizeMaximum.ToString(
                    CultureInfo.InvariantCulture),
                "startingDeck=" + string.Join(
                    ",",
                    startingDeck ?? Array.Empty<string>())
            });
        using var sha = SHA256.Create();
        return BitConverter.ToString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .Replace("-", "")
            .ToLowerInvariant();
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

    private static string JoinSorted(IEnumerable<string> values)
    {
        return string.Join(
            ",",
            (values ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
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
