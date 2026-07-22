using Newtonsoft.Json;

namespace AuraSkin.Shared.Models;

public sealed class SkinManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("skinId")]
    public string SkinId { get; set; } = "";

    [JsonProperty("targetCareerId")]
    public string TargetCareerId { get; set; } = "";

    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("author")]
    public string Author { get; set; } = "";

    [JsonProperty("preview")]
    public string Preview { get; set; } = "";

    [JsonProperty("assets")]
    public SkinAssets Assets { get; set; } = new();
}

public sealed class CharacterSkinManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("targetCareerId")]
    public string TargetCareerId { get; set; } = "";
}

public sealed class SkinAssets
{
    public string CareerImage { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string Character { get; set; } = "";
    public string DollIcon { get; set; } = "";
    public string ChoiceIcon { get; set; } = "";
    public string Animation { get; set; } = "";

    public string Get(string field)
    {
        return field switch
        {
            "CareerImage" => CareerImage,
            "Avatar" => Avatar,
            "Character" => Character,
            "DollIcon" => DollIcon,
            "ChoiceIcon" => ChoiceIcon,
            "Animation" => Animation,
            _ => ""
        };
    }
}

public sealed class SkinDefinition
{
    public string OwnerModId { get; set; } = "";
    public string SkinId { get; set; } = "";
    public string TargetCareerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string PreviewPath { get; set; } = "";
    public SkinAssets Assets { get; set; } = new();
    public string ContentHash { get; set; } = "";
    public string PackageId { get; set; } = "";
    public long PackageVersion { get; set; }
    public int Priority { get; set; }

    [JsonIgnore]
    public string QualifiedSkinId => Qualify(OwnerModId, TargetCareerId, SkinId);

    [JsonIgnore]
    public string SemanticKey => TargetCareerId + "::" + SkinId;

    public static string Qualify(string ownerModId, string targetCareerId, string skinId)
    {
        var owner = (ownerModId ?? "").Trim();
        var career = (targetCareerId ?? "").Trim();
        var id = (skinId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(owner))
        {
            return string.IsNullOrWhiteSpace(career) ? id : career + ":" + id;
        }

        return owner + ":" + career + ":" + id;
    }
}

[System.Serializable]
public sealed class SkinSelectionSnapshot
{
    public int SchemaVersion { get; set; } = 2;
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string CareerId { get; set; } = "";
    public string SkinId { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string PackageId { get; set; } = "";
    public long PackageVersion { get; set; }
    public string OwnerModId { get; set; } = "";
    public string QualifiedSkinId { get; set; } = "";
}

public sealed class SkinSelectionResolveResult
{
    public bool Success { get; set; }
    public bool DefaultSkin { get; set; }
    public string PlayerId { get; set; } = "";
    public string CareerId { get; set; } = "";
    public string SkinId { get; set; } = "";
    public string QualifiedSkinId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Warning { get; set; } = "";
}

internal static class SkinRemoteSelectionPolicy
{
    public static bool ShouldRetain(SkinSelectionSnapshot? snapshot, SkinSelectionResolveResult? result)
    {
        return snapshot != null
               && result != null
               && !result.DefaultSkin
               && !string.IsNullOrWhiteSpace(snapshot.PlayerId)
               && !string.IsNullOrWhiteSpace(snapshot.CareerId)
               && !string.IsNullOrWhiteSpace(snapshot.SkinId);
    }
}
