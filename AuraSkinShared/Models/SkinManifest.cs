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
    public string SkinId { get; set; } = "";
    public string TargetCareerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public string PreviewPath { get; set; } = "";
    public SkinAssets Assets { get; set; } = new();
}
