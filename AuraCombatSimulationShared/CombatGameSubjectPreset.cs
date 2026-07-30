using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AuraCombatSimulation.Shared;

public sealed class CombatGameSubjectPreset
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Id { get; set; } = "standard";

    public string DisplayName { get; set; } = "标准预设";

    public string RoleId { get; set; } = "career_1";

    public string PartnerId { get; set; } = "Partner_10001";

    public List<string> EnabledRewardCardPackIds { get; set; } = new()
    {
        "cardpack_1",
        "cardpack_2"
    };

    public int PreferredDeckSizeMinimum { get; set; } = 15;

    public int PreferredDeckSizeMaximum { get; set; } = 24;

    public List<string> ResolvedRoleSkillIds { get; set; } = new();

    public Dictionary<string, int> ResolvedRoleInitialStatuses { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> ResolvedRoleSkillCooldownTurns { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> ResolvedFamiliarBlessingIds { get; set; } = new();

    public CombatGameSubjectPreset Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        Id = NormalizeId(Id, "standard");
        DisplayName = string.IsNullOrWhiteSpace(DisplayName)
            ? "游戏主体预设"
            : DisplayName.Trim();
        RoleId = string.IsNullOrWhiteSpace(RoleId)
            ? "career_1"
            : RoleId.Trim();
        PartnerId = string.IsNullOrWhiteSpace(PartnerId)
            ? "Partner_10001"
            : PartnerId.Trim();
        EnabledRewardCardPackIds = NormalizeIds(
                EnabledRewardCardPackIds)
            .Where(item => !string.Equals(
                item,
                "cardpack_13",
                StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { "cardpack_1", "cardpack_2" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CardPackOrder)
            .ThenBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        PreferredDeckSizeMinimum = Math.Max(
            1,
            Math.Min(80, PreferredDeckSizeMinimum));
        PreferredDeckSizeMaximum = Math.Max(
            PreferredDeckSizeMinimum,
            Math.Min(80, PreferredDeckSizeMaximum));
        ResolvedRoleSkillIds = NormalizeIds(ResolvedRoleSkillIds);
        ResolvedFamiliarBlessingIds = NormalizeIds(
            ResolvedFamiliarBlessingIds);
        ResolvedRoleInitialStatuses = NormalizePositiveValues(
            ResolvedRoleInitialStatuses);
        ResolvedRoleSkillCooldownTurns = NormalizePositiveValues(
            ResolvedRoleSkillCooldownTurns);
        return this;
    }

    public CombatGameSubjectPreset Clone()
    {
        Normalize();
        return new CombatGameSubjectPreset
        {
            SchemaVersion = SchemaVersion,
            Id = Id,
            DisplayName = DisplayName,
            RoleId = RoleId,
            PartnerId = PartnerId,
            EnabledRewardCardPackIds =
                EnabledRewardCardPackIds.ToList(),
            PreferredDeckSizeMinimum = PreferredDeckSizeMinimum,
            PreferredDeckSizeMaximum = PreferredDeckSizeMaximum,
            ResolvedRoleSkillIds = ResolvedRoleSkillIds.ToList(),
            ResolvedRoleInitialStatuses = new Dictionary<string, int>(
                ResolvedRoleInitialStatuses,
                StringComparer.OrdinalIgnoreCase),
            ResolvedRoleSkillCooldownTurns = new Dictionary<string, int>(
                ResolvedRoleSkillCooldownTurns,
                StringComparer.OrdinalIgnoreCase),
            ResolvedFamiliarBlessingIds =
                ResolvedFamiliarBlessingIds.ToList()
        };
    }

    public static CombatGameSubjectPreset FromCampaign(
        CombatCampaignDefinition campaign)
    {
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        var player = campaign.Player ?? new CombatPlayerSetup();
        return new CombatGameSubjectPreset
        {
            Id = string.IsNullOrWhiteSpace(player.GameParameterPresetId)
                ? "standard"
                : player.GameParameterPresetId,
            DisplayName = "标准预设",
            RoleId = player.RoleId,
            PartnerId = player.PartnerId,
            EnabledRewardCardPackIds =
                (campaign.EnabledRewardCardPackIds
                 ?? new List<string>()).ToList(),
            PreferredDeckSizeMinimum = campaign.TargetDeckSizeMinimum,
            PreferredDeckSizeMaximum = campaign.TargetDeckSizeMaximum,
            ResolvedRoleSkillIds =
                (player.SkillCardIds ?? new List<string>()).ToList(),
            ResolvedRoleSkillCooldownTurns =
                new Dictionary<string, int>(
                    player.SkillCooldownTurns
                    ?? new Dictionary<string, int>(),
                    StringComparer.OrdinalIgnoreCase),
            ResolvedRoleInitialStatuses =
                (player.InitialStatuses ?? new List<CombatInitialStatus>())
                .Where(item => item != null
                               && !string.IsNullOrWhiteSpace(item.StatusId)
                               && item.Stacks > 0)
                .GroupBy(
                    item => item.StatusId.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.Stacks),
                    StringComparer.OrdinalIgnoreCase),
            ResolvedFamiliarBlessingIds =
                (player.FamiliarBlessingIds
                 ?? new List<string>()).ToList()
        }.Normalize();
    }

    private static List<string> NormalizeIds(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(item => (item ?? "").Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, int> NormalizePositiveValues(
        IReadOnlyDictionary<string, int>? values)
    {
        return (values ?? new Dictionary<string, int>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && item.Value > 0)
            .GroupBy(
                item => item.Key.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Max(item => item.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeId(string value, string fallback)
    {
        var normalized = new string(
            (value ?? "").Trim()
            .Select(character =>
                char.IsLetterOrDigit(character)
                || character == '-'
                || character == '_'
                || character == '.'
                    ? character
                    : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized;
    }

    private static int CardPackOrder(string id)
    {
        const string prefix = "cardpack_";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(
                   id.Substring(prefix.Length),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var value)
            ? value
            : int.MaxValue;
    }
}

public static class CombatGameSubjectPresetRuntime
{
    public static void Apply(
        CombatGameSubjectPreset preset,
        CombatCampaignDefinition campaign)
    {
        if (preset == null) throw new ArgumentNullException(nameof(preset));
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        var effective = preset.Clone();
        campaign.Player ??= new CombatPlayerSetup();
        campaign.Player.RoleId = effective.RoleId;
        campaign.Player.PartnerId = effective.PartnerId;
        campaign.Player.SkillCardIds =
            effective.ResolvedRoleSkillIds.ToList();
        campaign.Player.SkillCooldownTurns =
            new Dictionary<string, int>(
                effective.ResolvedRoleSkillCooldownTurns,
                StringComparer.OrdinalIgnoreCase);
        campaign.Player.InitialStatuses =
            effective.ResolvedRoleInitialStatuses
                .Select(item => new CombatInitialStatus
                {
                    StatusId = item.Key,
                    Stacks = item.Value
                })
                .ToList();
        campaign.Player.FamiliarBlessingIds =
            effective.ResolvedFamiliarBlessingIds.ToList();
        campaign.Player.GameParameterPresetId = effective.Id;
        campaign.EnabledRewardCardPackIds =
            effective.EnabledRewardCardPackIds.ToList();
        campaign.TargetDeckSizeMinimum =
            effective.PreferredDeckSizeMinimum;
        campaign.TargetDeckSizeMaximum =
            effective.PreferredDeckSizeMaximum;
        campaign.DeckSizeAlertThreshold = Math.Max(
            effective.PreferredDeckSizeMaximum + 5,
            effective.PreferredDeckSizeMaximum);
        campaign.Player.GameParameterHash = ComputeHash(
            effective,
            campaign.Player.Deck);
    }

    public static string ComputeHash(
        CombatGameSubjectPreset preset,
        IEnumerable<string>? startingDeck)
    {
        if (preset == null) throw new ArgumentNullException(nameof(preset));
        var effective = preset.Clone();
        var canonical = string.Join(
            "\n",
            new[]
            {
                "preset=" + effective.Id,
                "role=" + effective.RoleId,
                "partner=" + effective.PartnerId,
                "skills=" + JoinSorted(effective.ResolvedRoleSkillIds),
                "skillCooldowns="
                + string.Join(
                    ",",
                    effective.ResolvedRoleSkillCooldownTurns
                        .OrderBy(
                            item => item.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Key + ":" + item.Value)),
                "roleStatuses="
                + string.Join(
                    ",",
                    effective.ResolvedRoleInitialStatuses
                        .OrderBy(
                            item => item.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Key + ":" + item.Value)),
                "familiarBlessings="
                + JoinSorted(effective.ResolvedFamiliarBlessingIds),
                "rewardPacks="
                + JoinSorted(effective.EnabledRewardCardPackIds),
                "deckMin="
                + effective.PreferredDeckSizeMinimum.ToString(
                    CultureInfo.InvariantCulture),
                "deckMax="
                + effective.PreferredDeckSizeMaximum.ToString(
                    CultureInfo.InvariantCulture),
                "startingDeck="
                + string.Join(",", startingDeck ?? Array.Empty<string>())
            });
        using var sha = SHA256.Create();
        return BitConverter.ToString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private static string JoinSorted(IEnumerable<string>? values)
    {
        return string.Join(
            ",",
            (values ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class CombatGameSubjectCatalog
{
    public int SchemaVersion { get; set; } = 1;

    public string CatalogId { get; set; } = "";

    public string GameBuild { get; set; } = "";

    public List<CombatGameSubjectRole> Roles { get; set; } = new();

    public List<CombatGameSubjectFamiliar> Familiars { get; set; } = new();

    public List<CombatGameSubjectCardPack> CardPacks { get; set; } = new();

    public CombatGameSubjectCatalog Normalize()
    {
        Roles = (Roles ?? new List<CombatGameSubjectRole>())
            .Where(item => item != null
                           && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Normalize())
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Familiars = (Familiars ?? new List<CombatGameSubjectFamiliar>())
            .Where(item => item != null
                           && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Normalize())
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        CardPacks = (CardPacks ?? new List<CombatGameSubjectCardPack>())
            .Where(item => item != null
                           && !string.IsNullOrWhiteSpace(item.Id)
                           && !string.Equals(
                               item.Id,
                               "cardpack_13",
                               StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Normalize())
            .OrderBy(item => CardPackOrder(item.Id))
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return this;
    }

    public void ResolveReferences(CombatGameSubjectPreset preset)
    {
        if (preset == null) throw new ArgumentNullException(nameof(preset));
        Normalize();
        var role = Roles.FirstOrDefault(item => string.Equals(
            item.Id,
            preset.RoleId,
            StringComparison.OrdinalIgnoreCase));
        if (role != null)
        {
            preset.ResolvedRoleSkillIds = role.SkillCardIds.ToList();
            preset.ResolvedRoleSkillCooldownTurns =
                new Dictionary<string, int>(
                    role.SkillCooldownTurns,
                    StringComparer.OrdinalIgnoreCase);
            preset.ResolvedRoleInitialStatuses =
                new Dictionary<string, int>(
                    role.InitialStatuses,
                    StringComparer.OrdinalIgnoreCase);
        }
        var familiar = Familiars.FirstOrDefault(item => string.Equals(
            item.Id,
            preset.PartnerId,
            StringComparison.OrdinalIgnoreCase));
        if (familiar != null)
        {
            preset.ResolvedFamiliarBlessingIds =
                familiar.BlessingIds.ToList();
        }
        preset.Normalize();
    }

    private static int CardPackOrder(string id)
    {
        const string prefix = "cardpack_";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(id.Substring(prefix.Length), out var value)
            ? value
            : int.MaxValue;
    }
}

public sealed class CombatGameSubjectRole
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public List<string> SkillCardIds { get; set; } = new();

    public Dictionary<string, int> SkillCooldownTurns { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> InitialStatuses { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CombatGameSubjectRole Normalize()
    {
        Id = (Id ?? "").Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName)
            ? Id
            : DisplayName.Trim();
        SkillCardIds = (SkillCardIds ?? new List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SkillCooldownTurns = NormalizeValues(SkillCooldownTurns);
        InitialStatuses = NormalizeValues(InitialStatuses);
        return this;
    }

    private static Dictionary<string, int> NormalizeValues(
        IReadOnlyDictionary<string, int>? values)
    {
        return (values ?? new Dictionary<string, int>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && item.Value > 0)
            .ToDictionary(
                item => item.Key.Trim(),
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class CombatGameSubjectFamiliar
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public List<string> BlessingIds { get; set; } = new();

    public CombatGameSubjectFamiliar Normalize()
    {
        Id = (Id ?? "").Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName)
            ? Id
            : DisplayName.Trim();
        BlessingIds = (BlessingIds ?? new List<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return this;
    }
}

public sealed class CombatGameSubjectCardPack
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool Required { get; set; }

    public CombatGameSubjectCardPack Normalize()
    {
        Id = (Id ?? "").Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName)
            ? Id
            : DisplayName.Trim();
        Required = Required
                   || string.Equals(
                       Id,
                       "cardpack_1",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(
                       Id,
                       "cardpack_2",
                       StringComparison.OrdinalIgnoreCase);
        return this;
    }
}
