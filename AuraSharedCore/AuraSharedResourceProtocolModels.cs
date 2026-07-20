using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraShared.Core;

public static class AuraSharedResourceProtocolVersions
{
    public const int Current = 4;
    public const int MinimumSupported = 3;
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
    public const string RejectedProtocol = "RejectedProtocol";
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

public sealed class AuraSharedRegistrationManifestV3
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = AuraSharedParticipantKinds.Content;

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; } = 1;

    [JsonProperty("resources")]
    public List<AuraSharedResourceDeclarationV3> Resources { get; set; } = new();

    [JsonProperty("defaults")]
    public List<AuraSharedDefaultProfileV3> Defaults { get; set; } = new();

    public void Normalize(string fallbackOwner)
    {
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? (fallbackOwner ?? "").Trim() : OwnerModId.Trim();
        ParticipantKind = AuraSharedParticipantKinds.Normalize(ParticipantKind);
        PackageId = string.IsNullOrWhiteSpace(PackageId) ? OwnerModId + ".SharedResources" : PackageId.Trim();
        PackageVersion = Math.Max(1, PackageVersion);
        Resources ??= new List<AuraSharedResourceDeclarationV3>();
        Defaults ??= new List<AuraSharedDefaultProfileV3>();
        Resources.ForEach(resource => resource?.Normalize());
        Defaults.ForEach(profile => profile?.Normalize());
    }
}

public sealed class AuraSharedResourceDeclarationV3
{
    [JsonProperty("moduleId")]
    public string ModuleId { get; set; } = "";

    [JsonProperty("featureId")]
    public string FeatureId { get; set; } = "";

    [JsonProperty("scopeType")]
    public string ScopeType { get; set; } = "";

    [JsonProperty("scopeId")]
    public string ScopeId { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = AuraSharedResourceKinds.File;

    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("fileName")]
    public string FileName { get; set; } = "";

    [JsonProperty("legacyPaths")]
    public List<string> LegacyPaths { get; set; } = new();

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
        ResourceId = (ResourceId ?? "").Trim();
        Kind = string.Equals(Kind, AuraSharedResourceKinds.Directory, StringComparison.OrdinalIgnoreCase)
            ? AuraSharedResourceKinds.Directory
            : AuraSharedResourceKinds.File;
        Source = AuraSharedPaths.NormalizeRelativePath(Source);
        FileName = AuraSharedPaths.SafeSegment((FileName ?? "").Trim(), "content");
        LegacyPaths = CleanList(LegacyPaths, normalizePath: true);
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

public sealed class AuraSharedDefaultProfileV3
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

public sealed class AuraSharedActiveLeaseV3
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = "";

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; }

    [JsonProperty("registeredUtc")]
    public string RegisteredUtc { get; set; } = "";

    [JsonProperty("scopeKeys")]
    public List<string> ScopeKeys { get; set; } = new();
}

public sealed class AuraSharedCatalogQueryV3
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

    [JsonProperty("includeInactive")]
    public bool IncludeInactive { get; set; }

    public void Normalize()
    {
        ModuleId = (ModuleId ?? "").Trim();
        FeatureId = (FeatureId ?? "").Trim();
        ScopeType = (ScopeType ?? "").Trim();
        ScopeId = (ScopeId ?? "").Trim();
        OwnerModId = (OwnerModId ?? "").Trim();
    }
}

public sealed class AuraSharedCatalogEntryV3
{
    [JsonProperty("active")]
    public bool Active { get; set; }

    [JsonProperty("available")]
    public bool Available { get; set; }

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = "";

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; }

    [JsonProperty("resource")]
    public AuraSharedResourceDeclarationV3 Resource { get; set; } = new();

    [JsonProperty("defaults")]
    public List<AuraSharedDefaultProfileV3> Defaults { get; set; } = new();

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

public sealed class AuraSharedCatalogSnapshotV3
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("entries")]
    public List<AuraSharedCatalogEntryV3> Entries { get; set; } = new();
}

public sealed class AuraSharedResourceStateV3
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

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

public sealed class AuraSharedRegistrationItemResultV3
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
}

public sealed class AuraSharedRegistrationResultV3
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("items")]
    public List<AuraSharedRegistrationItemResultV3> Items { get; set; } = new();

    [JsonProperty("changedScopeKeys")]
    public List<string> ChangedScopeKeys { get; set; } = new();

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

public sealed class AuraSharedResourceResolutionV3
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("active")]
    public bool Active { get; set; }

    [JsonProperty("usedLegacyPath")]
    public bool UsedLegacyPath { get; set; }

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

public sealed class AuraSharedMigrationRecordV3
{
    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("sourceHash")]
    public string SourceHash { get; set; } = "";

    [JsonProperty("classification")]
    public string Classification { get; set; } = "";

    [JsonProperty("destination")]
    public string Destination { get; set; } = "";

    [JsonProperty("result")]
    public string Result { get; set; } = "";

    [JsonProperty("recordedUtc")]
    public string RecordedUtc { get; set; } = "";
}

public sealed class AuraSharedLocalOverrideV3
{
    [JsonProperty("enabled")]
    public bool? Enabled { get; set; }

    [JsonProperty("resourceOwnerModId")]
    public string ResourceOwnerModId { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("values")]
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AuraSharedUserOverrideDocumentV3
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("writerId")]
    public string WriterId { get; set; } = "";

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; } = "";

    [JsonProperty("override")]
    public AuraSharedLocalOverrideV3 Override { get; set; } = new();
}

public sealed class AuraSharedUserOverrideWriteResultV3
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

public sealed class AuraSharedEffectiveResolutionV3
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
