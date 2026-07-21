using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;

namespace AuraGameData.Shared;

public static class AuraGameDataConstants
{
    public const int SchemaVersion = 5;
    public const int PolicyVersion = 1;
    public const string RegistryAuthorityId = "AuraGameDataShared";
    public const string RegistryFileName = "game-data.registry.json";
    public const string SystemName = "GameData";
}

public static class AuraGameDataStorageKinds
{
    public const string Inline = "Inline";
    public const string Overlay = "Overlay";

    public static bool IsKnown(string? value)
    {
        var normalized = (value ?? "").Trim();
        return string.Equals(normalized, Inline, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, Overlay, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? value)
    {
        var normalized = (value ?? "").Trim();
        return string.Equals(normalized, Overlay, StringComparison.OrdinalIgnoreCase) ? Overlay : Inline;
    }
}

public static class AuraGameDataSourceKinds
{
    public const string UserManual = "UserManual";
    public const string Registered = "Registered";
    public const string Default = "Default";
    public const string Native = "Native";

    public static readonly IReadOnlyList<string> DefaultSearchOrder = new[]
    {
        UserManual,
        Registered,
        Default,
        Native
    };

    public static bool IsKnown(string? value)
    {
        return DefaultSearchOrder.Contains((value ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class AuraGameDataKey : IEquatable<AuraGameDataKey>
{
    public AuraGameDataKey()
    {
    }

    public AuraGameDataKey(string dataType, string id)
    {
        DataType = NormalizeDataType(dataType);
        Id = NormalizeId(id);
    }

    [JsonProperty("dataType")]
    public string DataType { get; set; } = "";

    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonIgnore]
    public string Canonical => NormalizeDataType(DataType) + ":" + NormalizeId(Id);

    public void Normalize()
    {
        DataType = NormalizeDataType(DataType);
        Id = NormalizeId(Id);
    }

    public AuraGameDataKey Clone()
    {
        return new AuraGameDataKey(DataType, Id);
    }

    public bool Equals(AuraGameDataKey? other)
    {
        return other != null
            && string.Equals(NormalizeDataType(DataType), NormalizeDataType(other.DataType), StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeId(Id), NormalizeId(other.Id), StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as AuraGameDataKey);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizeDataType(DataType)) * 397)
                ^ StringComparer.Ordinal.GetHashCode(NormalizeId(Id));
        }
    }

    public override string ToString()
    {
        return Canonical;
    }

    public static string NormalizeDataType(string? value)
    {
        return (value ?? "").Trim();
    }

    public static string NormalizeId(string? value)
    {
        return (value ?? "").Trim().TrimStart('*');
    }
}

public sealed class AuraGameDataDefinition
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraGameDataConstants.SchemaVersion;

    [JsonProperty("key")]
    public AuraGameDataKey Key { get; set; } = new();

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("writerId")]
    public string WriterId { get; set; } = "";

    [JsonProperty("sourceKind")]
    public string SourceKind { get; set; } = AuraGameDataSourceKinds.Registered;

    [JsonProperty("storageKind")]
    public string StorageKind { get; set; } = AuraGameDataStorageKinds.Inline;

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("retired")]
    public bool Retired { get; set; }

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("aliases")]
    public List<string> Aliases { get; set; } = new();

    [JsonProperty("fields")]
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    [JsonProperty("removeFields")]
    public List<string> RemoveFields { get; set; } = new();

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; } = "";

    [JsonIgnore]
    public string QualifiedId => Key.Canonical + ":" + OwnerModId;

    public void Normalize()
    {
        Key ??= new AuraGameDataKey();
        Key.Normalize();
        OwnerModId = (OwnerModId ?? "").Trim();
        WriterId = (WriterId ?? "").Trim();
        SourceKind = NormalizeSourceKind(SourceKind);
        StorageKind = AuraGameDataStorageKinds.Normalize(StorageKind);
        Revision = Math.Max(0, Revision);
        Aliases = (Aliases ?? new List<string>())
            .Select(AuraGameDataKey.NormalizeId)
            .Where(value => value.Length > 0 && !string.Equals(value, Key.Id, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        Fields = new Dictionary<string, string>(Fields ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        RemoveFields = (RemoveFields ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => !string.Equals(value, "Id", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrWhiteSpace(Key.Id))
        {
            Fields["Id"] = Key.Id;
        }
    }

    public AuraGameDataDefinition Clone()
    {
        return new AuraGameDataDefinition
        {
            SchemaVersion = SchemaVersion,
            Key = Key.Clone(),
            OwnerModId = OwnerModId,
            WriterId = WriterId,
            SourceKind = SourceKind,
            StorageKind = StorageKind,
            Priority = Priority,
            Enabled = Enabled,
            Retired = Retired,
            Revision = Revision,
            Aliases = Aliases.ToList(),
            Fields = new Dictionary<string, string>(Fields, StringComparer.Ordinal),
            RemoveFields = RemoveFields.ToList(),
            UpdatedUtc = UpdatedUtc
        };
    }

    private static string NormalizeSourceKind(string? value)
    {
        var normalized = (value ?? "").Trim();
        return AuraGameDataSourceKinds.IsKnown(normalized)
            ? AuraGameDataSourceKinds.DefaultSearchOrder.First(candidate =>
                string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
            : AuraGameDataSourceKinds.Registered;
    }
}

public sealed class AuraGameDataOwnerRule
{
    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("writerId")]
    public string WriterId { get; set; } = "";

    [JsonProperty("idPrefix")]
    public string IdPrefix { get; set; } = "";

    [JsonProperty("priority")]
    public int Priority { get; set; }

    public void Normalize()
    {
        OwnerModId = (OwnerModId ?? "").Trim();
        WriterId = (WriterId ?? "").Trim();
        IdPrefix = AuraGameDataKey.NormalizeId(IdPrefix);
    }

    public AuraGameDataOwnerRule Clone()
    {
        return new AuraGameDataOwnerRule
        {
            OwnerModId = OwnerModId,
            WriterId = WriterId,
            IdPrefix = IdPrefix,
            Priority = Priority
        };
    }
}

public sealed class AuraGameDataPatch
{
    [JsonProperty("setFields")]
    public Dictionary<string, string> SetFields { get; set; } = new(StringComparer.Ordinal);

    [JsonProperty("removeFields")]
    public List<string> RemoveFields { get; set; } = new();

    [JsonProperty("aliases")]
    public List<string>? Aliases { get; set; }

    [JsonProperty("enabled")]
    public bool? Enabled { get; set; }

    [JsonProperty("priority")]
    public int? Priority { get; set; }

    public void Normalize()
    {
        SetFields = new Dictionary<string, string>(SetFields ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        RemoveFields = (RemoveFields ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => !string.Equals(value, "Id", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (Aliases != null)
        {
            Aliases = Aliases
                .Select(AuraGameDataKey.NormalizeId)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }
}

public sealed class AuraGameDataRegistryDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = AuraGameDataConstants.SchemaVersion;

    [JsonProperty("definitions")]
    public List<AuraGameDataDefinition> Definitions { get; set; } = new();

    [JsonProperty("history")]
    public List<AuraGameDataDefinition> History { get; set; } = new();

    [JsonProperty("ownerRules")]
    public List<AuraGameDataOwnerRule> OwnerRules { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = AuraGameDataConstants.SchemaVersion;
        Definitions = NormalizeDefinitions(Definitions, includeRetired: false);
        History = NormalizeDefinitions(History, includeRetired: true);
        OwnerRules = (OwnerRules ?? new List<AuraGameDataOwnerRule>())
            .Where(value => value != null)
            .Select(value =>
            {
                value.Normalize();
                return value;
            })
            .Where(value => value.OwnerModId.Length > 0
                && value.WriterId.Length > 0
                && value.IdPrefix.Length > 0)
            .GroupBy(value => value.OwnerModId + "\u001f" + value.IdPrefix, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(value => value.Priority).First())
            .OrderByDescending(value => value.IdPrefix.Length)
            .ThenByDescending(value => value.Priority)
            .ThenBy(value => value.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<AuraGameDataDefinition> NormalizeDefinitions(
        List<AuraGameDataDefinition>? definitions,
        bool includeRetired)
    {
        var normalized = new Dictionary<string, AuraGameDataDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in (definitions ?? new List<AuraGameDataDefinition>()).Where(value => value != null))
        {
            definition.Normalize();
            if (definition.SchemaVersion != AuraGameDataConstants.SchemaVersion
                || string.IsNullOrWhiteSpace(definition.Key.DataType)
                || string.IsNullOrWhiteSpace(definition.Key.Id)
                || string.IsNullOrWhiteSpace(definition.OwnerModId)
                || string.IsNullOrWhiteSpace(definition.WriterId))
            {
                continue;
            }

            if (!includeRetired)
            {
                definition.Retired = false;
            }

            normalized[definition.QualifiedId] = definition;
        }

        return normalized.Values
            .OrderBy(value => value.Key.DataType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Key.Id, StringComparer.Ordinal)
            .ThenBy(value => value.OwnerModId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public AuraGameDataRegistryDocument Clone()
    {
        return new AuraGameDataRegistryDocument
        {
            SchemaVersion = SchemaVersion,
            Definitions = Definitions.Select(value => value.Clone()).ToList(),
            History = History.Select(value => value.Clone()).ToList(),
            OwnerRules = OwnerRules.Select(value => value.Clone()).ToList()
        };
    }
}

public sealed class AuraGameDataSnapshot
{
    private readonly AuraGameDataDefinition definition;
    private readonly IReadOnlyList<string> aliases;
    private readonly IReadOnlyDictionary<string, string> fields;

    public AuraGameDataSnapshot(AuraGameDataDefinition definition, long catalogEpoch = 0)
    {
        this.definition = definition?.Clone() ?? new AuraGameDataDefinition();
        this.definition.Normalize();
        aliases = new ReadOnlyCollection<string>(this.definition.Aliases.ToArray());
        fields = new ReadOnlyDictionary<string, string>(this.definition.Fields);
        CatalogEpoch = Math.Max(0, catalogEpoch);
        SelectionIdentity = BuildSelectionIdentity(this.definition);
    }

    public AuraGameDataDefinition Definition => definition.Clone();

    public AuraGameDataKey Key => definition.Key.Clone();

    public string DataType => definition.Key.DataType;

    public string Id => definition.Key.Id;

    public string OwnerModId => definition.OwnerModId;

    public string WriterId => definition.WriterId;

    public string SourceKind => definition.SourceKind;

    public string StorageKind => definition.StorageKind;

    public bool Enabled => definition.Enabled;

    public bool Retired => definition.Retired;

    public long Revision => definition.Revision;

    public int Priority => definition.Priority;

    public IReadOnlyList<string> Aliases => aliases;

    public IReadOnlyDictionary<string, string> Fields => fields;

    public long CatalogEpoch { get; }

    public string SelectionIdentity { get; }

    private static string BuildSelectionIdentity(AuraGameDataDefinition value)
    {
        return value.Key.Canonical
            + "|" + value.OwnerModId
            + "|" + value.SourceKind
            + "|" + value.Revision;
    }
}

public sealed class AuraGameDataDefinitionHandle
{
    public AuraGameDataKey Key { get; set; } = new();

    public string OwnerModId { get; set; } = "";

    public string SourceKind { get; set; } = "";

    public long Revision { get; set; }

    public long CatalogEpoch { get; set; }

    public string SelectionIdentity { get; set; } = "";

    public string Token { get; set; } = "";
}

public sealed class AuraGameDataCatalogVersion
{
    public AuraGameDataCatalogVersion(
        long epoch,
        long nativeGeneration,
        long registryRevision,
        int policyVersion = AuraGameDataConstants.PolicyVersion)
        : this(epoch, nativeGeneration, registryRevision, policyVersion, nativeReady: true)
    {
    }

    public AuraGameDataCatalogVersion(
        long epoch,
        long nativeGeneration,
        long registryRevision,
        int policyVersion,
        bool nativeReady)
    {
        Epoch = Math.Max(0, epoch);
        NativeGeneration = Math.Max(0, nativeGeneration);
        RegistryRevision = Math.Max(0, registryRevision);
        PolicyVersion = Math.Max(1, policyVersion);
        NativeReady = nativeReady;
    }

    public long Epoch { get; }

    public long NativeGeneration { get; }

    public long RegistryRevision { get; }

    public int PolicyVersion { get; }

    public bool NativeReady { get; }
}

public enum AuraGameDataCatalogState
{
    Uninitialized = 0,
    Capturing = 1,
    Compiling = 2,
    Ready = 3,
    Invalidated = 4,
    Failed = 5,
    AwaitingNativeCapture = 6
}

public sealed class AuraGameDataSourceSnapshot
{
    public AuraGameDataSourceSnapshot(
        long generation,
        IReadOnlyList<AuraGameDataDefinition>? definitions)
        : this(generation, definitions, isComplete: true)
    {
    }

    public AuraGameDataSourceSnapshot(
        long generation,
        IReadOnlyList<AuraGameDataDefinition>? definitions,
        bool isComplete)
    {
        Generation = Math.Max(0, generation);
        Definitions = definitions ?? Array.Empty<AuraGameDataDefinition>();
        IsComplete = isComplete;
    }

    public long Generation { get; }

    public IReadOnlyList<AuraGameDataDefinition> Definitions { get; }

    public bool IsComplete { get; }
}

public sealed class AuraGameDataQuery
{
    public string DataType { get; set; } = "";

    public List<string> CandidateIds { get; set; } = new();

    public List<string> OwnerModIds { get; set; } = new();

    public List<string> SourceOrder { get; set; } = new(AuraGameDataSourceKinds.DefaultSearchOrder);

    public bool IncludeDisabled { get; set; }

    public bool IncludeHistory { get; set; }

    public bool IncludeAllCandidates { get; set; }

    public void Normalize()
    {
        DataType = AuraGameDataKey.NormalizeDataType(DataType);
        CandidateIds = (CandidateIds ?? new List<string>())
            .Select(AuraGameDataKey.NormalizeId)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        OwnerModIds = (OwnerModIds ?? new List<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SourceOrder = NormalizeSourceOrder(SourceOrder);
    }

    public static List<string> NormalizeSourceOrder(IEnumerable<string>? values)
    {
        var result = (values ?? Array.Empty<string>())
            .Select(value => (value ?? "").Trim())
            .Where(AuraGameDataSourceKinds.IsKnown)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var source in AuraGameDataSourceKinds.DefaultSearchOrder)
        {
            if (!result.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(source);
            }
        }

        return result;
    }
}

public sealed class AuraGameDataQueryResult
{
    public AuraGameDataQueryResult(long revision, IReadOnlyList<AuraGameDataSnapshot> items)
    {
        Revision = Math.Max(0, revision);
        Items = items ?? Array.Empty<AuraGameDataSnapshot>();
    }

    public long Revision { get; }

    public IReadOnlyList<AuraGameDataSnapshot> Items { get; }
}

public sealed class AuraGameDataMutationResult
{
    public bool Success { get; set; }

    public bool Conflict { get; set; }

    public string Message { get; set; } = "";

    public long Revision { get; set; }

    public AuraGameDataDefinitionHandle? Handle { get; set; }

    public static AuraGameDataMutationResult Failed(string message, bool conflict = false, long revision = 0)
    {
        return new AuraGameDataMutationResult
        {
            Success = false,
            Conflict = conflict,
            Message = message ?? "",
            Revision = Math.Max(0, revision)
        };
    }
}

public sealed class AuraGameDataInstanceSnapshot
{
    public AuraGameDataKey Key { get; set; } = new();

    public string InstanceId { get; set; } = "";

    public Dictionary<string, string> Data { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Vars { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AuraGameDataInstancePatch
{
    public Dictionary<string, string> SetVars { get; set; } = new(StringComparer.Ordinal);

    public List<string> RemoveVars { get; set; } = new();
}
