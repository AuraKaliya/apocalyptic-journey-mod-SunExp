using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AuraSkin.Shared.Models;

public sealed class SkinPackageManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("packageId")]
    public string PackageId { get; set; } = "";

    [JsonProperty("packageVersion")]
    public int PackageVersion { get; set; } = 1;

    [JsonProperty("participantKind")]
    public string ParticipantKind { get; set; } = "Content";

    [JsonProperty("resources")]
    public List<SkinPackageResource> Resources { get; set; } = new();
}

public sealed class SkinPackageResource
{
    [JsonProperty("source")]
    public string Source { get; set; } = "";
}

public sealed class SkinPackageInstallResult
{
    public bool Success { get; set; } = true;

    public bool Changed { get; set; }

    public bool Activated { get; set; }

    public bool CatalogChanged { get; set; }

    public string Message { get; set; } = "";

    public int Installed { get; set; }

    public int Updated { get; set; }

    public int Repaired { get; set; }

    public int Deduplicated { get; set; }

    public int Conflicts { get; set; }

    public int ExpectedResources { get; set; }

    public int ProcessedResources { get; set; }
}
