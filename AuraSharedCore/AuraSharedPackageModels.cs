using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuraShared.Core;

public static class AuraSharedResourceKinds
{
    public const string File = "File";
    public const string Directory = "Directory";
}

public sealed class AuraSharedPackageManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("packageKind")]
    public string PackageKind { get; set; } = "Resource";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("dependencies")]
    public List<AuraSharedPackageDependency> Dependencies { get; set; } = new();

    [JsonProperty("capabilities")]
    public List<string> Capabilities { get; set; } = new();

    [JsonProperty("resources")]
    public List<AuraSharedPackageResource> Resources { get; set; } = new();
}

public sealed class AuraSharedPackageDependency
{
    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("minVersion")]
    public long MinVersion { get; set; } = 1;

    [JsonProperty("optional")]
    public bool Optional { get; set; }
}

public sealed class AuraSharedPackageResource
{
    [JsonProperty("system")]
    public string System { get; set; } = "";

    [JsonProperty("resourceId")]
    public string ResourceId { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = AuraSharedResourceKinds.File;

    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("destination")]
    public string Destination { get; set; } = "";

    [JsonProperty("targetRoleIds")]
    public List<string> TargetRoleIds { get; set; } = new();

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AuraSharedInstallRequest
{
    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("system")]
    public string System { get; set; } = "";

    [JsonProperty("logicalId")]
    public string LogicalId { get; set; } = "";

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; }

    [JsonProperty("kind")]
    public string Kind { get; set; } = AuraSharedResourceKinds.File;

    [JsonProperty("sourcePath")]
    public string SourcePath { get; set; } = "";

    [JsonProperty("destinationRelativePath")]
    public string DestinationRelativePath { get; set; } = "";
}

public sealed class AuraSharedInstallResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("changed")]
    public bool Changed { get; set; }

    [JsonProperty("conflict")]
    public bool Conflict { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; } = "";

    [JsonProperty("contentHash")]
    public string ContentHash { get; set; } = "";

    [JsonProperty("installedPath")]
    public string InstalledPath { get; set; } = "";

    [JsonProperty("message")]
    public string Message { get; set; } = "";
}

public sealed class AuraSharedResourceIndex
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonProperty("revision")]
    public long Revision { get; set; }

    [JsonProperty("resources")]
    public List<AuraSharedInstalledResource> Resources { get; set; } = new();
}

public sealed class AuraSharedInstalledResource
{
    [JsonProperty("resourceKey")]
    public string ResourceKey { get; set; } = "";

    [JsonProperty("system")]
    public string System { get; set; } = "";

    [JsonProperty("logicalId")]
    public string LogicalId { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = "";

    [JsonProperty("contentHash")]
    public string ContentHash { get; set; } = "";

    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("installedUtc")]
    public string InstalledUtc { get; set; } = "";

    [JsonProperty("sources")]
    public List<AuraSharedInstalledSource> Sources { get; set; } = new();

    [JsonProperty("files")]
    public List<AuraSharedInstalledFile> Files { get; set; } = new();
}

public sealed class AuraSharedInstalledSource
{
    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public long PackageVersion { get; set; }
}

public sealed class AuraSharedInstalledFile
{
    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonProperty("length")]
    public long Length { get; set; }
}

public sealed class AuraSharedTransactionJournal
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("transactionId")]
    public string TransactionId { get; set; } = "";

    [JsonProperty("state")]
    public string State { get; set; } = "Prepared";

    [JsonProperty("destinationPath")]
    public string DestinationPath { get; set; } = "";

    [JsonProperty("backupPath")]
    public string BackupPath { get; set; } = "";

    [JsonProperty("stagingPath")]
    public string StagingPath { get; set; } = "";

    [JsonProperty("registryPath")]
    public string RegistryPath { get; set; } = "";

    [JsonProperty("registryBackupPath")]
    public string RegistryBackupPath { get; set; } = "";

    [JsonProperty("destinationExisted")]
    public bool DestinationExisted { get; set; }

    [JsonProperty("registryExisted")]
    public bool RegistryExisted { get; set; }

    [JsonProperty("kind")]
    public string Kind { get; set; } = "";
}
