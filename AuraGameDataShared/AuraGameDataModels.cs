using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraGameData.Shared;

public static class AuraGameDataConstants
{
    public const int SchemaVersion = 4;
    public const string RegistryAuthorityId = "AuraGameDataShared";
    public const string RegistryFileName = "game-data.registry.json";
    public const string SystemName = "GameData";
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
        Revision = Math.Max(0, Revision);
        Aliases = (Aliases ?? new List<string>())
            .Select(AuraGameDataKey.NormalizeId)
            .Where(value => value.Length > 0 && !string.Equals(value, Key.Id, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        Fields = new Dictionary<string, string>(Fields ?? new Dictionary<string, string>(), StringComparer.Ordinal);
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
            Priority = Priority,
            Enabled = Enabled,
            Retired = Retired,
            Revision = Revision,
            Aliases = Aliases.ToList(),
            Fields = new Dictionary<string, string>(Fields, StringComparer.Ordinal),
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

    public void Normalize()
    {
        SchemaVersion = AuraGameDataConstants.SchemaVersion;
        Definitions ??= new List<AuraGameDataDefinition>();
        var normalized = new Dictionary<string, AuraGameDataDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in Definitions.Where(value => value != null))
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

            normalized[definition.QualifiedId] = definition;
        }

        Definitions = normalized.Values
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
            Definitions = Definitions.Select(value => value.Clone()).ToList()
        };
    }
}

public sealed class AuraGameDataSnapshot
{
    public AuraGameDataSnapshot(AuraGameDataDefinition definition)
    {
        Definition = definition.Clone();
    }

    public AuraGameDataDefinition Definition { get; }

    public AuraGameDataKey Key => Definition.Key.Clone();

    public string OwnerModId => Definition.OwnerModId;

    public string WriterId => Definition.WriterId;

    public string SourceKind => Definition.SourceKind;

    public bool Enabled => Definition.Enabled;

    public bool Retired => Definition.Retired;

    public long Revision => Definition.Revision;

    public IReadOnlyDictionary<string, string> Fields => Definition.Fields;
}

public sealed class AuraGameDataDefinitionHandle
{
    public AuraGameDataKey Key { get; set; } = new();

    public string OwnerModId { get; set; } = "";

    public string SourceKind { get; set; } = "";

    public long Revision { get; set; }

    public string Token { get; set; } = "";
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
