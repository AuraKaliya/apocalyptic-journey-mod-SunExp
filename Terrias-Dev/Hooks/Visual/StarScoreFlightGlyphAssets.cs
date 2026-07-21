using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks.Visual;

internal static class StarScoreFlightGlyphAssets
{
    private const string Root = "Mods/Terrias/ModResource/Images/Effects/StarScore/";

    public const string OpeningPath = Root + "flight_opening.png";
    public const string SustainPath = Root + "flight_sustain.png";
    public const string TurnPath = Root + "flight_turn.png";
    public const string ClosePath = Root + "flight_close.png";

    private static readonly Dictionary<string, Sprite?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> AllPaths()
    {
        yield return OpeningPath;
        yield return SustainPath;
        yield return TurnPath;
        yield return ClosePath;
    }

    public static Sprite? IconFor(StarScoreNote note)
    {
        return Load(note switch
        {
            StarScoreNote.Opening => OpeningPath,
            StarScoreNote.Sustain => SustainPath,
            StarScoreNote.Turn => TurnPath,
            StarScoreNote.Close => ClosePath,
            _ => ""
        });
    }

    private static Sprite? Load(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            sprite = TerriasResourceCache.Load<Sprite>(path, true);
            if (sprite?.texture != null)
            {
                sprite.texture.filterMode = FilterMode.Bilinear;
                sprite.texture.wrapMode = TextureWrapMode.Clamp;
            }

            if (sprite == null)
            {
                TerriasLog.Warn("[CardUseFx] flight glyph missing: " + path);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CardUseFx] flight glyph load failed " + path + ": " + ex.Message);
        }

        Cache[path] = sprite;
        return sprite;
    }
}
