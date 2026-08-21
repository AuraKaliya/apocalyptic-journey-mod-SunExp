using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsSkinSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 4;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("autoInstallBundledSkins")]
    public bool AutoInstallBundledSkins { get; set; } = true;

    [JsonProperty("showEntrySkinButton")]
    public bool ShowEntrySkinButton { get; set; } = true;

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; } = true;

    [JsonProperty("candidateSelectionConfigured")]
    public bool CandidateSelectionConfigured { get; set; }

    [JsonProperty("enabledSkinIds")]
    public List<string> EnabledSkinIds { get; set; } = new();

    [JsonProperty("selectionSchemaVersion")]
    public int SelectionSchemaVersion { get; set; }

    [JsonProperty("resourceOverrides")]
    public Dictionary<string, bool> ResourceOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        SchemaVersion = Math.Max(4, SchemaVersion);
        AutoInstallBundledSkins = true;
        EnabledSkinIds = (EnabledSkinIds ?? new List<string>())
            .Select(value => MigrateQualifiedSkinId((value ?? "").Trim()))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        ResourceOverrides = (ResourceOverrides ?? new Dictionary<string, bool>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => MigrateQualifiedSkinId(pair.Key.Trim()), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsCandidateEnabled(string qualifiedSkinId)
    {
        var id = (qualifiedSkinId ?? "").Trim();
        return id.Length == 0
               || !ResourceOverrides.TryGetValue(id, out var enabled)
               || enabled;
    }

    public void SetCandidateEnabled(string qualifiedSkinId, bool enabled, IEnumerable<string> currentCandidateIds)
    {
        var id = (qualifiedSkinId ?? "").Trim();
        if (id.Length == 0)
        {
            return;
        }

        ResourceOverrides[id] = enabled;
        SelectionSchemaVersion = 4;
    }

    public bool MigrateLegacyCandidateSelection(IEnumerable<string> currentCandidateIds)
    {
        if (!CandidateSelectionConfigured && SelectionSchemaVersion >= 4)
        {
            return false;
        }

        if (CandidateSelectionConfigured)
        {
            var legacyEnabled = new HashSet<string>(EnabledSkinIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in currentCandidateIds ?? Array.Empty<string>())
            {
                var id = (candidate ?? "").Trim();
                if (id.Length > 0 && !ResourceOverrides.ContainsKey(id))
                {
                    ResourceOverrides[id] = legacyEnabled.Contains(id);
                }
            }
        }

        CandidateSelectionConfigured = false;
        (EnabledSkinIds ??= new List<string>()).Clear();
        SelectionSchemaVersion = 4;
        return true;
    }

    private static string MigrateQualifiedSkinId(string value)
    {
        if (string.Equals(value, "Terrias:Terrias_wuna_wuna:Terrias.Terrias_wuna_wuna.summer_cool", StringComparison.OrdinalIgnoreCase))
            return "AuraToolsExp:Terrias_wuna_wuna:AuraToolsExp.Terrias_wuna_wuna.summer_cool";
        if (string.Equals(value, "Terrias:Terrias_columbina_columbina:Terrias.Terrias_columbina_columbina.restore_colors", StringComparison.OrdinalIgnoreCase))
            return "AuraToolsExp:Terrias_columbina_columbina:AuraToolsExp.Terrias_columbina_columbina.restore_colors";
        return value;
    }
}
