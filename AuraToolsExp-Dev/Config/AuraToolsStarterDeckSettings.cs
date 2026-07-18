using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class StarterDeckSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("mode")]
    public string Mode { get; set; } = StarterDeckModes.Global;

    [JsonProperty("preferRoleModProfile")]
    public bool PreferRoleModProfile { get; set; } = true;

    [JsonProperty("globalProfile")]
    public StarterDeckLocalProfileSettings GlobalProfile { get; set; } = StarterDeckLocalProfileSettings.CreateGlobal();

    [JsonProperty("roles")]
    public Dictionary<string, StarterDeckLocalProfileSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("selectedProfileByRole")]
    public Dictionary<string, string> SelectedProfileByRole { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("deckSize")]
    public int DeckSize { get; set; } = 11;

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    public void Normalize()
    {
        Mode = StarterDeckModes.Normalize(Mode);
        PreferRoleModProfile = true;
        DeckSize = Math.Max(1, DeckSize);
        CardIds ??= new List<string>();
        CardIds.RemoveAll(string.IsNullOrWhiteSpace);

        GlobalProfile ??= StarterDeckLocalProfileSettings.CreateGlobal();
        if (GlobalProfile.CardIds.Count == 0 && CardIds.Count > 0)
        {
            GlobalProfile.DeckSize = DeckSize;
            GlobalProfile.CardIds = CardIds.ToList();
        }

        GlobalProfile.Normalize("", "全局自定义卡组");
        Roles ??= new Dictionary<string, StarterDeckLocalProfileSettings>(StringComparer.OrdinalIgnoreCase);
        var normalizedRoles = new Dictionary<string, StarterDeckLocalProfileSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Roles)
        {
            var role = pair.Value ?? new StarterDeckLocalProfileSettings();
            role.Normalize(pair.Key, pair.Key);
            var normalizedKey = RoleCatalog.NormalizeRoleId(role.RoleId);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                normalizedKey = RoleCatalog.NormalizeRoleId(pair.Key);
            }

            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                continue;
            }

            role.RoleId = normalizedKey;
            normalizedRoles[normalizedKey] = role;
        }

        Roles = normalizedRoles;
        var normalizedSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in SelectedProfileByRole ?? new Dictionary<string, string>())
        {
            var roleId = RoleCatalog.NormalizeRoleId(pair.Key);
            var profileId = pair.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(profileId))
            {
                continue;
            }

            normalizedSelections[roleId] = profileId;
        }

        SelectedProfileByRole = normalizedSelections;

        DeckSize = GlobalProfile.DeckSize;
        CardIds = GlobalProfile.CardIds.ToList();
    }
}

public static class StarterDeckModes
{
    public const string Global = "Global";
    public const string RoleSpecific = "RoleSpecific";

    public static string Normalize(string? value)
    {
        return string.Equals(value, RoleSpecific, StringComparison.OrdinalIgnoreCase) ? RoleSpecific : Global;
    }
}

public sealed class StarterDeckLocalProfileSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("deckSize")]
    public int DeckSize { get; set; } = 11;

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    [JsonProperty("derivedFromProfileId")]
    public string DerivedFromProfileId { get; set; } = "";

    public static StarterDeckLocalProfileSettings CreateGlobal()
    {
        return new StarterDeckLocalProfileSettings
        {
            RoleId = "",
            DisplayName = "全局自定义卡组",
            DeckSize = 11
        };
    }

    public void Normalize(string fallbackRoleId, string fallbackDisplayName = "")
    {
        RoleId = RoleCatalog.NormalizeRoleId(string.IsNullOrWhiteSpace(RoleId) ? fallbackRoleId : RoleId);
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? fallbackDisplayName : DisplayName.Trim();
        DerivedFromProfileId = DerivedFromProfileId?.Trim() ?? "";
        DeckSize = Math.Max(1, DeckSize);
        CardIds ??= new List<string>();
        CardIds.RemoveAll(string.IsNullOrWhiteSpace);
        for (var i = 0; i < CardIds.Count; i++)
        {
            CardIds[i] = CardIds[i].Trim();
        }
    }
}
