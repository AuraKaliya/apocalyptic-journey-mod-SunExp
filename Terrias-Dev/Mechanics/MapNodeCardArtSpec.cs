using System;
using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Mechanics;

public sealed class MapNodeCardArtSpec
{
    public MapNodeCardArtSpec(
        string texturePath,
        MapNodeCardArtFitMode fitMode,
        IEnumerable<string>? mapIds = null,
        IEnumerable<string>? levelIds = null,
        IEnumerable<string>? enemyIds = null,
        float boundsWidth = MapNodeTextureFitService.DefaultFightBoundsWidth,
        float boundsHeight = MapNodeTextureFitService.DefaultFightBoundsHeight,
        float alphaThreshold = MapNodeTextureFitService.DefaultAlphaThreshold,
        float offsetX = 0f,
        float offsetY = 0f,
        int priority = 0)
    {
        TexturePath = texturePath ?? "";
        FitMode = fitMode;
        MapIds = Normalize(mapIds);
        LevelIds = Normalize(levelIds);
        EnemyIds = Normalize(enemyIds);
        BoundsWidth = boundsWidth;
        BoundsHeight = boundsHeight;
        AlphaThreshold = alphaThreshold;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Priority = priority;
    }

    public string TexturePath { get; }

    public MapNodeCardArtFitMode FitMode { get; }

    public IReadOnlyList<string> MapIds { get; }

    public IReadOnlyList<string> LevelIds { get; }

    public IReadOnlyList<string> EnemyIds { get; }

    public float BoundsWidth { get; }

    public float BoundsHeight { get; }

    public float AlphaThreshold { get; }

    public float OffsetX { get; }

    public float OffsetY { get; }

    public int Priority { get; }

    public bool Matches(string? mapId, string? levelId, string? enemyId)
    {
        return MatchesAny(MapIds, mapId)
            || MatchesAny(LevelIds, levelId)
            || MatchesAny(EnemyIds, enemyId);
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? values)
    {
        return values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();
    }

    private static bool MatchesAny(IReadOnlyList<string> values, string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual) || values.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], actual, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
