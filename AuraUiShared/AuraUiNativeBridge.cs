using System;
using System.Reflection;
using AuraShared.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuraUi.Shared;

public static class AuraUiNativeBridge
{
    public const string GameFontAssetPath = "Fonts/SourceFont/HarmonyOS_Sans_Medium SDF";

    private static TMP_FontAsset? gameFont;
    private static Font? legacyFont;
    private static bool gameFontLoadAttempted;
    private static bool legacyFontLoadAttempted;

    public static TMP_FontAsset? ResolveGameFont(Action<string>? warn = null)
    {
        if (gameFont != null || gameFontLoadAttempted)
        {
            return gameFont;
        }

        gameFontLoadAttempted = true;
        try
        {
            gameFont = AuraSharedResourceCache.Load<TMP_FontAsset>(
                "Aura.Shared",
                GameFontAssetPath,
                true,
                "ui.font",
                warn);
            if (gameFont == null && GameApp.Instance != null)
            {
                gameFont = GameApp.Instance.MainFontAsset;
            }
        }
        catch (Exception ex)
        {
            warn?.Invoke("[AuraUi] game TMP font unavailable: " + ex.Message);
        }

        return gameFont;
    }

    public static Font ResolveLegacyFont(Action<string>? warn = null)
    {
        if (legacyFont != null || legacyFontLoadAttempted)
        {
            return legacyFont ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        legacyFontLoadAttempted = true;
        try
        {
            var tmpFont = ResolveGameFont(warn);
            var sourceFontProperty = typeof(TMP_FontAsset).GetProperty(
                "sourceFontFile",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            legacyFont = sourceFontProperty?.GetValue(tmpFont, null) as Font;
        }
        catch (Exception ex)
        {
            warn?.Invoke("[AuraUi] legacy game font bridge unavailable: " + ex.Message);
        }

        legacyFont ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
        return legacyFont;
    }

    public static void Apply(TMP_Text text, AuraUiTheme? theme = null, Action<string>? warn = null)
    {
        if (text == null)
        {
            return;
        }

        var resolvedTheme = theme ?? AuraUiStyleRegistry.Resolve(AuraUiStyleIds.WitchNative);
        if ((resolvedTheme.Capabilities & AuraUiCapabilities.GameFont) != 0)
        {
            var font = ResolveGameFont(warn);
            if (font != null)
            {
                text.font = font;
            }
        }

        text.raycastTarget = false;
    }

    public static void Apply(Text text, Action<string>? warn = null)
    {
        if (text == null)
        {
            return;
        }

        text.font = ResolveLegacyFont(warn);
        text.raycastTarget = false;
    }

    internal static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
    {
        return anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Center
        };
    }
}
