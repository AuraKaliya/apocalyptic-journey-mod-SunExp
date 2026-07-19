using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class FeastSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("playCg")]
    public bool PlayCg { get; set; } = true;

    [JsonProperty("maxBatchCount")]
    public int MaxBatchCount { get; set; } = 64;

    [JsonProperty("defaultPresentation")]
    public SkillCgPresentationSettings DefaultPresentation { get; set; } = CreateDefaultPresentation();

    [JsonProperty("roles")]
    public Dictionary<string, FeastRoleSettings> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
        PlayCg = true;
        MaxBatchCount = Math.Max(1, Math.Min(128, MaxBatchCount));
        DefaultPresentation = (DefaultPresentation ?? CreateDefaultPresentation()).Resolve(CreateDefaultPresentation());
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

    [JsonProperty("selectedCgId")]
    public string SelectedCgId { get; set; } = "";

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
        SelectedCgId = (SelectedCgId ?? "").Trim();
        LocalCgId = (LocalCgId ?? "").Trim();
        LocalResource = (LocalResource ?? "").Trim().Replace('\\', '/').TrimStart('/');
        LocalSeedHash = (LocalSeedHash ?? "").Trim();
        LocalContentHash = (LocalContentHash ?? "").Trim();
        LastSeenRoleRevision = Math.Max(0, LastSeenRoleRevision);
        Presentation = (Presentation ?? SkillCgPresentationSettings.CreateInherited()).Resolve(fallbackPresentation);
        EffectivePresentation = Presentation;
    }
}
