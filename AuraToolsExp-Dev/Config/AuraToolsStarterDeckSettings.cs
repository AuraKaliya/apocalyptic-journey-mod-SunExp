using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class StarterDeckSettings
{
    public const int CurrentSchemaVersion = 2;
    public const int MaximumCardCount = 15;
    public const int MaximumRelicCount = 6;

    private int legacyDeckSize = 11;
    private List<string> legacyCardIds = new();
    private bool hasLegacyCardIds;
    private int schemaVersion = CurrentSchemaVersion;
    private bool schemaVersionRead;
    private bool legacyShapeRead;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion
    {
        get => schemaVersion;
        set
        {
            schemaVersion = value;
            schemaVersionRead = true;
        }
    }

    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("mode")]
    public string Mode { get; set; } = StarterDeckModes.Global;

    [JsonProperty("globalProfile")]
    public StarterDeckLocalProfileSettings GlobalProfile { get; set; } = StarterDeckLocalProfileSettings.CreateGlobal();

    [JsonProperty("roles")]
    public Dictionary<string, StarterDeckLocalProfileSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Legacy aggregate fields are read for migration but are no longer written.
    [JsonProperty("deckSize")]
    private int LegacyDeckSize
    {
        set
        {
            legacyDeckSize = value;
            legacyShapeRead = true;
        }
    }

    [JsonProperty("cardIds")]
    private List<string>? LegacyCardIds
    {
        set
        {
            legacyCardIds = value ?? new List<string>();
            hasLegacyCardIds = true;
            legacyShapeRead = true;
        }
    }

    [JsonProperty("preferRoleModProfile")]
    private bool LegacyPreferRoleModProfile
    {
        set => legacyShapeRead = true;
    }

    [JsonProperty("selectedProfileByRole")]
    private Dictionary<string, string>? LegacySelectedProfileByRole
    {
        set => legacyShapeRead = true;
    }

    public void Normalize()
    {
        GlobalProfile ??= StarterDeckLocalProfileSettings.CreateGlobal();
        Roles ??= new Dictionary<string, StarterDeckLocalProfileSettings>(StringComparer.OrdinalIgnoreCase);
        var loadedSchemaVersion = schemaVersionRead
            ? SchemaVersion
            : legacyShapeRead || GlobalProfile.LegacyShapeRead || Roles.Values.Any(role => role?.LegacyShapeRead == true)
                ? 1
                : CurrentSchemaVersion;
        Mode = StarterDeckModes.Normalize(Mode);
        if (GlobalProfile.CardIds.Count == 0 && hasLegacyCardIds && legacyCardIds.Count > 0)
        {
            GlobalProfile.CardIds = legacyCardIds.ToList();
        }

        GlobalProfile.InheritCards = false;
        GlobalProfile.InheritRelics = false;
        GlobalProfile.Normalize("", "全局自定义开局");

        var normalizedRoles = new Dictionary<string, StarterDeckLocalProfileSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Roles)
        {
            var role = pair.Value ?? StarterDeckLocalProfileSettings.CreateRole(pair.Key, pair.Key);
            if (loadedSchemaVersion < CurrentSchemaVersion)
            {
                role.InheritCards = role.CardIds.Count == 0;
                role.InheritRelics = true;
            }

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
        SchemaVersion = CurrentSchemaVersion;
        _ = legacyDeckSize;
    }

    public CustomStartEffectiveSettings ResolveEffective(string roleId)
    {
        Normalize();
        var result = new CustomStartEffectiveSettings
        {
            CardIds = GlobalProfile.CardIds.ToList(),
            RelicIds = GlobalProfile.RelicIds.ToList()
        };
        if (Mode != StarterDeckModes.RoleSpecific
            || string.IsNullOrWhiteSpace(roleId)
            || !Roles.TryGetValue(roleId, out var role))
        {
            return result;
        }

        if (!role.InheritCards)
        {
            result.CardIds = role.CardIds.ToList();
            result.CardSource = "role";
        }

        if (!role.InheritRelics)
        {
            result.RelicIds = role.RelicIds.ToList();
            result.RelicSource = "role";
        }

        return result;
    }
}

public sealed class CustomStartEffectiveSettings
{
    public List<string> CardIds { get; set; } = new();

    public List<string> RelicIds { get; set; } = new();

    public string CardSource { get; set; } = "global";

    public string RelicSource { get; set; } = "global";
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
    [JsonIgnore]
    internal bool LegacyShapeRead { get; private set; }

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("inheritCards")]
    public bool InheritCards { get; set; }

    [JsonProperty("inheritRelics")]
    public bool InheritRelics { get; set; }

    [JsonProperty("cardIds")]
    public List<string> CardIds { get; set; } = new();

    [JsonProperty("relicIds")]
    public List<string> RelicIds { get; set; } = new();

    [JsonProperty("enabled")]
    private bool LegacyEnabled
    {
        set => LegacyShapeRead = true;
    }

    [JsonProperty("deckSize")]
    private int LegacyDeckSize
    {
        set => LegacyShapeRead = true;
    }

    [JsonProperty("derivedFromProfileId")]
    private string? LegacyDerivedFromProfileId
    {
        set => LegacyShapeRead = true;
    }

    public static StarterDeckLocalProfileSettings CreateGlobal()
    {
        return new StarterDeckLocalProfileSettings
        {
            RoleId = "",
            DisplayName = "全局自定义开局",
            InheritCards = false,
            InheritRelics = false
        };
    }

    public static StarterDeckLocalProfileSettings CreateRole(string roleId, string displayName)
    {
        return new StarterDeckLocalProfileSettings
        {
            RoleId = RoleCatalog.NormalizeRoleId(roleId),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? roleId : displayName.Trim(),
            InheritCards = true,
            InheritRelics = true
        };
    }

    public StarterDeckLocalProfileSettings Clone()
    {
        return new StarterDeckLocalProfileSettings
        {
            RoleId = RoleId,
            DisplayName = DisplayName,
            InheritCards = InheritCards,
            InheritRelics = InheritRelics,
            CardIds = CardIds.ToList(),
            RelicIds = RelicIds.ToList()
        };
    }

    public void Normalize(string fallbackRoleId, string fallbackDisplayName = "")
    {
        RoleId = RoleCatalog.NormalizeRoleId(string.IsNullOrWhiteSpace(RoleId) ? fallbackRoleId : RoleId);
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? fallbackDisplayName : DisplayName.Trim();
        CardIds = NormalizeIds(CardIds, StarterDeckSettings.MaximumCardCount, preserveDuplicates: true);
        RelicIds = NormalizeIds(RelicIds, StarterDeckSettings.MaximumRelicCount, preserveDuplicates: false);
    }

    private static List<string> NormalizeIds(IEnumerable<string>? values, int maximum, bool preserveDuplicates)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values ?? Array.Empty<string>())
        {
            var value = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value) || (!preserveDuplicates && !seen.Add(value)))
            {
                continue;
            }

            result.Add(value);
            if (result.Count >= maximum)
            {
                break;
            }
        }

        return result;
    }
}
