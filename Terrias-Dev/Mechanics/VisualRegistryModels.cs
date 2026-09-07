using System.Collections.Generic;
using Newtonsoft.Json;

namespace Terrias.Dll.Mechanics;

public sealed class VisualRegistryDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "Terrias";

    [JsonProperty("textures")]
    public List<TextureVisualSpec> Textures { get; set; } = new();

    [JsonProperty("videos")]
    public List<VideoVisualSpec> Videos { get; set; } = new();

    [JsonProperty("modeEntries")]
    public List<ModeEntryVisualSpec> ModeEntries { get; set; } = new();

    [JsonProperty("frameAnimations")]
    public List<FrameAnimationVisualSpec> FrameAnimations { get; set; } = new();

    [JsonProperty("mapNodeArt")]
    public List<MapNodeArtVisualSpec> MapNodeArt { get; set; } = new();

    [JsonProperty("shaders")]
    public List<ShaderVisualSpec> Shaders { get; set; } = new();

    [JsonProperty("effects")]
    public List<VisualEffectVisualSpec> Effects { get; set; } = new();

    [JsonProperty("fieldPresentation")]
    public FieldPresentationOptions FieldPresentation { get; set; } = new();

    [JsonProperty("fields")]
    public List<FieldVisualSpec> Fields { get; set; } = new();
}

public sealed class VideoVisualSpec
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class TextureVisualSpec
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("path")]
    public string Path { get; set; } = "";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class ModeEntryVisualSpec
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("normalTitleSprite")]
    public string NormalTitleSprite { get; set; } = "";

    [JsonProperty("highlightedTitleSprite")]
    public string HighlightedTitleSprite { get; set; } = "";

    [JsonProperty("titleArtHeightRatio")]
    public float TitleArtHeightRatio { get; set; } = 0.735f;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class FrameAnimationVisualSpec
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("targetKind")]
    public string TargetKind { get; set; } = "";

    [JsonProperty("frameSeconds")]
    public float FrameSeconds { get; set; } = 0.2f;

    [JsonProperty("matchIds")]
    public List<string> MatchIds { get; set; } = new();

    [JsonProperty("matchSpriteNames")]
    public List<string> MatchSpriteNames { get; set; } = new();

    [JsonProperty("framePaths")]
    public List<string> FramePaths { get; set; } = new();

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class MapNodeArtVisualSpec
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("texturePath")]
    public string TexturePath { get; set; } = "";

    [JsonProperty("fitMode")]
    public string FitMode { get; set; } = nameof(MapNodeCardArtFitMode.ContainTrimmed);

    [JsonProperty("mapIds")]
    public List<string> MapIds { get; set; } = new();

    [JsonProperty("levelIds")]
    public List<string> LevelIds { get; set; } = new();

    [JsonProperty("enemyIds")]
    public List<string> EnemyIds { get; set; } = new();

    [JsonProperty("boundsWidth")]
    public float BoundsWidth { get; set; } = MapNodeTextureFitService.DefaultFightBoundsWidth;

    [JsonProperty("boundsHeight")]
    public float BoundsHeight { get; set; } = MapNodeTextureFitService.DefaultFightBoundsHeight;

    [JsonProperty("alphaThreshold")]
    public float AlphaThreshold { get; set; } = MapNodeTextureFitService.DefaultAlphaThreshold;

    [JsonProperty("offsetX")]
    public float OffsetX { get; set; }

    [JsonProperty("offsetY")]
    public float OffsetY { get; set; }

    [JsonProperty("priority")]
    public int Priority { get; set; }

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class ShaderVisualSpec
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("shaderName")]
    public string ShaderName { get; set; } = "";

    [JsonProperty("shaderPath")]
    public string ShaderPath { get; set; } = "";

    [JsonProperty("bundlePath")]
    public string BundlePath { get; set; } = "";

    [JsonProperty("materialPath")]
    public string MaterialPath { get; set; } = "";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class VisualEffectVisualSpec
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("kind")]
    public string Kind { get; set; } = "";

    [JsonProperty("shaderId")]
    public string ShaderId { get; set; } = "";

    [JsonProperty("bundlePath")]
    public string BundlePath { get; set; } = "";

    [JsonProperty("materialPath")]
    public string MaterialPath { get; set; } = "";

    [JsonProperty("textures")]
    public Dictionary<string, string> Textures { get; set; } = new();

    [JsonProperty("floats")]
    public Dictionary<string, float> Floats { get; set; } = new();

    [JsonProperty("colors")]
    public Dictionary<string, string> Colors { get; set; } = new();

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}
