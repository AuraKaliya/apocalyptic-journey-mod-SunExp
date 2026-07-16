using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuraMode.Shared;

public static class AuraModeConstants
{
    public const string SystemName = "Mode";
    public const int DefinitionSchemaVersion = 1;
    public const int ActiveSnapshotSchemaVersion = 1;
    public const string RuntimeAuthorityId = "AuraMode.Runtime";
}

public static class AuraModeStates
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
}

public static class AuraModeStarterDeckAuthorities
{
    public const string InheritHost = "InheritHost";
    public const string ModeOwnerExclusive = "ModeOwnerExclusive";
    public const string OfficialOnly = "OfficialOnly";
}

public static class AuraModeCombatContracts
{
    public const string InheritHost = "InheritHost";
    public const string NativeCombatV1 = "Aura:NativeCombatV1";
}

[Serializable]
public sealed class AuraModeDisplay
{
    [JsonProperty("nameKey")]
    public string NameKey { get; set; } = "";

    [JsonProperty("fallbackName")]
    public string FallbackName { get; set; } = "";
}

[Serializable]
public sealed class AuraModeHost
{
    [JsonProperty("nativeModeType")]
    public string NativeModeType { get; set; } = "";

    [JsonProperty("runtimeManagerHint")]
    public string RuntimeManagerHint { get; set; } = "";
}

[Serializable]
public sealed class AuraModeStarterDeckPolicy
{
    [JsonProperty("mutationAuthority")]
    public string MutationAuthority { get; set; } = AuraModeStarterDeckAuthorities.InheritHost;

    [JsonProperty("providerId")]
    public string ProviderId { get; set; } = "";
}

[Serializable]
public sealed class AuraModePolicies
{
    [JsonProperty("starterDeck")]
    public AuraModeStarterDeckPolicy StarterDeck { get; set; } = new();
}

[Serializable]
public sealed class AuraModeCapabilities
{
    [JsonProperty("combatContractId")]
    public string CombatContractId { get; set; } = AuraModeCombatContracts.InheritHost;
}

[Serializable]
public sealed class AuraModeDefinition
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraModeConstants.DefinitionSchemaVersion;

    [JsonProperty("modeId")]
    public string ModeId { get; set; } = "";

    [JsonProperty("aliases")]
    public List<string> Aliases { get; set; } = new();

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("display")]
    public AuraModeDisplay Display { get; set; } = new();

    [JsonProperty("host")]
    public AuraModeHost Host { get; set; } = new();

    [JsonProperty("journeyId")]
    public string JourneyId { get; set; } = "";

    [JsonProperty("defaultPolicies")]
    public AuraModePolicies DefaultPolicies { get; set; } = new();

    [JsonProperty("capabilities")]
    public AuraModeCapabilities Capabilities { get; set; } = new();

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

[Serializable]
public sealed class AuraModeRunBinding
{
    [JsonProperty("runId")]
    public string RunId { get; set; } = "";

    [JsonProperty("saveSlotId")]
    public string SaveSlotId { get; set; } = "";

    [JsonProperty("startedUtc")]
    public string StartedUtc { get; set; } = "";
}

[Serializable]
public sealed class AuraActiveModeSnapshot
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraModeConstants.ActiveSnapshotSchemaVersion;

    [JsonProperty("status")]
    public string Status { get; set; } = AuraModeStates.Inactive;

    [JsonProperty("modeId")]
    public string ModeId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("run")]
    public AuraModeRunBinding Run { get; set; } = new();

    [JsonProperty("definitionRevision")]
    public long DefinitionRevision { get; set; }

    [JsonProperty("display")]
    public AuraModeDisplay Display { get; set; } = new();

    [JsonProperty("host")]
    public AuraModeHost Host { get; set; } = new();

    [JsonProperty("journeyId")]
    public string JourneyId { get; set; } = "";

    [JsonProperty("resolvedPolicies")]
    public AuraModePolicies ResolvedPolicies { get; set; } = new();

    [JsonProperty("capabilities")]
    public AuraModeCapabilities Capabilities { get; set; } = new();

    [JsonProperty("authorityId")]
    public string AuthorityId { get; set; } = "";

    [JsonProperty("sequence")]
    public long Sequence { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; } = "";

    [JsonIgnore]
    public bool IsActive => string.Equals(Status, AuraModeStates.Active, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(ModeId);
}

public sealed class AuraModeTransitionResult
{
    public bool Success { get; set; }

    public bool Applied { get; set; }

    public bool Conflict { get; set; }

    public long Revision { get; set; }

    public string Message { get; set; } = "";

    public AuraActiveModeSnapshot Snapshot { get; set; } = new();
}

public sealed class AuraModePolicyDecision
{
    public bool Allowed { get; set; }

    public string PolicyId { get; set; } = "";

    public string AuthorityProviderId { get; set; } = "";

    public string Reason { get; set; } = "";
}
