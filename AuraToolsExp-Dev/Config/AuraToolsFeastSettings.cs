using System;
using System.Collections.Generic;
using System.Linq;
using AuraCg.Shared;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class FeastSettings
{
    public const int CurrentSchemaVersion = 2;

    private int schemaVersion = CurrentSchemaVersion;
    private bool schemaVersionRead;
    private bool legacyShapeRead;
    private bool? legacyPlayCg;
    private SkillCgPresentationSettings? legacyDefaultPresentation;
    private Dictionary<string, FeastRoleSettings>? legacyRoles;

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
    public bool Enabled { get; set; } = true;

    [JsonProperty("maxBatchCount")]
    public int MaxBatchCount { get; set; } = 64;

    [JsonProperty("cg")]
    public FeastCgSettings Cg { get; set; } = new();

    [JsonIgnore]
    public bool IsCgEffective => Enabled && Cg.Enabled;

    [JsonProperty("playCg")]
    private bool LegacyPlayCg
    {
        set
        {
            legacyPlayCg = value;
            legacyShapeRead = true;
        }
    }

    [JsonProperty("defaultPresentation")]
    private SkillCgPresentationSettings? LegacyDefaultPresentation
    {
        set
        {
            legacyDefaultPresentation = value;
            legacyShapeRead = true;
        }
    }

    [JsonProperty("roles")]
    private Dictionary<string, FeastRoleSettings>? LegacyRoles
    {
        set
        {
            legacyRoles = value;
            legacyShapeRead = true;
        }
    }

    public static SkillCgPresentationSettings CreateDefaultPresentation()
    {
        return new SkillCgPresentationSettings
        {
            Mode = SkillCgPresentationModeNames.FullscreenFade,
            Fit = SkillCgFitModeNames.Cover,
            FadeIn = 0.35f,
            Hold = 1.5f,
            FadeOut = 0.5f,
            FocusX = 0.5f,
            FocusY = 0.5f,
            SafeScale = 1f
        };
    }

    public void Normalize()
    {
        var loadedSchemaVersion = schemaVersionRead
            ? SchemaVersion
            : legacyShapeRead ? 1 : CurrentSchemaVersion;
        Cg ??= new FeastCgSettings();
        if (loadedSchemaVersion < CurrentSchemaVersion || legacyShapeRead)
        {
            Cg.Enabled = legacyPlayCg ?? true;
            if (legacyDefaultPresentation != null)
            {
                Cg.DefaultPresentation = legacyDefaultPresentation;
            }
            if (legacyRoles != null)
            {
                Cg.Roles = legacyRoles;
            }
            legacyPlayCg = null;
            legacyDefaultPresentation = null;
            legacyRoles = null;
            legacyShapeRead = false;
        }

        MaxBatchCount = Math.Max(1, Math.Min(128, MaxBatchCount));
        Cg.Normalize();
        SchemaVersion = CurrentSchemaVersion;
    }
}

public sealed class FeastCgSettings
{
    public const int CurrentSchemaVersion = 1;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("defaultPresentation")]
    public SkillCgPresentationSettings DefaultPresentation { get; set; } = FeastSettings.CreateDefaultPresentation();

    [JsonProperty("roles")]
    public Dictionary<string, FeastRoleSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        SchemaVersion = Math.Max(CurrentSchemaVersion, SchemaVersion);
        DefaultPresentation = (DefaultPresentation ?? FeastSettings.CreateDefaultPresentation())
            .Resolve(FeastSettings.CreateDefaultPresentation());
        Roles ??= new Dictionary<string, FeastRoleSettings>(StringComparer.OrdinalIgnoreCase);

        var normalizedRoles = new Dictionary<string, FeastRoleSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Roles)
        {
            var role = pair.Value ?? new FeastRoleSettings();
            role.Normalize(pair.Key, DefaultPresentation);
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
    }
}

public sealed class FeastRoleSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("candidateSelectionConfigured")]
    public bool CandidateSelectionConfigured { get; set; }

    [JsonProperty("enabledCgIds")]
    public List<string> EnabledCgIds { get; set; } = new();

    [JsonProperty("selectionSchemaVersion")]
    public int SelectionSchemaVersion { get; set; }

    [JsonProperty("resourceOverrides")]
    public Dictionary<string, bool> ResourceOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("manualResources")]
    public List<FeastManualResourceSettings> ManualResources { get; set; } = new();

    [JsonProperty("selectionMode")]
    public string SelectionMode { get; set; } = AuraCgSelectionModes.Priority;

    [JsonProperty("selectedCgId", NullValueHandling = NullValueHandling.Ignore)]
    public string? LegacySelectedCgId { get; set; }

    [JsonProperty("active")]
    public bool Active { get; set; } = true;

    [JsonProperty("localCgId")]
    public string LocalCgId { get; set; } = "";

    [JsonProperty("localResource")]
    public string LocalResource { get; set; } = "";

    [JsonProperty("localSeedHash")]
    public string LocalSeedHash { get; set; } = "";

    [JsonProperty("localContentHash")]
    public string LocalContentHash { get; set; } = "";

    [JsonProperty("localCustomized")]
    public bool LocalCustomized { get; set; }

    [JsonProperty("lastSeenRoleRevision")]
    public long LastSeenRoleRevision { get; set; }

    [JsonProperty("presentation")]
    public SkillCgPresentationSettings Presentation { get; set; } = SkillCgPresentationSettings.CreateInherited();

    [JsonIgnore]
    public SkillCgPresentationSettings EffectivePresentation { get; private set; } = FeastSettings.CreateDefaultPresentation();

    public void Normalize(string fallbackRoleId, SkillCgPresentationSettings fallbackPresentation)
    {
        RoleId = RoleCatalog.NormalizeRoleId(string.IsNullOrWhiteSpace(RoleId) ? fallbackRoleId : RoleId);
        DisplayName = DisplayName?.Trim() ?? "";
        var legacySelectedCgId = (LegacySelectedCgId ?? "").Trim();
        EnabledCgIds = (EnabledCgIds ?? new List<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!CandidateSelectionConfigured && !string.IsNullOrWhiteSpace(legacySelectedCgId))
        {
            EnabledCgIds = new List<string> { legacySelectedCgId };
            CandidateSelectionConfigured = true;
        }
        LegacySelectedCgId = null;
        ResourceOverrides = (ResourceOverrides ?? new Dictionary<string, bool>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        ManualResources ??= new List<FeastManualResourceSettings>();
        foreach (var manual in ManualResources)
        {
            manual?.Normalize();
        }
        ManualResources = ManualResources
            .Where(manual => manual != null
                             && !string.IsNullOrWhiteSpace(manual.ManualId)
                             && !string.IsNullOrWhiteSpace(manual.Resource))
            .GroupBy(manual => manual.ManualId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        if (ManualResources.Count == 0
            && LocalCustomized
            && !string.IsNullOrWhiteSpace(LocalResource))
        {
            ManualResources.Add(new FeastManualResourceSettings
            {
                ManualId = "legacy-local",
                DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? "人工配置" : DisplayName + " - 人工配置",
                Resource = LocalResource,
                SeedHash = LocalSeedHash,
                ContentHash = LocalContentHash,
                Priority = 1000
            });
        }
        if (LocalCustomized)
        {
            LocalCustomized = false;
            LocalCgId = "";
            LocalResource = "";
            LocalSeedHash = "";
            LocalContentHash = "";
        }
        SelectionMode = AuraCgSelectionModes.Normalize(SelectionMode);
        LocalCgId = (LocalCgId ?? "").Trim();
        LocalResource = (LocalResource ?? "").Trim().Replace('\\', '/').TrimStart('/');
        LocalSeedHash = (LocalSeedHash ?? "").Trim();
        LocalContentHash = (LocalContentHash ?? "").Trim();
        LastSeenRoleRevision = Math.Max(0, LastSeenRoleRevision);
        Presentation = (Presentation ?? SkillCgPresentationSettings.CreateInherited()).Resolve(fallbackPresentation);
        EffectivePresentation = Presentation;
    }

    public bool IsCandidateEnabled(string qualifiedCgId)
    {
        var id = (qualifiedCgId ?? "").Trim();
        return id.Length == 0
               || !ResourceOverrides.TryGetValue(id, out var enabled)
               || enabled;
    }

    public void SetCandidateEnabled(string qualifiedCgId, bool enabled, IEnumerable<string> currentCandidates)
    {
        var id = (qualifiedCgId ?? "").Trim();
        if (id.Length == 0)
        {
            return;
        }

        ResourceOverrides[id] = enabled;
        SelectionSchemaVersion = 2;
    }

    public bool MigrateLegacyCandidateSelection(IEnumerable<string> currentCandidates)
    {
        if (!CandidateSelectionConfigured && SelectionSchemaVersion >= 2)
        {
            return false;
        }

        if (CandidateSelectionConfigured)
        {
            var legacyEnabled = new HashSet<string>(EnabledCgIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in currentCandidates ?? Array.Empty<string>())
            {
                var id = (candidate ?? "").Trim();
                if (id.Length > 0 && !ResourceOverrides.ContainsKey(id))
                {
                    ResourceOverrides[id] = legacyEnabled.Contains(id);
                }
            }
        }

        CandidateSelectionConfigured = false;
        (EnabledCgIds ??= new List<string>()).Clear();
        SelectionSchemaVersion = 2;
        return true;
    }
}

public sealed class FeastManualResourceSettings
{
    [JsonProperty("manualId")]
    public string ManualId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("resource")]
    public string Resource { get; set; } = "";

    [JsonProperty("seedHash")]
    public string SeedHash { get; set; } = "";

    [JsonProperty("contentHash")]
    public string ContentHash { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; } = 1000;

    public void Normalize()
    {
        ManualId = (ManualId ?? "").Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? "人工配置" : DisplayName.Trim();
        Resource = (Resource ?? "").Trim().Replace('\\', '/').TrimStart('/');
        SeedHash = (SeedHash ?? "").Trim();
        ContentHash = (ContentHash ?? "").Trim();
    }
}
