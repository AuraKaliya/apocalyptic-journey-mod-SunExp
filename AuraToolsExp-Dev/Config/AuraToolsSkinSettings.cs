using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsSkinSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

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

    public void Normalize()
    {
        SchemaVersion = Math.Max(2, SchemaVersion);
        AutoInstallBundledSkins = true;
        EnabledSkinIds = (EnabledSkinIds ?? new List<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsCandidateEnabled(string qualifiedSkinId)
    {
        return !CandidateSelectionConfigured
               || EnabledSkinIds.Contains((qualifiedSkinId ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public void SetCandidateEnabled(string qualifiedSkinId, bool enabled, IEnumerable<string> currentCandidateIds)
    {
        var id = (qualifiedSkinId ?? "").Trim();
        if (id.Length == 0)
        {
            return;
        }

        if (!CandidateSelectionConfigured)
        {
            EnabledSkinIds = (currentCandidateIds ?? Array.Empty<string>())
                .Select(value => (value ?? "").Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            CandidateSelectionConfigured = true;
        }

        EnabledSkinIds.RemoveAll(value => string.Equals(value, id, StringComparison.OrdinalIgnoreCase));
        if (enabled)
        {
            EnabledSkinIds.Add(id);
        }
    }
}
