using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Features.Cg;

internal sealed class AuraToolsEventCgArtCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string Revision { get; set; } = "";
    public List<string> PreviewRoles { get; set; } = new();
    public Dictionary<string, AuraToolsEventCgArtAsset> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AuraToolsEventCgThemeArt> Themes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AuraToolsEventCgCharacterArt> Characters { get; set; } = new();

    internal int PoseCount => Characters.Sum(character => character.Poses.Count);

    internal static AuraToolsEventCgArtCatalog Parse(string json)
    {
        var catalog = JsonConvert.DeserializeObject<AuraToolsEventCgArtCatalog>(json)
            ?? throw new InvalidDataException("The event CG art catalog is empty.");
        if (catalog.SchemaVersion != 1) throw new InvalidDataException("Unsupported event CG art schema.");
        catalog.Assets = new Dictionary<string, AuraToolsEventCgArtAsset>(catalog.Assets ?? new(), StringComparer.OrdinalIgnoreCase);
        catalog.Themes = new Dictionary<string, AuraToolsEventCgThemeArt>(catalog.Themes ?? new(), StringComparer.OrdinalIgnoreCase);
        catalog.Characters ??= new();
        catalog.PreviewRoles ??= new();
        foreach (var pair in catalog.Assets)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || string.IsNullOrWhiteSpace(pair.Value.Path))
                throw new InvalidDataException("An event CG artwork entry is incomplete.");
            pair.Value.Portrait ??= new();
            pair.Value.Portrait.Normalize();
            pair.Value.Layers ??= new();
        }
        foreach (var character in catalog.Characters)
        {
            character.RoleIds ??= new();
            character.VariantIds ??= new();
            character.Poses = new Dictionary<string, string>(character.Poses ?? new(), StringComparer.OrdinalIgnoreCase);
            if (character.RoleIds.Count == 0 || !catalog.Assets.ContainsKey(character.Neutral))
                throw new InvalidDataException("A character has no identity or neutral portrait.");
            foreach (var pose in character.Poses.Values)
                if (!catalog.Assets.ContainsKey(pose)) throw new InvalidDataException("A pose references missing artwork.");
        }
        foreach (var theme in catalog.Themes.Values)
        {
            theme.Layers ??= new();
            if (!catalog.Assets.ContainsKey(theme.Background)) throw new InvalidDataException("A theme has no background artwork.");
        }
        foreach (var layer in catalog.Assets.Values.SelectMany(asset => asset.Layers).Concat(catalog.Themes.Values.SelectMany(theme => theme.Layers)))
            if (!catalog.Assets.ContainsKey(layer.Asset)) throw new InvalidDataException("A companion references missing artwork.");
        return catalog;
    }

    internal AuraToolsEventCgCharacterArt? FindCharacter(string roleId, string variantId = "") =>
        Characters.FirstOrDefault(character => character.RoleIds.Contains(roleId, StringComparer.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(variantId)
                ? character.VariantIds.Count == 0
                : character.VariantIds.Contains(variantId, StringComparer.OrdinalIgnoreCase)));

    internal string ResolvePose(AuraToolsEventCgCharacterArt character, string sceneId) =>
        character.Poses.TryGetValue(sceneId, out var pose) ? pose : character.Neutral;

    internal static void ApplyMotionPreference(AuraCgSceneArtwork artwork, bool enabled)
    {
        if (enabled) return;
        artwork.CameraPush = 0f;
        foreach (var layer in artwork.Layers)
            layer.MotionX = layer.MotionY = layer.Pulse = 0f;
    }

    internal static string ResolveAssetPath(string artDirectory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidDataException("Packaged CG artwork must use a relative path.");
        var root = Path.GetFullPath(artDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CG artwork path leaves its package directory.");
        return path;
    }
}

internal sealed class AuraToolsEventCgArtAsset
{
    public string Path { get; set; } = "";
    public AuraCgPortraitFraming Portrait { get; set; } = new();
    public List<AuraToolsEventCgCompanionArt> Layers { get; set; } = new();
}

internal sealed class AuraToolsEventCgCharacterArt
{
    public string Id { get; set; } = "";
    public List<string> RoleIds { get; set; } = new();
    public List<string> VariantIds { get; set; } = new();
    public string Neutral { get; set; } = "";
    public Dictionary<string, string> Poses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class AuraToolsEventCgThemeArt
{
    public string Background { get; set; } = "";
    public bool DarkTitle { get; set; }
    public float CameraPush { get; set; } = 0.02f;
    public List<AuraToolsEventCgCompanionArt> Layers { get; set; } = new();
}

internal sealed class AuraToolsEventCgCompanionArt
{
    public string Asset { get; set; } = "";
    public bool Foreground { get; set; } = true;
    public bool Required { get; set; } = true;
    public float Opacity { get; set; } = 1f;
    public float MotionX { get; set; }
    public float MotionY { get; set; }
    public float Pulse { get; set; }
}
