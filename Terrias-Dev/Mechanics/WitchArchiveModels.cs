using System.Collections.Generic;
using Newtonsoft.Json;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public sealed class WitchArchiveDocument
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "Terrias";

    [JsonProperty("entries")]
    public List<WitchArchiveEntry> Entries { get; set; } = new();
}

public sealed class WitchArchiveEntry
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("careerId")]
    public string CareerId { get; set; } = "";

    [JsonProperty("sort")]
    public int Sort { get; set; }

    [JsonProperty("avatarPath")]
    public string AvatarPath { get; set; } = "";

    [JsonProperty("portraitPath")]
    public string PortraitPath { get; set; } = "";

    [JsonProperty("portraitOffsetX")]
    public float PortraitOffsetX { get; set; }

    [JsonProperty("portraitOffsetY")]
    public float PortraitOffsetY { get; set; }

    [JsonProperty("name")]
    public WitchArchiveLocalizedText Name { get; set; } = new();

    [JsonProperty("title")]
    public WitchArchiveLocalizedText Title { get; set; } = new();

    [JsonProperty("summary")]
    public WitchArchiveLocalizedText Summary { get; set; } = new();

    [JsonProperty("background")]
    public WitchArchiveLocalizedText Background { get; set; } = new();

    [JsonProperty("backgroundFiles")]
    public WitchArchiveLocalizedText BackgroundFiles { get; set; } = new();

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;
}

public sealed class WitchArchiveLocalizedText
{
    [JsonProperty("zh-Hans")]
    public string ZhHans { get; set; } = "";

    [JsonProperty("zh-Hant")]
    public string ZhHant { get; set; } = "";

    [JsonProperty("en")]
    public string English { get; set; } = "";

    [JsonProperty("ja")]
    public string Japanese { get; set; } = "";

    public string Resolve(string fallback = "")
    {
        var row = new Dictionary<string, string>
        {
            ["Text"] = Clean(ZhHans),
            ["Text_zh-Hant"] = Clean(ZhHant),
            ["Text_en"] = Clean(English),
            ["Text_ja"] = Clean(Japanese)
        };

        try
        {
            var localized = row.Localize("Text");
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized.Trim();
            }
        }
        catch
        {
            // The archive remains readable while the native language service is unavailable.
        }

        foreach (var value in new[] { ZhHans, English, ZhHant, Japanese, fallback })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string Clean(string? value)
    {
        return value?.Trim() ?? "";
    }
}

public sealed class WitchArchiveDisplayEntry
{
    public WitchArchiveDisplayEntry(
        string id,
        string roleId,
        string name,
        string title,
        string summary,
        string background,
        string avatarPath,
        string portraitPath,
        float portraitOffsetX,
        float portraitOffsetY)
    {
        Id = id;
        RoleId = roleId;
        Name = name;
        Title = title;
        Summary = summary;
        Background = background;
        AvatarPath = avatarPath;
        PortraitPath = portraitPath;
        PortraitOffsetX = portraitOffsetX;
        PortraitOffsetY = portraitOffsetY;
    }

    public string Id { get; }

    public string RoleId { get; }

    public string Name { get; }

    public string Title { get; }

    public string Summary { get; }

    public string Background { get; }

    public string AvatarPath { get; }

    public string PortraitPath { get; }

    public float PortraitOffsetX { get; }

    public float PortraitOffsetY { get; }
}
