using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal readonly struct ReplayRgbaSampleV17
{
    internal ReplayRgbaSampleV17(byte red, byte green, byte blue, byte alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    internal byte Red { get; }
    internal byte Green { get; }
    internal byte Blue { get; }
    internal byte Alpha { get; }
}

internal static class ReplayRenderPixelContractV17
{
    internal static string Validate(IReadOnlyList<ReplayRgbaSampleV17> pixels)
    {
        if (pixels == null || pixels.Count == 0) return "pixel-sample-empty";
        var visible = 0;
        var bright = 0;
        var darkest = 255;
        var lightest = 0;
        foreach (var pixel in pixels)
        {
            if (pixel.Alpha >= 8) visible++;
            var luminance = (pixel.Red * 54 + pixel.Green * 183 + pixel.Blue * 19) >> 8;
            if (luminance >= 24) bright++;
            darkest = Math.Min(darkest, luminance);
            lightest = Math.Max(lightest, luminance);
        }
        if (visible < pixels.Count / 4) return "pixel-alpha-empty:" + visible;
        if (bright < 8) return "pixel-black:" + bright;
        if (lightest - darkest < 12) return "pixel-flat:" + (lightest - darkest);
        return "";
    }
}
