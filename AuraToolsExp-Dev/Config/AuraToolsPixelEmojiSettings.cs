using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsPixelEmojiSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumFavoriteLimit = 64;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; }

    [JsonProperty("syncRemote")]
    public bool SyncRemote { get; set; } = true;

    [JsonProperty("maxFavorites")]
    public int MaxFavorites { get; set; } = MaximumFavoriteLimit;

    [JsonProperty("favoriteIds")]
    public List<string> FavoriteIds { get; set; } = new();

    public void Normalize()
    {
        SchemaVersion = Math.Max(CurrentSchemaVersion, SchemaVersion);
        MaxFavorites = Math.Max(1, Math.Min(MaximumFavoriteLimit, MaxFavorites));
        FavoriteIds = (FavoriteIds ?? new List<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFavorites)
            .ToList();
    }

    public bool IsFavorite(string itemId)
    {
        return FavoriteIds.Any(value => string.Equals(value, itemId, StringComparison.OrdinalIgnoreCase));
    }
}
