using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;

namespace SunExp.Dll.Hooks.Ui;

public static class StarScoreHudAssets
{
    private const string Root = "Mods/SunExp/ModResource/Images/UI/";

    public const string FullPath = Root + "\u661f\u8c31.png";
    public const string BackgroundPath = Root + "\u661f\u8c31-\u80cc\u666f.png";
    public const string HeadPath = Root + "\u661f\u8c31-head.png";
    public const string Score1Path = Root + "\u661f\u8c31-1.png";
    public const string Score2Path = Root + "\u661f\u8c31-2.png";
    public const string Score3Path = Root + "\u661f\u8c31-3.png";
    public const string SpacePath = Root + "\u661f\u8c31-space.png";
    public const string OpeningIconPath = Root + "\u542f.png";
    public const string SustainIconPath = Root + "\u627f.png";
    public const string TurnIconPath = Root + "\u8f6c.png";
    public const string CloseIconPath = Root + "\u5408.png";

    private static readonly Dictionary<string, Sprite?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> StructuralPaths()
    {
        yield return FullPath;
        yield return BackgroundPath;
        yield return HeadPath;
        yield return Score1Path;
        yield return Score2Path;
        yield return Score3Path;
        yield return SpacePath;
    }

    public static IEnumerable<string> NoteIconPaths()
    {
        yield return OpeningIconPath;
        yield return SustainIconPath;
        yield return TurnIconPath;
        yield return CloseIconPath;
    }

    public static IEnumerable<string> AllPaths()
    {
        foreach (var path in StructuralPaths())
        {
            yield return path;
        }

        foreach (var path in NoteIconPaths())
        {
            yield return path;
        }
    }

    public static Sprite? Load(string path)
    {
        if (Cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Sprite? sprite = null;
        try
        {
            sprite = SunExpResourceCache.Load<Sprite>(path, true);
            if (sprite?.texture != null)
            {
                sprite.texture.filterMode = FilterMode.Point;
                sprite.texture.wrapMode = TextureWrapMode.Clamp;
            }

            if (sprite == null)
            {
                SunExpLog.Warn("[StarScoreHud] UI sprite missing: " + path);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[StarScoreHud] failed to load UI sprite " + path + ": " + ex.Message);
        }

        Cache[path] = sprite;
        return sprite;
    }

    public static Sprite? IconFor(StarScoreNote note)
    {
        return note switch
        {
            StarScoreNote.Opening => Load(OpeningIconPath),
            StarScoreNote.Sustain => Load(SustainIconPath),
            StarScoreNote.Turn => Load(TurnIconPath),
            StarScoreNote.Close => Load(CloseIconPath),
            _ => null
        };
    }
}
