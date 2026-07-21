using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraShared.Core;

public static class AuraSharedResourceProtocolVersions
{
    // Resource schema v4 remains the manifest boundary. Runtime protocol 6
    // adds bounded physical paths, canonical relocation, and structured
    // activation results; older global components must not accept these calls.
    public const int Current = 6;
    public const int MinimumSupported = 6;
}

public static class AuraSharedResourceSchemaVersions
{
    public const int Current = 4;
}

public static class AuraSharedPackageSourceKinds
{
    public const string ModPackage = "ModPackage";
    public const string LocalPackage = "LocalPackage";
}

public static class AuraSharedOriginKinds
{
    public const string ContentRegistered = "ContentRegistered";
    public const string ToolRegistered = "ToolRegistered";
    public const string ToolDefault = "ToolDefault";
    public const string FoundationDefault = "FoundationDefault";
    public const string UserManual = "UserManual";

    public static bool IsValid(string value)
    {
        return value == ContentRegistered || value == ToolRegistered || value == ToolDefault
               || value == FoundationDefault || value == UserManual;
    }
}

public static class AuraSharedSelectionModes
{
    public const string Priority = "Priority";
    public const string Random = "Random";
    public const string Sequential = "Sequential";
    public const string All = "All";

    public static string Normalize(string value)
    {
        if (string.Equals(value, Random, StringComparison.OrdinalIgnoreCase)) return Random;
        if (string.Equals(value, Sequential, StringComparison.OrdinalIgnoreCase)) return Sequential;
        if (string.Equals(value, All, StringComparison.OrdinalIgnoreCase)) return All;
        return Priority;
    }
}

public static class AuraSharedCatalogVisibilities
{
    public const string Active = "Active";
    public const string History = "History";
    public const string All = "All";
}

public static class AuraSharedHistoryReasons
{
    public const string InactiveOwner = "InactiveOwner";
    public const string Unavailable = "Unavailable";
    public const string Archived = "Archived";
    public const string Retired = "Retired";
    public const string Inapplicable = "Inapplicable";
    public const string Invalid = "Invalid";
}

public static class AuraSharedParticipantKinds
{
    public const string Foundation = "Foundation";
    public const string Content = "Content";
    public const string Tool = "Tool";

    public static string Normalize(string value)
    {
        if (string.Equals(value, Tool, StringComparison.OrdinalIgnoreCase)) return Tool;
        if (string.Equals(value, Foundation, StringComparison.OrdinalIgnoreCase)) return Foundation;
        return Content;
    }
}

public static class AuraSharedEffectModes
{
    public const string Additive = "Additive";
    public const string Replacement = "Replacement";
}

public static class AuraSharedMissingPolicies
{
    public const string Skip = "Skip";
    public const string NativeFallback = "NativeFallback";
}

public static class AuraSharedRegistrationStatuses
{
    public const string Installed = "Installed";
    public const string Updated = "Updated";
    public const string PreservedLocal = "PreservedLocal";
    public const string Deduplicated = "Deduplicated";
    public const string Unavailable = "Unavailable";
    public const string UnsupportedSchema = "UnsupportedSchema";
    public const string Invalid = "Invalid";
}

public sealed class AuraSharedScopeKey : IEquatable<AuraSharedScopeKey>
{
    [JsonProperty("moduleId")]
    public string ModuleId { get; set; } = "";

    [JsonProperty("featureId")]
    public string FeatureId { get; set; } = "";

    [JsonProperty("scopeType")]
    public string ScopeType { get; set; } = "";

    [JsonProperty("scopeId")]
    public string ScopeId { get; set; } = "";

    [JsonIgnore]
    public string Key => ModuleId + ":" + FeatureId + ":" + ScopeType + ":" + ScopeId;

    public void Normalize()
    {
        ModuleId = Clean(ModuleId, "General");
        FeatureId = Clean(FeatureId, "General");
        ScopeType = Clean(ScopeType, "Global");
        ScopeId = Clean(ScopeId, "all");
    }

    public bool Equals(AuraSharedScopeKey? other)
    {
        return other != null && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as AuraSharedScopeKey);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Key);

    public AuraSharedScopeKey Clone()
    {
        return new AuraSharedScopeKey
        {
            ModuleId = ModuleId,
            FeatureId = FeatureId,
            ScopeType = ScopeType,
            ScopeId = ScopeId
        };
    }

    private static string Clean(string value, string fallback)
    {
        var clean = (value ?? "").Trim();
        return clean.Length == 0 ? fallback : clean;
    }
}

public sealed class AuraSharedRegistrationManifestV4
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraSharedResourceSchemaVersions.Current;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = AuraSharedParticipantKinds.Content;

    [JsonProperty("packageSourceKind")]
    public string PackageSourceKind { get; set; } = AuraSharedPackageSourceKinds.ModPackage;

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; } = 1;

    [JsonProperty("resources")]
    public List<AuraSharedResourceDeclarationV4> Resources { get; set; } = new();

    [JsonProperty("defaults")]
    public List<AuraSharedDefaultProfileV4> Defaults { get; set; } = new();

    public void Normalize(string fallbackOwner)
    {
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? (fallbackOwner ?? "").Trim() : OwnerModId.Trim();
        ParticipantKind = AuraSharedParticipantKinds.Normalize(ParticipantKind);
        PackageSourceKind = string.Equals(PackageSourceKind, AuraSharedPackageSourceKinds.LocalPackage, StringComparison.OrdinalIgnoreCase)
            ? AuraSharedPackageSourceKinds.LocalPackage
            : AuraSharedPackageSourceKinds.ModPackage;
        PackageId = string.IsNullOrWhiteSpace(PackageId) ? OwnerModId + ".SharedResources" : PackageId.Trim();
        PackageVersion = Math.Max(1, PackageVersion);
        Resources ??= new List<AuraSharedResourceDeclarationV4>();
        Defaults ??= new List<AuraSharedDefaultProfileV4>();
        Resources.ForEach(resource => resource?.Normalize());
        Defaults.ForEach(profile => profile?.Normalize());
    }
}

public sealed class AuraSharedResourceDeclarationV4
{
    [JsonProperty("moduleId")]
    public string ModuleId { get; set; } = "";

    [JsonProperty("featureId")]
    public string FeatureId { get; set; } = "";

    [JsonProperty("scopeType")]
    public string ScopeType { get; set; } = "";

    [JsonProperty("scopeId")]
    public string ScopeId { get; set; } = "";

    [JsonProperty("scopeOwnerModId")]
    public string ScopeOwnerModId { get; set; } = "";

    [JsonProperty("scopeAliases")]
    public List<string> ScopeAliases { get; set; } = new();

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = AuraSharedResourceKinds.File;

    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("fileName")]
    public string FileName { get; set; } = "";

    [JsonProperty("originKind")]
    public string OriginKind { get; set; } = "";

    [JsonProperty("writerId")]
    public string WriterId { get; set; } = "";

    [JsonProperty("defaultEnabled")]
    public bool DefaultEnabled { get; set; } = true;

    [JsonProperty("archived")]
    public bool Archived { get; set; }

    [JsonProperty("retired")]
    public bool Retired { get; set; }

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("effectMode")]
    public string EffectMode { get; set; } = AuraSharedEffectModes.Additive;

    [JsonProperty("missingPolicy")]
    public string MissingPolicy { get; set; } = AuraSharedMissingPolicies.Skip;

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public AuraSharedScopeKey Scope => new()
    {
        ModuleId = ModuleId,
        FeatureId = FeatureId,
        ScopeType = ScopeType,
        ScopeId = ScopeId
    };

    public void Normalize()
    {
        var scope = Scope;
        scope.Normalize();
        ModuleId = scope.ModuleId;
        FeatureId = scope.FeatureId;
        ScopeType = scope.ScopeType;
        ScopeId = scope.ScopeId;
        ScopeOwnerModId = (ScopeOwnerModId ?? "").Trim();
        ScopeAliases = CleanList(ScopeAliases, normalizePath: false);
        ResourceId = (ResourceId ?? "").Trim();
        Kind = string.Equals(Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase)
            ? AuraSharedResourceKinds.Directory
            : AuraSharedResourceKinds.File;
        Source = AuraSharedPaths.NormalizeRelativePath(Source);
        FileName = AuraSharedPaths.SafeSegment((FileName ?? "").Trim(), "content");
        OriginKind = (OriginKind ?? "").Trim();
        WriterId = (WriterId ?? "").Trim();
        EffectMode = string.Equals(EffectMode, AuraSharedEffectModes.Replacement, StringComparison.OrdinalIgnoreCase)
            ? AuraSharedEffectModes.Replacement
            : AuraSharedEffectModes.Additive;
        MissingPolicy = string.Equals(MissingPolicy, AuraSharedMissingPolicies.NativeFallback, StringComparison.OrdinalIgnoreCase)
            ? AuraSharedMissingPolicies.NativeFallback
            : AuraSharedMissingPolicies.Skip;
        Tags = CleanList(Tags, normalizePath: false);
        Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> CleanList(IEnumerable<string>? values, bool normalizePath)
    {
        return (values ?? Array.Empty<string>())
            .Select(value => normalizePath ? AuraSharedPaths.NormalizeRelativePath(value) : (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class AuraSharedDefaultProfileV4
{
    [JsonProperty("moduleId")]
    public string ModuleId { get; set; } = "";

    [JsonProperty("featureId")]
    public string FeatureId { get; set; } = "";

    [JsonProperty("scopeType")]
    public string ScopeType { get; set; } = "";

    [JsonProperty("scopeId")]
    public string ScopeId { get; set; } = "";

    [JsonProperty("profileId")]
    public string ProfileId { get; set; } = "default";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("resourceOwnerModId")]
    public string ResourceOwnerModId { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("values")]
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public AuraSharedScopeKey Scope => new()
    {
        ModuleId = ModuleId,
        FeatureId = FeatureId,
        ScopeType = ScopeType,
        ScopeId = ScopeId
    };

    public void Normalize()
    {
        var scope = Scope;
        scope.Normalize();
        ModuleId = scope.ModuleId;
        FeatureId = scope.FeatureId;
        ScopeType = scope.ScopeType;
        ScopeId = scope.ScopeId;
        ProfileId = string.IsNullOrWhiteSpace(ProfileId) ? "default" : ProfileId.Trim();
        ResourceOwnerModId = (ResourceOwnerModId ?? "").Trim();
        ResourceId = (ResourceId ?? "").Trim();
        Values ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class AuraSharedActiveLeaseV4
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraSharedResourceSchemaVersions.Current;

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = "";

    [JsonProperty("packageSourceKind")]
    public string PackageSourceKind { get; set; } = AuraSharedPackageSourceKinds.ModPackage;

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; }

    [JsonProperty("registeredUtc")]
    public string RegisteredUtc { get; set; } = "";

    [JsonProperty("scopeKeys")]
    public List<string> ScopeKeys { get; set; } = new();
}

public sealed class AuraSharedCatalogQueryV4
{
    [JsonProperty("moduleId")]
    public string ModuleId { get; set; } = "";

    [JsonProperty("featureId")]
    public string FeatureId { get; set; } = "";

    [JsonProperty("scopeType")]
    public string ScopeType { get; set; } = "";

    [JsonProperty("scopeId")]
    public string ScopeId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("visibility")]
    public string Visibility { get; set; } = AuraSharedCatalogVisibilities.Active;

    public void Normalize()
    {
        ModuleId = (ModuleId ?? "").Trim();
        FeatureId = (FeatureId ?? "").Trim();
        ScopeType = (ScopeType ?? "").Trim();
        ScopeId = (ScopeId ?? "").Trim();
        OwnerModId = (OwnerModId ?? "").Trim();
        Visibility = string.Equals(Visibility, AuraSharedCatalogVisibilities.History, StringComparison.OrdinalIgnoreCase)
            ? AuraSharedCatalogVisibilities.History
            : string.Equals(Visibility, AuraSharedCatalogVisibilities.All, StringComparison.OrdinalIgnoreCase)
                ? AuraSharedCatalogVisibilities.All
                : AuraSharedCatalogVisibilities.Active;
    }
}

public sealed class AuraSharedCatalogEntryV4
{
    [JsonProperty("registered")]
    public bool Registered { get; set; } = true;

    [JsonProperty("active")]
    public bool Active { get; set; }

    [JsonProperty("available")]
    public bool Available { get; set; }

    [JsonProperty("applicable")]
    public bool Applicable { get; set; } = true;

    [JsonProperty("configuredEnabled")]
    public bool ConfiguredEnabled { get; set; } = true;

    [JsonProperty("effectiveEnabled")]
    public bool EffectiveEnabled { get; set; } = true;

    [JsonProperty("historyReasons")]
    public List<string> HistoryReasons { get; set; } = new();

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = "";

    [JsonProperty("packageSourceKind")]
    public string PackageSourceKind { get; set; } = AuraSharedPackageSourceKinds.ModPackage;

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; }

    [JsonProperty("resource")]
    public AuraSharedResourceDeclarationV4 Resource { get; set; } = new();

    [JsonProperty("defaults")]
    public List<AuraSharedDefaultProfileV4> Defaults { get; set; } = new();

    [JsonProperty("canonicalPath")]
    public string CanonicalPath { get; set; } = "";

    [JsonIgnore]
    public string SemanticResourceId => Resource.Scope.Key + ":" + Resource.ResourceId;

    [JsonIgnore]
    public string QualifiedResourceId => Resource.ModuleId
                                         + "/" + Resource.ScopeType
                                         + "/" + Resource.ScopeId
                                         + "/" + Resource.FeatureId
                                         + "/" + OwnerModId
                                         + "/" + Resource.ResourceId;
}

public sealed class AuraSharedCatalogSnapshotV4
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraSharedResourceSchemaVersions.Current;

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("entries")]
    public List<AuraSharedCatalogEntryV4> Entries { get; set; } = new();
}

public sealed class AuraSharedResourceStateV4
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraSharedResourceSchemaVersions.Current;

    [JsonProperty("seedHash")]
    public string SeedHash { get; set; } = "";

    [JsonProperty("contentHash")]
    public string ContentHash { get; set; } = "";

    [JsonProperty("customized")]
    public bool Customized { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; } = "";

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; } = "";
}

public sealed class AuraSharedRegistrationItemResultV4
{
    [JsonProperty("scopeKey")]
    public string ScopeKey { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("status")]
    public string Status { get; set; } = "";

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("changed")]
    public bool Changed { get; set; }

    [JsonProperty("canonicalPath")]
    public string CanonicalPath { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    [JsonProperty("failureCode")]
    public string FailureCode { get; set; } = "";

    [JsonProperty("failedPath")]
    public string FailedPath { get; set; } = "";

    [JsonProperty("failedPathLength")]
    public int FailedPathLength { get; set; }
}

public sealed class AuraSharedRegistrationResultV4
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("items")]
    public List<AuraSharedRegistrationItemResultV4> Items { get; set; } = new();

    [JsonProperty("changedScopeKeys")]
    public List<string> ChangedScopeKeys { get; set; } = new();

    [JsonProperty("message")]
    public string Message { get; set; } = "";

    [JsonProperty("activated")]
    public bool Activated { get; set; }

    [JsonProperty("contentChanged")]
    public bool ContentChanged { get; set; }

    [JsonProperty("catalogChanged")]
    public bool CatalogChanged { get; set; }

    [JsonProperty("expectedItemCount")]
    public int ExpectedItemCount { get; set; }

    [JsonProperty("processedItemCount")]
    public int ProcessedItemCount { get; set; }

    [JsonProperty("failureCode")]
    public string FailureCode { get; set; } = "";

    [JsonProperty("failedPath")]
    public string FailedPath { get; set; } = "";

    [JsonProperty("failedPathLength")]
    public int FailedPathLength { get; set; }
}

public sealed class AuraSharedManualResourceRequestV4
{
    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("writerId")]
    public string WriterId { get; set; } = "LocalUser";

    [JsonProperty("sourcePath")]
    public string SourcePath { get; set; } = "";

    [JsonProperty("archive")]
    public bool Archive { get; set; }

    [JsonProperty("resource")]
    public AuraSharedResourceDeclarationV4 Resource { get; set; } = new();
}

public sealed class AuraSharedResourceResolutionV4
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("active")]
    public bool Active { get; set; }

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("scopeKey")]
    public string ScopeKey { get; set; } = "";

    [JsonProperty("resolvedPath")]
    public string ResolvedPath { get; set; } = "";

    [JsonProperty("outcome")]
    public string Outcome { get; set; } = "";

    [JsonProperty("fallback")]
    public string Fallback { get; set; } = "";

    [JsonProperty("revision")]
    public long Revision { get; set; }
}

public sealed class AuraSharedLocalOverrideV4
{
    [JsonProperty("enabled")]
    public bool? Enabled { get; set; }

    [JsonProperty("selectionMode")]
    public string SelectionMode { get; set; } = AuraSharedSelectionModes.Priority;

    [JsonProperty("resourceOverrides")]
    public Dictionary<string, bool> ResourceOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("values")]
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        SelectionMode = AuraSharedSelectionModes.Normalize(SelectionMode);
        ResourceOverrides ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Values ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class AuraSharedUserOverrideDocumentV4
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraSharedResourceSchemaVersions.Current;

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("writerId")]
    public string WriterId { get; set; } = "";

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; } = "";

    [JsonProperty("enabled")]
    public bool? Enabled { get; set; }

    [JsonProperty("selectionMode")]
    public string SelectionMode { get; set; } = AuraSharedSelectionModes.Priority;

    [JsonProperty("resourceOverrides")]
    public Dictionary<string, bool> ResourceOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonProperty("values")]
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public AuraSharedLocalOverrideV4 Override
    {
        get => new()
        {
            Enabled = Enabled,
            SelectionMode = SelectionMode,
            ResourceOverrides = ResourceOverrides,
            Values = Values
        };
        set
        {
            var normalized = value ?? new AuraSharedLocalOverrideV4();
            normalized.Normalize();
            Enabled = normalized.Enabled;
            SelectionMode = normalized.SelectionMode;
            ResourceOverrides = normalized.ResourceOverrides;
            Values = normalized.Values;
        }
    }
}

public sealed class AuraSharedUserOverrideWriteResultV4
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("conflict")]
    public bool Conflict { get; set; }

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

public sealed class AuraSharedEffectiveResolutionV4
{
    [JsonProperty("scopeKey")]
    public string ScopeKey { get; set; } = "";

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("configSource")]
    public string ConfigSource { get; set; } = "";

    [JsonProperty("configOwnerModId")]
    public string ConfigOwnerModId { get; set; } = "";

    [JsonProperty("profileId")]
    public string ProfileId { get; set; } = "";

    [JsonProperty("selectionMode")]
    public string SelectionMode { get; set; } = AuraSharedSelectionModes.Priority;

    [JsonProperty("resources")]
    public List<AuraSharedEffectiveResourceV4> Resources { get; set; } = new();

    [JsonProperty("resourceOwnerModId")]
    public string ResourceOwnerModId { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("resourcePath")]
    public string ResourcePath { get; set; } = "";

    [JsonProperty("outcome")]
    public string Outcome { get; set; } = "";

    [JsonProperty("fallback")]
    public string Fallback { get; set; } = "";

    [JsonProperty("values")]
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AuraSharedEffectiveResourceV4
{
    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("resourcePath")]
    public string ResourcePath { get; set; } = "";

    [JsonProperty("originKind")]
    public string OriginKind { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; }
}
