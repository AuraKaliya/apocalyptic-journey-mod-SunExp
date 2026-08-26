using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Network.Command;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace AuraCg.Shared;

public static class SkillCgMediaTypes
{
    public const string Image = "image";
    public const string Sequence = "sequence";
    public const string Scene = "scene";

    public static string Normalize(string? value)
    {
        var type = value?.Trim() ?? "";
        if (string.Equals(type, Sequence, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "frames", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "pngSequence", StringComparison.OrdinalIgnoreCase))
        {
            return Sequence;
        }

        if (string.Equals(type, Scene, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "teamScene", StringComparison.OrdinalIgnoreCase))
        {
            return Scene;
        }

        return Image;
    }
}

public static class SkillCgAlphaModes
{
    public const string None = "none";
    public const string BlackKey = "blackKey";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, BlackKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "lumaKey", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "black", StringComparison.OrdinalIgnoreCase))
        {
            return BlackKey;
        }

        return None;
    }
}

public static class SkillCgFlashModes
{
    public const string Screen = "screen";
    public const string MaskedInvert = "maskedInvert";
    public const string ScreenBwPulse = "screenBwPulse";
    public const string HybridBwPulse = "hybridBwPulse";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, HybridBwPulse, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "hybridBlackWhite", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "hybridBw", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "blackWhiteImpact", StringComparison.OrdinalIgnoreCase))
        {
            return HybridBwPulse;
        }

        if (string.Equals(mode, ScreenBwPulse, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "screenBlackWhite", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "blackWhiteScreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "bwPulse", StringComparison.OrdinalIgnoreCase))
        {
            return ScreenBwPulse;
        }

        if (string.Equals(mode, MaskedInvert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "objectInvert", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "maskedDifference", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "difference", StringComparison.OrdinalIgnoreCase))
        {
            return MaskedInvert;
        }

        return Screen;
    }
}

public static class SkillCgPresentationModes
{
    public const string Slide = "slide";
    public const string FullscreenFade = "fullscreenFade";
    public const string CenterFade = "centerFade";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, FullscreenFade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullScreenFade", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullscreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fullScreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fade", StringComparison.OrdinalIgnoreCase))
        {
            return FullscreenFade;
        }

        if (string.Equals(mode, CenterFade, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "center", StringComparison.OrdinalIgnoreCase))
        {
            return CenterFade;
        }

        return Slide;
    }
}

public static class SkillCgFitModes
{
    public const string Contain = "contain";
    public const string Cover = "cover";
    public const string Stretch = "stretch";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        if (string.Equals(mode, Cover, StringComparison.OrdinalIgnoreCase))
        {
            return Cover;
        }

        if (string.Equals(mode, Stretch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "fill", StringComparison.OrdinalIgnoreCase))
        {
            return Stretch;
        }

        return Contain;
    }
}
