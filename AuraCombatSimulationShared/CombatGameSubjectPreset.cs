using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AuraCombatSimulation.Shared;

public sealed class CombatGameSubjectPreset
{
    public const int CurrentSchemaVersion = 3;

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

    public Dictionary<string, int> ResolvedRoleInitialSkillCooldownTurns { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int ResolvedRoleMaximumHp { get; set; }

    public Dictionary<string, double> ResolvedRoleInitialVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string ResolvedRoleNativeScriptHash { get; set; } = "";

    public string ResolvedRoleFightScript { get; set; } = "";

    public List<string> ResolvedRoleNativeManagedSkillCooldownIds { get; set; } =
        new();

    public List<CombatRoleRuntimeForm> ResolvedRoleRuntimeForms { get; set; } =
        new();

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
        ResolvedRoleInitialSkillCooldownTurns = NormalizePositiveValues(
            ResolvedRoleInitialSkillCooldownTurns);
        ResolvedRoleMaximumHp = Math.Max(0, Math.Min(1000000, ResolvedRoleMaximumHp));
        ResolvedRoleInitialVariables = NormalizeFiniteValues(
            ResolvedRoleInitialVariables);
        ResolvedRoleNativeScriptHash = (ResolvedRoleNativeScriptHash ?? "").Trim();
        ResolvedRoleFightScript = ResolvedRoleFightScript ?? "";
        ResolvedRoleNativeManagedSkillCooldownIds = NormalizeIds(
            ResolvedRoleNativeManagedSkillCooldownIds);
        ResolvedRoleRuntimeForms = (ResolvedRoleRuntimeForms
                                    ?? new List<CombatRoleRuntimeForm>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.RoleId))
            .GroupBy(item => item.RoleId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Clone())
            .ToList();
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
            ResolvedRoleInitialSkillCooldownTurns = new Dictionary<string, int>(
                ResolvedRoleInitialSkillCooldownTurns,
                StringComparer.OrdinalIgnoreCase),
            ResolvedRoleMaximumHp = ResolvedRoleMaximumHp,
            ResolvedRoleInitialVariables = new Dictionary<string, double>(
                ResolvedRoleInitialVariables,
                StringComparer.OrdinalIgnoreCase),
            ResolvedRoleNativeScriptHash = ResolvedRoleNativeScriptHash,
            ResolvedRoleFightScript = ResolvedRoleFightScript,
            ResolvedRoleNativeManagedSkillCooldownIds =
                ResolvedRoleNativeManagedSkillCooldownIds.ToList(),
            ResolvedRoleRuntimeForms = ResolvedRoleRuntimeForms
                .Select(item => item.Clone())
                .ToList(),
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
            ResolvedRoleInitialSkillCooldownTurns =
                new Dictionary<string, int>(
                    player.InitialSkillCooldownTurns
                    ?? new Dictionary<string, int>(),
                    StringComparer.OrdinalIgnoreCase),
            ResolvedRoleMaximumHp = player.MaxHp,
            ResolvedRoleInitialVariables = new Dictionary<string, double>(
                player.Variables ?? new Dictionary<string, double>(),
                StringComparer.OrdinalIgnoreCase),
            ResolvedRoleNativeScriptHash = player.RoleNativeScriptHash,
            ResolvedRoleFightScript = player.RoleFightScript,
            ResolvedRoleNativeManagedSkillCooldownIds =
                (player.NativeManagedSkillCooldownIds
                 ?? new List<string>()).ToList(),
            ResolvedRoleRuntimeForms = (player.RoleRuntimeForms
                                        ?? new List<CombatRoleRuntimeForm>())
                .Select(item => item.Clone())
                .ToList(),
            ResolvedRoleInitialStatuses =
                player.RolePassiveContract?.InitialStatuses?.Count > 0
                    ? new Dictionary<string, int>(
                        player.RolePassiveContract.InitialStatuses,
                        StringComparer.OrdinalIgnoreCase)
                    : (player.InitialStatuses ?? new List<CombatInitialStatus>())
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

    private static Dictionary<string, double> NormalizeFiniteValues(
        IReadOnlyDictionary<string, double>? values)
    {
        return (values ?? new Dictionary<string, double>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && !double.IsNaN(item.Value)
                           && !double.IsInfinity(item.Value))
            .ToDictionary(
                item => item.Key.Trim(),
                item => item.Value,
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
        campaign.Player.InitialSkillCooldownTurns =
            new Dictionary<string, int>(
                effective.ResolvedRoleInitialSkillCooldownTurns,
                StringComparer.OrdinalIgnoreCase);
        campaign.Player.NativeManagedSkillCooldownIds =
            effective.ResolvedRoleNativeManagedSkillCooldownIds.ToList();
        campaign.Player.RoleNativeScriptHash =
            effective.ResolvedRoleNativeScriptHash;
        campaign.Player.RoleFightScript = effective.ResolvedRoleFightScript;
        campaign.Player.RolePassiveContract =
            CombatRolePassiveContractProtocol.Create(effective);
        campaign.Player.RoleRuntimeForms = effective.ResolvedRoleRuntimeForms
            .Select(item => item.Clone())
            .ToList();
        campaign.Player.PersistentMaxHpAdjustment = 0;
        if (effective.ResolvedRoleMaximumHp > 0)
        {
            campaign.Player.MaxHp = effective.ResolvedRoleMaximumHp;
            campaign.Player.CurrentHp = effective.ResolvedRoleMaximumHp;
        }
        campaign.Player.Variables = new Dictionary<string, double>(
            effective.ResolvedRoleInitialVariables,
            StringComparer.OrdinalIgnoreCase);
        campaign.Player.InitialStatuses = string.IsNullOrWhiteSpace(
                effective.ResolvedRoleFightScript)
            ? effective.ResolvedRoleInitialStatuses
                .Select(item => new CombatInitialStatus
                {
                    StatusId = item.Key,
                    Stacks = item.Value
                })
                .ToList()
            : new List<CombatInitialStatus>();
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
                "initialSkillCooldowns="
                + string.Join(
                    ",",
                    effective.ResolvedRoleInitialSkillCooldownTurns
                        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Key + ":" + item.Value)),
                "maximumHp=" + effective.ResolvedRoleMaximumHp.ToString(
                    CultureInfo.InvariantCulture),
                "roleVariables="
                + string.Join(
                    ",",
                    effective.ResolvedRoleInitialVariables
                        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Key + ":" + item.Value.ToString(
                            "R",
                            CultureInfo.InvariantCulture))),
                "roleStatuses="
                + string.Join(
                    ",",
                    effective.ResolvedRoleInitialStatuses
                        .OrderBy(
                            item => item.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Key + ":" + item.Value)),
                "roleNativeScriptHash=" + effective.ResolvedRoleNativeScriptHash,
                "nativeManagedSkillCooldowns="
                + JoinSorted(effective.ResolvedRoleNativeManagedSkillCooldownIds),
                "roleForms="
                + string.Join(
                    ";",
                    effective.ResolvedRoleRuntimeForms
                        .OrderBy(item => item.RoleId, StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.RoleId
                                        + ":" + item.MaximumHp
                                        + ":" + JoinSorted(item.SkillCardIds)
                                        + ":" + string.Join(
                                            ",",
                                            item.SkillCooldownTurns
                                                .OrderBy(
                                                    pair => pair.Key,
                                                    StringComparer.OrdinalIgnoreCase)
                                                .Select(pair => pair.Key
                                                                + "="
                                                                + pair.Value)))),
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

public static class CombatRolePassiveContractProtocol
{
    private static readonly Regex TriggerPattern = new(
        @"AddEvent\s*\(\s*[""'](?<id>[^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static CombatRolePassiveContract Create(
        CombatGameSubjectPreset preset)
    {
        if (preset == null) throw new ArgumentNullException(nameof(preset));
        var effective = preset.Clone();
        var contract = new CombatRolePassiveContract
        {
            RoleId = effective.RoleId,
            NativeScriptHash = effective.ResolvedRoleNativeScriptHash,
            MaximumHp = effective.ResolvedRoleMaximumHp,
            InitialStatuses = new Dictionary<string, int>(
                effective.ResolvedRoleInitialStatuses,
                StringComparer.OrdinalIgnoreCase),
            InitialVariables = new Dictionary<string, double>(
                effective.ResolvedRoleInitialVariables,
                StringComparer.OrdinalIgnoreCase),
            InitialSkillCooldownTurns = new Dictionary<string, int>(
                effective.ResolvedRoleInitialSkillCooldownTurns,
                StringComparer.OrdinalIgnoreCase),
            TriggerIds = TriggerPattern.Matches(effective.ResolvedRoleFightScript)
                .Cast<Match>()
                .Select(match => match.Groups["id"].Value.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            NativeManagedSkillCooldownIds = effective
                .ResolvedRoleNativeManagedSkillCooldownIds
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RuntimeFormIds = effective.ResolvedRoleRuntimeForms
                .Select(item => item.RoleId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        contract.ContractHash = ComputeHash(contract);
        return contract;
    }

    public static IReadOnlyList<string> ValidateBeforeRoleInitialization(
        CombatPlayerSetup player,
        CombatBattleState state)
    {
        if (player == null || state == null)
        {
            return new[] { "initialization-context-missing" };
        }
        var contract = player.RolePassiveContract;
        if (contract == null
            || !contract.AuditInitialization
            || string.IsNullOrWhiteSpace(contract.ContractHash))
        {
            return Array.Empty<string>();
        }
        var actor = state.Player;
        if (actor == null)
        {
            return new[] { "player-state-missing" };
        }
        var errors = new List<string>();
        if (!string.Equals(
                contract.RoleId,
                player.RoleId,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("role-id-mismatch");
        }
        if (!string.Equals(
                contract.NativeScriptHash,
                player.RoleNativeScriptHash,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("native-script-hash-mismatch");
        }
        var expectedMaximumHp = contract.MaximumHp
                                + player.PersistentMaxHpAdjustment;
        if (contract.MaximumHp > 0 && actor.MaxHp != expectedMaximumHp)
        {
            errors.Add(
                "maximum-hp:expected=" + expectedMaximumHp
                + ",actual=" + actor.MaxHp);
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateInitialized(
        CombatPlayerSetup player,
        CombatBattleState state)
    {
        if (player == null || state == null)
        {
            return new[] { "initialization-context-missing" };
        }
        var contract = player.RolePassiveContract;
        if (contract == null
            || !contract.AuditInitialization
            || string.IsNullOrWhiteSpace(contract.ContractHash))
        {
            return Array.Empty<string>();
        }
        var actor = state.Player;
        if (actor == null)
        {
            return new[] { "player-state-missing" };
        }
        var errors = new List<string>();
        if (!string.Equals(
                contract.RoleId,
                player.RoleId,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("role-id-mismatch");
        }
        if (!string.Equals(
                contract.NativeScriptHash,
                player.RoleNativeScriptHash,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("native-script-hash-mismatch");
        }
        foreach (var expected in contract.InitialStatuses
                                 ?? new Dictionary<string, int>())
        {
            var actual = actor.Statuses
                .Where(item => string.Equals(
                    item.StatusId,
                    expected.Key,
                    StringComparison.OrdinalIgnoreCase))
                .Sum(item => Math.Max(0, item.Stacks));
            if (actual < expected.Value)
            {
                errors.Add(
                    "status:" + expected.Key
                    + ":expected=" + expected.Value
                    + ",actual=" + actual);
            }
        }
        foreach (var expected in contract.InitialVariables
                                 ?? new Dictionary<string, double>())
        {
            if (!actor.Variables.TryGetValue(expected.Key, out var actual)
                || Math.Abs(actual - expected.Value) > 0.000001d)
            {
                errors.Add(
                    "variable:" + expected.Key
                    + ":expected=" + expected.Value.ToString(
                        "R",
                        CultureInfo.InvariantCulture)
                    + ",actual=" + (actor.Variables.TryGetValue(
                        expected.Key,
                        out actual)
                            ? actual.ToString("R", CultureInfo.InvariantCulture)
                            : "missing"));
            }
        }
        foreach (var expected in contract.InitialSkillCooldownTurns
                                 ?? new Dictionary<string, int>())
        {
            var instanceIds = state.SkillCards.Where(instanceId =>
                state.Cards.Any(card => card.InstanceId == instanceId
                                        && string.Equals(
                                            card.CardId,
                                            expected.Key,
                                            StringComparison.OrdinalIgnoreCase)));
            foreach (var instanceId in instanceIds)
            {
                var actual = state.SkillCooldowns.TryGetValue(
                    instanceId,
                    out var cooldown)
                    ? cooldown
                    : 0;
                if (actual != expected.Value)
                {
                    errors.Add(
                        "skill-cooldown:" + expected.Key
                        + ":expected=" + expected.Value
                        + ",actual=" + actual);
                }
            }
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateAfterRoleInitialization(
        CombatPlayerSetup player,
        CombatBattleState state)
    {
        var errors = ValidateInitialized(player, state).ToList();
        if (player == null || state == null)
        {
            return errors;
        }
        var contract = player.RolePassiveContract;
        var actor = state.Player;
        if (contract == null
            || actor == null
            || !contract.AuditInitialization
            || string.IsNullOrWhiteSpace(contract.ContractHash))
        {
            return errors;
        }
        var expectedMaximumHp = contract.MaximumHp
                                + player.PersistentMaxHpAdjustment;
        if (contract.MaximumHp > 0 && actor.MaxHp != expectedMaximumHp)
        {
            errors.Add(
                "maximum-hp:expected=" + expectedMaximumHp
                + ",actual=" + actor.MaxHp);
        }
        return errors;
    }

    private static string ComputeHash(CombatRolePassiveContract contract)
    {
        var canonical = string.Join("\n", new[]
        {
            "schema=" + CombatRolePassiveContract.CurrentSchemaVersion,
            "role=" + contract.RoleId,
            "native=" + contract.NativeScriptHash,
            "maximumHp=" + contract.MaximumHp,
            "statuses=" + Join(contract.InitialStatuses),
            "variables=" + string.Join(
                ",",
                contract.InitialVariables
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Key + ":" + item.Value.ToString(
                        "R",
                        CultureInfo.InvariantCulture))),
            "cooldowns=" + Join(contract.InitialSkillCooldownTurns),
            "triggers=" + string.Join(",", contract.TriggerIds),
            "nativeCooldowns=" + string.Join(",", contract.NativeManagedSkillCooldownIds),
            "forms=" + string.Join(",", contract.RuntimeFormIds)
        });
        using var sha = SHA256.Create();
        return BitConverter.ToString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private static string Join<T>(IReadOnlyDictionary<string, T> values)
    {
        return string.Join(
            ",",
            (values ?? new Dictionary<string, T>())
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Key + ":" + item.Value));
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
            preset.ResolvedRoleInitialSkillCooldownTurns =
                new Dictionary<string, int>(
                    role.InitialSkillCooldownTurns,
                    StringComparer.OrdinalIgnoreCase);
            preset.ResolvedRoleMaximumHp = role.MaximumHp;
            preset.ResolvedRoleInitialVariables =
                new Dictionary<string, double>(
                    role.InitialVariables,
                    StringComparer.OrdinalIgnoreCase);
            preset.ResolvedRoleInitialStatuses =
                new Dictionary<string, int>(
                    role.InitialStatuses,
                    StringComparer.OrdinalIgnoreCase);
            preset.ResolvedRoleNativeScriptHash = role.NativeScriptHash;
            preset.ResolvedRoleFightScript = role.FightScript;
            preset.ResolvedRoleNativeManagedSkillCooldownIds =
                role.NativeManagedSkillCooldownIds.ToList();
            preset.ResolvedRoleRuntimeForms = new[] { role.Id }
                .Concat(role.TransformRoleIds)
                .Select(id => Roles.FirstOrDefault(item => string.Equals(
                    item.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase)))
                .Where(item => item != null)
                .Select(item => new CombatRoleRuntimeForm
                {
                    RoleId = item!.Id,
                    MaximumHp = item.MaximumHp,
                    SkillCardIds = item.SkillCardIds.ToList(),
                    SkillCooldownTurns = new Dictionary<string, int>(
                        item.SkillCooldownTurns,
                        StringComparer.OrdinalIgnoreCase)
                })
                .ToList();
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

    public Dictionary<string, int> InitialSkillCooldownTurns { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int MaximumHp { get; set; }

    public Dictionary<string, double> InitialVariables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> InitialStatuses { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string NativeScriptHash { get; set; } = "";

    public string FightScript { get; set; } = "";

    public List<string> NativeManagedSkillCooldownIds { get; set; } = new();

    public List<string> TransformRoleIds { get; set; } = new();

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
        InitialSkillCooldownTurns = NormalizeValues(InitialSkillCooldownTurns);
        MaximumHp = Math.Max(0, Math.Min(1000000, MaximumHp));
        InitialVariables = (InitialVariables ?? new Dictionary<string, double>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && !double.IsNaN(item.Value)
                           && !double.IsInfinity(item.Value))
            .ToDictionary(
                item => item.Key.Trim(),
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
        InitialStatuses = NormalizeValues(InitialStatuses);
        NativeScriptHash = (NativeScriptHash ?? "").Trim();
        FightScript = FightScript ?? "";
        NativeManagedSkillCooldownIds = NormalizeIds(
            NativeManagedSkillCooldownIds);
        TransformRoleIds = NormalizeIds(TransformRoleIds);
        return this;
    }

    private static List<string> NormalizeIds(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
