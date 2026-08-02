using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraShared.Core;

public static class AuraSharedStorageScopes
{
    public const string Shared = "Shared";
    public const string Owner = "Owner";
    public const string Runtime = "Runtime";
    public const string Registry = "Registry";
}

public sealed class AuraSharedStorageRequest
{
    [JsonProperty("scope")]
    public string Scope { get; set; } = AuraSharedStorageScopes.Shared;

    [JsonProperty("system")]
    public string System { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("writerId")]
    public string WriterId { get; set; } = "";

    [JsonProperty("authorityId")]
    public string AuthorityId { get; set; } = "";

    [JsonProperty("fileName")]
    public string FileName { get; set; } = "config.json";

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("expectedRevision")]
    public long ExpectedRevision { get; set; } = -1;

    [JsonProperty("payloadJson")]
    public string PayloadJson { get; set; } = "";

    [JsonProperty("createBackup")]
    public bool CreateBackup { get; set; } = true;
}

public sealed class AuraSharedStorageResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("found")]
    public bool Found { get; set; }

    [JsonProperty("conflict")]
    public bool Conflict { get; set; }

    [JsonProperty("changed")]
    public bool Changed { get; set; }

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonProperty("authorityId")]
    public string AuthorityId { get; set; } = "";

    [JsonProperty("payloadJson")]
    public string PayloadJson { get; set; } = "";

    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

public sealed class AuraSharedStorageEnvelope
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; } = "";

    [JsonProperty("updatedBy")]
    public string UpdatedBy { get; set; } = "";

    [JsonProperty("authorityId")]
    public string AuthorityId { get; set; } = "";

    [JsonProperty("data")]
    public JToken Data { get; set; } = JValue.CreateNull();
}

public sealed class AuraSharedConfigSnapshot<T>
{
    public bool Found { get; set; }

    public long Revision { get; set; }

    public int SchemaVersion { get; set; }

    public string AuthorityId { get; set; } = "";

    public T Value { get; set; } = default!;
}

public sealed class AuraSharedConfigWriteResult
{
    public bool Success { get; set; }

    public bool Conflict { get; set; }

    public bool Changed { get; set; }

    public long Revision { get; set; }

    public string Message { get; set; } = "";
}

public sealed class AuraSharedChangeRecord
{
    [JsonProperty("sequence")]
    public long Sequence { get; set; }

    [JsonProperty("kind")]
    public string Kind { get; set; } = "";

    [JsonProperty("system")]
    public string System { get; set; } = "";

    [JsonProperty("logicalId")]
    public string LogicalId { get; set; } = "";

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("changedUtc")]
    public string ChangedUtc { get; set; } = "";
}

public sealed class AuraSharedChangeFeed
{
    [JsonProperty("latestSequence")]
    public long LatestSequence { get; set; }

    [JsonProperty("changes")]
    public AuraSharedChangeRecord[] Changes { get; set; } = Array.Empty<AuraSharedChangeRecord>();
}
