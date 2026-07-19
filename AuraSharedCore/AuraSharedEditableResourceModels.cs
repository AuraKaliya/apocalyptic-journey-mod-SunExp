using Newtonsoft.Json;

namespace AuraShared.Core;

public static class AuraSharedEditableResourceStatuses
{
    public const string Created = "Created";
    public const string ExistingDefault = "ExistingDefault";
    public const string UpdatedDefault = "UpdatedDefault";
    public const string PreservedCustomized = "PreservedCustomized";
    public const string Reset = "Reset";
    public const string Failed = "Failed";
}

public sealed class AuraSharedEditableResourceRequest
{
    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("system")]
    public string System { get; set; } = "";

    [JsonProperty("logicalId")]
    public string LogicalId { get; set; } = "";

    [JsonProperty("sourcePath")]
    public string SourcePath { get; set; } = "";

    [JsonProperty("destinationRelativePath")]
    public string DestinationRelativePath { get; set; } = "";

    [JsonProperty("previousSeedHash")]
    public string PreviousSeedHash { get; set; } = "";

    [JsonProperty("forceReset")]
    public bool ForceReset { get; set; }
}

public sealed class AuraSharedEditableResourceResult
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("changed")]
    public bool Changed { get; set; }

    [JsonProperty("customized")]
    public bool Customized { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; } = "";

    [JsonProperty("seedHash")]
    public string SeedHash { get; set; } = "";

    [JsonProperty("contentHash")]
    public string ContentHash { get; set; } = "";

    [JsonProperty("installedPath")]
    public string InstalledPath { get; set; } = "";

    [JsonProperty("backupPath")]
    public string BackupPath { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}
